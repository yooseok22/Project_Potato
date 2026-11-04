using System.Collections.Generic;
using UnityEngine;

/// <summary>Moving robot bodies pass through each other; docks and other world colliders remain solid.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class FarmRobotBody : MonoBehaviour
{
    private static readonly HashSet<FarmRobotBody> activeBodies = new();
    private Collider[] bodyColliders;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => activeBodies.Clear();

    private void OnEnable()
    {
        bodyColliders = GetComponentsInChildren<Collider>(true);
        activeBodies.RemoveWhere(body => !body);
        foreach (var other in activeBodies)
        {
            if (other == this) continue;
            foreach (var ownCollider in bodyColliders)
                foreach (var otherCollider in other.bodyColliders)
                    if (ownCollider && otherCollider) Physics.IgnoreCollision(ownCollider, otherCollider, true);
        }
        activeBodies.Add(this);
    }

    private void OnDisable() => activeBodies.Remove(this);
    private void OnDestroy() => activeBodies.Remove(this);
}
