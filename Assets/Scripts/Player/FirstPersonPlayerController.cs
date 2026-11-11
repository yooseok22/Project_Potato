using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-100)]
public sealed class FirstPersonPlayerController : MonoBehaviour
{
    [SerializeField] private FirstPersonCamera viewCamera;
    [SerializeField] private Transform eyeAnchor;
    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 3f;
    [SerializeField, Min(0f)] private float crouchSpeedMultiplier = 0.6f;
    [SerializeField, Min(0f)] private float sprintSpeedMultiplier = 1.8f;
    [SerializeField, Min(0f)] private float acceleration = 20f;
    [SerializeField] private float gravity = -20f;
    [SerializeField, Min(0f)] private float jumpHeight = 1f;
    [Header("Stance")]
    [SerializeField, Min(0.2f)] private float standingHeight = 2f;
    [SerializeField, Min(0.2f)] private float crouchingHeight = 1.2f;

    private CharacterController controller;
    private NetworkObject networkObject;
    private PlayerActionBlocker blocker;
    private PlayerStunController stun;
    private PlayerVisualController visuals;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private Vector3 standingCenter;
    private bool captured;
    private bool localConfigured;

    public bool CursorCapturedThisFrame { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsGrounded => controller && controller.enabled && controller.isGrounded;
    public bool IsLocallyControlled => !networkObject || !networkObject.IsSpawned || networkObject.IsOwner;
    public bool HasGameplayFocus => captured && Cursor.lockState == CursorLockMode.Locked && Application.isFocused;
    public bool CanUseGameplayInput => IsLocallyControlled && HasGameplayFocus && !(blocker && blocker.GameplayBlocked) && !(stun && stun.IsStunned);
    public bool CanUseItemInput => IsLocallyControlled && !(stun && stun.IsStunned);
    public FirstPersonCamera ViewCamera => viewCamera;

    public void Configure(FirstPersonCamera cameraRig)
    {
        viewCamera = cameraRig;
        if (viewCamera) viewCamera.Configure(transform, eyeAnchor);
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        networkObject = GetComponent<NetworkObject>();
        blocker = GetComponent<PlayerActionBlocker>();
        stun = GetComponent<PlayerStunController>();
        visuals = GetComponent<PlayerVisualController>();
        standingCenter = controller.center;
        standingHeight = Mathf.Max(standingHeight, controller.radius * 2f);
        crouchingHeight = Mathf.Clamp(crouchingHeight, controller.radius * 2f, standingHeight);
    }

    private void Start()
    {
        if (IsLocallyControlled) ConfigureLocalPlayer();
    }

    private void Update()
    {
        CursorCapturedThisFrame = false;
        if (!IsLocallyControlled)
        {
            if (captured) SetCapture(false);
            return;
        }
        if (!localConfigured) ConfigureLocalPlayer();

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCapture(false);
        else if (!captured && mouse != null && mouse.leftButton.wasPressedThisFrame && !(blocker && blocker.GameplayBlocked))
        {
            SetCapture(true);
            CursorCapturedThisFrame = true;
        }

        bool active = CanUseGameplayInput;
        Vector2 move = Vector2.zero;
        bool crouch = false;
        bool sprint = false;
        bool jump = false;
        if (active)
        {
            if (mouse != null && viewCamera) viewCamera.ApplyLook(mouse.delta.ReadValue());
            if (keyboard != null)
            {
                move = new Vector2(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                crouch = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
                sprint = !crouch && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
                jump = keyboard.spaceKey.wasPressedThisFrame;
            }
        }
        SimulateMove(move, sprint, crouch, jump, Time.deltaTime);
    }

    private void ConfigureLocalPlayer()
    {
        localConfigured = true;
        if (!viewCamera)
        {
            var camera = Camera.main;
            if (camera)
            {
                viewCamera = camera.GetComponent<FirstPersonCamera>();
                if (!viewCamera) viewCamera = camera.gameObject.AddComponent<FirstPersonCamera>();
            }
        }
        if (viewCamera) viewCamera.Configure(transform, eyeAnchor);
        if (visuals) visuals.Configure(viewCamera ? viewCamera.GetComponent<Camera>() : Camera.main, true);
        SetCapture(true);
    }

    public Vector3 GetMoveDirection(Vector2 input)
    {
        Vector2 clamped = Vector2.ClampMagnitude(input, 1f);
        return (viewCamera ? viewCamera.PlanarRotation : transform.rotation) * new Vector3(clamped.x, 0f, clamped.y);
    }

    public void SimulateMove(Vector2 input, bool sprint, float deltaTime, bool rotateToMovement = true)
        => SimulateMove(input, sprint, false, false, deltaTime);

    public void SimulateMove(Vector2 input, bool sprint, bool crouch, bool jump, float deltaTime)
    {
        if (!controller) controller = GetComponent<CharacterController>();
        if (!controller.enabled || deltaTime <= 0f) return;

        SetCrouching(crouch || (IsCrouching && !CanStand()));
        IsSprinting = sprint && !IsCrouching && input.sqrMagnitude > 0.001f;
        if (viewCamera)
        {
            transform.rotation = viewCamera.PlanarRotation;
            viewCamera.SetCrouching(IsCrouching);
        }

        Vector3 direction = GetMoveDirection(input);
        float speed = walkSpeed * (IsCrouching ? crouchSpeedMultiplier : IsSprinting ? sprintSpeedMultiplier : 1f);
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, direction * speed, acceleration * deltaTime);

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        if (jump && controller.isGrounded && !IsCrouching)
            verticalVelocity = Mathf.Sqrt(Mathf.Max(0f, -2f * gravity * jumpHeight));
        verticalVelocity = Mathf.Max(verticalVelocity + gravity * deltaTime, -50f);

        CollisionFlags collisions = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);
        if ((collisions & CollisionFlags.Below) != 0) verticalVelocity = -2f;
        bool jumping = !controller.isGrounded && verticalVelocity > -0.5f;
        if (visuals) visuals.SetPose(jumping, IsCrouching, Mathf.Clamp01(horizontalVelocity.magnitude / Mathf.Max(0.01f, walkSpeed)));
    }

    public void StopMotion()
    {
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    public void SetBasketSelectionOpen(bool open)
    {
        if (!IsLocallyControlled) return;
        blocker.SetBlocked(PlayerActionBlockReason.BasketSelection, open);
        StopMotion();
        SetCapture(!open && isActiveAndEnabled && Application.isFocused && !blocker.GameplayBlocked);
        if (!open) CursorCapturedThisFrame = true; // Consume the closing potato click before tools update.
    }

    public void SetTabletInputOpen(bool open)
    {
        if (!IsLocallyControlled) return;
        StopMotion();
        SetCapture(!open && isActiveAndEnabled && Application.isFocused && !blocker.GameplayBlocked);
        if (!open) CursorCapturedThisFrame = true;
    }

    private void SetCrouching(bool value)
    {
        if (IsCrouching == value) return;
        IsCrouching = value;
        float height = value ? crouchingHeight : standingHeight;
        controller.height = height;
        controller.center = standingCenter + Vector3.down * ((standingHeight - height) * 0.5f);
    }

    private bool CanStand()
    {
        float radius = Mathf.Max(0.01f, controller.radius - controller.skinWidth);
        float expansion = standingHeight - controller.height;
        if (expansion <= 0.001f) return true;
        Vector3 currentTopSphere = transform.TransformPoint(controller.center +
            Vector3.up * Mathf.Max(0f, controller.height * 0.5f - radius));
        foreach (var hit in Physics.SphereCastAll(currentTopSphere, radius, Vector3.up, expansion,
                     Physics.AllLayers, QueryTriggerInteraction.Ignore))
            if (hit.collider != controller && !hit.collider.transform.IsChildOf(transform)) return false;
        return true;
    }

    private void SetCapture(bool value)
    {
        if (!IsLocallyControlled && value) return;
        captured = value;
        Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !value;
        if (!value) horizontalVelocity = Vector3.zero;
    }

    private void OnApplicationFocus(bool focused) { if (!focused) SetCapture(false); }
    private void OnDisable()
    {
        if (IsLocallyControlled) SetCapture(false);
        StopMotion();
    }
}
