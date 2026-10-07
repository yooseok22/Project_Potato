using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class BoarSpawnManager : MonoBehaviour
{
    [SerializeField] private WorldClock clock;
    [SerializeField] private PotatoFarmingSystem farmingSystem;
    [Tooltip("Only unlocked player fields belong here. Empty finds every loaded GroundGrid.")]
    [SerializeField] private GroundGrid[] unlockedFields = Array.Empty<GroundGrid>();
    [SerializeField] private BoarController boarPrefab;
    [SerializeField, Min(1f)] private float appearanceIntervalSeconds = 600f;
    [SerializeField, Range(0f, 1f)] private float appearanceChance = 0.3f;
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 4f;

    private double nextAppearanceAt;
    private BoarController activeBoar;

    public BoarController ActiveBoar => activeBoar;

    private void Awake()
    {
        if (!clock) clock = FindAnyObjectByType<WorldClock>();
        if (!farmingSystem) farmingSystem = FindAnyObjectByType<PotatoFarmingSystem>();
        if (unlockedFields == null || unlockedFields.Length == 0)
            unlockedFields = FindObjectsByType<GroundGrid>(FindObjectsSortMode.None);
        nextAppearanceAt = clock ? clock.ElapsedSeconds + appearanceIntervalSeconds : appearanceIntervalSeconds;
    }

    private void OnEnable()
    {
        if (clock) clock.TimeAdvanced += CheckAppearance;
    }

    private void Start()
    {
        if (!clock) clock = FindAnyObjectByType<WorldClock>();
        if (clock)
        {
            clock.TimeAdvanced -= CheckAppearance;
            clock.TimeAdvanced += CheckAppearance;
            if (nextAppearanceAt <= clock.ElapsedSeconds)
                nextAppearanceAt = clock.ElapsedSeconds + appearanceIntervalSeconds;
        }
    }

    private void CheckAppearance()
    {
        if (!clock || appearanceIntervalSeconds <= 0f) return;
        while (clock.ElapsedSeconds >= nextAppearanceAt)
        {
            nextAppearanceAt += appearanceIntervalSeconds;
            if (!activeBoar && UnityEngine.Random.value < appearanceChance) TrySpawnBoar();
        }
    }

    public bool TrySpawnBoar()
    {
        if (activeBoar || !boarPrefab || !farmingSystem) return false;
        GroundGrid field = ChooseFieldWithCrop();
        if (!field || !TryChooseSpawnPosition(field, out var position)) return false;
        activeBoar = Instantiate(boarPrefab, position, Quaternion.identity);
        activeBoar.Initialize(farmingSystem, unlockedFields);
        activeBoar.Despawned += OnBoarDespawned;
        Debug.Log($"[Boar] Appeared at Ground='{field.name}', Position={position}.", activeBoar);
        return true;
    }

    private GroundGrid ChooseFieldWithCrop()
    {
        var candidates = new List<GroundGrid>();
        foreach (var field in unlockedFields)
        {
            if (!field) continue;
            foreach (var crop in farmingSystem.GetFarmingTiles())
            {
                if (crop.Ground != field || crop.IsDead) continue;
                candidates.Add(field);
                break;
            }
        }
        return candidates.Count > 0 ? candidates[UnityEngine.Random.Range(0, candidates.Count)] : null;
    }

    private bool TryChooseSpawnPosition(GroundGrid field, out Vector3 position)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            var cell = new Vector2Int(UnityEngine.Random.Range(0, field.Width),
                UnityEngine.Random.Range(0, field.Depth));
            Vector3 candidate = field.CellCenter(cell);
            candidate.y = field.Origin.y;
            if (NavMesh.SamplePosition(candidate, out var hit, navMeshSampleDistance, NavMesh.AllAreas))
            {
                position = hit.position;
                return true;
            }
            position = candidate;
            return true;
        }
        position = default;
        return false;
    }

    private void OnBoarDespawned(BoarController boar)
    {
        if (boar) boar.Despawned -= OnBoarDespawned;
        if (activeBoar == boar) activeBoar = null;
    }

    private void OnDisable()
    {
        if (clock) clock.TimeAdvanced -= CheckAppearance;
    }
}
