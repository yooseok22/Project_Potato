using System;
using UnityEngine;

/// <summary>Assign a permanent unique ID to objects that may be renamed, reparented or spawned.</summary>
[DisallowMultipleComponent]
public sealed class SaveIdentity : MonoBehaviour
{
    [SerializeField] private string id;
    public string Id => id;

    private void Reset() => id = Guid.NewGuid().ToString("N");

    public static string Key(Component component)
    {
        if (!component) throw new InvalidOperationException("A saved object was destroyed.");
        var identity = component.GetComponent<SaveIdentity>();
        string key;
        if (identity && !string.IsNullOrWhiteSpace(identity.Id)) key = "id:" + identity.Id;
        else
        {
            key = "";
            for (var current = component.transform; current; current = current.parent)
                key = "/" + Uri.EscapeDataString(current.name) + key;
            key = component.gameObject.scene.path + key;
        }
        return key + ":" + component.GetType().Name;
    }
}
