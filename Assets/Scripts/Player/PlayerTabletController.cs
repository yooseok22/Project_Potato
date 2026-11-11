using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerActionBlocker), typeof(FirstPersonPlayerController))]
public sealed class PlayerTabletController : MonoBehaviour
{
    [SerializeField] private TabletView viewPrefab;
    private TabletView view;
    private PlayerActionBlocker blocker;
    private FirstPersonPlayerController player;
    public bool IsOpen { get; private set; }
    public TabletView View => view;
    public void Configure(TabletView prefab) { viewPrefab = prefab; }

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
        if (value)
        {
            if (!player.IsLocallyControlled || blocker.GameplayBlocked || !viewPrefab) return;
            var automation = GetComponent<AutomationPlayerController>();
            if (automation && automation.ConsumesGameplayActions) return;
            if (!UnityEngine.EventSystems.EventSystem.current && !FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>())
            {
                var events = new GameObject("TabletEventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            }
            if (!view) { view = Instantiate(viewPrefab); view.name = "TabletUI"; }
        }
        IsOpen = value;
        blocker.SetTabletOpen(value);
        player.SetTabletInputOpen(value);
        if (value) view.Open(this, GetComponent<TabletGameState>(), GetComponent<TabletPreferences>());
        else if (view) view.Close();
    }

    private void OnDisable()
    {
        if (IsOpen) SetOpen(false);
    }
    private void OnDestroy() { if (view) Destroy(view.gameObject); }
}
