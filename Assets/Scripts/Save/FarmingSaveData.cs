using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable] public sealed class FarmCellData
{
    public int x, y;
    public GroundCellState state;
    public byte soilCoverLayers;
    public byte exposureDepth;
    public bool watered;
}

[Serializable] public sealed class FarmGridData
{
    public string id;
    public int width, depth;
    public float cellSize;
    public Vector3 origin;
    public List<FarmCellData> cells = new();
}

[Serializable] public sealed class FarmClockData
{
    public string id;
    public double elapsedSeconds;
    public float dayLengthSeconds, startingHour, sunriseHour, sunsetHour, speed;
    public bool paused;
}

[Serializable] public sealed class FarmCropData
{
    public string gridId;
    public int x, y;
    public double plantedAt, growthStartedAt, growthPausedAt, totalPausedSeconds;
    public bool watered, paused;
    public PotatoGrowthStage stage;
    public float health, maxHealth;
    public PotatoStatus status;
    public bool isSoilCovered, soilCoverDeadlineChecked;
    public bool isMulchingFilmApplied;
    public bool hasBugs, bugSpawnChecked;
    public double bugExpiresAt;
}

[Serializable] public sealed class FarmSystemData
{
    public string id, clockId;
    public List<FarmCropData> crops = new();
}

[Serializable] public sealed class FarmHarvestCellData
{
    public string gridId;
    public int x, y;
    public bool cut, covered;
    public PotatoHarvestTiming timing;
    public HarvestedPotatoCondition conditions;
}

[Serializable] public sealed class FarmHarvestData
{
    public string id, systemId;
    public List<FarmHarvestCellData> cells = new();
}

[Serializable] public sealed class FarmingSaveData
{
    public List<FarmGridData> grids = new();
    public List<FarmClockData> clocks = new();
    public List<FarmSystemData> systems = new();
    public List<FarmHarvestData> harvests = new();
}

internal static class SaveValidation
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    public static bool NonNegative(double value) => Finite(value) && value >= 0;
}
