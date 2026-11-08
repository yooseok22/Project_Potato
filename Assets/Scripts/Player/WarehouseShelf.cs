using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class WarehouseShelf : MonoBehaviour, IPlayerInteractable
{
    public const int ShelfCapacity = 50;
    [SerializeField] private string shelfId;
    [SerializeField] private WarehouseStorageArea warehouse;
    private PotatoInventory inventory;
    private PotatoStorageSaveAdapter persistence;
    public PotatoInventory Inventory => inventory ??= new PotatoInventory("shelf:" + warehouse.WarehouseId + ":" + shelfId, ShelfCapacity);
    public WarehouseStorageArea Warehouse => warehouse;
    public string ShelfId => shelfId;
    public string DisplayName => "선반 " + shelfId;
    public string Prompt => $"{DisplayName} · 감자 {Inventory.Count}/{ShelfCapacity}\n" +
        (Inventory.Count > 0 ? "[E] 감자 꺼내기" + (Inventory.Free > 0 ? "   [Shift+E] 바구니 감자 보관" : "") : "[E] 바구니 감자 보관");
    public bool RequiresHold => false;
    public float HoldDuration => 0f;

    public void Configure(WarehouseStorageArea area, string id) { warehouse = area; shelfId = id; inventory = null; }
    private void Start()
    {
        persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        if (persistence) persistence.Register(Inventory);
    }

    public bool CanInteract(PlayerInteractionController player)
    {
        if (!isActiveAndEnabled || !player || !warehouse || !warehouse.Contains(player.transform.position)) return false;
        var basket = player.GetComponent<PlayerBasketInventory>();
        if (!persistence) persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        return basket && basket.IsLocalPlayer && player.GetComponent<FirstPersonPlayerController>().CanUseGameplayInput &&
            ((Inventory.Count > 0 && basket.Inventory.Free > 0) || (basket.Inventory.Count > 0 && Inventory.Free > 0)) &&
            persistence && persistence.IsReady && persistence.IsOffline;
    }

    public void Interact(PlayerInteractionController player)
    {
        if (!CanInteract(player)) return;
        var keyboard = Keyboard.current;
        bool deposit = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        if (Inventory.Count > 0 && !deposit)
        {
            var withdrawal = player.GetComponent<WarehouseWithdrawalController>();
            if (!withdrawal) withdrawal = player.gameObject.AddComponent<WarehouseWithdrawalController>();
            withdrawal.TryOpen(this);
            return;
        }
        var basket = player.GetComponent<PlayerBasketInventory>();
        if (PotatoStorageService.TryDeposit(basket.Inventory, Inventory, persistence.SaveActive, out int moved))
            Debug.Log($"[창고] {DisplayName}에 {moved}개 보관. 선반={Inventory.Count}/{ShelfCapacity}, 바구니={basket.Inventory.Count}/{PlayerBasketInventory.BasketCapacity}", this);
        else Debug.LogWarning("[창고] 저장 실패로 이동을 취소했습니다. " + persistence.LastError, this);
    }

    private void OnDestroy() { if (persistence) persistence.Unregister(inventory); }
}
