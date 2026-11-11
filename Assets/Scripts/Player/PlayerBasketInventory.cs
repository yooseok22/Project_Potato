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
        if (!player || !player.CanUseGameplayInput || Keyboard.current == null) return;
        var keyboard = Keyboard.current;
        if (keyboard.iKey.wasPressedThisFrame) PrintInventory();
        if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame) TryFillDebugBasket();
    }

    public void PrintInventory() => Debug.Log(Inventory.Describe("바구니"), this);

    [ContextMenu("Debug/Fill Basket with Random Potatoes")]
    public void TryFillDebugBasket()
    {
        if (!Application.isPlaying || !IsLocalPlayer) return;
        if (!persistence) persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        if (!persistence || !persistence.IsReady || !persistence.IsOffline)
        {
            Debug.LogWarning("[바구니 테스트] 저장 연결이 준비되지 않았습니다.", this);
            return;
        }
        if (!PotatoStorageService.TryFillDebugBasket(Inventory, persistence.SaveActive, out int added, out string error))
        {
            Debug.Log("[바구니 테스트] " + error, this);
            return;
        }
        Debug.Log($"[바구니 테스트] 랜덤 감자 {added}개 추가. {Inventory.Count}/{BasketCapacity}", this);
        PrintInventory();
    }

    public bool TryHarvest(PotatoHarvestCoordinator harvest, GroundGrid ground, Vector2Int cell)
    {
        if (!IsLocalPlayer) return false;
        if (!persistence) persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        if (!persistence || !persistence.IsReady || !persistence.IsOffline)
        {
            Debug.LogWarning("[바구니] 저장 연결이 준비되지 않아 수확하지 않았습니다.", this);
            return false;
        }
        int previous = Inventory.Count;
        bool SaveHarvest() => TabletGameState.Local ? TabletGameState.Local.RecordHarvestAndSave(Inventory.Count - previous) : persistence.SaveActive();
        bool success = PotatoStorageService.TryHarvest(Inventory, harvest, ground, cell, SaveHarvest, out string error);
        if (!success && !string.IsNullOrEmpty(error)) Debug.LogWarning("[바구니] " + error, this);
        if (success) PrintInventory();
        return success;
    }

    private void OnDestroy() { if (persistence) persistence.Unregister(inventory); }
}
