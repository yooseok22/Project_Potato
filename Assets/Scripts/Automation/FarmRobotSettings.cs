using System;
using UnityEngine;

public enum FarmRobotRole { Seeder, SprayerDrone, Mulching }
public enum FarmRobotSize { Small, Medium, Large }
public enum FarmRobotSupply { SeedPotato, Pesticide, MulchingFilm }

/// <summary>Future item inventories implement this contract; reservations are consumed only for successful work.</summary>
public interface IFarmRobotSupplies
{
    bool HasSupply(FarmRobotSupply kind, int amount);
    bool TryConsumeSupply(FarmRobotSupply kind, int amount);
}

[Serializable]
public sealed class FarmRobotSizeSettings
{
    [Min(1)] public int maxWorkCells;
    [Min(1f)] public float chargeSeconds;
    [Min(0.1f)] public float moveSpeed = 2f;
    [Min(0.01f)] public float workSeconds = 0.5f;
    [Min(1)] public int droneAreaSideCells;
    [Min(1)] public int droneRangeCells;
    [Min(1f)] public float preventionSeconds = 180f;
}

[CreateAssetMenu(menuName = "Project Potato/Farm Robot Settings")]
public sealed class FarmRobotSettings : ScriptableObject
{
    public FarmRobotSizeSettings small = new() { maxWorkCells = 10, chargeSeconds = 60, droneAreaSideCells = 3, droneRangeCells = 8 };
    public FarmRobotSizeSettings medium = new() { maxWorkCells = 25, chargeSeconds = 90, droneAreaSideCells = 5, droneRangeCells = 12 };
    public FarmRobotSizeSettings large = new() { maxWorkCells = 60, chargeSeconds = 120, droneAreaSideCells = 7, droneRangeCells = 16 };
    [Min(0.5f)] public float droneFlightHeight = 3f;
    [Tooltip("Until the item database and physical baskets are ready, supplies are unlimited.")]
    public bool unlimitedSupplies = true;

    public FarmRobotSizeSettings For(FarmRobotSize size) => size switch
    {
        FarmRobotSize.Medium => medium,
        FarmRobotSize.Large => large,
        _ => small
    };
}
