using UnityEngine;

public enum AutomationMachineKind { Generator, WaterTank, Sprinkler, Conveyor }
public enum AutomationSupplyKind { Electricity, Water }

/// <summary>A grid-sized machine. Supply values are recomputed by AutomationWorld.</summary>
[DisallowMultipleComponent]
public sealed class AutomationBlock : MonoBehaviour
{
    [SerializeField] private AutomationMachineKind kind;
    [SerializeField, Min(0.1f)] private float blockSize = 2f;
    [SerializeField, Min(0f)] private float generatorOutput = 100f;
    [SerializeField, Min(0.1f)] private float powerDemand = 10f;
    [SerializeField, Min(0.1f)] private float maximumLineLength = 20f;
    [SerializeField, Min(0f)] private float conveyorSpeed = 2f;
    [SerializeField, Min(1)] private int poweredRailReach = 15;
    [SerializeField, Min(0f)] private float sprinklerReach = 6f;
    [SerializeField, Min(0.1f)] private float sprinklerInterval = 1f;
    [SerializeField] private bool sprinklerPassThroughUnlocked;
    private float nextWaterAt;
    private Material indicatorMaterial;
    private ParticleSystem leftSpray, rightSpray;
    private Collider[] blockingColliders;

    public AutomationMachineKind Kind => kind;
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
        kind == AutomationMachineKind.Conveyor && PowerFraction > 0f;
    public float TransportSpeed => IsOperating ? conveyorSpeed * PowerFraction : 0f;
    public Vector3 PortPosition => transform.position + Vector3.up * (kind == AutomationMachineKind.Conveyor ? 0.18f : 0.55f);
    public string DisplayName => kind switch
    {
        AutomationMachineKind.Generator => "발전기",
        AutomationMachineKind.WaterTank => "물탱크",
        AutomationMachineKind.Sprinkler => "스프링클러",
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
        : kind == AutomationMachineKind.Sprinkler || kind == AutomationMachineKind.Conveyor;

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
        SetSprinklerPassThroughUnlocked(sprinklerPassThroughUnlocked);
    }

    internal int WaterCrops(PotatoFarmingSystem farming, GroundGrid grid)
    {
        if (!IsOperating || !farming || !grid) return 0;
        // A narrow strip along the T bar's local X axis, on both sides of the pillar.
        Vector2Int minimum = grid.WorldToCell(transform.position - Vector3.one * (sprinklerReach + grid.CellSize));
        Vector2Int maximum = grid.WorldToCell(transform.position + Vector3.one * (sprinklerReach + grid.CellSize));
        int count = 0;
        for (int x = Mathf.Max(0, minimum.x); x <= Mathf.Min(grid.Width - 1, maximum.x); x++)
            for (int z = Mathf.Max(0, minimum.y); z <= Mathf.Min(grid.Depth - 1, maximum.y); z++)
            {
                var cell = new Vector2Int(x, z);
                Vector3 offset = Quaternion.Inverse(transform.rotation) * (grid.CellCenter(cell) - transform.position);
                if (Mathf.Abs(offset.x) > sprinklerReach || Mathf.Abs(offset.z) > grid.CellSize * 0.5f ||
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
        Spray(leftSpray, operating); Spray(rightSpray, operating);
        if (kind != AutomationMachineKind.Sprinkler || Time.time < nextWaterAt) return;
        nextWaterAt = Time.time + sprinklerInterval;
        foreach (var grid in world.Grids)
            foreach (var farming in world.FarmingSystems)
                WaterCrops(farming, grid);
    }

    private static void Spray(ParticleSystem particles, bool active)
    {
        if (!particles) return;
        if (active && !particles.isPlaying) particles.Play();
        else if (!active && particles.isPlaying) particles.Stop();
    }
}
