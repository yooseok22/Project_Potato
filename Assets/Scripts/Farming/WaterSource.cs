using UnityEngine;

[DisallowMultipleComponent]
public sealed class WaterSource : MonoBehaviour
{
    [SerializeField] private bool hasWater = true;
    public bool HasWater => hasWater;
}
