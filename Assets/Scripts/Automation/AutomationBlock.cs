using UnityEngine;

public enum AutomationMachineKind { Generator, WaterTank, Sprinkler, Conveyor, SeederStation, DroneStation, MulchingStation }
public enum AutomationSupplyKind { Electricity, Water }

/// <summary>A grid-sized machine. Supply values are recomputed by AutomationWorld.</summary>
[DisallowMultipleComponent]
public sealed class AutomationBlock : MonoBehaviour
{
    public const float DefaultSprinklerReach = 6f;
    [SerializeField] private AutomationMachineKind kind;
    [SerializeField, Min(0.1f)] private float blockSize = 2f;
    [SerializeField, Min(0f)] private float generatorOutput = 100f;
    [SerializeField, Min(0.1f)] private float powerDemand = 10f;
    [SerializeField, Min(0.1f)] private float maximumLineLength = 20f;
    [SerializeField, Min(0f)] private float conveyorSpeed = 2f;
    [SerializeField, Min(1)] private int poweredRailReach = 15;
    [SerializeField, Min(0f)] private float sprinklerReach = DefaultSprinklerReach;
    [SerializeField, Min(0.1f)] private float sprinklerInterval = 1f;
    [SerializeField, Min(0f)] private float sprinklerDegreesPerSecond = 30f;
    [SerializeField] private bool sprinklerPassThroughUnlocked;
    private float nextWaterAt;
    private Material indicatorMaterial;
    private ParticleSystem leftSpray, rightSpray;
    private Collider[] blockingColliders;
    private Transform sprinklerHead;
    private float sprinklerAngle;
    private float pendingSprinklerSweep;

    public float SprinklerAngle => sprinklerAngle;

    public AutomationMachineKind Kind => kind;
    public bool IsRobotStation => kind >= AutomationMachineKind.SeederStation;
    public float BlockSize => blockSize;
    public int OutletCount => kind == AutomationMachineKind.Generator ? 3 : kind == AutomationMachineKind.WaterTank ? 4 : 0;
    public AutomationSupplyKind SupplyKind => kind == AutomationMachineKind.WaterTank ? AutomationSupplyKind.Water : AutomationSupplyKind.Electricity;
    public float MaximumLineLength => maximumLineLength;
    public float GeneratorOutput => generatorOutput;
    public float PowerDemand => powerDemand;
    public int PoweredRailReach => poweredRailReach;
    public float PowerFraction { get; internal set; }
    public bool HasWater { get; internal set; }
    public bool IsOperating => kind == AutomationMachineKind.Sprinkler ? PowerFraction >= 0.999f && HasWater :
        kind == AutomationMachineKind.Conveyor ? PowerFraction > 0f : IsRobotStation && PowerFraction >= 0.999f;
    public float TransportSpeed => kind == AutomationMachineKind.Conveyor && IsOperating ? conveyorSpeed * PowerFraction : 0f;
    public Vector3 PortPosition => transform.position + Vector3.up * (kind == AutomationMachineKind.Conveyor ? 0.18f : 0.55f);
    public string DisplayName => kind switch
    {
        AutomationMachineKind.Generator => "발전기",
        AutomationMachineKind.WaterTank => "물탱크",
        AutomationMachineKind.Sprinkler => "스프링클러",
        AutomationMachineKind.SeederStation => "감자 파종 로봇 충전소",
        AutomationMachineKind.DroneStation => "방제 드론 충전소",
        AutomationMachineKind.MulchingStation => "멀칭 로봇 충전소",
        _ => "작물 이동 레일"
    };

    public void Configure(AutomationMachineKind value, float size)
    {
        kind = value;
        blockSize = Mathf.Max(0.5f, size);
        maximumLineLength = kind == AutomationMachineKind.WaterTank ? 16f : 20f;
    }

    public bool Accepts(AutomationSupplyKind supply) => supply == AutomationSupplyKind.Water
        ? kind == AutomationMachineKind.Sprinkler
        : kind == AutomationMachineKind.Sprinkler || kind == AutomationMachineKind.Conveyor || IsRobotStation;

    public void SetSprinklerPassThroughUnlocked(bool unlocked)
    {
        sprinklerPassThroughUnlocked = unlocked;
        if (kind != AutomationMachineKind.Sprinkler) return;
        blockingColliders ??= GetComponentsInChildren<Collider>();
        foreach (var blocker in blockingColliders) if (blocker) blocker.isTrigger = unlocked;
        var obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle) obstacle.enabled = !unlocked;
    }

    internal void BindVisuals(Material indicator, ParticleSystem left, ParticleSystem right)
    {
        indicatorMaterial = indicator;
        leftSpray = left;
        rightSpray = right;
        sprinklerHead = left && left.transform.parent != transform ? left.transform.parent : null;
        SetSprinklerPassThroughUnlocked(sprinklerPassThroughUnlocked);
    }

    internal void AdvanceSprinkler(float deltaSeconds)
    {
        if (kind != AutomationMachineKind.Sprinkler || !IsOperating || deltaSeconds <= 0f) return;
        float angle = sprinklerDegreesPerSecond * deltaSeconds;
        sprinklerAngle = Mathf.Repeat(sprinklerAngle + angle, 360f);
        pendingSprinklerSweep = Mathf.Min(360f, pendingSprinklerSweep + angle);
        if (sprinklerHead) sprinklerHead.localRotation = Quaternion.Euler(0, sprinklerAngle, 0);
    }

    internal int WaterCrops(PotatoFarmingSystem farming, GroundGrid grid)
    {
        if (!IsOperating || !farming || !grid) return 0;
        // Cover the arc swept by both jets since the last watering tick, including across 0 degrees.
        Vector2Int minimum = grid.WorldToCell(transform.position - Vector3.one * (sprinklerReach + grid.CellSize));
        Vector2Int maximum = grid.WorldToCell(transform.position + Vector3.one * (sprinklerReach + grid.CellSize));
        int count = 0;
        for (int x = Mathf.Max(0, minimum.x); x <= Mathf.Min(grid.Width - 1, maximum.x); x++)
            for (int z = Mathf.Max(0, minimum.y); z <= Mathf.Min(grid.Depth - 1, maximum.y); z++)
            {
                var cell = new Vector2Int(x, z);
                Vector3 offset = Quaternion.Inverse(transform.rotation) * (grid.CellCenter(cell) - transform.position);
                float radius = new Vector2(offset.x, offset.z).magnitude;
                float halfWidth = grid.CellSize * 0.5f;
                float tolerance = radius > halfWidth ? Mathf.Asin(halfWidth / radius) * Mathf.Rad2Deg : 90f;
                float bearing = Mathf.Atan2(-offset.z, offset.x) * Mathf.Rad2Deg;
                // Opposed nozzles repeat the same coverage every 180 degrees.
                float sweptBearing = Mathf.Repeat(bearing - (sprinklerAngle - pendingSprinklerSweep) + tolerance, 180f);
                if (radius > sprinklerReach || sweptBearing > pendingSprinklerSweep + tolerance * 2f + 0.001f ||
                    Mathf.Abs(offset.y) > 1f || !grid.CanWater(cell) ||
                    !farming.TryGetFarmingTile(grid, cell, out var crop) || crop.IsDead) continue;
                if (grid.TryWater(cell)) { farming.TryWater(grid, cell); count++; }
            }
        return count;
    }

    internal void Tick(AutomationWorld world)
    {
        bool operating = IsOperating;
        if (indicatorMaterial) indicatorMaterial.color = operating ? new Color(0.15f, 0.9f, 0.4f) : new Color(0.9f, 0.25f, 0.12f);
        AdvanceSprinkler(Time.deltaTime);
        Spray(leftSpray, operating); Spray(rightSpray, operating);
        if (kind != AutomationMachineKind.Sprinkler) return;
        if (!operating) { pendingSprinklerSweep = 0f; return; }
        if (Time.time < nextWaterAt) return;
        nextWaterAt = Time.time + sprinklerInterval;
        foreach (var grid in world.Grids)
            foreach (var farming in world.FarmingSystems)
                WaterCrops(farming, grid);
        pendingSprinklerSweep = 0f;
    }

    private static void Spray(ParticleSystem particles, bool active)
    {
        if (!particles) return;
        if (active && !particles.isPlaying) particles.Play();
        else if (!active && particles.isPlaying) particles.Stop();
    }
}
