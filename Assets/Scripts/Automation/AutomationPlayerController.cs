using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Free offline construction. B toggles, Z/X select, R rotates; E connects, RMB pins.</summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(FirstPersonPlayerController))]
public sealed class AutomationPlayerController : MonoBehaviour
{
    private FirstPersonPlayerController player;
    private PlayerItemController items;
    private CharacterController character;
    private AutomationMachineKind selected;
    private int rotation;
    private AutomationLine heldLine;
    private LineRenderer preview;
    private readonly LineRenderer[] previewArrows = new LineRenderer[2];
    private readonly TMP_Text[] previewLabels = new TMP_Text[2];
    private Material previewMaterial;
    private Canvas canvas;
    private RectTransform panel;
    private TMP_Text label;
    private string notification;
    private float notificationUntil;
    private RaycastHit[] hits = new RaycastHit[24];
    public bool IsBuilding { get; private set; }
    public bool ConsumesGameplayActions => IsBuilding;

    private void Awake()
    {
        player = GetComponent<FirstPersonPlayerController>();
        items = GetComponent<PlayerItemController>();
        character = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!player.IsLocallyControlled) { SetBuilding(false); return; }
        var world = AutomationWorld.Instance;
        if (!world) return;
        if (!canvas) CreateView();
        var keyboard = Keyboard.current;
        if (!AutomationWorld.Offline) { SetBuilding(false); DrawText("자동화는 싱글플레이에서 사용할 수 있습니다."); return; }
        if (keyboard != null && keyboard.bKey.wasPressedThisFrame && player.CanUseGameplayInput)
        {
            if (items && (items.IsAimingThrow || items.HasItem(items.SelectedSlot))) Notify("들고 있는 물건을 내려놓고 건설 모드를 여세요.");
            else SetBuilding(!IsBuilding);
        }
        if (!IsBuilding)
        {
            DrawText(Time.unscaledTime < notificationUntil ? notification : "[B] 농장 자동화");
            return;
        }
        if (!player.CanUseGameplayInput)
        {
            if (preview) preview.gameObject.SetActive(false);
            DrawText("건설 모드 일시 정지 · [B] 나가기");
            return;
        }
        if (keyboard == null) return;
        if (keyboard.zKey.wasPressedThisFrame) selected = (AutomationMachineKind)(((int)selected + 3) % 4);
        if (keyboard.xKey.wasPressedThisFrame) selected = (AutomationMachineKind)(((int)selected + 1) % 4);
        if (keyboard.rKey.wasPressedThisFrame) rotation = (rotation + 1) % 4;
        bool hasHit = Aim(out var hit);
        var target = hasHit ? hit.collider.GetComponentInParent<AutomationBlock>() : null;
        bool ground = hasHit && !target && hit.normal.y >= 0.9f && !hit.collider.attachedRigidbody;
        Vector3 position = ground ? world.Snap(hit.point) : Vector3.zero;
        bool supported = ground && Supported(position, world.BlockSize, out position);
        bool valid = supported && world.CanPlace(position, world.BlockSize);
        UpdatePreview(position, world.BlockSize, valid, ground && !heldLine);
        var mouse = Mouse.current;
        if (keyboard.backspaceKey.wasPressedThisFrame && heldLine)
        {
            if (heldLine.Anchors.Count > 0) heldLine.RemoveLastAnchor(); else CancelLine();
        }
        if (heldLine)
        {
            var camera = ViewCamera();
            if (camera) heldLine.SetLooseEnd(camera.transform.position + camera.transform.forward * 0.5f);
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && ground)
            {
                if (!heldLine.TryPin(hit.point + Vector3.up * 0.04f)) Notify("최대 길이를 넘거나 고정 지점이 너무 가깝습니다.");
            }
            if (keyboard.eKey.wasPressedThisFrame && target)
            {
                if (world.Connect(heldLine, target)) { heldLine = null; Notify("연결했습니다."); }
                else Notify("연결 종류·최대 길이·이미 연결된 입력을 확인하세요.");
            }
        }
        else
        {
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !player.CursorCapturedThisFrame)
            {
                if (valid) world.Place(selected, position, Quaternion.Euler(0, rotation * 90, 0));
                else Notify("평평하고 비어 있는 땅을 선택하세요.");
            }
            if (keyboard.eKey.wasPressedThisFrame && target && target.OutletCount > 0)
            {
                heldLine = world.BeginLine(target);
                if (!heldLine) Notify("남은 코드·호스가 없습니다. [F]로 기존 연결을 회수하세요.");
            }
            if (keyboard.fKey.wasPressedThisFrame && target) { world.Disconnect(target); Notify("이 기계의 연결을 회수했습니다."); }
            if (keyboard.deleteKey.wasPressedThisFrame && target) world.RemoveBlock(target);
        }
        string text = $"농장 자동화 · [B] 나가기\n[Z / X] {Name(selected)}  ·  [R] {rotation * 90}°\n";
        text += heldLine
            ? $"{(heldLine.Source.SupplyKind == AutomationSupplyKind.Water ? "호스" : "코드")} {heldLine.RouteLength(ViewCamera() ? ViewCamera().transform.position : transform.position):0.0} / {heldLine.Source.MaximumLineLength:0}m\n[우클릭] 바닥 고정 · 기계에 [E] 연결\n[Backspace] 마지막 고정 취소 / 줄 회수"
            : "[좌클릭] 설치 · 공급 장치에 [E] 줄 꺼내기\n[F] 연결 회수 · [Delete] 기계 철거";
        if (target)
            text += target.OutletCount > 0 ? $"\n{target.DisplayName} · 연결 {world.UsedOutlets(target)} / {target.OutletCount}" :
                $"\n{target.DisplayName} · 전력 {target.PowerFraction:P0}" +
                (target.Kind == AutomationMachineKind.Sprinkler ? $" · 물 {(target.HasWater ? "연결" : "없음")}" : $" · 속도 {target.TransportSpeed:0.0}m/s");
        if (Time.unscaledTime < notificationUntil) text += "\n" + notification;
        DrawText(text);
    }

    private static string Name(AutomationMachineKind kind) => kind switch
    { AutomationMachineKind.Generator => "발전기", AutomationMachineKind.WaterTank => "물탱크", AutomationMachineKind.Sprinkler => "스프링클러", _ => "작물 이동 레일" };
    private Camera ViewCamera() => player.ViewCamera ? player.ViewCamera.GetComponent<Camera>() : Camera.main;
    private bool Aim(out RaycastHit closest)
    {
        closest = default;
        var camera = ViewCamera(); if (!camera) return false;
        var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        int count;
        while (true)
        {
            count = Physics.RaycastNonAlloc(ray, hits, 6f, ~0, QueryTriggerInteraction.Collide);
            if (count < hits.Length) break;
            Array.Resize(ref hits, hits.Length * 2);
        }
        float distance = float.PositiveInfinity;
        for (int index = 0; index < count; index++)
        {
            var hit = hits[index];
            if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
            if (hit.collider.isTrigger && !hit.collider.GetComponentInParent<AutomationBlock>()) continue;
            distance = hit.distance; closest = hit;
        }
        return distance < float.PositiveInfinity;
    }

    private static bool Supported(Vector3 center, float size, out Vector3 adjusted)
    {
        adjusted = center;
        float height = 0;
        for (int index = 0; index < 5; index++)
        {
            Vector3 point = center;
            if (index > 0) point += new Vector3((index % 2 == 0 ? 1 : -1) * size * 0.43f, 0, (index <= 2 ? 1 : -1) * size * 0.43f);
            if (!Physics.Raycast(point + Vector3.up * 0.4f, Vector3.down, out var hit, 0.8f, ~0, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < 0.9f || hit.collider.attachedRigidbody || hit.collider.GetComponentInParent<AutomationBlock>()) return false;
            if (index == 0) height = hit.point.y;
            else if (Mathf.Abs(hit.point.y - height) > 0.12f) return false;
        }
        adjusted.y = height; return true;
    }

    private void SetBuilding(bool value)
    {
        IsBuilding = value;
        if (preview) preview.gameObject.SetActive(false);
        if (!value) CancelLine();
    }
    private void CancelLine() { if (heldLine && AutomationWorld.Instance) AutomationWorld.Instance.RemoveLine(heldLine); heldLine = null; }
    private void Notify(string message) { notification = message; notificationUntil = Time.unscaledTime + 3f; }

    private void UpdatePreview(Vector3 center, float size, bool valid, bool visible)
    {
        if (!preview)
        {
            var go = new GameObject("AutomationPlacementPreview"); go.transform.SetParent(transform);
            preview = go.AddComponent<LineRenderer>();
            previewMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            preview.sharedMaterial = previewMaterial; preview.startWidth = preview.endWidth = 0.04f;
            preview.positionCount = 5; preview.useWorldSpace = true;
            var font = Resources.Load<TMP_FontAsset>("Fonts/MalgunGothic-Dynamic");
            for (int index = 0; index < previewArrows.Length; index++)
            {
                var arrow = new GameObject("DirectionArrow_" + index); arrow.transform.SetParent(go.transform, false);
                var line = arrow.AddComponent<LineRenderer>();
                line.sharedMaterial = previewMaterial; line.startWidth = line.endWidth = 0.075f;
                line.positionCount = 5; line.useWorldSpace = true;
                line.numCapVertices = 3; line.numCornerVertices = 3;
                previewArrows[index] = line;
                var labelObject = new GameObject("DirectionLabel_" + index);
                labelObject.transform.SetParent(go.transform, false);
                var directionLabel = labelObject.AddComponent<TextMeshPro>();
                if (font) directionLabel.font = font;
                directionLabel.fontSize = 3.2f; directionLabel.enableAutoSizing = false;
                directionLabel.alignment = TextAlignmentOptions.Center;
                directionLabel.rectTransform.sizeDelta = new Vector2(2.4f, 0.6f);
                directionLabel.raycastTarget = false;
                previewLabels[index] = directionLabel;
            }
        }
        preview.gameObject.SetActive(visible);
        if (!visible) return;
        previewMaterial.color = valid ? Color.green : Color.red;
        float half = size * 0.47f; center.y += 0.04f;
        var turn = Quaternion.Euler(0, rotation * 90, 0);
        preview.SetPositions(new[] { center + turn * new Vector3(-half, 0, -half), center + turn * new Vector3(half, 0, -half),
            center + turn * new Vector3(half, 0, half), center + turn * new Vector3(-half, 0, half),
            center + turn * new Vector3(-half, 0, -half) });

        bool sprinkler = selected == AutomationMachineKind.Sprinkler;
        var camera = ViewCamera();
        for (int index = 0; index < previewArrows.Length; index++)
        {
            bool show = index == 0 || sprinkler;
            previewArrows[index].gameObject.SetActive(show);
            previewLabels[index].gameObject.SetActive(show);
            if (!show) continue;
            // Source sockets are on local -Z. Belts move along +Z; spray travels along +/-X.
            Vector3 direction = turn * (sprinkler ? (index == 0 ? Vector3.left : Vector3.right) :
                selected == AutomationMachineKind.Conveyor ? Vector3.forward : Vector3.back);
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            Vector3 arrowCenter = center + Vector3.up * 0.025f;
            Vector3 tail = arrowCenter + direction * (sprinkler ? size * 0.08f : -size * 0.28f);
            Vector3 tip = arrowCenter + direction * size * 0.65f;
            Vector3 wing = tip - direction * size * 0.20f;
            previewArrows[index].SetPositions(new[] { tail, tip, wing + side * size * 0.14f, tip, wing - side * size * 0.14f });
            var directionLabel = previewLabels[index];
            string caption = sprinkler ? (index == 0 ? "좌 · 살수" : "우 · 살수") :
                selected == AutomationMachineKind.Conveyor ? "이동 방향" : "앞면 · 단자";
            if (directionLabel.text != caption) directionLabel.text = caption;
            directionLabel.color = valid ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.4f, 0.4f);
            directionLabel.transform.position = center + direction * size * 0.92f + Vector3.up * 0.22f;
            if (camera) directionLabel.transform.rotation = camera.transform.rotation;
        }
    }

    private void CreateView()
    {
        var root = new GameObject("AutomationHUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 15;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        panel = new GameObject("AutomationHelp", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
        panel.SetParent(root.transform, false); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 1);
        panel.anchoredPosition = new Vector2(-24, -140); panel.sizeDelta = new Vector2(560, 240);
        var background = panel.GetComponent<UnityEngine.UI.Image>(); background.color = new Color(0.03f, 0.06f, 0.07f, 0.87f); background.raycastTarget = false;
        label = new GameObject("Instructions", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        label.rectTransform.SetParent(panel, false); label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(16, 10); label.rectTransform.offsetMax = new Vector2(-16, -10);
        var font = Resources.Load<TMP_FontAsset>("Fonts/MalgunGothic-Dynamic"); if (font) label.font = font;
        label.fontSize = 22; label.color = Color.white; label.raycastTarget = false; label.enableAutoSizing = false;
    }
    private void DrawText(string text)
    {
        if (!label) return;
        canvas.enabled = player.IsLocallyControlled;
        if (label.text != text) label.text = text;
        int lines = 1;
        foreach (char character in text) if (character == '\n') lines++;
        panel.sizeDelta = new Vector2(560, Mathf.Max(64, lines * 30 + 24));
    }

    private void LateUpdate()
    {
        // CharacterController players ride belts without needing Rigidbody conversion.
        if (!AutomationWorld.Offline || !player.IsLocallyControlled || !character || !character.enabled || !character.isGrounded) return;
        Vector3 foot = character.bounds.center - Vector3.up * character.bounds.extents.y;
        if (!Physics.Raycast(foot + Vector3.up * 0.1f, Vector3.down, out var hit, 0.25f, ~0, QueryTriggerInteraction.Ignore)) return;
        var belt = hit.collider.GetComponentInParent<AutomationBlock>();
        if (belt && belt.Kind == AutomationMachineKind.Conveyor && belt.TransportSpeed > 0)
            character.Move(belt.transform.forward * belt.TransportSpeed * Time.deltaTime);
    }
    private void OnDisable() { SetBuilding(false); if (canvas) canvas.enabled = false; }
    private void OnDestroy() { if (previewMaterial) Destroy(previewMaterial); }
}
