using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;

[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshSurface))]
public sealed class RuntimeNavMeshBaker : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return null;
        GetComponent<NavMeshSurface>().BuildNavMesh();
    }
}
