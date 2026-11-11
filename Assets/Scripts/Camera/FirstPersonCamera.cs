using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class FirstPersonCamera : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Transform eyeAnchor;
    [SerializeField, Min(0.001f)] private float mouseSensitivity = 0.12f;
    [SerializeField] private float standingEyeHeight = 0.82f;
    [SerializeField] private float crouchingEyeHeight = 0.3f;
    [SerializeField] private float initialPitch;
    [SerializeField] private float minimumPitch = -80f;
    [SerializeField] private float maximumPitch = 85f;
    [SerializeField, Min(0f)] private float stanceSmoothing = 14f;

    private float pitch;
    private float yaw;
    private float currentEyeHeight;
    private bool crouching;
    private float tabletSensitivity = 1f;
    public void SetTabletSensitivity(float value) => tabletSensitivity = Mathf.Clamp(value, 0.01f, 16f);

    public Quaternion PlanarRotation => Quaternion.Euler(0f, yaw, 0f);
    public Quaternion ViewRotation => Quaternion.Euler(pitch, yaw, 0f);
    public Transform Target => player;

    public void Configure(Transform target, Transform targetEye = null)
    {
        player = target;
        eyeAnchor = targetEye;
        yaw = target ? target.eulerAngles.y : transform.eulerAngles.y;
        pitch = Mathf.Clamp(initialPitch, minimumPitch, maximumPitch);
        currentEyeHeight = crouching ? crouchingEyeHeight : standingEyeHeight;
        RefreshPose(true);
    }

    public void ApplyLook(Vector2 mouseDelta)
    {
        yaw = Mathf.Repeat(yaw + mouseDelta.x * mouseSensitivity * tabletSensitivity, 360f);
        pitch = Mathf.Clamp(pitch - mouseDelta.y * mouseSensitivity * tabletSensitivity, minimumPitch, maximumPitch);
    }

    public void SetCrouching(bool value) => crouching = value;

    private void Awake()
    {
        yaw = transform.eulerAngles.y;
        pitch = NormalizePitch(transform.eulerAngles.x);
        currentEyeHeight = standingEyeHeight;
    }

    private void LateUpdate() => RefreshPose(false);

    public void RefreshPose(bool immediate = false)
    {
        if (!player) return;
        float targetHeight = crouching ? crouchingEyeHeight : standingEyeHeight;
        currentEyeHeight = immediate || stanceSmoothing <= 0f
            ? targetHeight
            : Mathf.Lerp(currentEyeHeight, targetHeight, 1f - Mathf.Exp(-stanceSmoothing * Time.deltaTime));
        Vector3 position = eyeAnchor
            ? eyeAnchor.position + Vector3.up * (currentEyeHeight - standingEyeHeight)
            : player.position + Vector3.up * currentEyeHeight;
        transform.SetPositionAndRotation(position, ViewRotation);
    }

    private static float NormalizePitch(float value)
    {
        if (value > 180f) value -= 360f;
        return value;
    }
}
