using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Captures all loaded farm grids, including prepared soil without planted crops.</summary>
public sealed class FarmingSaveSection : ISaveSection
{
    private readonly Func<GroundGrid[]> getGrids;
    private readonly Func<WorldClock[]> getClocks;
    private readonly Func<PotatoFarmingSystem[]> getSystems;
    private readonly Func<PotatoHarvestCoordinator[]> getHarvests;
    public string Key => "farming";
    public int Version => 1;

    public FarmingSaveSection() : this(Find<GroundGrid>, Find<WorldClock>, Find<PotatoFarmingSystem>, Find<PotatoHarvestCoordinator>) { }

    public FarmingSaveSection(Func<GroundGrid[]> grids, Func<WorldClock[]> clocks,
        Func<PotatoFarmingSystem[]> systems, Func<PotatoHarvestCoordinator[]> harvests)
    {
        getGrids = grids; getClocks = clocks; getSystems = systems; getHarvests = harvests;
    }

    private static T[] Find<T>() where T : Component
        => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    private static Dictionary<string, T> Map<T>(T[] components) where T : Component
    {
        var map = new Dictionary<string, T>();
        foreach (var component in components)
        {
            string id = SaveIdentity.Key(component);
            SaveValidation.Require(map.TryAdd(id, component), "Duplicate save identity: " + id);
        }
        return map;
    }

    public string Capture()
    {
        var data = new FarmingSaveData();
        foreach (var grid in Map(getGrids()).Values) data.grids.Add(grid.CaptureSaveData());
        foreach (var clock in Map(getClocks()).Values) data.clocks.Add(clock.CaptureSaveData());
        foreach (var system in Map(getSystems()).Values) data.systems.Add(system.CaptureSaveData());
        foreach (var harvest in Map(getHarvests()).Values) data.harvests.Add(harvest.CaptureSaveData());
        return JsonUtility.ToJson(data);
    }

    public Action PrepareRestore(int version, string json)
    {
        SaveValidation.Require(version == Version, "Unsupported farming version.");
        var data = JsonUtility.FromJson<FarmingSaveData>(json);
        SaveValidation.Require(data != null && data.grids != null && data.clocks != null && data.systems != null && data.harvests != null,
            "Incomplete farming data.");
        var grids = Map(getGrids());
        var clocks = Map(getClocks());
        var systems = Map(getSystems());
        var harvests = Map(getHarvests());
        Match(data.grids, grids, value => value.id);
        Match(data.clocks, clocks, value => value.id);
        Match(data.systems, systems, value => value.id);
        Match(data.harvests, harvests, value => value.id);
        foreach (var grid in data.grids) grids[grid.id].ValidateSaveData(grid);
        var clockData = new Dictionary<string, FarmClockData>();
        foreach (var clock in data.clocks) { WorldClock.ValidateSaveData(clock); clockData.Add(clock.id, clock); }
        var ownedCells = new HashSet<(string, int, int)>();
        foreach (var system in data.systems)
        {
            SaveValidation.Require(system.crops != null && (string.IsNullOrEmpty(system.clockId) || clocks.ContainsKey(system.clockId)), "Invalid farm clock reference.");
            foreach (var crop in system.crops)
            {
                SaveValidation.Require(crop != null && crop.gridId != null && grids.ContainsKey(crop.gridId) && system.clockId != null
                    && clockData.ContainsKey(system.clockId), "Invalid crop references.");
                double now = clockData[system.clockId].elapsedSeconds;
                SaveValidation.Require(grids[crop.gridId].ContainsCell(new Vector2Int(crop.x, crop.y))
                    && ownedCells.Add((crop.gridId, crop.x, crop.y))
                    && SaveValidation.NonNegative(crop.plantedAt) && crop.plantedAt <= now
                    && SaveValidation.NonNegative(crop.totalPausedSeconds)
                    && SaveValidation.NonNegative(crop.growthStartedAt) && SaveValidation.NonNegative(crop.growthPausedAt)
                    && (!crop.watered || crop.growthStartedAt >= crop.plantedAt && crop.growthStartedAt <= now)
                    && (!crop.paused || crop.watered && crop.growthPausedAt >= crop.growthStartedAt && crop.growthPausedAt <= now)
                    && (!crop.watered || crop.totalPausedSeconds <= (crop.paused ? crop.growthPausedAt : now) - crop.growthStartedAt)
                    && Enum.IsDefined(typeof(PotatoGrowthStage), crop.stage)
                    && SaveValidation.Finite(crop.maxHealth) && crop.maxHealth >= 1
                    && SaveValidation.NonNegative(crop.health) && crop.health <= crop.maxHealth
                    && SaveValidation.NonNegative(crop.bugExpiresAt)
                    && (!crop.hasBugs || crop.bugSpawnChecked && crop.watered), "Invalid crop state.");
            }
        }
        foreach (var harvest in data.harvests)
        {
            SaveValidation.Require(harvest.cells != null && (string.IsNullOrEmpty(harvest.systemId) || systems.ContainsKey(harvest.systemId)), "Invalid harvest system reference.");
            var seen = new HashSet<(string, int, int)>();
            foreach (var cell in harvest.cells)
                SaveValidation.Require(cell != null && cell.gridId != null && grids.ContainsKey(cell.gridId)
                    && grids[cell.gridId].ContainsCell(new Vector2Int(cell.x, cell.y)) && seen.Add((cell.gridId, cell.x, cell.y))
                    && (cell.conditions & ~PotatoHarvestConditionRules.Harvestable) == 0
                    && Enum.IsDefined(typeof(PotatoHarvestTiming), cell.timing), "Invalid harvest state.");
        }
        return () =>
        {
            foreach (var clock in data.clocks) clocks[clock.id].RestoreSaveData(clock);
            foreach (var system in data.systems) systems[system.id].RestoreSaveData(system, grids, clocks);
            foreach (var harvest in data.harvests) harvests[harvest.id].RestoreSaveData(harvest, grids, systems);
            foreach (var grid in data.grids) grids[grid.id].RestoreSaveData(grid);
            foreach (var clock in clocks.Values) clock.NotifySaveRestored();
            foreach (var grid in grids.Values) grid.NotifySaveRestored();
        };
    }

    private static void Match<TData, TComponent>(List<TData> data, Dictionary<string, TComponent> components, Func<TData, string> key)
        where TData : class where TComponent : Component
    {
        SaveValidation.Require(data.Count == components.Count, "Load the same gameplay scenes before restoring a save.");
        var seen = new HashSet<string>();
        foreach (var item in data)
        {
            SaveValidation.Require(item != null, "Null farming entry.");
            string id = key(item);
            SaveValidation.Require(id != null && components.ContainsKey(id) && seen.Add(id), "Missing or duplicate saved scene object: " + id);
        }
    }
}
