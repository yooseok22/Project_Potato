using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerActionBlocker), typeof(FirstPersonPlayerController))]
public sealed class PlayerTabletController : MonoBehaviour
{
    private PlayerActionBlocker blocker;
    private FirstPersonPlayerController player;
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        blocker = GetComponent<PlayerActionBlocker>();
        player = GetComponent<FirstPersonPlayerController>();
    }

    private void Update()
    {
        if (!player.IsLocallyControlled || Keyboard.current == null) return;
        if (Keyboard.current.tabKey.wasPressedThisFrame) SetOpen(!IsOpen);
        else if (IsOpen && Keyboard.current.escapeKey.wasPressedThisFrame) SetOpen(false);
    }

    public void SetOpen(bool value)
    {
        if (IsOpen == value) return;
        IsOpen = value;
        blocker.SetTabletOpen(value);
        Debug.Log(value ? "[Tablet] 태블릿 열림" : "[Tablet] 태블릿 닫힘", this);
    }

    private void OnDisable()
    {
        if (IsOpen) SetOpen(false);
    }
}
