using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class PlayerBasketInventory : MonoBehaviour
{
    public const int BasketCapacity = 10;
    [SerializeField] private string ownerId = "basket:local-player";
    private PotatoInventory inventory;
    private FirstPersonPlayerController player;
    private PotatoStorageSaveAdapter persistence;
    public PotatoInventory Inventory => inventory ??= new PotatoInventory(ownerId, BasketCapacity);
    public bool IsLocalPlayer => !GetComponent<FirstPersonPlayerController>() || GetComponent<FirstPersonPlayerController>().IsLocallyControlled;

    private void Awake() => player = GetComponent<FirstPersonPlayerController>();
    private void Start()
    {
        persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        if (IsLocalPlayer && persistence) persistence.Register(Inventory);
    }

    private void Update()
    {
        if (player && player.CanUseGameplayInput && Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame) PrintInventory();
    }

    public void PrintInventory() => Debug.Log(Inventory.Describe("바구니"), this);

    public bool TryHarvest(PotatoHarvestCoordinator harvest, GroundGrid ground, Vector2Int cell)
    {
        if (!IsLocalPlayer) return false;
        if (!persistence) persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        if (!persistence || !persistence.IsReady || !persistence.IsOffline)
        {
            Debug.LogWarning("[바구니] 저장 연결이 준비되지 않아 수확하지 않았습니다.", this);
            return false;
        }
        bool success = PotatoStorageService.TryHarvest(Inventory, harvest, ground, cell, persistence.SaveActive, out string error);
        if (!success && !string.IsNullOrEmpty(error)) Debug.LogWarning("[바구니] " + error, this);
        if (success) PrintInventory();
        return success;
    }

    private void OnDestroy() { if (persistence) persistence.Unregister(inventory); }
}
