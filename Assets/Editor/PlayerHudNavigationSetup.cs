using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerHudNavigationSetup
{
    private const string HudPath = "Assets/UI/Prefabs/PotatoHUD_Design.prefab";
    private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    [MenuItem("Tools/Project Potato/Connect HUD Compass and Tool Wheel")]
    public static void Connect()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            Configure(root);
            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var scene = SceneManager.GetActiveScene();
        foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))
                     .Where(c => c.name == "PotatoHUD_Design"))
        {
            Configure(canvas.gameObject);
            foreach (var component in canvas.GetComponentsInChildren<Component>(true))
            {
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Player HUD] 나침반 및 이전·현재·다음 도구 UI 연결, 프리팹·씬 저장 완료.");
    }

    private static Sprite SpriteAt(string path, string name) => AssetDatabase.LoadAllAssetsAtPath(path)
        .OfType<Sprite>().Single(s => s.name == name);

    private static T Component<T>(GameObject go) where T : Component
        => go.GetComponent<T>() ? go.GetComponent<T>() : go.AddComponent<T>();

    private static RectTransform Child(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child) return (RectTransform)child;
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        return rect;
    }

    private static void Rect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static TMP_Text Text(Transform parent, string name, string value, Vector2 position, Vector2 size,
        TMP_FontAsset font, float fontSize)
    {
        var rect = Child(parent, name);
        Rect(rect, position, size);
        var text = Component<TextMeshProUGUI>(rect.gameObject);
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    private static void Configure(GameObject hud)
    {
        var compass = hud.transform.Find("Compass");
        var tools = hud.transform.Find("ToolsAndBasket");
        if (!compass || !tools) throw new InvalidOperationException("HUD needs Compass and ToolsAndBasket.");
        var font = hud.GetComponentsInChildren<TMP_Text>(true).Select(t => t.font).FirstOrDefault(f => f);
        if (!font) throw new InvalidOperationException("Reuse an existing HUD TMP font.");
        var toolFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/MalgunGothic-Dynamic.asset");
        if (!toolFont) toolFont = font;
        var artwork = SpriteAt("Assets/UI/compass.png", "compass_0");
        var viewport = (RectTransform)compass.Find("CompassTicksAndDirections");
        var oldImage = viewport.GetComponent<UnityEngine.UI.Image>();
        oldImage.sprite = artwork;
        oldImage.enabled = false;
        oldImage.raycastTarget = false;
        Component<UnityEngine.UI.RectMask2D>(viewport.gameObject);
        // Use the supplied hand-drawn ticks. Its baked repeated north labels are replaced by accurate world bearings.
        var source = artwork.rect;
        var uv = new Rect(source.x / artwork.texture.width,
            (source.y + source.height * 0.35f) / artwork.texture.height,
            source.width / 6f / artwork.texture.width, source.height * 0.65f / artwork.texture.height);
        var ticks = new RectTransform[8];
        for (int i = 0; i < ticks.Length; i++)
        {
            ticks[i] = Child(viewport, "CompassTicks_" + i);
            Rect(ticks[i], Vector2.zero, new Vector2(360f, 25f));
            var image = Component<UnityEngine.UI.RawImage>(ticks[i].gameObject);
            image.texture = artwork.texture;
            image.uvRect = uv;
            image.color = Color.white;
            image.raycastTarget = false;
        }
        var bearings = new RectTransform[24];
        for (int i = 0; i < bearings.Length; i++)
        {
            string label = i % 3 == 0 ? Directions[i / 3] : (i * 15).ToString();
            var text = Text(viewport, "CompassBearing_" + i, label, Vector2.zero, new Vector2(54f, 20f), font, i % 3 == 0 ? 18f : 14f);
            bearings[i] = (RectTransform)text.transform;
        }
        var marker = Child(compass, "HeadingMarker");
        Rect(marker, new Vector2(0f, 35f), new Vector2(2f, 12f));
        var markerImage = Component<UnityEngine.UI.Image>(marker.gameObject);
        markerImage.color = new Color(1f, 0.84f, 0.37f);
        markerImage.raycastTarget = false;
        var heading = Text(compass, "HeadingDegrees", "000", new Vector2(0f, 52f), new Vector2(64f, 20f), font, 17f);

        var iconNames = new[] { "SickleIcon", "SelectedWateringCan", "ScraperIcon" };
        var icons = iconNames.Select(n => tools.Find(n).GetComponent<UnityEngine.UI.Image>()).ToArray();
        var fallback = new TMP_Text[3];
        var names = new TMP_Text[3];
        float[] x = { 32f, 146f, 260f };
        for (int i = 0; i < icons.Length; i++)
        {
            var rect = icons[i].rectTransform;
            // Preserve the current three-slot arrangement and its wheel hints.
            rect.sizeDelta = i == 1 ? new Vector2(70f, 74f) : new Vector2(42f, 46f);
            icons[i].raycastTarget = false;
            icons[i].preserveAspect = true;
            // ToolsAndBasket uses a bottom-left origin; convert to its centered child coordinates.
            Vector2 at = new Vector2(x[i] - 146f, (i == 1 ? 296f : 258f) - 174f);
            fallback[i] = Text(tools, "ToolFallback_" + i, "맨손", at, i == 1 ? new Vector2(80, 46) : new Vector2(54, 34), toolFont, i == 1 ? 20f : 15f);
            names[i] = Text(tools, "ToolName_" + i, "", new Vector2(x[i] - 146f, (i == 1 ? 235f : 222f) - 174f),
                new Vector2(i == 1 ? 150f : 106f, 20f), toolFont, i == 1 ? 14f : 12f);
        }
        var sprites = new Sprite[5];
        sprites[(int)PlayerItemSlot.Hoe] = SpriteAt("Assets/UI/UI_ver2.png", "Icon_Tool_Scraper");
        sprites[(int)PlayerItemSlot.WateringCan] = SpriteAt("Assets/UI/UI_ver2.png", "Icon_Tool_Kettle");
        sprites[(int)PlayerItemSlot.Sickle] = SpriteAt("Assets/UI/UI_ver2.png", "Icon_Tool_Sickle");
        sprites[(int)PlayerItemSlot.PotatoFork] = SpriteAt("Assets/UI/basketFarming_tool.png", "basketFarming_tool_1");
        var driver = Component<PlayerHudNavigation>(hud);
        driver.Configure(artwork, viewport, ticks, bearings, heading, icons, fallback, names, sprites);
        EditorUtility.SetDirty(driver);
    }
}
