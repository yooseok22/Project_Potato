using System;
using UnityEngine;

[Flags]
public enum PlayerActionBlockReason
{
    None = 0,
    Settings = 1 << 0,
    Tablet = 1 << 1,
    InteractionHold = 1 << 2,
    Cooking = 1 << 3,
    Stunned = 1 << 4,
    BasketSelection = 1 << 5
}

public sealed class PlayerActionBlocker : MonoBehaviour
{
    public PlayerActionBlockReason ActiveReasons { get; private set; }
    public bool GameplayBlocked => (ActiveReasons & ~PlayerActionBlockReason.InteractionHold) != 0;
    public bool CanDrop => (ActiveReasons & (PlayerActionBlockReason.Settings | PlayerActionBlockReason.Tablet |
        PlayerActionBlockReason.InteractionHold | PlayerActionBlockReason.Cooking | PlayerActionBlockReason.Stunned | PlayerActionBlockReason.BasketSelection)) == 0;

    public void SetBlocked(PlayerActionBlockReason reason, bool blocked)
    {
        if (blocked) ActiveReasons |= reason;
        else ActiveReasons &= ~reason;
    }

    public void SetSettingsOpen(bool value) => SetBlocked(PlayerActionBlockReason.Settings, value);
    public void SetTabletOpen(bool value) => SetBlocked(PlayerActionBlockReason.Tablet, value);
    public void SetCooking(bool value) => SetBlocked(PlayerActionBlockReason.Cooking, value);
}
