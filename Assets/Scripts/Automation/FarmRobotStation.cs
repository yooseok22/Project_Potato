using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum FarmRobotState { Docked, Charging, Outbound, Working, Returning, WaitingForReturnPath, TravellingToRoute }

/// <summary>A charged trip visits the connected work tiles and returns to its dock, detouring around obstacles.</summary>
[RequireComponent(typeof(AutomationBlock))]
public sealed class FarmRobotStation : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private FarmRobotRole role;
    [SerializeField] private FarmRobotSize size;
    [SerializeField] private FarmRobotSettings settings;
    [SerializeField] private MonoBehaviour supplyInventory;
    private AutomationBlock power;
    private GroundGrid grid;
    private Transform robot;
    private FarmRobotRoute route = new();
    private List<FarmRobotVisit> tripRoute = new();
    private readonly List<FarmDroneArea> areas = new();
    private readonly HashSet<Vector2Int> worked = new();
    private readonly List<Vector3> emergencyPath = new();
    private Collider[] overlap = new Collider[32];
    private float charge;
    private float workRemaining;
    private float nextDecision;
    private float nextReturnSearch;
    private int routeIndex, areaIndex, workCount, emergencyIndex;
    private bool emergency;
    private bool approachingRoute;
    private Vector2Int workingCell;

    public FarmRobotRole Role => role;
    public FarmRobotSize Size => size;
    public FarmRobotState State { get; private set; }
    public FarmRobotSizeSettings Profile => settings.For(size);
    public FarmRobotRoute Route => route;
    public IReadOnlyList<FarmDroneArea> Areas => areas;
    public GroundGrid Grid => grid;
    public int WorkedThisTrip => workCount;
    public float Charge01 => Mathf.Clamp01(charge);
    public bool IsAtDock => State == FarmRobotState.Docked || State == FarmRobotState.Charging;
    public string LastNotice { get; private set; }
    public string RoleName => role switch { FarmRobotRole.Seeder => "파종 로봇", FarmRobotRole.Mulching => "멀칭 로봇", _ => "방제 드론" };
    public string StateName => State switch { FarmRobotState.Charging => "충전 중", FarmRobotState.Outbound => "출발 경로 이동 중",
        FarmRobotState.Working => "작업 중", FarmRobotState.Returning => "복귀 중", FarmRobotState.WaitingForReturnPath => "우회 경로 대기",
        FarmRobotState.TravellingToRoute => "작업 경로로 이동 중", _ => "대기" };
    public string Prompt => $"설정하기 [ E ] · {RoleName} / {AutomationPlayerController.RobotSizeName(size)} · {StateName} · 충전 {Charge01:P0}";
    public bool RequiresHold => false;
    public float HoldDuration => 0;

    public void Initialize(FarmRobotRole robotRole, FarmRobotSize robotSize, FarmRobotSettings configuration, GroundGrid ground, Transform visual)
    {
        role = robotRole; size = robotSize; settings = configuration; grid = ground; robot = visual;
        power = GetComponent<AutomationBlock>(); State = FarmRobotState.Docked;
        robot.position = DockPosition;
    }

    public bool CanInteract(PlayerInteractionController player) => AutomationWorld.Offline && IsAtDock && grid && settings;
    public void Interact(PlayerInteractionController player)
    {
        var controls = player.GetComponent<AutomationPlayerController>();
        if (controls) controls.BeginRobotConfiguration(this);
    }
    public bool ConfigureRoute(FarmRobotRoute replacement)
    {
        if (!IsAtDock || !grid || role == FarmRobotRole.SprayerDrone || replacement == null || replacement.Count == 0) return false;
        var checkedRoute = new FarmRobotRoute();
        for (int index = 0; index < replacement.Count; index++)
        {
            if (!checkedRoute.TryAdd(replacement.Cells[index], grid.ContainsCell) || checkedRoute.Count != index + 1) return false;
            if (!replacement.WorksAt(index)) checkedRoute.Toggle(checkedRoute.Cells[index]);
        }
        route = checkedRoute; nextDecision = 0; return true;
    }
    public bool ConfigureAreas(IReadOnlyList<FarmDroneArea> replacement)
    {
        if (!IsAtDock || role != FarmRobotRole.SprayerDrone || replacement == null || replacement.Count > 4) return false;
        foreach (var area in replacement)
            if (!ValidArea(area)) return false;
        areas.Clear(); areas.AddRange(replacement); nextDecision = 0; return true;
    }
    public bool ValidArea(FarmDroneArea area)
    {
        if (!grid || area.minimum.x > area.maximum.x || area.minimum.y > area.maximum.y ||
            !grid.ContainsCell(area.minimum) || !grid.ContainsCell(area.maximum)) return false;
        int width = area.maximum.x - area.minimum.x + 1, depth = area.maximum.y - area.minimum.y + 1;
        var origin = grid.WorldToCell(transform.position);
        return width == depth && width <= Profile.droneAreaSideCells &&
            Mathf.Max(Mathf.Abs(area.minimum.x - origin.x), Mathf.Abs(area.maximum.x - origin.x),
                Mathf.Abs(area.minimum.y - origin.y), Mathf.Abs(area.maximum.y - origin.y)) <= Profile.droneRangeCells;
    }
    public void BindSupplies(MonoBehaviour inventory)
    {
        if (inventory && !(inventory is IFarmRobotSupplies)) throw new ArgumentException("IFarmRobotSupplies adapter required.");
        supplyInventory = inventory;
    }
    private Vector3 DockPosition => transform.position + Vector3.up * (role == FarmRobotRole.SprayerDrone ? 0.3f : 0.12f);
    private Vector3 CellPosition(Vector2Int cell) => grid.CellCenter(cell) + Vector3.up *
        (role == FarmRobotRole.SprayerDrone ? settings.droneFlightHeight : 0.12f);
    private FarmRobotSupply Supply => role switch
    { FarmRobotRole.Seeder => FarmRobotSupply.SeedPotato, FarmRobotRole.Mulching => FarmRobotSupply.MulchingFilm, _ => FarmRobotSupply.Pesticide };
    private bool HasSupply => settings.unlimitedSupplies || supplyInventory is IFarmRobotSupplies inventory && inventory.HasSupply(Supply, 1);

    private PotatoFarmingSystem WorkOwner(Vector2Int cell)
    {
        var world = AutomationWorld.Instance;
        if (!world || !grid || cell == grid.WorldToCell(transform.position)) return null;
        foreach (var farming in world.FarmingSystems)
        {
            bool eligible = role switch
            {
                FarmRobotRole.Seeder => farming.CanRobotPlant(grid, cell),
                FarmRobotRole.Mulching => farming.CanRobotMulch(grid, cell),
                _ => farming.CanPreventBugs(grid, cell)
            };
            if (eligible) return farming;
        }
        return null;
    }
    private bool CanWork(Vector2Int cell) => workCount < Profile.maxWorkCells && !worked.Contains(cell) && WorkOwner(cell);
    private bool HasPendingWork()
    {
        if (role == FarmRobotRole.SprayerDrone)
            return areas.Any(area => AreaCells(area).Any(cell => WorkOwner(cell)));
        for (int index = 0; index < route.Count; index++) if (route.WorksAt(index) && WorkOwner(route.Cells[index])) return true;
        return false;
    }
    private static IEnumerable<Vector2Int> AreaCells(FarmDroneArea area)
    {
        for (int x = area.minimum.x; x <= area.maximum.x; x++)
            for (int y = area.minimum.y; y <= area.maximum.y; y++) yield return new Vector2Int(x, y);
    }
    private bool DoWork(Vector2Int cell)
    {
        var owner = WorkOwner(cell);
        if (!owner || !CanWork(cell)) return false;
        if (!HasSupply) { ReturnEarly("보급품 부족"); return false; }
        bool success = role switch
        {
            FarmRobotRole.Seeder => owner.TryRobotPlant(grid, cell),
            FarmRobotRole.Mulching => owner.TryApplyMulchingFilm(grid, cell),
            _ => owner.TryPreventBugs(grid, cell, Profile.preventionSeconds)
        };
        if (!success) return false;
        if (!settings.unlimitedSupplies && supplyInventory is IFarmRobotSupplies inventory)
            if (!inventory.TryConsumeSupply(Supply, 1))
                Debug.LogError("Robot supply adapter changed after HasSupply; item adapters must run atomically on the main thread.", this);
        worked.Add(cell); workCount++; return true;
    }

    private void Update() => Simulate(Time.deltaTime);
    private void LateUpdate()
    {
        if (!robot || role != FarmRobotRole.SprayerDrone) return;
        var spray = robot.GetComponentInChildren<ParticleSystem>();
        if (spray)
        {
            bool spraying = State == FarmRobotState.Working && AutomationWorld.Offline;
            if (spraying && !spray.isPlaying) spray.Play();
            else if (!spraying && spray.isPlaying) spray.Stop();
        }
        foreach (Transform part in robot) if (part.name == "Rotor" && !IsAtDock) part.Rotate(0, 900f * Time.deltaTime, 0);
    }
    internal void Simulate(float deltaTime)
    {
        if (!AutomationWorld.Offline || !settings || !grid || !robot || deltaTime <= 0) return;
        if (IsAtDock)
        {
            if (power.PowerFraction < 0.999f) { State = FarmRobotState.Docked; return; }
            State = charge < 1f ? FarmRobotState.Charging : FarmRobotState.Docked;
            charge = Mathf.Min(1f, charge + deltaTime / Mathf.Max(1f, Profile.chargeSeconds));
            if (charge < 1f || Time.time < nextDecision) return;
            nextDecision = Time.time + 1f;
            // Editing holds the robot at the dock without discarding its current charge.
            if (BeingConfigured || !HasSupply || !HasPendingWork()) return;
            worked.Clear(); workCount = 0; routeIndex = 0; areaIndex = 0; emergency = false;
            State = FarmRobotState.Outbound; LastNotice = null;
            if (role != FarmRobotRole.SprayerDrone)
            {
                tripRoute = route.BuildRoundTrip();
                while (routeIndex < tripRoute.Count &&
                    (!CellClear(tripRoute[routeIndex].Cell) || !PlanPath(tripRoute[routeIndex].Cell))) routeIndex++;
                if (routeIndex >= tripRoute.Count)
                {
                    State = FarmRobotState.Docked; LastNotice = "작업 경로로 가는 길이 막혀 있습니다."; return;
                }
                approachingRoute = true; emergency = true; State = FarmRobotState.TravellingToRoute;
            }
        }
        if (State == FarmRobotState.WaitingForReturnPath)
        {
            if (Time.time >= nextReturnSearch) { nextReturnSearch = Time.time + 1f; PlanEmergencyReturn(); }
            return;
        }
        if (emergency) { FollowEmergencyReturn(deltaTime); return; }
        if (State == FarmRobotState.Working)
        {
            workRemaining -= deltaTime;
            if (workRemaining > 0) return;
            DoWork(workingCell);
            if (emergency) return;
            State = FarmRobotState.Outbound;
            // Drone area work visits all eligible cells from its centre before advancing.
            if (role != FarmRobotRole.SprayerDrone) StepRoute();
            return;
        }
        if (role == FarmRobotRole.SprayerDrone) TickDrone(deltaTime); else TickGround(deltaTime);
    }
    public bool BeingConfigured { get; set; }

    private void TickGround(float deltaTime)
    {
        if (routeIndex >= tripRoute.Count) { approachingRoute = false; emergency = true; PlanEmergencyReturn(); return; }
        var cell = tripRoute[routeIndex].Cell;
        if (!CellClear(cell)) { SkipBlockedWorkCell(); return; }
        if (!ClearSegment(robot.position, CellPosition(cell))) { PlanWorkApproach(); return; }
        if (!Move(CellPosition(cell), deltaTime)) return;
        if (route.WorksAt(cell) && CanWork(cell))
        { workingCell = cell; workRemaining = Profile.workSeconds; State = FarmRobotState.Working; return; }
        StepRoute();
    }
    private void StepRoute()
    {
        routeIndex++;
        State = routeIndex < tripRoute.Count && !tripRoute[routeIndex].Returning ? FarmRobotState.Outbound : FarmRobotState.Returning;
    }
    private void SkipBlockedWorkCell()
    {
        LastNotice = "설치물 또는 장애물로 접근할 수 없는 작업 칸을 건너뜁니다.";
        approachingRoute = false; emergency = false; emergencyPath.Clear(); StepRoute();
    }
    private void PlanWorkApproach()
    {
        if (routeIndex >= tripRoute.Count || !CellClear(tripRoute[routeIndex].Cell) || !PlanPath(tripRoute[routeIndex].Cell))
        { SkipBlockedWorkCell(); return; }
        LastNotice = "설치물 또는 장애물을 우회하여 작업을 계속합니다.";
        approachingRoute = true; emergency = true; State = FarmRobotState.TravellingToRoute;
    }
    private void TickDrone(float deltaTime)
    {
        if (areaIndex >= areas.Count || workCount >= Profile.maxWorkCells) { MoveHome(deltaTime); return; }
        var area = areas[areaIndex];
        Vector3 center = (grid.CellCenter(area.minimum) + grid.CellCenter(area.maximum)) * 0.5f + Vector3.up * settings.droneFlightHeight;
        if (!Move(center, deltaTime)) return;
        foreach (var cell in AreaCells(area))
            if (CanWork(cell)) { workingCell = cell; workRemaining = Profile.workSeconds; State = FarmRobotState.Working; return; }
        areaIndex++;
    }
    private void MoveHome(float deltaTime)
    {
        State = FarmRobotState.Returning;
        if (role == FarmRobotRole.SprayerDrone)
        {
            Vector3 above = transform.position + Vector3.up * settings.droneFlightHeight;
            if (Vector2.Distance(new Vector2(robot.position.x, robot.position.z), new Vector2(above.x, above.z)) > 0.02f && !Move(above, deltaTime)) return;
        }
        if (Move(DockPosition, deltaTime)) ArriveHome();
    }
    private void ArriveHome()
    {
        robot.position = DockPosition; charge = 0; State = FarmRobotState.Charging; emergency = false; approachingRoute = false; emergencyPath.Clear();
    }
    private bool Move(Vector3 target, float deltaTime)
    {
        Vector3 next = Vector3.MoveTowards(robot.position, target, Profile.moveSpeed * deltaTime);
        if (!ClearSegment(robot.position, next))
        {
            if (role != FarmRobotRole.SprayerDrone && (!emergency || approachingRoute)) PlanWorkApproach();
            else ReturnEarly("이동 경로에 장애물이 있어 우회 복귀");
            return false;
        }
        Vector3 directionToTarget = target - robot.position;
        if (new Vector2(directionToTarget.x, directionToTarget.z).sqrMagnitude > 0.001f)
            robot.rotation = Quaternion.LookRotation(new Vector3(directionToTarget.x, 0, directionToTarget.z));
        robot.position = next;
        return (robot.position - target).sqrMagnitude < 0.0004f;
    }
    private bool ClearSegment(Vector3 from, Vector3 to)
    {
        float distance = Vector3.Distance(from, to);
        if (distance <= 0) return true;
        foreach (var hit in Physics.BoxCastAll(from + Vector3.up * 0.35f, new Vector3(0.28f, 0.25f, 0.28f),
            (to - from).normalized, Quaternion.identity, distance, ~0, QueryTriggerInteraction.Ignore))
            if (BlocksMovement(hit.collider)) return false;
        return true;
    }
    private bool BlocksMovement(Collider collider) => collider && !collider.transform.IsChildOf(transform) &&
        !collider.GetComponentInParent<FarmRobotBody>() &&
        !collider.GetComponentInParent<GroundTileRenderer>() && !collider.GetComponentInParent<PotatoGrowth>();
    private bool CellClear(Vector2Int cell)
    {
        int count;
        do
        {
            count = Physics.OverlapBoxNonAlloc(CellPosition(cell) + Vector3.up * 0.35f,
                new Vector3(0.28f, 0.25f, 0.28f), overlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (count < overlap.Length) break;
            Array.Resize(ref overlap, overlap.Length * 2);
        } while (true);
        for (int index = 0; index < count; index++) if (BlocksMovement(overlap[index])) return false;
        return true;
    }
    private void ReturnEarly(string reason)
    {
        LastNotice = reason; emergency = true; approachingRoute = false; State = FarmRobotState.Returning; PlanEmergencyReturn();
    }
    private void PlanEmergencyReturn()
    {
        State = PlanPath(grid.WorldToCell(transform.position)) ? FarmRobotState.Returning : FarmRobotState.WaitingForReturnPath;
    }
    private bool PlanPath(Vector2Int goal)
    {
        emergencyPath.Clear(); emergencyIndex = 0;
        var start = grid.WorldToCell(robot.position);
        var queue = new Queue<Vector2Int>(); var previous = new Dictionary<Vector2Int, Vector2Int>();
        queue.Enqueue(start); previous[start] = start;
        var offsets = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0 && !previous.ContainsKey(goal))
        {
            var current = queue.Dequeue();
            foreach (var offset in offsets)
            {
                var next = current + offset;
                if (!grid.ContainsCell(next) || previous.ContainsKey(next) || !CellClear(next) ||
                    !ClearSegment(CellPosition(current), CellPosition(next))) continue;
                previous[next] = current; queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(goal)) return false;
        var reversed = new List<Vector3>(); var cursor = goal;
        while (cursor != start) { reversed.Add(CellPosition(cursor)); cursor = previous[cursor]; }
        reversed.Add(CellPosition(start)); reversed.Reverse(); emergencyPath.AddRange(reversed);
        return true;
    }
    private void FollowEmergencyReturn(float deltaTime)
    {
        if (emergencyIndex >= emergencyPath.Count)
        {
            if (approachingRoute)
            {
                approachingRoute = false; emergency = false; emergencyPath.Clear(); State = FarmRobotState.Outbound; return;
            }
            if (Move(DockPosition, deltaTime)) ArriveHome();
            return;
        }
        if (approachingRoute && !CellClear(tripRoute[routeIndex].Cell)) { SkipBlockedWorkCell(); return; }
        if (Move(emergencyPath[emergencyIndex], deltaTime)) emergencyIndex++;
    }
}
