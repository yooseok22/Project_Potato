using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class AutomationPlayerController
{
    private FarmRobotStation configuringRobot;
    private FarmRobotRoute editingRoute;
    private readonly List<FarmDroneArea> editingAreas = new();
    private readonly List<LineRenderer> routeLines = new();
    private Material routeMaterial;
    private Vector2Int? areaFirstCorner;
    private bool dirtyRouteView;
    public static string RobotSizeName(FarmRobotSize size) => size switch
    { FarmRobotSize.Medium => "중형", FarmRobotSize.Large => "대형", _ => "소형" };

    public void BeginRobotConfiguration(FarmRobotStation station)
    {
        if (!station || !station.IsAtDock || !AutomationWorld.Offline) return;
        EndRobotConfiguration(false); SetBuilding(false);
        configuringRobot = station; station.BeingConfigured = true;
        editingRoute = station.Route.Copy(); editingAreas.Clear(); editingAreas.AddRange(station.Areas);
        areaFirstCorner = null; dirtyRouteView = true;
    }
    private void EndRobotConfiguration(bool apply)
    {
        if (configuringRobot)
        {
            if (apply)
            {
                bool accepted = configuringRobot.Role == FarmRobotRole.SprayerDrone
                    ? configuringRobot.ConfigureAreas(editingAreas) : configuringRobot.ConfigureRoute(editingRoute);
                if (!accepted) { Notify("유효한 경로 또는 영역을 먼저 설정하세요."); return; }
            }
            configuringRobot.BeingConfigured = false;
        }
        configuringRobot = null; areaFirstCorner = null;
        foreach (var line in routeLines) if (line) AutomationWorld.DestroyOwned(line.gameObject);
        routeLines.Clear();
        if (routeMaterial) AutomationWorld.DestroyOwned(routeMaterial); routeMaterial = null;
    }
    private void UpdateRobotConfiguration()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.escapeKey.wasPressedThisFrame || keyboard.bKey.wasPressedThisFrame) { EndRobotConfiguration(false); return; }
        bool drone = configuringRobot.Role == FarmRobotRole.SprayerDrone;
        if (!player.CanUseGameplayInput) { DrawText("로봇 설정 일시 정지 · 화면 클릭으로 재개 · [Esc] 취소"); return; }
        if (keyboard.enterKey.wasPressedThisFrame) { EndRobotConfiguration(true); return; }
        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            if (drone)
            {
                if (areaFirstCorner.HasValue) areaFirstCorner = null;
                else if (editingAreas.Count > 0) editingAreas.RemoveAt(editingAreas.Count - 1);
            }
            else editingRoute.RemoveLast();
            dirtyRouteView = true;
        }
        var mouse = Mouse.current;
        if (mouse != null && Aim(out var hit))
        {
            var grid = configuringRobot.Grid; var cell = grid.WorldToCell(hit.point);
            if (grid.ContainsCell(cell) && !player.CursorCapturedThisFrame)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    if (!drone)
                    {
                        if (!editingRoute.TryAdd(cell, grid.ContainsCell))
                            Notify("기존 타일의 상하좌우에 면을 맞대어 추가하세요. 대각선이나 떨어진 타일은 추가할 수 없습니다.");
                    }
                    else if (editingAreas.Count >= 4) Notify("드론 영역은 최대 4개입니다.");
                    else if (!areaFirstCorner.HasValue) areaFirstCorner = cell;
                    else
                    {
                        var area = new FarmDroneArea { minimum = Vector2Int.Min(areaFirstCorner.Value, cell), maximum = Vector2Int.Max(areaFirstCorner.Value, cell) };
                        if (configuringRobot.ValidArea(area)) { editingAreas.Add(area); areaFirstCorner = null; }
                        else Notify("정사각형 영역의 크기와 충전소로부터의 지정 거리를 확인하세요.");
                    }
                    dirtyRouteView = true;
                }
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    if (!drone) editingRoute.Toggle(cell);
                    else
                    {
                        int index = editingAreas.FindLastIndex(area => area.Contains(cell));
                        if (index >= 0) editingAreas.RemoveAt(index); else areaFirstCorner = null;
                    }
                    dirtyRouteView = true;
                }
            }
        }
        if (dirtyRouteView) RebuildRobotRouteView();
        string title = $"로봇 설정 · {RobotSizeName(configuringRobot.Size)} · 최대 {configuringRobot.Profile.maxWorkCells}칸\n";
        title += drone
            ? $"[좌클릭 2회] 정사각형 영역 · {editingAreas.Count}/4개\n최대 {configuringRobot.Profile.droneAreaSideCells}×{configuringRobot.Profile.droneAreaSideCells}칸 · 지정 거리 {configuringRobot.Profile.droneRangeCells}칸\n[우클릭] 영역 삭제 · [Backspace] 이전 지정 취소"
            : $"[좌클릭] {(editingRoute.Count == 0 ? "원하는 곳에 첫 타일 선택" : "기존 타일 상하좌우로 한 칸씩 추가")} · {editingRoute.Count}칸\nㄱ자·ㄷ자·갈림길 가능 · 대각선 불가\n[우클릭] 파랑: 작업 / 빨강: 이동만\n경로 밖은 이동만 · [Backspace] 마지막 타일 취소";
        title += "\n[Enter] 확정 · [Esc / B] 취소";
        if (areaFirstCorner.HasValue) title += "\n첫 꼭짓점 선택됨 · 반대 꼭짓점을 선택하세요.";
        if (Time.unscaledTime < notificationUntil) title += "\n" + notification;
        DrawText(title);
    }
    private void RebuildRobotRouteView()
    {
        dirtyRouteView = false;
        foreach (var line in routeLines) if (line) AutomationWorld.DestroyOwned(line.gameObject);
        routeLines.Clear();
        if (!routeMaterial) routeMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        // White material allows each line's vertex colour to distinguish work and transit cells.
        routeMaterial.color = Color.white;
        var grid = configuringRobot.Grid;
        void Rectangle(Vector3 minimum, Vector3 maximum, Color color)
        {
            var go = new GameObject("RobotRouteCell"); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = routeMaterial;
            line.startColor = line.endColor = color; line.startWidth = line.endWidth = 0.07f;
            line.useWorldSpace = true; line.positionCount = 5;
            line.SetPositions(new[] { minimum, new Vector3(maximum.x, minimum.y, minimum.z), maximum,
                new Vector3(minimum.x, minimum.y, maximum.z), minimum }); routeLines.Add(line);
        }
        for (int index = 0; index < editingRoute.Count; index++)
        {
            var center = grid.CellCenter(editingRoute.Cells[index]) + Vector3.up * 0.07f;
            var half = new Vector3(grid.CellSize * 0.5f, 0, grid.CellSize * 0.5f);
            Rectangle(center - half, center + half, editingRoute.WorksAt(index) ? new Color(0.1f, 0.55f, 1f) : Color.red);
        }
        foreach (var area in editingAreas)
        {
            var half = new Vector3(grid.CellSize * 0.5f, 0, grid.CellSize * 0.5f);
            Rectangle(grid.CellCenter(area.minimum) - half + Vector3.up * 0.07f,
                grid.CellCenter(area.maximum) + half + Vector3.up * 0.07f, new Color(0.1f, 0.55f, 1f));
        }
        if (areaFirstCorner.HasValue)
        {
            var center = grid.CellCenter(areaFirstCorner.Value) + Vector3.up * 0.09f;
            Rectangle(center - new Vector3(0.2f, 0, 0.2f), center + new Vector3(0.2f, 0, 0.2f), Color.yellow);
        }
    }
}
