using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Offline automation ownership, construction and power distribution.</summary>
public sealed class AutomationWorld : MonoBehaviour
{
    public static AutomationWorld Instance { get; private set; }
    public readonly List<AutomationBlock> Blocks = new();
    public readonly List<AutomationLine> Lines = new();
    public GroundGrid[] Grids { get; private set; } = System.Array.Empty<GroundGrid>();
    public PotatoFarmingSystem[] FarmingSystems { get; private set; } = System.Array.Empty<PotatoFarmingSystem>();
    public static bool Offline => !NetworkManager.Singleton || !NetworkManager.Singleton.IsListening;
    public float BlockSize => Grids.Length > 0 && Grids[0] ? Grids[0].CellSize : 2f;
    private float nextResolveAt;
    private float nextPowerAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; SceneManager.sceneLoaded -= OnSceneLoaded; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize() { SceneManager.sceneLoaded += OnSceneLoaded; EnsureWorld(); }
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureWorld();
    private static void EnsureWorld()
    {
        if (Instance || !FindAnyObjectByType<PotatoFarmingSystem>()) return;
        new GameObject("FarmAutomation").AddComponent<AutomationWorld>();
    }

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ResolveScene();
    }

    private void ResolveScene()
    {
        Grids = FindObjectsByType<GroundGrid>();
        FarmingSystems = FindObjectsByType<PotatoFarmingSystem>();
        foreach (var player in FindObjectsByType<FirstPersonPlayerController>())
            if (!player.GetComponent<AutomationPlayerController>()) player.gameObject.AddComponent<AutomationPlayerController>();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextResolveAt) { ResolveScene(); nextResolveAt = Time.unscaledTime + 1f; }
        Blocks.RemoveAll(block => !block);
        for (int index = Lines.Count - 1; index >= 0; index--)
            if (!Lines[index] || !Lines[index].Source) { if (Lines[index]) Destroy(Lines[index].gameObject); Lines.RemoveAt(index); }
        if (Time.time >= nextPowerAt || !Offline) { RebuildPower(); nextPowerAt = Time.time + 0.2f; }
        foreach (var block in Blocks) if (block && block.isActiveAndEnabled) block.Tick(this);
    }

    public Vector3 Snap(Vector3 point)
    {
        var origin = Grids.Length > 0 && Grids[0] ? Grids[0].Origin : Vector3.zero;
        float size = BlockSize;
        return new Vector3(origin.x + (Mathf.Floor((point.x - origin.x) / size) + 0.5f) * size,
            point.y, origin.z + (Mathf.Floor((point.z - origin.z) / size) + 0.5f) * size);
    }

    public bool CanPlace(Vector3 position, float size)
    {
        if (!Offline) return false;
        foreach (var block in Blocks)
            if (block && Mathf.Abs(block.transform.position.y - position.y) < 1f &&
                Mathf.Abs(block.transform.position.x - position.x) < (block.BlockSize + size) * 0.49f &&
                Mathf.Abs(block.transform.position.z - position.z) < (block.BlockSize + size) * 0.49f) return false;
        // Reserve the footprint against actors, props and walls. The floor stays below this volume.
        foreach (var hit in Physics.OverlapBox(position + Vector3.up * 0.6f, new Vector3(size * 0.43f, 0.48f, size * 0.43f),
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.GetComponentInParent<GroundTileRenderer>() && !hit.GetComponentInParent<PotatoGrowth>()) return false;
        return true;
    }

    public AutomationBlock Place(AutomationMachineKind kind, Vector3 position, Quaternion rotation)
    {
        if (!CanPlace(position, BlockSize)) return null;
        return CreateBlock(kind, position, rotation);
    }

    internal AutomationBlock CreateBlock(AutomationMachineKind kind, Vector3 position, Quaternion rotation)
    {
        var block = AutomationMachineFactory.Create(kind, BlockSize, transform);
        block.transform.SetPositionAndRotation(position, rotation);
        Blocks.Add(block);
        return block;
    }

    public AutomationLine BeginLine(AutomationBlock source)
    {
        if (!Offline || !source || !Blocks.Contains(source)) return null;
        for (int outlet = 0; outlet < source.OutletCount; outlet++)
        {
            if (Lines.Any(line => line && line.Source == source && line.Outlet == outlet)) continue;
            var go = new GameObject(source.SupplyKind == AutomationSupplyKind.Water ? "WaterHose" : "PowerCable");
            go.transform.SetParent(transform);
            var line = go.AddComponent<AutomationLine>();
            line.Initialize(source, outlet);
            Lines.Add(line);
            return line;
        }
        return null;
    }

    public bool Connect(AutomationLine line, AutomationBlock target)
    {
        if (!Offline || !line || !Lines.Contains(line) || !Blocks.Contains(target)) return false;
        // Each machine has one power inlet and one water inlet.
        if (Lines.Any(other => other && other != line && other.Target == target &&
            other.Source && other.Source.SupplyKind == line.Source.SupplyKind)) return false;
        return line.TryConnect(target);
    }

    public int UsedOutlets(AutomationBlock source) => Lines.Count(line => line && line.Source == source);
    public void RemoveLine(AutomationLine line) { if (!line) return; Lines.Remove(line); line.gameObject.SetActive(false); DestroyOwned(line.gameObject); }
    public void Disconnect(AutomationBlock block)
    {
        foreach (var line in Lines.Where(line => line && (line.Source == block || line.Target == block)).ToArray()) RemoveLine(line);
    }
    public void RemoveBlock(AutomationBlock block)
    {
        if (!Offline || !block) return;
        Disconnect(block); Blocks.Remove(block); block.gameObject.SetActive(false); DestroyOwned(block.gameObject);
    }

    public static bool AreAdjacentRails(AutomationBlock first, AutomationBlock second)
    {
        if (!first || !second || first == second || first.Kind != AutomationMachineKind.Conveyor || second.Kind != AutomationMachineKind.Conveyor) return false;
        Vector3 offset = second.transform.position - first.transform.position;
        float size = (first.BlockSize + second.BlockSize) * 0.5f;
        return Mathf.Abs(offset.y) < 0.15f &&
            ((Mathf.Abs(Mathf.Abs(offset.x) - size) < 0.05f && Mathf.Abs(offset.z) < 0.05f) ||
             (Mathf.Abs(Mathf.Abs(offset.z) - size) < 0.05f && Mathf.Abs(offset.x) < 0.05f));
    }

    public void RebuildPower()
    {
        foreach (var block in Blocks) if (block) { block.PowerFraction = 0; block.HasWater = false; }
        if (!Offline) return;
        var active = Blocks.Where(block => block && block.isActiveAndEnabled).ToArray();
        var rails = active.Where(block => block.Kind == AutomationMachineKind.Conveyor).ToArray();
        // Connected rail islands share the load. Two outlets into the same island count its demand once.
        var groups = new Dictionary<AutomationBlock, HashSet<AutomationBlock>>();
        foreach (var rail in rails)
        {
            if (groups.ContainsKey(rail)) continue;
            var group = new HashSet<AutomationBlock> { rail };
            var queue = new Queue<AutomationBlock>(); queue.Enqueue(rail);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in rails) if (!group.Contains(next) && AreAdjacentRails(current, next)) { group.Add(next); queue.Enqueue(next); }
            }
            foreach (var member in group) groups[member] = group;
        }
        foreach (var line in Lines)
            if (line && line.IsLive && line.Source.SupplyKind == AutomationSupplyKind.Water) line.Target.HasWater = true;
        var generators = active.Where(block => block.Kind == AutomationMachineKind.Generator).ToArray();
        var generatorConsumers = new Dictionary<AutomationBlock, HashSet<AutomationBlock>>();
        var generatorDemands = new Dictionary<AutomationBlock, float>();
        var generatorOutputs = new Dictionary<AutomationBlock, AutomationLine[]>();
        foreach (var generator in generators)
        {
            var outputs = Lines.Where(line => line && line.IsLive && line.Source == generator).ToArray();
            var consumers = new HashSet<AutomationBlock>();
            foreach (var output in outputs)
                if (groups.TryGetValue(output.Target, out var group)) consumers.UnionWith(group); else consumers.Add(output.Target);
            generatorConsumers[generator] = consumers;
            generatorDemands[generator] = consumers.Sum(block => block.PowerDemand);
            generatorOutputs[generator] = outputs;
        }
        foreach (var generator in generators)
        {
            float demand = generatorDemands[generator];
            float strength = demand > 0 ? Mathf.Clamp01(generator.GeneratorOutput / demand) : 0;
            var outputs = generatorOutputs[generator];
            foreach (var output in outputs)
            {
                if (!groups.TryGetValue(output.Target, out var group))
                {
                    output.Target.PowerFraction = Mathf.Max(output.Target.PowerFraction, strength);
                    continue;
                }
                // Distinct generators add capacity to an island. Additional outlets from one generator do not duplicate it.
                float islandStrength = 0;
                foreach (var provider in generators)
                    if (generatorDemands[provider] > 0 && generatorConsumers[provider].Contains(output.Target))
                        islandStrength += provider.GeneratorOutput / generatorDemands[provider];
                islandStrength = Mathf.Clamp01(islandStrength);
                var distances = new Dictionary<AutomationBlock, int> { [output.Target] = 0 };
                var queue = new Queue<AutomationBlock>(); queue.Enqueue(output.Target);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    int distance = distances[current];
                    current.PowerFraction = Mathf.Max(current.PowerFraction, Mathf.Max(0, islandStrength - (float)distance / generator.PoweredRailReach));
                    foreach (var next in group)
                        if (!distances.ContainsKey(next) && AreAdjacentRails(current, next)) { distances[next] = distance + 1; queue.Enqueue(next); }
                }
            }
        }
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    internal static void DestroyOwned(Object value)
    {
        if (!value) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
