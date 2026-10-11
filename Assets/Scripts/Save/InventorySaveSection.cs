using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable] public sealed class InventoryItemData
{
    public string itemId;
    public int quantity;
    public string instanceId;
    public int slot = -1;
    public int propertiesVersion = 1;
    public string propertiesJson;
}

[Serializable] public sealed class InventoryData
{
    public string ownerId;
    public List<InventoryItemData> items = new();
}

/// <summary>The player team supplies this adapter. Validate must not mutate; Restore replaces, never appends.</summary>
public interface IInventorySaveTarget
{
    string OwnerId { get; }
    InventoryData CaptureInventory();
    void ValidateInventory(InventoryData data);
    void RestoreInventory(InventoryData data);
}

/// <summary>Unbound inventories remain in memory and survive save/load until their player adapter binds.</summary>
public sealed class InventorySaveSection : ISaveSection
{
    [Serializable] private sealed class Data { public List<InventoryData> inventories = new(); }
    private Data state = new();
    private readonly Dictionary<string, IInventorySaveTarget> targets = new();
    public string Key => "inventory";
    public int Version => 1;

    public void Bind(IInventorySaveTarget target)
    {
        if (target == null || string.IsNullOrWhiteSpace(target.OwnerId)) throw new ArgumentException("Stable owner ID required.");
        if (targets.ContainsKey(target.OwnerId)) throw new ArgumentException("Duplicate inventory owner.");
        var saved = state.inventories.Find(value => value.ownerId == target.OwnerId);
        if (saved != null)
        {
            var copy = Clone(saved);
            target.ValidateInventory(copy);
            target.RestoreInventory(copy);
        }
        targets.Add(target.OwnerId, target);
    }

    public void Unbind(IInventorySaveTarget target)
    {
        if (target == null || !targets.TryGetValue(target.OwnerId, out var current) || !ReferenceEquals(current, target)) return;
        var data = CaptureTarget(target);
        Put(state, data);
        targets.Remove(target.OwnerId);
    }

    public void SetUnboundInventory(InventoryData data)
    {
        Validate(data);
        if (targets.ContainsKey(data.ownerId)) throw new InvalidOperationException("Use the bound player's inventory API.");
        Put(state, Clone(data));
    }

    public InventoryData GetInventory(string ownerId)
    {
        if (targets.TryGetValue(ownerId, out var target)) return CaptureTarget(target);
        var data = state.inventories.Find(value => value.ownerId == ownerId);
        return data == null ? null : Clone(data);
    }

    public string Capture()
    {
        var snapshot = JsonUtility.FromJson<Data>(JsonUtility.ToJson(state));
        foreach (var target in targets.Values) Put(snapshot, CaptureTarget(target));
        return JsonUtility.ToJson(snapshot);
    }

    public Action PrepareRestore(int version, string json)
    {
        if (version != Version) throw new InvalidDataException("Unsupported inventory version.");
        var data = JsonUtility.FromJson<Data>(json);
        if (data?.inventories == null) throw new InvalidDataException("Missing inventories.");
        var owners = new HashSet<string>();
        foreach (var inventory in data.inventories)
        {
            Validate(inventory);
            if (!owners.Add(inventory.ownerId)) throw new InvalidDataException("Duplicate inventory owner.");
        }
        // Absent bound players receive an empty replacement, so previous-slot items cannot leak.
        var replacements = new List<(IInventorySaveTarget target, InventoryData data)>();
        foreach (var target in targets.Values)
        {
            var replacement = Clone(data.inventories.Find(value => value.ownerId == target.OwnerId)
                ?? new InventoryData { ownerId = target.OwnerId });
            target.ValidateInventory(replacement);
            replacements.Add((target, replacement));
        }
        return () =>
        {
            foreach (var replacement in replacements) replacement.target.RestoreInventory(Clone(replacement.data));
            state = JsonUtility.FromJson<Data>(JsonUtility.ToJson(data));
        };
    }

    private static InventoryData CaptureTarget(IInventorySaveTarget target)
    {
        var data = target.CaptureInventory();
        Validate(data);
        if (data.ownerId != target.OwnerId) throw new InvalidDataException("Inventory owner mismatch.");
        return Clone(data);
    }

    private static void Put(Data data, InventoryData inventory)
    {
        data.inventories.RemoveAll(value => value.ownerId == inventory.ownerId);
        data.inventories.Add(inventory);
    }

    private static InventoryData Clone(InventoryData value) => JsonUtility.FromJson<InventoryData>(JsonUtility.ToJson(value));

    private static void Validate(InventoryData data)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.ownerId) || data.items == null) throw new InvalidDataException("Invalid inventory.");
        var slots = new HashSet<int>();
        var instances = new HashSet<string>();
        foreach (var item in data.items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.quantity <= 0 || item.slot < -1 || item.propertiesVersion < 1
                || item.slot >= 0 && !slots.Add(item.slot)
                || !string.IsNullOrEmpty(item.instanceId) && (!instances.Add(item.instanceId) || item.quantity != 1))
                throw new InvalidDataException("Invalid inventory item, slot or instance ID.");
        }
    }
}
