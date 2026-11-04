using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>A runtime identity for one potato, even when its properties match another potato.</summary>
public sealed class BasketPotato
{
    private static long nextId;
    public long Id { get; } = System.Threading.Interlocked.Increment(ref nextId);
    public PotatoHarvestGrade Grade { get; }
    public HarvestedPotatoCondition Conditions { get; }
    public bool CanProcess => Conditions == HarvestedPotatoCondition.None &&
        Grade >= PotatoHarvestGrade.OneStar && Grade <= PotatoHarvestGrade.ThreeStar;
    public string DisplayName => new PotatoStack { grade = Grade, conditions = Conditions }.DisplayName;
    public BasketPotato(PotatoHarvestGrade grade, HarvestedPotatoCondition conditions)
    { Grade = grade; Conditions = conditions; }
}

[Serializable]
public sealed class PotatoStack
{
    public PotatoHarvestGrade grade;
    public HarvestedPotatoCondition conditions;
    public int quantity;

    public string DisplayName => conditions != HarvestedPotatoCondition.None
        ? $"상태이상 감자 ({PotatoHarvestConditionRules.Describe(conditions)})"
        : grade == PotatoHarvestGrade.None ? "일반 감자" : $"{(int)grade}성 감자";
}

/// <summary>Capacity counts individual potatoes, not stacks. Implements the existing save contract.</summary>
public sealed class PotatoInventory : IInventorySaveTarget
{
    [Serializable] private sealed class Properties
    {
        public PotatoHarvestGrade grade;
        public HarvestedPotatoCondition conditions;
    }

    private const HarvestedPotatoCondition AllowedConditions = PotatoHarvestConditionRules.Harvestable | HarvestedPotatoCondition.Rotten;
    private readonly List<PotatoStack> stacks = new();
    private readonly List<BasketPotato> potatoes = new();
    public IReadOnlyList<BasketPotato> Potatoes => potatoes.AsReadOnly();
    public int Revision { get; private set; }
    public string OwnerId { get; }
    public int Capacity { get; }
    public int Count { get; private set; }
    public int Free => Capacity - Count;

    public PotatoInventory(string ownerId, int capacity)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || capacity <= 0) throw new ArgumentException("Stable owner ID and positive capacity required.");
        OwnerId = ownerId;
        Capacity = capacity;
    }

    public bool TryAdd(PotatoHarvestGrade grade, HarvestedPotatoCondition conditions, int quantity)
    {
        ValidateProperties(grade, conditions);
        if (quantity <= 0 || quantity > Free) return false;
        AddUnchecked(grade, conditions, quantity);
        return true;
    }

    public bool TryAddHarvest(PotatoHarvestResult result)
    {
        if (result.Count <= 0 || result.Count > Free || result.ItemConditions == null || result.ItemConditions.Length != result.Count) return false;
        for (int i = 0; i < result.Count; i++) ValidateProperties(result.Grades[i], result.ItemConditions[i]);
        for (int i = 0; i < result.Count; i++) AddUnchecked(result.Grades[i], result.ItemConditions[i], 1);
        return true;
    }

    public int TransferTo(PotatoInventory destination)
    {
        if (destination == null || ReferenceEquals(this, destination)) return 0;
        int remaining = Math.Min(Count, destination.Free);
        int moved = remaining;
        for (int i = 0; i < stacks.Count && remaining > 0;)
        {
            var stack = stacks[i];
            int amount = Math.Min(stack.quantity, remaining);
            destination.AddUnchecked(stack.grade, stack.conditions, amount);
            int toRemove = amount;
            for (int item = 0; item < potatoes.Count && toRemove > 0;)
                if (potatoes[item].Grade == stack.grade && potatoes[item].Conditions == stack.conditions)
                { potatoes.RemoveAt(item); toRemove--; }
                else item++;
            stack.quantity -= amount;
            Count -= amount;
            remaining -= amount;
            if (stack.quantity == 0) stacks.RemoveAt(i);
            else i++;
        }
        if (moved > 0) Revision++;
        return moved;
    }

    public BasketPotato FindPotato(long id) => potatoes.Find(p => p.Id == id);

    /// <summary>Commit one removal, preserving the exact identities and ordering if persistence fails.</summary>
    public bool TryConsume(long id, Func<bool> commit)
    {
        int itemIndex = potatoes.FindIndex(p => p.Id == id);
        if (itemIndex < 0 || commit == null) return false;
        var item = potatoes[itemIndex];
        int stackIndex = stacks.FindIndex(s => s.grade == item.Grade && s.conditions == item.Conditions);
        var stack = stacks[stackIndex];
        potatoes.RemoveAt(itemIndex);
        stack.quantity--;
        if (stack.quantity == 0) stacks.RemoveAt(stackIndex);
        Count--;
        Revision++;
        try { if (commit()) return true; }
        catch (Exception error) { Debug.LogWarning("[Potato Storage] " + error.Message); }
        if (stack.quantity == 0) stacks.Insert(stackIndex, stack);
        stack.quantity++;
        potatoes.Insert(itemIndex, item);
        Count++;
        Revision++;
        return false;
    }

    public InventoryData CaptureInventory()
    {
        var data = new InventoryData { ownerId = OwnerId };
        foreach (var stack in stacks)
            data.items.Add(new InventoryItemData { itemId = "potato", quantity = stack.quantity,
                propertiesVersion = 1, propertiesJson = JsonUtility.ToJson(new Properties { grade = stack.grade, conditions = stack.conditions }) });
        return data;
    }

    public void ValidateInventory(InventoryData data) => ReadInventory(data);

    public void RestoreInventory(InventoryData data)
    {
        var replacement = ReadInventory(data); // Validate everything before changing the live inventory.
        stacks.Clear();
        potatoes.Clear();
        Count = 0;
        foreach (var stack in replacement) AddUnchecked(stack.grade, stack.conditions, stack.quantity);
        Revision++;
    }

    public string Describe(string label)
    {
        var text = new StringBuilder($"[{label}] {Count}/{Capacity}");
        if (stacks.Count == 0) return text.Append("\n비어 있음").ToString();
        foreach (var stack in stacks) text.Append('\n').Append(stack.DisplayName).Append(": ").Append(stack.quantity).Append("개");
        return text.ToString();
    }

    private List<PotatoStack> ReadInventory(InventoryData data)
    {
        if (data == null || data.ownerId != OwnerId || data.items == null) throw new InvalidDataException("Potato inventory owner mismatch or missing items.");
        int available = Capacity;
        var result = new List<PotatoStack>();
        foreach (var item in data.items)
        {
            if (item == null || item.itemId != "potato" || item.quantity <= 0 || item.quantity > available ||
                item.slot != -1 || !string.IsNullOrEmpty(item.instanceId) || item.propertiesVersion != 1)
                throw new InvalidDataException("Unsupported potato item or inventory capacity exceeded.");
            // The original save contract permits plain potato entries without optional properties.
            var properties = string.IsNullOrWhiteSpace(item.propertiesJson) ? new Properties() : JsonUtility.FromJson<Properties>(item.propertiesJson);
            if (properties == null) throw new InvalidDataException("Missing potato properties.");
            ValidateProperties(properties.grade, properties.conditions);
            result.Add(new PotatoStack { grade = properties.grade, conditions = properties.conditions, quantity = item.quantity });
            available -= item.quantity;
        }
        return result;
    }

    private static void ValidateProperties(PotatoHarvestGrade grade, HarvestedPotatoCondition conditions)
    {
        if ((int)grade < 0 || (int)grade > 3 || (conditions & ~AllowedConditions) != 0)
            throw new InvalidDataException("Unknown potato grade or condition.");
    }

    private void AddUnchecked(PotatoHarvestGrade grade, HarvestedPotatoCondition conditions, int quantity)
    {
        if (conditions != HarvestedPotatoCondition.None) grade = PotatoHarvestGrade.None;
        var stack = stacks.Find(s => s.grade == grade && s.conditions == conditions);
        if (stack == null) stacks.Add(new PotatoStack { grade = grade, conditions = conditions, quantity = quantity });
        else stack.quantity += quantity;
        Count += quantity;
        for (int i = 0; i < quantity; i++) potatoes.Add(new BasketPotato(grade, conditions));
        Revision++;
    }
}
