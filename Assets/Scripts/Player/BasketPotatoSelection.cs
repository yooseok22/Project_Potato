using System.Collections.Generic;
using UnityEngine;

/// <summary>Three individual potatoes; only the immediately preceding presentation is excluded.</summary>
public sealed class BasketPotatoSelection
{
    private readonly List<BasketPotato> visible = new();
    public IReadOnlyList<BasketPotato> Visible => visible.AsReadOnly();

    public void Reroll(PotatoInventory inventory)
    {
        var previous = new HashSet<long>();
        foreach (var potato in visible) previous.Add(potato.Id);
        var fresh = new List<BasketPotato>();
        var repeats = new List<BasketPotato>();
        foreach (var potato in inventory.Potatoes)
            (previous.Contains(potato.Id) ? repeats : fresh).Add(potato);
        Shuffle(fresh);
        Shuffle(repeats);
        visible.Clear();
        foreach (var potato in fresh) { if (visible.Count == 3) break; visible.Add(potato); }
        // Four/five potatoes can leave fewer than three fresh candidates. Reuse only the deficit.
        foreach (var potato in repeats) { if (visible.Count == 3) break; visible.Add(potato); }
    }

    public void Remove(long id) => visible.RemoveAll(p => p.Id == id);
    public void Clear() => visible.Clear();

    private static void Shuffle(List<BasketPotato> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
