using UnityEngine;

[DisallowMultipleComponent]
public sealed class WarehouseStorageArea : MonoBehaviour
{
    [SerializeField] private string warehouseId;
    [SerializeField] private Bounds interior;
    public string WarehouseId => warehouseId;
    public Bounds Interior => interior;

    public void Configure(string id, Bounds bounds) { warehouseId = id; interior = bounds; }
    public bool Contains(Vector3 worldPosition) => interior.Contains(transform.InverseTransformPoint(worldPosition));
}
