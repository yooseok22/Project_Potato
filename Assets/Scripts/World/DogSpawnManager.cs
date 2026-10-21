using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class DogSpawnManager : MonoBehaviour
{
    [SerializeField] private DogHelperController dogPrefab;
    [SerializeField] private PotatoFarmingSystem farmingSystem;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private bool spawnOnStart;
    public DogHelperController ActiveDog { get; private set; }

    private void Start() { if (spawnOnStart) SpawnDog(); }

    [ContextMenu("Spawn Dog (Play Mode)")]
    public void SpawnDog()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play Mode to spawn the helper dog.", this); return; }
        if (ActiveDog) return;
        if (!dogPrefab) { Debug.LogWarning("Assign a helper dog prefab first.", this); return; }
        if (!farmingSystem) farmingSystem = FindAnyObjectByType<PotatoFarmingSystem>();
        Vector3 position = spawnPoint ? spawnPoint.position : transform.position;
        if (NavMesh.SamplePosition(position, out var hit, 5f, NavMesh.AllAreas)) position = hit.position;
        else { Debug.LogWarning("No walkable NavMesh near the dog spawn point. Wait for the bake or move the spawn point.", this); return; }
        ActiveDog = Instantiate(dogPrefab, position, Quaternion.identity);
        ActiveDog.Initialize(farmingSystem);
    }
}
