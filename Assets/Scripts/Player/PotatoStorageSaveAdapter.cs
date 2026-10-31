using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Adapter only: the existing save controller, sections and file format are unchanged.</summary>
[DefaultExecutionOrder(-250)]
[DisallowMultipleComponent]
public sealed class PotatoStorageSaveAdapter : MonoBehaviour
{
    [SerializeField, Range(1, 2)] private int activeSlot = 1;
    private readonly HashSet<PotatoInventory> bound = new();
    private SaveGameController controller;
    public int ActiveSlot => activeSlot;
    public bool IsReady { get; private set; }
    public string LastError { get; private set; }
    public bool IsOffline => !NetworkManager.Singleton || !NetworkManager.Singleton.IsListening;

    private void Start() => Initialize();

    public bool Initialize()
    {
        if (controller) return IsReady;
        controller = SaveGameController.Instance;
        if (!controller) return Fail("농장 저장 시스템을 찾을 수 없습니다.");
        controller.Saved += OnSlotSaved;
        controller.Loaded += OnSlotLoaded;
        try
        {
            foreach (var basket in FindObjectsByType<PlayerBasketInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (basket.IsLocalPlayer) Register(basket.Inventory);
            foreach (var shelf in FindObjectsByType<WarehouseShelf>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Register(shelf.Inventory);
            if (!IsOffline) return Fail("감자 보관 테스트는 오프라인 세션에서 사용하세요.");
            if (controller.Slots.Exists(activeSlot) && !controller.Load(activeSlot)) return Fail(controller.LastError);
            IsReady = true;
            LastError = null;
            return true;
        }
        catch (Exception error) { return Fail(error.Message); }
    }

    public void Register(PotatoInventory inventory)
    {
        if (!controller || inventory == null || bound.Contains(inventory)) return;
        controller.Inventories.Bind(inventory);
        bound.Add(inventory);
    }

    public void Unregister(PotatoInventory inventory)
    {
        if (!controller || inventory == null || !bound.Remove(inventory)) return;
        controller.Inventories.Unbind(inventory);
    }

    public bool SaveActive()
    {
        if (!IsReady || !controller || !IsOffline) return Fail("감자 저장 연결이 준비되지 않았습니다.");
        if (!controller.Save(activeSlot)) return Fail(controller.LastError, false);
        LastError = null;
        return true;
    }

    public string DescribeWarehouse()
    {
        var text = new System.Text.StringBuilder();
        foreach (var shelf in FindObjectsByType<WarehouseShelf>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            text.AppendLine(shelf.Inventory.Describe(shelf.name));
        return text.ToString();
    }

    [ContextMenu("Print All Warehouse Potatoes")]
    public void PrintWarehouse() => Debug.Log(DescribeWarehouse(), this);

    private bool Fail(string message, bool block = true)
    {
        LastError = message;
        if (block) IsReady = false;
        Debug.LogWarning("[Potato Storage] " + message, this);
        return false;
    }

    private void OnSlotSaved(int slot) => activeSlot = slot;
    private void OnSlotLoaded(int slot) { activeSlot = slot; IsReady = true; LastError = null; }

    private void OnDestroy()
    {
        if (!controller) return;
        controller.Saved -= OnSlotSaved;
        controller.Loaded -= OnSlotLoaded;
        foreach (var inventory in bound) controller.Inventories.Unbind(inventory);
        bound.Clear();
    }
}
