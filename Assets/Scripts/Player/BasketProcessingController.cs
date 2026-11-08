using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Owns the local basket selection mode. Selection clicks never reach tools or world interactions.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerBasketInventory), typeof(FirstPersonPlayerController), typeof(PlayerActionBlocker))]
[DefaultExecutionOrder(-40)]
public sealed class BasketProcessingController : MonoBehaviour
{
    private readonly BasketPotatoSelection selection = new();
    private PlayerBasketInventory basket;
    private FirstPersonPlayerController player;
    private PlayerItemController items;
    private PlayerVisualController visuals;
    private PlayerActionBlocker blocker;
    private PotatoStorageSaveAdapter persistence;
    private PotatoStarchProcessor processor;
    private BasketProcessingView view;
    private PlayerItemSlot previousSlot;
    private int inventoryRevision, openedFrame;
    private float shakeRemaining;
    public bool IsOpen { get; private set; }
    public bool IsShaking => shakeRemaining > 0f;
    public BasketPotatoSelection Selection => selection;
    public Vector3 CarriedBasketPosition => view ? view.BasketPosition : transform.position + Vector3.up;

    private void Awake()
    {
        basket = GetComponent<PlayerBasketInventory>();
        player = GetComponent<FirstPersonPlayerController>();
        items = GetComponent<PlayerItemController>();
        visuals = GetComponent<PlayerVisualController>();
        blocker = GetComponent<PlayerActionBlocker>();
    }

    private void Start()
    {
        if (player.IsLocallyControlled) RefreshCarriedBasket();
    }

    private void EnsureView()
    {
        if (view) return;
        var camera = player.ViewCamera ? player.ViewCamera.GetComponent<Camera>() : Camera.main;
        if (!camera) return;
        view = gameObject.AddComponent<BasketProcessingView>();
        view.Configure(camera);
    }

    public bool TryOpen(PotatoStarchProcessor target)
    {
        if (IsOpen || !player.IsLocallyControlled || !player.CanUseGameplayInput || !target) return false;
        if (items && (items.IsAimingThrow || items.HasItem(PlayerItemSlot.Hands)))
            return Reject("손에 든 물건을 내려놓고 바구니를 꺼내세요.");
        if (basket.Inventory.Count == 0) return Reject("바구니가 비어 있습니다.");
        if (!persistence) persistence = FindAnyObjectByType<PotatoStorageSaveAdapter>();
        if (!persistence || !persistence.IsReady || !persistence.IsOffline)
            return Reject("감자 저장 연결이 준비되지 않았습니다.");
        EnsureView();
        if (!view || !target.TryLoadBlender()) return false;
        persistence.Register(target);
        processor = target;
        previousSlot = items ? items.SelectedSlot : PlayerItemSlot.Hands;
        items?.SelectSlot(PlayerItemSlot.Hands);
        visuals?.SetHandsVisible(false);
        player.SetBasketSelectionOpen(true);
        IsOpen = true;
        openedFrame = Time.frameCount;
        inventoryRevision = basket.Inventory.Revision;
        // Retain the current presentation on closing/reopening; E cannot bypass the reroll cooldown.
        bool stale = false;
        foreach (var potato in selection.Visible)
            if (basket.Inventory.FindPotato(potato.Id) == null) stale = true;
        if (stale) selection.Clear();
        if (selection.Visible.Count == 0) selection.Reroll(basket.Inventory);
        view.SetOpen(true);
        Refresh();
        return true;
    }

    private void Update()
    {
        if (!IsOpen)
        {
            if (player.IsLocallyControlled && (!view || inventoryRevision != basket.Inventory.Revision)) RefreshCarriedBasket();
            return;
        }
        var otherBlocks = blocker.ActiveReasons & ~PlayerActionBlockReason.BasketSelection;
        if (!processor || !processor.isActiveAndEnabled || !player.IsLocallyControlled || !Application.isFocused ||
            otherBlocks != PlayerActionBlockReason.None || basket.Inventory.Revision != inventoryRevision)
        { Close(); return; }
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (Time.frameCount == openedFrame) return;
        if (IsShaking)
        {
            shakeRemaining = Mathf.Max(0f, shakeRemaining - Time.unscaledDeltaTime);
            view.SetShake(shakeRemaining > 0f ? 1f - shakeRemaining / 0.45f : 0f, shakeRemaining > 0f);
            if (!IsShaking) { selection.Reroll(basket.Inventory); Refresh(); }
            return;
        }
        var mouse = Mouse.current;
        int hover = mouse != null ? view.Hover(mouse.position.ReadValue()) : -1;
        view.SetHover(hover, mouse != null ? mouse.position.ReadValue() : Vector2.zero);
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame) { BeginReroll(); return; }
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && hover >= 0) TrySelect(hover);
        if (IsOpen) RefreshHeader();
    }

    public bool BeginReroll()
    {
        if (!IsOpen || IsShaking || basket.Inventory.Count == 0) return false;
        shakeRemaining = 0.45f;
        view.SetHover(-1, Vector2.zero);
        view.SetMessage("바구니를 흔드는 중…");
        return true;
    }

    public bool TrySelect(int index)
    {
        if (!IsOpen || IsShaking || index < 0 || index >= selection.Visible.Count) return false;
        var potato = selection.Visible[index];
        if (!PotatoProcessingService.TryInsert(basket.Inventory, potato.Id, processor, persistence.SaveActive, out string error))
        {
            inventoryRevision = basket.Inventory.Revision;
            view.SetMessage(error);
            return Reject(error);
        }
        selection.Remove(potato.Id);
        inventoryRevision = basket.Inventory.Revision;
        Refresh();
        if (processor.LoadedPotatoCount == processor.PotatoesPerBatch) Close();
        else view.SetMessage($"{potato.DisplayName} 1개 투입 완료");
        return true;
    }

    private void Refresh()
    {
        view.SetPotatoes(selection.Visible);
        RefreshHeader();
    }

    private void RefreshHeader() => view.SetHeader(processor.LoadedPotatoCount, processor.PotatoesPerBatch, basket.Inventory.Count,
        processor.BlenderState == StarchBlenderState.LidOpening);

    public void RefreshCarriedBasket()
    {
        if (IsOpen || !player.IsLocallyControlled) return;
        EnsureView();
        if (!view) return;
        view.SetPotatoes(basket.Inventory.Potatoes);
        inventoryRevision = basket.Inventory.Revision;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        shakeRemaining = 0f;
        if (view) { view.SetShake(0f, false); view.SetOpen(false); }
        RefreshCarriedBasket();
        visuals?.SetHandsVisible(true);
        if (items) items.SelectSlot(previousSlot);
        player.SetBasketSelectionOpen(false);
        processor = null;
    }

    private bool Reject(string message) { Debug.Log("[가공 바구니] " + message, this); return false; }
    private void OnDisable() => Close();
}
