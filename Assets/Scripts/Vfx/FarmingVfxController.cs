using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PotatoFarmingSystem), typeof(PotatoHarvestCoordinator))]
public sealed class FarmingVfxController : MonoBehaviour
{
    [SerializeField] private GameObject boarCrashPrefab;
    [SerializeField] private GameObject bugPrefab;
    [SerializeField] private GameObject leafCrashPrefab;
    [SerializeField] private GameObject potatoCrashPrefab;
    [SerializeField] private Vector3 boarOffset = new(0f, 0.05f, 0f);
    [SerializeField] private Vector3 bugOffset = new(0f, -0.29f, 0f);
    [SerializeField] private Vector3 leafOffset = new(0f, 0.35f, 0f);
    [SerializeField] private Vector3 potatoOffset = new(0f, 0.05f, 0f);
    [SerializeField, Min(0f)] private float bugFadeOutSeconds = 0.6f;
    private readonly Dictionary<(GroundGrid, Vector2Int), ParticleVfxInstance> bugs = new();
    private PotatoFarmingSystem farm;
    private PotatoHarvestCoordinator harvest;

    public int ActiveBugCount => bugs.Count;

    public void Configure(GameObject boar, GameObject bug, GameObject leaf, GameObject potato)
    {
        boarCrashPrefab = boar;
        bugPrefab = bug;
        leafCrashPrefab = leaf;
        potatoCrashPrefab = potato;
        if (Application.isPlaying && isActiveAndEnabled) RestoreBugs();
    }

    private void OnEnable()
    {
        farm = GetComponent<PotatoFarmingSystem>();
        harvest = GetComponent<PotatoHarvestCoordinator>();
        farm.FarmingBugsChanged += SetBugs;
        farm.FarmingTileDied += RemoveBugs;
        farm.FarmingTileRemoved += RemoveBugs;
        farm.FarmingStateRestored += RestoreBugs;
        farm.FarmingCropDestroyedByBoar += PlayBoarCrash;
        harvest.LeavesCut += PlayLeafCrash;
        harvest.CropDiscarded += PlayPotatoCrash;
        if (Application.isPlaying) RestoreBugs();
    }

    private void PlayBoarCrash(GroundGrid ground, Vector2Int cell) => Play(boarCrashPrefab, ground, cell, boarOffset);
    private void PlayLeafCrash(GroundGrid ground, Vector2Int cell) => Play(leafCrashPrefab, ground, cell, leafOffset);
    private void PlayPotatoCrash(GroundGrid ground, Vector2Int cell) => Play(potatoCrashPrefab, ground, cell, potatoOffset);

    private void Play(GameObject prefab, GroundGrid ground, Vector2Int cell, Vector3 offset)
    {
        if (ground) ParticleVfxInstance.Spawn(prefab, ground.CellCenter(cell) + offset, transform, false);
    }

    private void SetBugs(GroundGrid ground, Vector2Int cell, bool active)
    {
        if (!active)
        {
            if (bugs.Remove((ground, cell), out var previous) && previous) previous.FadeOut(bugFadeOutSeconds);
            return;
        }
        var key = (ground, cell);
        if (!ground || bugs.ContainsKey(key) || !farm.TryGetFarmingTile(ground, cell, out var crop) ||
            crop.IsDead || !crop.HasBugs) return;
        var effect = ParticleVfxInstance.Spawn(bugPrefab, ground.CellCenter(cell) + bugOffset, transform, true);
        if (effect) bugs.Add(key, effect);
    }

    private void RemoveBugs(GroundGrid ground, Vector2Int cell)
    {
        if (!bugs.Remove((ground, cell), out var effect)) return;
        if (effect) effect.Release();
    }

    private void RestoreBugs()
    {
        ClearBugs();
        if (!farm || !farm.isActiveAndEnabled) return;
        foreach (var crop in farm.GetFarmingTiles())
            if (crop.HasBugs && !crop.IsDead) SetBugs(crop.Ground, crop.Cell, true);
    }

    private void ClearBugs()
    {
        foreach (var effect in bugs.Values) if (effect) effect.Release();
        bugs.Clear();
        // Loading or disabling must also clear any already dismissed bugs that are still fading.
        foreach (var effect in GetComponentsInChildren<ParticleVfxInstance>(true))
            if (effect.IsFading) effect.Release();
    }

    private void OnDisable()
    {
        if (farm)
        {
            farm.FarmingBugsChanged -= SetBugs;
            farm.FarmingTileDied -= RemoveBugs;
            farm.FarmingTileRemoved -= RemoveBugs;
            farm.FarmingStateRestored -= RestoreBugs;
            farm.FarmingCropDestroyedByBoar -= PlayBoarCrash;
        }
        if (harvest)
        {
            harvest.LeavesCut -= PlayLeafCrash;
            harvest.CropDiscarded -= PlayPotatoCrash;
        }
        ClearBugs();
        foreach (var effect in GetComponentsInChildren<ParticleVfxInstance>(true)) effect.Release();
    }
}
