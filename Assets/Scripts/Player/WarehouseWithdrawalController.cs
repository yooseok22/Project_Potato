using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Local, cursor-driven withdrawal from one warehouse compartment.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerBasketInventory), typeof(FirstPersonPlayerController), typeof(PlayerActionBlocker))]
[DefaultExecutionOrder(-40)]
public sealed class WarehouseWithdrawalController : MonoBehaviour
{
    private PlayerBasketInventory basket;
    private FirstPersonPlayerController player;
    private PlayerActionBlocker blocker;
    private PlayerItemController items;
    private PlayerVisualController visuals;
    private BasketProcessingController carriedBasket;
    private PotatoStorageSaveAdapter persistence;
    private WarehouseWithdrawalView view;
    private WarehouseShelf shelf;
    private Camera camera;
    private FirstPersonCamera cameraRig;
    private bool cameraRigWasEnabled;
    private Vector3 previousCameraPosition;
    private Quaternion previousCameraRotation;
    private PlayerItemSlot previousSlot;
    private int basketRevision, shelfRevision, openedFrame;

    public bool IsOpen { get; private set; }
    public WarehouseShelf CurrentShelf => shelf;

    private void Awake()
    {
        basket = GetComponent<PlayerBasketInventory>();
        player = GetComponent<FirstPersonPlayerController>();
        blocker = GetComponent<PlayerActionBlocker>();
        items = GetComponent<PlayerItemController>();
        visuals = GetComponent<PlayerVisualController>();
        carriedBasket = GetComponent<BasketProcessingController>();
    }

    public bool TryOpen(WarehouseShelf target)
    {
        if (IsOpen || !player.IsLocallyControlled || !player.CanUseGameplayInput || !target ||
            !target.isActiveAndEnabled || !target.Warehouse || !target.Warehouse.Contains(transform.position)) return false;
        if (basket.Inventory.Free == 0) return Reject("바구니가 가득 찼습니다. 감자를 먼저 비워 주세요.");
        if (target.Inventory.Count == 0) return Reject("선반이 비어 있습니다.");
        if (items && (items.IsAimingThrow || items.HasItem(PlayerItemSlot.Hands)))
            return Reject("손에 든 물건을 내려놓고 감자를 꺼내세요.");
        if (!persistence) persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        if (!persistence || !persistence.IsReady || !persistence.IsOffline)
            return Reject("감자 저장 연결이 준비되지 않았습니다.");
        camera = player.ViewCamera ? player.ViewCamera.GetComponent<Camera>() : Camera.main;
        if (!camera) return false;
        if (!carriedBasket) carriedBasket = gameObject.AddComponent<BasketProcessingController>();
        carriedBasket.RefreshCarriedBasket();
        if (!view) view = gameObject.AddComponent<WarehouseWithdrawalView>();
        view.Configure(camera);
        shelf = target;
        persistence.Register(shelf.Inventory);
        persistence.Register(basket.Inventory);
        basketRevision = basket.Inventory.Revision;
        shelfRevision = shelf.Inventory.Revision;
        previousSlot = items ? items.SelectedSlot : PlayerItemSlot.Hands;
        items?.SelectSlot(PlayerItemSlot.Hands);
        visuals?.SetHandsVisible(false);
        previousCameraPosition = camera.transform.position;
        previousCameraRotation = camera.transform.rotation;
        cameraRig = camera.GetComponent<FirstPersonCamera>();
        cameraRigWasEnabled = cameraRig && cameraRig.enabled;
        if (cameraRig) cameraRig.enabled = false;
        player.SetBasketSelectionOpen(true);
        IsOpen = true;
        openedFrame = Time.frameCount;
        view.Open(shelf, basket.Inventory.Count);
        return true;
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (!shelf || !shelf.isActiveAndEnabled || !shelf.Warehouse || !shelf.Warehouse.Contains(transform.position) ||
            !player.isActiveAndEnabled || !player.IsLocallyControlled || !Application.isFocused || !camera || !view ||
            !persistence || !persistence.IsReady || !persistence.IsOffline ||
            (blocker.ActiveReasons & ~PlayerActionBlockReason.BasketSelection) != PlayerActionBlockReason.None ||
            basket.Inventory.Revision != basketRevision || shelf.Inventory.Revision != shelfRevision)
        { Close(); return; }
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (Time.frameCount == openedFrame) return;
        var mouse = Mouse.current;
        if (mouse == null) return;
        long id = view.Hover(mouse.position.ReadValue());
        view.SetHover(id, mouse.position.ReadValue());
        if (id != 0 && mouse.leftButton.wasPressedThisFrame) TrySelect(id);
    }

    public bool TrySelect(long potatoId)
    {
        if (!IsOpen || !shelf || !persistence || !persistence.IsReady || !persistence.IsOffline ||
            (blocker.ActiveReasons & ~PlayerActionBlockReason.BasketSelection) != PlayerActionBlockReason.None) return false;
        if (basket.Inventory.Revision != basketRevision || shelf.Inventory.Revision != shelfRevision) { Close(); return false; }
        var potato = shelf.Inventory.FindPotato(potatoId);
        if (potato == null) return false;
        if (!PotatoStorageService.TryWithdraw(shelf.Inventory, basket.Inventory, potatoId, persistence.SaveActive, out string error))
        {
            basketRevision = basket.Inventory.Revision;
            shelfRevision = shelf.Inventory.Revision;
            view.SetMessage(error);
            return false;
        }
        basketRevision = basket.Inventory.Revision;
        shelfRevision = shelf.Inventory.Revision;
        view.AnimateTransfer(potatoId, carriedBasket.CarriedBasketPosition);
        carriedBasket.RefreshCarriedBasket();
        view.SetHeader(shelf, basket.Inventory.Count);
        view.SetMessage(potato.DisplayName + " 1개를 바구니에 담았습니다.");
        if (basket.Inventory.Free == 0 || shelf.Inventory.Count == 0) Close();
        return true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (view) view.Hide();
        if (camera) camera.transform.SetPositionAndRotation(previousCameraPosition, previousCameraRotation);
        if (cameraRig) cameraRig.enabled = cameraRigWasEnabled;
        visuals?.SetHandsVisible(true);
        if (items) items.SelectSlot(previousSlot);
        player.SetBasketSelectionOpen(false);
        shelf = null;
    }

    private bool Reject(string message) { Debug.Log("[창고 꺼내기] " + message, this); return false; }
    private void OnDisable() => Close();
}
