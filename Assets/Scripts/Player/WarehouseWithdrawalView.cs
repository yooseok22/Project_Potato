using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Physical potatoes on the selected shelf face, with hover details and a flight to the backpack.</summary>
public sealed class WarehouseWithdrawalView : MonoBehaviour
{
    private sealed class PotatoVisual
    {
        public BasketPotato Potato;
        public Transform Root;
        public Collider Hitbox;
    }

    private sealed class Flight
    {
        public Transform Root;
        public Vector3 From, To;
        public float Progress;
    }

    private readonly Dictionary<long, PotatoVisual> potatoes = new();
    private readonly List<Flight> flights = new();
    private readonly List<Material> materials = new();
    private Camera viewCamera;
    private Transform shelfRoot;
    private Canvas canvas;
    private RectTransform tooltip;
    private TMP_Text header, tooltipText, message;
    private TMP_FontAsset font;
    private Material potatoMaterial, darkMaterial, sproutMaterial;
    private long hovered;
    private bool open;
    private float messageUntil;

    public void Configure(Camera camera)
    {
        viewCamera = camera;
        if (canvas) return;
        font = Resources.Load<TMP_FontAsset>("Fonts/MalgunGothic-Dynamic") ?? TMP_Settings.defaultFontAsset;
        potatoMaterial = MakeMaterial("Warehouse potato", new Color(0.73f, 0.51f, 0.26f));
        darkMaterial = MakeMaterial("Warehouse potato eyes", new Color(0.21f, 0.13f, 0.06f));
        sproutMaterial = MakeMaterial("Warehouse potato sprout", new Color(0.31f, 0.51f, 0.13f));
        BuildCanvas();
    }

    public void Open(WarehouseShelf shelf, int basketCount)
    {
        ClearShelf();
        open = true;
        var renderer = shelf.GetComponent<Renderer>();
        var bounds = renderer ? renderer.bounds : new Bounds(shelf.transform.position, Vector3.one * 2f);
        Vector3 offset = viewCamera.transform.position - bounds.center;
        Vector3 normal = Mathf.Abs(offset.x) > Mathf.Abs(offset.z)
            ? new Vector3(Mathf.Sign(offset.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(offset.z));
        Vector3 forward = -normal;
        Vector3 center = bounds.center + Vector3.Scale(normal, bounds.extents + Vector3.one * 0.06f);
        center += Vector3.up * bounds.size.y * 0.22f;
        float width = Mathf.Min(1.8f, (normal.x == 0f ? bounds.size.x : bounds.size.z) * 0.9f);
        float height = Mathf.Min(1f, bounds.size.y * 0.5f);
        // Fit all 50 potatoes for both wide and tall game views.
        float halfFov = viewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f;
        float distance = Mathf.Max(height * 0.5f / Mathf.Tan(halfFov),
            width * 0.5f / (Mathf.Tan(halfFov) * Mathf.Max(0.1f, viewCamera.aspect))) * 1.45f;
        distance = Mathf.Max(1.7f, distance);
        viewCamera.transform.SetPositionAndRotation(center + normal * distance, Quaternion.LookRotation(forward, Vector3.up));
        shelfRoot = new GameObject("WarehouseSelectablePotatoes").transform;
        shelfRoot.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));
        for (int i = 0; i < shelf.Inventory.Potatoes.Count; i++)
        {
            var potato = shelf.Inventory.Potatoes[i];
            var root = new GameObject("StoredPotato_" + potato.Id).transform;
            root.SetParent(shelfRoot, false);
            root.localPosition = new Vector3((i % 10 - 4.5f) * width / 10f, (2f - i / 10) * height / 5f, 0f);
            root.localRotation = Quaternion.Euler(0f, i * 47f, i % 3 * 13f);
            root.gameObject.layer = 2;
            var body = Part("Potato", root, Vector3.zero, new Vector3(0.15f, 0.12f, 0.12f), potatoMaterial, PrimitiveType.Sphere);
            var properties = new MaterialPropertyBlock();
            Color color = (potato.Conditions & HarvestedPotatoCondition.Green) != 0 ? new Color(0.36f, 0.51f, 0.16f) :
                (potato.Conditions & HarvestedPotatoCondition.Rotten) != 0 ? new Color(0.24f, 0.18f, 0.10f) :
                new Color(0.65f + (int)potato.Grade * 0.035f, 0.44f + (int)potato.Grade * 0.025f, 0.22f);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            body.GetComponent<Renderer>().SetPropertyBlock(properties);
            for (int eye = 0; eye < 3; eye++)
                Part("Eye", root, new Vector3(-0.04f + eye * 0.035f, 0.035f, -0.043f), Vector3.one * 0.012f, darkMaterial, PrimitiveType.Sphere);
            if ((potato.Conditions & HarvestedPotatoCondition.Sprouted) != 0)
                Part("Sprout", root, new Vector3(0.025f, 0.075f, 0f), new Vector3(0.015f, 0.06f, 0.015f), sproutMaterial);
            if ((potato.Conditions & (HarvestedPotatoCondition.BugEaten | HarvestedPotatoCondition.Injured)) != 0)
                Part("DamagedPatch", root, new Vector3(0f, 0f, -0.055f), new Vector3(0.05f, 0.025f, 0.012f), darkMaterial, PrimitiveType.Sphere);
            var hitbox = root.gameObject.AddComponent<BoxCollider>();
            hitbox.isTrigger = true;
            hitbox.size = new Vector3(0.16f, 0.15f, 0.15f);
            potatoes.Add(potato.Id, new PotatoVisual { Potato = potato, Root = root, Hitbox = hitbox });
        }
        Physics.SyncTransforms();
        canvas.gameObject.SetActive(true);
        SetHeader(shelf, basketCount);
        SetMessage("감자에 마우스를 올려 등급과 상태를 확인하고, 클릭해서 꺼내세요.");
    }

    public long Hover(Vector2 screenPosition)
    {
        if (!open || !viewCamera) return 0;
        var ray = viewCamera.ScreenPointToRay(screenPosition);
        long id = 0;
        float closest = float.PositiveInfinity;
        foreach (var pair in potatoes)
            if (pair.Value.Hitbox.Raycast(ray, out var hit, 10f) && hit.distance < closest)
            { id = pair.Key; closest = hit.distance; }
        return id;
    }

    public void SetHover(long id, Vector2 position)
    {
        if (hovered != id)
        {
            if (potatoes.TryGetValue(hovered, out var old)) old.Root.localScale = Vector3.one;
            hovered = id;
            if (potatoes.TryGetValue(id, out var current))
            {
                current.Root.localScale = Vector3.one * 1.12f;
                tooltipText.text = current.Potato.DisplayName + "\n<color=#E8CB83>좌클릭 · 바구니에 1개 담기</color>";
            }
        }
        tooltip.gameObject.SetActive(id != 0 && potatoes.ContainsKey(id));
        if (id == 0) return;
        var rect = (RectTransform)canvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, position, null, out var point);
        tooltip.anchoredPosition = new Vector2(Mathf.Clamp(point.x + 22f, rect.rect.xMin + 8f, rect.rect.xMax - tooltip.rect.width - 8f),
            Mathf.Clamp(point.y + 22f, rect.rect.yMin + tooltip.rect.height + 8f, rect.rect.yMax - 8f));
    }

    public void AnimateTransfer(long id, Vector3 basketPosition)
    {
        if (!potatoes.Remove(id, out var visual)) return;
        visual.Hitbox.enabled = false;
        visual.Root.SetParent(null, true);
        visual.Root.localScale = Vector3.one;
        flights.Add(new Flight { Root = visual.Root, From = visual.Root.position, To = basketPosition });
        SetHover(0, Vector2.zero);
    }

    private void LateUpdate()
    {
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            var flight = flights[i];
            flight.Progress = Mathf.Min(1f, flight.Progress + Time.unscaledDeltaTime / 0.4f);
            float t = Mathf.SmoothStep(0f, 1f, flight.Progress);
            flight.Root.position = Vector3.Lerp(flight.From, flight.To, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.45f);
            flight.Root.Rotate(0f, 240f * Time.unscaledDeltaTime, 0f);
            if (flight.Progress < 1f) continue;
            Destroy(flight.Root.gameObject);
            flights.RemoveAt(i);
        }
        if (message && Time.unscaledTime > messageUntil) message.text = string.Empty;
    }

    public void SetHeader(WarehouseShelf shelf, int basketCount) =>
        header.text = $"{shelf.DisplayName}  {shelf.Inventory.Count}/{WarehouseShelf.ShelfCapacity}     바구니 <color=#E8CB83>{basketCount}/{PlayerBasketInventory.BasketCapacity}</color>";

    public void SetMessage(string text) { message.text = text; messageUntil = Time.unscaledTime + 4f; }

    public void Hide()
    {
        open = false;
        if (canvas) canvas.gameObject.SetActive(false);
        ClearShelf();
    }

    private void ClearShelf()
    {
        hovered = 0;
        potatoes.Clear();
        if (tooltip) tooltip.gameObject.SetActive(false);
        if (!shelfRoot) return;
        shelfRoot.gameObject.SetActive(false);
        Destroy(shelfRoot.gameObject);
        shelfRoot = null;
    }

    private Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name + " (Runtime)", color = color };
        materials.Add(material);
        return material;
    }

    private static Transform Part(string name, Transform parent, Vector3 position, Vector3 scale, Material material, PrimitiveType type = PrimitiveType.Cube)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.layer = 2;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        var collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return part.transform;
    }

    private void BuildCanvas()
    {
        var go = new GameObject("WarehouseWithdrawalCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var panel = Panel("Instructions", go.transform, new Vector2(960f, 112f));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 38f);
        header = Label("Title", panel, new Vector2(0f, 76f), new Vector2(930f, 40f), 27f);
        Label("Controls", panel, new Vector2(0f, 36f), new Vector2(930f, 32f), 22f).text = "좌클릭 · 감자 꺼내기     10개가 차면 자동 종료     [Esc] 닫기";
        message = Label("Feedback", go.transform, new Vector2(0f, 176f), new Vector2(1200f, 44f), 23f);
        tooltip = Panel("PotatoTooltip", go.transform, new Vector2(510f, 104f));
        tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
        tooltip.pivot = new Vector2(0f, 1f);
        tooltipText = Label("Description", tooltip, new Vector2(0f, 52f), new Vector2(480f, 90f), 22f);
        tooltipText.alignment = TextAlignmentOptions.MidlineLeft;
        tooltipText.textWrappingMode = TextWrappingModes.Normal;
        go.SetActive(false);
    }

    private static RectTransform Panel(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.1f, 0.075f, 0.045f, 0.94f);
        image.raycastTarget = false;
        return rect;
    }

    private TMP_Text Label(string name, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.color = new Color(0.97f, 0.93f, 0.83f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private void OnDestroy()
    {
        if (shelfRoot) Destroy(shelfRoot.gameObject);
        if (canvas) Destroy(canvas.gameObject);
        foreach (var flight in flights) if (flight.Root) Destroy(flight.Root.gameObject);
        foreach (var material in materials) if (material) Destroy(material);
    }
}
