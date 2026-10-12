using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerItemSlot { Hands, Hoe, WateringCan, Sickle, PotatoFork }

public readonly struct PlayerItemActionContext
{
    public readonly GameObject Player;
    public readonly Vector3 Origin;
    public readonly Vector3 Direction;
    public readonly float Speed;

    public PlayerItemActionContext(GameObject player, Vector3 origin, Vector3 direction, float speed)
    {
        Player = player;
        Origin = origin;
        Direction = direction;
        Speed = speed;
    }
}

public interface IPlayerHeldItem
{
    float Mass { get; }
    bool CanDrop { get; }
    bool CanThrow { get; }
    bool TryDrop(PlayerItemActionContext context);
    bool TryThrow(PlayerItemActionContext context);
}

[RequireComponent(typeof(PlayerActionBlocker), typeof(FirstPersonPlayerController))]
public sealed class PlayerItemController : MonoBehaviour
{
    [SerializeField] private MonoBehaviour[] slotItems = new MonoBehaviour[5];
    [SerializeField, Min(0.1f)] private float throwSpeedAtOneKilogram = 10f;
    [SerializeField, Min(0.1f)] private float minimumThrowSpeed = 3f;
    [SerializeField, Min(0.1f)] private float maximumThrowSpeed = 16f;

    private FirstPersonPlayerController player;
    private PlayerActionBlocker blocker;
    private bool aimingThrow;

    public int SelectedIndex { get; private set; }
    public PlayerItemSlot SelectedSlot => (PlayerItemSlot)SelectedIndex;
    public bool IsAimingThrow => aimingThrow;
    public bool ConsumesPointerInput => aimingThrow;
    public Transform HeldItemAnchor { get; private set; }
    public event Action<PlayerItemSlot> SelectedSlotChanged;
    public event Action<PlayerItemSlot> DropRequested;
    public event Action<PlayerItemSlot, bool> ThrowAimingChanged;
    public event Action<PlayerItemSlot, PlayerItemActionContext> ThrowRequested;

    private void Awake()
    {
        player = GetComponent<FirstPersonPlayerController>();
        blocker = GetComponent<PlayerActionBlocker>();
        if (slotItems == null || slotItems.Length != 5) Array.Resize(ref slotItems, 5);
    }

    private void Start()
    {
        var visuals = GetComponent<PlayerVisualController>();
        HeldItemAnchor = visuals ? visuals.HeldItemAnchor : null;
        ApplySlotVisibility();
    }

    private void Update()
    {
        if (!player.IsLocallyControlled || !player.CanUseItemInput) return;
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (player.HasGameplayFocus && keyboard != null && !aimingThrow)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) SelectSlot(PlayerItemSlot.Hands);
            else if (keyboard.digit2Key.wasPressedThisFrame) SelectSlot(PlayerItemSlot.Hoe);
            else if (keyboard.digit3Key.wasPressedThisFrame) SelectSlot(PlayerItemSlot.WateringCan);
            else if (keyboard.digit4Key.wasPressedThisFrame) SelectSlot(PlayerItemSlot.Sickle);
            else if (keyboard.digit5Key.wasPressedThisFrame) SelectSlot(PlayerItemSlot.PotatoFork);
        }

        if (player.HasGameplayFocus && mouse != null && !aimingThrow)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll < -0.01f) SelectRelative(1);
            else if (scroll > 0.01f) SelectRelative(-1);
        }
        if (keyboard != null && keyboard.qKey.wasPressedThisFrame && blocker.CanDrop)
            TryDropSelected();

        var item = CurrentItem;
        if (!player.HasGameplayFocus || mouse == null || item == null || !item.CanThrow)
        {
            if (aimingThrow) CancelThrow();
            return;
        }
        if (!aimingThrow && mouse.rightButton.wasPressedThisFrame) BeginThrow();
        else if (aimingThrow && mouse.leftButton.wasPressedThisFrame) CancelThrow();
        else if (aimingThrow && mouse.rightButton.wasReleasedThisFrame) ReleaseThrow();
    }

    public void RegisterItem(PlayerItemSlot slot, MonoBehaviour item)
    {
        if (item && !(item is IPlayerHeldItem))
            throw new ArgumentException("Item must implement IPlayerHeldItem.", nameof(item));
        slotItems[(int)slot] = item;
        ApplySlotVisibility();
    }

    public bool TryHoldInHands(MonoBehaviour item)
    {
        if (!item || !(item is IPlayerHeldItem) || slotItems[(int)PlayerItemSlot.Hands]) return false;
        RegisterItem(PlayerItemSlot.Hands, item);
        SelectSlot(PlayerItemSlot.Hands);
        return true;
    }

    public bool TryReleaseFromHands(MonoBehaviour item)
    {
        int index = (int)PlayerItemSlot.Hands;
        if (!item || slotItems == null || slotItems[index] != item) return false;
        slotItems[index] = null;
        ApplySlotVisibility();
        return true;
    }

    public bool HasItem(PlayerItemSlot slot) => slotItems != null && slotItems[(int)slot];

    public void SelectSlot(PlayerItemSlot slot)
    {
        int index = (int)slot;
        if (SelectedIndex == index) return;
        if (aimingThrow) CancelThrow();
        SelectedIndex = index;
        ApplySlotVisibility();
        SelectedSlotChanged?.Invoke(SelectedSlot);
    }

    public void SelectRelative(int direction)
    {
        int count = Enum.GetValues(typeof(PlayerItemSlot)).Length;
        SelectSlot((PlayerItemSlot)((SelectedIndex + direction + count) % count));
    }

    private IPlayerHeldItem CurrentItem => slotItems != null && SelectedIndex < slotItems.Length
        ? slotItems[SelectedIndex] as IPlayerHeldItem : null;

    private void TryDropSelected()
    {
        var item = CurrentItem;
        if (item == null || !item.CanDrop) return;
        var context = CreateContext(0f);
        DropRequested?.Invoke(SelectedSlot);
        if (!item.TryDrop(context)) return;
        slotItems[SelectedIndex] = null;
        ApplySlotVisibility();
    }

    private void BeginThrow()
    {
        aimingThrow = true;
        ThrowAimingChanged?.Invoke(SelectedSlot, true);
    }

    private void CancelThrow()
    {
        if (!aimingThrow) return;
        aimingThrow = false;
        ThrowAimingChanged?.Invoke(SelectedSlot, false);
    }

    private void ReleaseThrow()
    {
        var item = CurrentItem;
        if (item == null || !item.CanThrow) { CancelThrow(); return; }
        float speed = Mathf.Clamp(throwSpeedAtOneKilogram / Mathf.Sqrt(Mathf.Max(0.05f, item.Mass)),
            minimumThrowSpeed, maximumThrowSpeed);
        var context = CreateContext(speed);
        aimingThrow = false;
        ThrowAimingChanged?.Invoke(SelectedSlot, false);
        ThrowRequested?.Invoke(SelectedSlot, context);
        if (!item.TryThrow(context)) return;
        slotItems[SelectedIndex] = null;
        ApplySlotVisibility();
    }

    private PlayerItemActionContext CreateContext(float speed)
    {
        var camera = player.ViewCamera ? player.ViewCamera.GetComponent<Camera>() : Camera.main;
        Transform source = camera ? camera.transform : transform;
        return new PlayerItemActionContext(gameObject, source.position, source.forward, speed);
    }

    private void ApplySlotVisibility()
    {
        if (slotItems == null) return;
        for (int index = 0; index < slotItems.Length; index++)
            if (slotItems[index]) slotItems[index].gameObject.SetActive(index == SelectedIndex);
    }

    private void OnDisable() => CancelThrow();
}
