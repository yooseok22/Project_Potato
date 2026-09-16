using UnityEngine;

public sealed class MultiplayerSpawnPoints : MonoBehaviour
{
    [SerializeField] private Transform[] points = new Transform[4];
    public Transform[] Points => points;

    private void OnDrawGizmos()
    {
        if (points == null) return;
        Gizmos.color = Color.cyan;
        foreach (var point in points)
        {
            if (!point) continue;
            Gizmos.DrawWireSphere(point.position, 0.4f);
            Gizmos.DrawRay(point.position, point.forward);
        }
    }
}
