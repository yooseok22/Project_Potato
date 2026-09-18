using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class ThirdPersonPlayerController : MonoBehaviour
{
    [SerializeField] private QuarterViewCamera viewCamera;
    [SerializeField, Min(0f)] private float walkSpeed = 3f;
    [SerializeField, Min(0f)] private float sprintSpeed = 6f;
    [SerializeField, Min(0f)] private float acceleration = 20f;
    [SerializeField, Min(0f)] private float turnSpeed = 720f;
    [SerializeField] private float gravity = -20f;
    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private bool captured;
    public bool CursorCapturedThisFrame { get; private set; }

    public void Configure(QuarterViewCamera cameraRig) => viewCamera = cameraRig;
    private void Awake() => controller = GetComponent<CharacterController>();
    private void Start() => SetCapture(true);

    private void Update()
    {
        CursorCapturedThisFrame = false;
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCapture(false);
        else if (!captured && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            SetCapture(true);
            CursorCapturedThisFrame = true;
        }

        bool active = captured && Cursor.lockState == CursorLockMode.Locked && Application.isFocused;
        Vector2 move = Vector2.zero;
        bool sprint = false;
        if (active)
        {
            if (mouse != null && viewCamera) viewCamera.ApplyLook(mouse.delta.ReadValue());
            if (keyboard != null)
            {
                move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            }
        }
        SimulateMove(move, sprint, Time.deltaTime, active);
    }

    public Vector3 GetMoveDirection(Vector2 input)
    {
        Vector2 clamped = Vector2.ClampMagnitude(input, 1f);
        return (viewCamera ? viewCamera.PlanarRotation : Quaternion.identity) * new Vector3(clamped.x, 0f, clamped.y);
    }

    public void SimulateMove(Vector2 input, bool sprint, float deltaTime, bool rotateToMovement = true)
    {
        if (!controller) controller = GetComponent<CharacterController>();
        if (!controller.enabled || deltaTime <= 0f) return;
        Vector3 direction = GetMoveDirection(input);
        // Camera orbit is independent; only movement changes the character's facing.
        if (rotateToMovement && direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * deltaTime);
        Vector3 targetVelocity = direction * (sprint ? sprintSpeed : walkSpeed);
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, acceleration * deltaTime);
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity = Mathf.Max(verticalVelocity + gravity * deltaTime, -50f);
        CollisionFlags collisions = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);
        if ((collisions & CollisionFlags.Below) != 0) verticalVelocity = -2f;
    }

    private void SetCapture(bool value)
    {
        captured = value;
        Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !value;
        if (!value) horizontalVelocity = Vector3.zero;
    }

    private void OnApplicationFocus(bool focused) { if (!focused) SetCapture(false); }
    private void OnDisable() { SetCapture(false); horizontalVelocity = Vector3.zero; verticalVelocity = 0f; }
}
