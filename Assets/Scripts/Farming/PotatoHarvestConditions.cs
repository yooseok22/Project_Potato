using System;
using UnityEngine;

[Flags]
public enum HarvestedPotatoCondition : byte
{
    None = 0, Green = 1, BugEaten = 2, Injured = 4, Sprouted = 8, Rotten = 16
}

/// <summary>Conditions belong to individual rewarded potatoes, separately from the crop's HP.</summary>
public static class PotatoHarvestConditionRules
{
    public const HarvestedPotatoCondition Harvestable = HarvestedPotatoCondition.Green |
        HarvestedPotatoCondition.BugEaten | HarvestedPotatoCondition.Injured | HarvestedPotatoCondition.Sprouted;

    public static int AbnormalCountAt(float health, int count, float roll)
    {
        if (count <= 0 || health <= 0f) return 0;
        roll = Mathf.Clamp01(roll);
        if (health >= 90f)
        {
            if (count >= 3) return roll < 0.4f ? 3 : roll < 0.8f ? 2 : 1;
            if (count == 2) return roll < 0.5f ? 2 : 1;
        }
        else if (health >= 80f && count >= 2) return roll < 0.7f ? 2 : 1;
        return 1;
    }

    public static HarvestedPotatoCondition FromCrop(PotatoStatus status)
    {
        HarvestedPotatoCondition result = HarvestedPotatoCondition.None;
        if ((status & PotatoStatus.Green) != 0) result |= HarvestedPotatoCondition.Green;
        if ((status & PotatoStatus.BugEaten) != 0) result |= HarvestedPotatoCondition.BugEaten;
        if ((status & PotatoStatus.Injured) != 0) result |= HarvestedPotatoCondition.Injured;
        return result;
    }

    public static HarvestedPotatoCondition[] Roll(float health, int count, HarvestedPotatoCondition cropConditions)
    {
        var result = new HarvestedPotatoCondition[count];
        cropConditions &= Harvestable; // Rotting is a post-harvest outdoor-storage process.
        if (cropConditions == HarvestedPotatoCondition.None) return result;
        int abnormal = AbnormalCountAt(health, count, UnityEngine.Random.value);
        var indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;
        for (int i = 0; i < abnormal; i++)
        {
            int selected = UnityEngine.Random.Range(i, count);
            (indices[i], indices[selected]) = (indices[selected], indices[i]);
            result[indices[i]] = cropConditions;
        }
        return result;
    }

    public static string Describe(HarvestedPotatoCondition condition)
    {
        if (condition == HarvestedPotatoCondition.None) return "정상";
        var names = new System.Collections.Generic.List<string>();
        if ((condition & HarvestedPotatoCondition.BugEaten) != 0) names.Add("벌레 먹음");
        if ((condition & HarvestedPotatoCondition.Green) != 0) names.Add("녹색");
        if ((condition & HarvestedPotatoCondition.Injured) != 0) names.Add("상처 입은");
        if ((condition & HarvestedPotatoCondition.Sprouted) != 0) names.Add("싹난");
        if ((condition & HarvestedPotatoCondition.Rotten) != 0) names.Add("썩음");
        return string.Join("+", names);
    }
}
