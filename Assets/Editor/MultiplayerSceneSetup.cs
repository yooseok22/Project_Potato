using System.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Initial authoring utility. Refuses to overwrite scenes that have already been authored.
public static class MultiplayerSceneSetup
{
    public const string MenuPath = "Assets/Scenes/MultiplayerMenu.unity";
    public const string GamePath = "Assets/Scenes/MultiplayerGame.unity";
    private static readonly Color Ink = new Color(0.08f, 0.12f, 0.10f);
    private static readonly Color Cream = new Color(0.94f, 0.93f, 0.85f);

    [MenuItem("Tools/Project Potato/Create Multiplayer Scenes")]
    public static void CreateScenes()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuPath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(GamePath))
            throw new System.InvalidOperationException("Multiplayer scenes already exist; edit them directly to preserve references.");
        var game = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
        SceneManager.SetActiveScene(game);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0, -0.25f, 0);
        floor.transform.localScale = new Vector3(20, 0.5f, 20);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = new Color(0.40f, 0.49f, 0.36f);
        AssetDatabase.CreateAsset(material, "Assets/Scenes/MultiplayerFloor.mat");
        floor.GetComponent<Renderer>().sharedMaterial = material;
        var spawns = new GameObject("SpawnPoints").AddComponent<MultiplayerSpawnPoints>();
        var spawnSettings = new SerializedObject(spawns);
        var points = spawnSettings.FindProperty("points");
        points.arraySize = 4;
        var positions = new[] { new Vector3(-3, 0.1f, -3), new Vector3(3, 0.1f, -3), new Vector3(-3, 0.1f, 3), new Vector3(3, 0.1f, 3) };
        for (int i = 0; i < 4; i++)
        {
            var point = new GameObject("SpawnPoint_" + (i + 1)).transform;
            point.SetParent(spawns.transform);
            point.position = positions[i];
            point.rotation = Quaternion.LookRotation(new Vector3(-point.position.x, 0, -point.position.z));
            points.GetArrayElementAtIndex(i).objectReferenceValue = point;
        }
        spawnSettings.ApplyModifiedPropertiesWithoutUndo();
        var camera = game.GetRootGameObjects().Select(x => x.GetComponent<Camera>()).First(x => x);
        camera.transform.position = new Vector3(0, 15, -15);
        camera.transform.LookAt(Vector3.zero);
        EditorSceneManager.SaveScene(game, GamePath);
        EditorSceneManager.CloseScene(game, true);

        var menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(menu);
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
        cameraObject.GetComponent<Camera>().backgroundColor = Ink;
        var root = new GameObject("MultiplayerRoot");
        var transport = root.AddComponent<UnityTransport>();
        var network = root.AddComponent<NetworkManager>();
        network.NetworkConfig.NetworkTransport = transport;
        network.NetworkConfig.EnableSceneManagement = true;
        network.NetworkConfig.PlayerPrefab = null;
        var controller = root.AddComponent<MultiplayerSessionController>();
        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        events.transform.SetParent(root.transform);
        events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        var canvasObject = new GameObject("ConnectionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(root.transform);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        var view = canvasObject.AddComponent<MultiplayerConnectionView>();
        var background = Rect("MenuPanel", canvasObject.transform);
        Stretch(background);
        background.gameObject.AddComponent<Image>().color = Ink;
        var card = Rect("ConnectionCard", background);
        card.sizeDelta = new Vector2(480, 420);
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 16;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        Label("Brand", card, "PROJECT POTATO", 18, 28, Cream);
        Label("Title", card, "PLAY TOGETHER", 38, 58, Color.white);
        Label("Hint", card, "Host a game or join your friends. Up to 4 players.", 17, 34, Cream);
        var create = Button("CreateSessionButton", card, "CREATE SESSION");
        var inputRect = Rect("SessionCodeInput", card);
        Height(inputRect, 54);
        inputRect.gameObject.AddComponent<Image>().color = Cream;
        var input = inputRect.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = Rect("TextArea", inputRect);
        Stretch(input.textViewport, 18, 8);
        input.textViewport.gameObject.AddComponent<RectMask2D>();
        var inputText = Label("Text", input.textViewport, "", 22, 40, Ink);
        Stretch(inputText.rectTransform);
        input.textComponent = inputText;
        var placeholder = Label("Placeholder", input.textViewport, "Enter session code", 20, 40, new Color(0.35f, 0.40f, 0.35f));
        Stretch(placeholder.rectTransform);
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 16;
        input.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
        var join = Button("JoinSessionButton", card, "JOIN SESSION");
        join.interactable = false;

        var session = Rect("SessionPanel", canvasObject.transform);
        session.anchorMin = session.anchorMax = new Vector2(0, 1);
        session.pivot = new Vector2(0, 1);
        session.anchoredPosition = new Vector2(24, -24);
        session.sizeDelta = new Vector2(400, 176);
        session.gameObject.AddComponent<Image>().color = Ink;
        var sessionLayout = session.gameObject.AddComponent<VerticalLayoutGroup>();
        sessionLayout.padding = new RectOffset(16, 16, 12, 12);
        sessionLayout.spacing = 8;
        sessionLayout.childControlWidth = sessionLayout.childControlHeight = true;
        sessionLayout.childForceExpandWidth = true;
        sessionLayout.childForceExpandHeight = false;
        var code = Label("SessionCode", session, "SESSION CODE", 24, 36, Cream);
        var copy = Button("CopyCodeButton", session, "COPY CODE", 40);
        var leave = Button("LeaveSessionButton", session, "LEAVE SESSION", 40);
        session.gameObject.SetActive(false);
        var status = Label("Status", canvasObject.transform, "Create a session or enter a code to join.", 17, 50, Cream);
        status.rectTransform.anchorMin = new Vector2(0, 0);
        status.rectTransform.anchorMax = new Vector2(1, 0);
        status.rectTransform.pivot = new Vector2(0.5f, 0);
        status.rectTransform.anchoredPosition = new Vector2(0, 18);
        status.rectTransform.sizeDelta = new Vector2(-48, 60);
        status.alignment = TextAlignmentOptions.Center;
        var serialized = new SerializedObject(view);
        Set(serialized, "controller", controller);
        Set(serialized, "menuPanel", background.gameObject);
        Set(serialized, "sessionPanel", session.gameObject);
        Set(serialized, "codeInput", input);
        Set(serialized, "createButton", create);
        Set(serialized, "joinButton", join);
        Set(serialized, "copyButton", copy);
        Set(serialized, "leaveButton", leave);
        Set(serialized, "codeLabel", code);
        Set(serialized, "statusLabel", status);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(menu, MenuPath);
        var existing = EditorBuildSettings.scenes.Where(x => x.path != MenuPath && x.path != GamePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuPath, true), new EditorBuildSettingsScene(GamePath, true) }.Concat(existing).ToArray();
        AssetDatabase.SaveAssets();
    }

    private static void Set(SerializedObject target, string field, Object value) => target.FindProperty(field).objectReferenceValue = value;
    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        return rect;
    }
    private static void Stretch(RectTransform rect, float x = 0, float y = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(x, y);
        rect.offsetMax = new Vector2(-x, -y);
    }
    private static void Height(RectTransform rect, float height)
    {
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        rect.sizeDelta = new Vector2(0, height);
    }
    private static TextMeshProUGUI Label(string name, Transform parent, string value, float size, float height, Color color)
    {
        var rect = Rect(name, parent);
        Height(rect, height);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }
    private static Button Button(string name, Transform parent, string value, float height = 54)
    {
        var rect = Rect(name, parent);
        Height(rect, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.70f, 0.82f, 0.39f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.disabledColor = new Color(0.4f, 0.43f, 0.38f, 0.65f);
        button.colors = colors;
        var label = Label("Label", rect, value, 19, height, Ink);
        Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
