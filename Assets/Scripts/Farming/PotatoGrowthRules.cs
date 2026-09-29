public enum PotatoGrowthStage
{
    SoilMound, Sprout, TwoLargeLeaves, FourLargeLeaves,
    SixLargeLeaves, FreshSixLargeLeaves, WitheredSixLeaves
}

/// <summary>Pure growth rules shared by central farming and the legacy object-based sample.</summary>
public static class PotatoGrowthRules
{
    public const double BaseGrowthSeconds = 300d;
    public const double ReadyToHarvestSeconds = 260d;
    private static readonly double[] StageStarts = { 0d, 40d, 80d, 150d, 220d, 260d, 300d };

    public static PotatoGrowthStage StageAt(double ageSeconds)
    {
        for (int i = StageStarts.Length - 1; i > 0; i--)
            if (ageSeconds >= StageStarts[i]) return (PotatoGrowthStage)i;
        return PotatoGrowthStage.SoilMound;
    }
}
