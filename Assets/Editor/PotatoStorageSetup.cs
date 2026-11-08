using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoStorageSetup
{
    [MenuItem("Tools/Project Potato/Connect Basket and Warehouse")]
    public static void ConnectScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var warehouses = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "FBX_Warehouse").ToArray();
        if (warehouses.Length == 0) throw new InvalidOperationException("The scene needs FBX_Warehouse.");
        foreach (var root in warehouses)
        {
            var area = root.GetComponent<WarehouseStorageArea>();
            if (!area) area = Undo.AddComponent<WarehouseStorageArea>(root.gameObject);
            var mesh = root.GetComponent<MeshFilter>().sharedMesh;
            var interior = mesh.bounds;
            interior.size -= new Vector3(0.2f, 0f, 0.2f);
            Undo.RecordObject(area, "Configure warehouse interior");
            area.Configure(string.IsNullOrEmpty(area.WarehouseId) ? Guid.NewGuid().ToString("N") : area.WarehouseId, interior);
            Record(area);
            // A static mesh collider preserves the actual door opening and walkable floor.
            var buildingCollider = root.GetComponent<MeshCollider>();
            if (!buildingCollider) buildingCollider = Undo.AddComponent<MeshCollider>(root.gameObject);
            buildingCollider.sharedMesh = mesh;
            Record(buildingCollider);
            foreach (var child in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("FBX_Warehouse shelf ", StringComparison.Ordinal)))
            {
                var shelf = child.GetComponent<WarehouseShelf>();
                if (!shelf) shelf = Undo.AddComponent<WarehouseShelf>(child.gameObject);
                Undo.RecordObject(shelf, "Configure warehouse shelf");
                shelf.Configure(area, string.IsNullOrEmpty(shelf.ShelfId) ? child.name.Substring("FBX_Warehouse shelf ".Length) : shelf.ShelfId);
                Record(shelf);
                // Imported shelves on one side are mirrored. Cancel reflection on a child
                // hitbox so PhysX receives a positive scale while the artwork stays intact.
                var previous = child.GetComponent<BoxCollider>();
                if (previous) Undo.DestroyObjectImmediate(previous);
                var hitbox = child.Find("StorageHitbox");
                if (!hitbox)
                {
                    var obj = new GameObject("StorageHitbox");
                    Undo.RegisterCreatedObjectUndo(obj, "Create shelf hitbox");
                    hitbox = obj.transform;
                    hitbox.SetParent(child, false);
                }
                var bounds = child.GetComponent<MeshFilter>().sharedMesh.bounds;
                var scale = child.lossyScale;
                hitbox.localPosition = bounds.center;
                hitbox.localRotation = Quaternion.identity;
                hitbox.localScale = new Vector3(Mathf.Sign(scale.x), Mathf.Sign(scale.y), Mathf.Sign(scale.z));
                var collider = hitbox.GetComponent<BoxCollider>();
                if (!collider) collider = Undo.AddComponent<BoxCollider>(hitbox.gameObject);
                collider.center = Vector3.zero;
                collider.size = bounds.size;
                Record(collider);
            }
        }
        const string playerPath = "Assets/Prefabs/Player.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            bool changed = false;
            if (!prefab.GetComponent<PlayerBasketInventory>())
            {
                prefab.AddComponent<PlayerBasketInventory>();
                changed = true;
            }
            if (!prefab.GetComponent<WarehouseWithdrawalController>()) { prefab.AddComponent<WarehouseWithdrawalController>(); changed = true; }
            if (changed) PrefabUtility.SaveAsPrefabAsset(prefab, playerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var player in roots.SelectMany(r => r.GetComponentsInChildren<FirstPersonPlayerController>(true)))
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player) != playerPath)
            {
                if (!player.GetComponent<PlayerBasketInventory>()) Undo.AddComponent<PlayerBasketInventory>(player.gameObject);
                if (!player.GetComponent<WarehouseWithdrawalController>()) Undo.AddComponent<WarehouseWithdrawalController>(player.gameObject);
            }
        if (!roots.SelectMany(r => r.GetComponentsInChildren<PotatoStorageSaveAdapter>(true)).Any())
        {
            var service = new GameObject("PotatoStorage");
            SceneManager.MoveGameObjectToScene(service, scene);
            Undo.RegisterCreatedObjectUndo(service, "Create potato storage adapter");
            Undo.AddComponent<PotatoStorageSaveAdapter>(service);
        }
        var canvas = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).First(c => c.name == "PotatoHUD_Design");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/MalgunGothic-Dynamic.asset");
        var panel = canvas.transform.Find("ShelfStoragePrompt") as RectTransform;
        if (!panel)
        {
            var obj = new GameObject("ShelfStoragePrompt", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            Undo.RegisterCreatedObjectUndo(obj, "Create shelf prompt");
            panel = obj.GetComponent<RectTransform>();
            panel.SetParent(canvas.transform, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(0f, -110f);
            panel.sizeDelta = new Vector2(520f, 92f);
            var image = obj.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.055f, 0.07f, 0.045f, 0.92f);
            image.raycastTarget = false;
        }
        var label = panel.GetComponentInChildren<TextMeshProUGUI>(true);
        if (!label)
        {
            label = CreateText(panel, "ShelfCount", font, 25f);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(12f, 8f);
            label.rectTransform.offsetMax = new Vector2(-12f, -8f);
        }
        var tools = canvas.transform.Find("ToolsAndBasket");
        var basket = tools.Find("BasketCount")?.GetComponent<TextMeshProUGUI>();
        if (!basket)
        {
            basket = CreateText(tools, "BasketCount", font, 19f);
        }
        basket.rectTransform.anchorMin = basket.rectTransform.anchorMax = Vector2.zero;
        basket.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        basket.rectTransform.anchoredPosition = new Vector2(146f, 20f);
        basket.rectTransform.sizeDelta = new Vector2(200f, 30f);
        basket.text = "[I] 바구니  0/10";
        var shadow = basket.GetComponent<UnityEngine.UI.Shadow>();
        if (!shadow) shadow = Undo.AddComponent<UnityEngine.UI.Shadow>(basket.gameObject);
        shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        var oldKey = tools.Find("BasketKey_B");
        if (oldKey)
        {
            Undo.RecordObject(oldKey.gameObject, "Update basket key hint");
            oldKey.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldKey.gameObject);
        }
        var view = canvas.GetComponent<ShelfHoverView>();
        if (!view) view = Undo.AddComponent<ShelfHoverView>(canvas.gameObject);
        Undo.RecordObject(view, "Connect shelf UI");
        view.Configure(panel.gameObject, label, basket);
        Record(view);
        panel.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Potato Storage] 바구니, 창고 선반, 수량 UI와 저장 어댑터 연결 및 씬 저장 완료.");
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, TMP_FontAsset font, float size)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        Undo.RegisterCreatedObjectUndo(obj, "Create storage label");
        obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = new Color(1f, 0.95f, 0.8f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private static void Record(Component component)
    {
        EditorUtility.SetDirty(component);
        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }
}
