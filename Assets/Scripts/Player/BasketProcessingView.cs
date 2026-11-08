using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Disposable prototype made from wooden slats and potato primitives; no art dependency.</summary>
public sealed class BasketProcessingView : MonoBehaviour
{
    private Camera viewCamera;
    private Transform prop;
    private readonly Transform[] potatoRoots = new Transform[PlayerBasketInventory.BasketCapacity];
    private readonly Collider[] hitboxes = new Collider[PlayerBasketInventory.BasketCapacity];
    private readonly List<Material> materials = new();
    private IReadOnlyList<BasketPotato> shown;
    private Canvas canvas;
    private RectTransform tooltip;
    private TMP_Text header, details, tooltipText, message;
    private TMP_FontAsset font;
    private bool open;
    private float presentation, shakePhase;
    private bool shaking;
    private float messageUntil;
    private int hovered = -1;
    private readonly Vector3 frontPosition = new Vector3(0f, -0.30f, 0.78f);
    public Vector3 BasketPosition => prop ? prop.position + Vector3.up * 0.15f : transform.position;

    public void Configure(Camera camera)
    {
        viewCamera = camera;
        font = Resources.Load<TMP_FontAsset>("Fonts/MalgunGothic-Dynamic") ?? TMP_Settings.defaultFontAsset;
        var wood = MakeMaterial("Basket wood", new Color(0.40f, 0.23f, 0.10f));
        var rim = MakeMaterial("Basket rim", new Color(0.67f, 0.42f, 0.19f));
        var strap = MakeMaterial("Basket straps", new Color(0.16f, 0.12f, 0.08f));
        prop = new GameObject("PrototypePotatoBasket").transform;
        prop.SetParent(transform, false);
        MakePart("Base", prop, new Vector3(0f, -0.1f, 0f), new Vector3(0.62f, 0.035f, 0.36f), wood);
        for (int row = 0; row < 4; row++)
        {
            float y = -0.06f + row * 0.055f;
            foreach (float z in new[] { -0.18f, 0.18f })
                MakePart("WovenRail", prop, new Vector3(0f, y, z), new Vector3(0.64f, 0.038f, 0.025f), row == 3 ? rim : wood);
            foreach (float x in new[] { -0.31f, 0.31f })
                MakePart("SideRail", prop, new Vector3(x, y, 0f), new Vector3(0.025f, 0.038f, 0.36f), row == 3 ? rim : wood);
        }
        foreach (float x in new[] { -0.29f, -0.15f, 0f, 0.15f, 0.29f })
            foreach (float z in new[] { -0.185f, 0.185f })
                MakePart("Upright", prop, new Vector3(x, 0.015f, z), new Vector3(0.022f, 0.24f, 0.025f), rim);
        foreach (float x in new[] { -0.18f, 0.18f })
        {
            var belt = MakePart("BackStrap", prop, new Vector3(x, 0.03f, 0.23f), new Vector3(0.04f, 0.40f, 0.035f), strap);
            belt.localEulerAngles = new Vector3(-15f, 0f, 0f);
        }
        DockOnBack();
        BuildCanvas();
    }

    public void SetOpen(bool value)
    {
        open = value;
        if (value)
        {
            prop.SetParent(viewCamera.transform, false);
            prop.localScale = Vector3.one;
            presentation = 0f;
            canvas.gameObject.SetActive(true);
            SetMessage("마우스를 감자 위에 올려 등급과 상태를 확인하세요.");
        }
        else
        {
            canvas.gameObject.SetActive(false);
            SetHover(-1, Vector2.zero);
        }
    }

    private void LateUpdate()
    {
        if (!prop || !viewCamera) return;
        if (open || presentation > 0f)
        {
            presentation = Mathf.MoveTowards(presentation, open ? 1f : 0f, Time.unscaledDeltaTime / 0.28f);
            float t = Mathf.SmoothStep(0f, 1f, presentation);
            Vector3 hidden = frontPosition + new Vector3(0.65f, -0.45f, -0.08f);
            prop.localPosition = Vector3.Lerp(hidden, frontPosition, t);
            float wiggle = shaking ? Mathf.Sin(shakePhase * Mathf.PI * 8f) * Mathf.Sin(shakePhase * Mathf.PI) : 0f;
            prop.localPosition += new Vector3(wiggle * 0.035f, Mathf.Abs(wiggle) * 0.018f, 0f);
            prop.localRotation = Quaternion.Euler(12f, 0f, -wiggle * 9f + (1f - t) * -28f);
            if (!open && presentation == 0f) DockOnBack();
        }
        if (message && Time.unscaledTime > messageUntil) message.text = string.Empty;
    }

    private void DockOnBack()
    {
        prop.SetParent(transform, false);
        prop.localPosition = new Vector3(0f, 1.0f, -0.37f);
        prop.localRotation = Quaternion.identity;
    }

    public void SetShake(float phase, bool active) { shakePhase = phase; shaking = active; }

    public void SetPotatoes(IReadOnlyList<BasketPotato> potatoes)
    {
        shown = potatoes;
        hovered = -1;
        tooltip.gameObject.SetActive(false);
        for (int i = 0; i < potatoRoots.Length; i++)
        {
            if (potatoRoots[i])
            {
                potatoRoots[i].gameObject.SetActive(false);
                Destroy(potatoRoots[i].gameObject);
            }
            hitboxes[i] = null;
            if (i >= potatoes.Count) continue;
            var potato = potatoes[i];
            var root = new GameObject("SelectablePotato_" + potato.Id).transform;
            root.SetParent(prop, false);
            root.localPosition = open ? new Vector3((i - 1) * 0.19f, 0.15f, -0.015f) :
                new Vector3((i % 5 - 2) * 0.11f, 0.08f + (i / 5) * 0.04f, (i / 5 - 0.5f) * 0.14f);
            root.localRotation = Quaternion.Euler(0f, i * 47f, (i - 1) * 12f);
            potatoRoots[i] = root;
            // MaterialPropertyBlock shares one potato material without accumulating one per reroll.
            if (!potatoMaterial) potatoMaterial = MakeMaterial("Prototype potato", new Color(0.69f, 0.48f, 0.23f));
            var body = MakePart("Potato", root, Vector3.zero, new Vector3(0.15f, 0.11f, 0.12f), potatoMaterial, PrimitiveType.Sphere);
            var color = (potato.Conditions & HarvestedPotatoCondition.Green) != 0
                ? new Color(0.39f, 0.51f, 0.16f) : new Color(0.65f + (int)potato.Grade * 0.035f, 0.44f + (int)potato.Grade * 0.025f, 0.22f);
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            body.GetComponent<Renderer>().SetPropertyBlock(properties);
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.17f, 0.14f, 0.15f);
            hitboxes[i] = box;
            root.gameObject.layer = 2;
            if (!eyeMaterial) eyeMaterial = MakeMaterial("Potato eyes", new Color(0.24f, 0.16f, 0.08f));
            for (int eye = 0; eye < 3; eye++)
                MakePart("Eye", root, new Vector3(-0.045f + eye * 0.04f, 0.045f, -0.025f + eye * 0.016f),
                    Vector3.one * 0.009f, eyeMaterial, PrimitiveType.Sphere);
            if ((potato.Conditions & HarvestedPotatoCondition.Sprouted) != 0)
            {
                if (!sproutMaterial) sproutMaterial = MakeMaterial("Potato sprout", new Color(0.29f, 0.48f, 0.12f));
                var sprout = MakePart("Sprout", root, new Vector3(0.02f, 0.075f, 0f), new Vector3(0.012f, 0.055f, 0.012f), sproutMaterial);
                sprout.localEulerAngles = new Vector3(0f, 0f, -20f);
            }
        }
    }
    private Material potatoMaterial, eyeMaterial, sproutMaterial;

    public int Hover(Vector2 screenPosition)
    {
        if (!open || shaking || presentation < 0.99f) return -1;
        Ray ray = viewCamera.ScreenPointToRay(screenPosition);
        for (int i = 0; i < hitboxes.Length; i++)
            if (hitboxes[i] && hitboxes[i].Raycast(ray, out _, 3f)) return i;
        return -1;
    }

    public void SetHover(int index, Vector2 screenPosition)
    {
        hovered = index;
        tooltip.gameObject.SetActive(index >= 0);
        for (int i = 0; i < potatoRoots.Length; i++)
            if (potatoRoots[i]) potatoRoots[i].localScale = Vector3.one * (i == index ? 1.09f : 1f);
        if (index < 0 || shown == null || index >= shown.Count) return;
        var potato = shown[index];
        tooltipText.text = potato.DisplayName + "\n" + (potato.CanProcess ? "<color=#E8CB83>좌클릭 · 믹서기에 1개 투입</color>" : "<color=#F0A48B>가공기에 투입할 수 없음</color>");
        var canvasRect = (RectTransform)canvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out var point);
        tooltip.anchoredPosition = new Vector2(Mathf.Clamp(point.x + 24f, canvasRect.rect.xMin + 10f, canvasRect.rect.xMax - 350f),
            Mathf.Clamp(point.y + 24f, canvasRect.rect.yMin + 100f, canvasRect.rect.yMax - 10f));
    }

    public void SetHeader(int loaded, int capacity, int count, bool opening)
    {
        header.text = $"가공할 감자 선택   <color=#E8CB83>{loaded} / {capacity}</color>";
        details.text = $"바구니 {count}개  ·  " + (opening ? "믹서기 뚜껑을 여는 중" : "좌클릭 투입     [R] 바구니 흔들기     [Esc] 닫기");
    }

    public void SetMessage(string value) { if (!message) return; message.text = value; messageUntil = Time.unscaledTime + 3.5f; }

    private Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name + " (Runtime)", color = color };
        materials.Add(material);
        return material;
    }

    private static Transform MakePart(string name, Transform parent, Vector3 position, Vector3 scale, Material material, PrimitiveType type = PrimitiveType.Cube)
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
        renderer.receiveShadows = false;
        return part.transform;
    }

    private void BuildCanvas()
    {
        var go = new GameObject("BasketSelectionCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var panel = Panel("Instructions", go.transform, new Vector2(820f, 112f));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 38f);
        header = Label("Title", panel, new Vector2(0f, 75f), new Vector2(790f, 40f), 27f);
        details = Label("Controls", panel, new Vector2(0f, 36f), new Vector2(790f, 34f), 22f);
        message = Label("Feedback", go.transform, new Vector2(0f, 175f), new Vector2(1000f, 45f), 23f);
        tooltip = Panel("PotatoTooltip", go.transform, new Vector2(340f, 92f));
        tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
        tooltip.pivot = new Vector2(0f, 1f);
        tooltipText = Label("Description", tooltip, new Vector2(0f, 46f), new Vector2(312f, 78f), 22f);
        tooltipText.alignment = TextAlignmentOptions.MidlineLeft;
        tooltip.gameObject.SetActive(false);
        go.SetActive(false);
    }

    private static RectTransform Panel(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.10f, 0.075f, 0.045f, 0.94f);
        image.raycastTarget = false;
        return rect;
    }

    private TMP_Text Label(string name, Transform parent, Vector2 position, Vector2 size, float textSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = textSize;
        text.color = new Color(0.97f, 0.93f, 0.83f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private void OnDestroy()
    {
        if (prop) Destroy(prop.gameObject);
        if (canvas) Destroy(canvas.gameObject);
        foreach (var material in materials) if (material) Destroy(material);
    }
}
