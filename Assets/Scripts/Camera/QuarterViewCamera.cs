using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class QuarterViewCamera : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private float playerFootOffset = 1f;
    [SerializeField, Range(-20f, 80f)] private float pitch = 30f;
    [SerializeField] private float yaw = 45f;
    [SerializeField, Min(1f)] private float distance = 5f;
    [SerializeField, Min(0f)] private float pivotHeight = 1.5f;
    [SerializeField] private float shoulderOffset = 0.8f;
    [SerializeField, Min(0.001f)] private float mouseSensitivity = 0.12f;
    [SerializeField] private float minimumPitch = -15f;
    [SerializeField] private float maximumPitch = 75f;

    public Quaternion PlanarRotation => Quaternion.Euler(0f, yaw, 0f);

    public void ApplyLook(Vector2 mouseDelta)
    {
        // Mouse delta is already a per-frame displacement, so do not multiply by deltaTime.
        yaw = Mathf.Repeat(yaw + mouseDelta.x * mouseSensitivity, 360f);
        pitch = Mathf.Clamp(pitch - mouseDelta.y * mouseSensitivity, minimumPitch, maximumPitch);
    }

    public void Configure(Transform target) { player = target; RefreshPose(); }
    private void LateUpdate() => RefreshPose();

    public void RefreshPose()
    {
        if (!player) return;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = player.position + Vector3.up * (pivotHeight - playerFootOffset);
        // Orbit around the player, not a point on the ground. Pitch now moves the farming ray nearer/farther.
        Vector3 shoulder = PlanarRotation * Vector3.right * shoulderOffset;
        transform.SetPositionAndRotation(pivot + shoulder - rotation * Vector3.forward * distance, rotation);
    }
}
