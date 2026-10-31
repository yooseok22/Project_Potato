using System;
using TMPro;
using UnityEngine;

/// <summary>Shows counts while aiming inside the warehouse, even for empty/full shelves.</summary>
public sealed class ShelfHoverView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text basketLabel;
    [SerializeField] private float displayRange = 12f;
    private FirstPersonPlayerController player;
    private RaycastHit[] hits = new RaycastHit[16];
    public WarehouseShelf HoveredShelf { get; private set; }

    public void Configure(GameObject root, TMP_Text text, TMP_Text basketText) { panel = root; label = text; basketLabel = basketText; }

    private void LateUpdate()
    {
        if (!player || !player.IsLocallyControlled)
        {
            player = null;
            foreach (var candidate in FindObjectsByType<FirstPersonPlayerController>(FindObjectsSortMode.None))
                if (candidate.IsLocallyControlled) { player = candidate; break; }
        }
        HoveredShelf = null;
        if (player)
        {
            var basket = player.GetComponent<PlayerBasketInventory>();
            if (basketLabel && basket) basketLabel.text = $"[I] 바구니  {basket.Inventory.Count}/{PlayerBasketInventory.BasketCapacity}";
            var camera = player.ViewCamera ? player.ViewCamera.GetComponent<Camera>() : Camera.main;
            if (camera && player.HasGameplayFocus)
            {
                var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                int count;
                while ((count = Physics.RaycastNonAlloc(ray, hits, displayRange, ~0, QueryTriggerInteraction.Ignore)) == hits.Length)
                    Array.Resize(ref hits, hits.Length * 2);
                float closest = float.PositiveInfinity;
                Collider target = null;
                for (int i = 0; i < count; i++)
                    if (hits[i].collider && !hits[i].collider.transform.IsChildOf(player.transform) && hits[i].distance < closest)
                    { closest = hits[i].distance; target = hits[i].collider; }
                var shelf = target ? target.GetComponentInParent<WarehouseShelf>() : null;
                if (shelf && shelf.Warehouse && shelf.Warehouse.Contains(player.transform.position))
                {
                    HoveredShelf = shelf;
                    var interaction = player.GetComponent<PlayerInteractionController>();
                    string action = !basket || basket.Inventory.Count == 0 ? "바구니가 비어 있습니다" :
                        shelf.Inventory.Free == 0 ? "선반이 가득 찼습니다" :
                        interaction && ReferenceEquals(interaction.CurrentTarget, shelf) ? "[E] 바구니 감자 보관" : "가까이 다가가 보관하세요";
                    if (label) label.text = $"{shelf.DisplayName} · 감자 {shelf.Inventory.Count}/{WarehouseShelf.ShelfCapacity}\n{action}";
                }
            }
        }
        if (panel && panel.activeSelf != (HoveredShelf != null)) panel.SetActive(HoveredShelf != null);
    }
}
