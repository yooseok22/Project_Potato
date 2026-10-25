using UnityEngine;

/// <summary>One damage event per local player's grounded cell entry or landing, including re-entry.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(250)]
public sealed class CropTrampleController : MonoBehaviour
{
    [SerializeField] private PotatoFarmingSystem farmingSystem;
    [SerializeField, Min(0f)] private float maximumGroundHeight = 1.2f;
    [SerializeField, Min(0f)] private float airborneResetSeconds = 0.15f;
    private FirstPersonPlayerController player;
    private CharacterController character;
    private GroundGrid[] grounds;
    private GroundGrid contactedGround;
    private Vector2Int contactedCell;
    private float airborneSeconds;

    public void Configure(PotatoFarmingSystem system)
    {
        farmingSystem = system;
        grounds = FindObjectsByType<GroundGrid>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private void Awake()
    {
        player = GetComponent<FirstPersonPlayerController>();
        character = GetComponent<CharacterController>();
        Configure(farmingSystem ? farmingSystem : FindAnyObjectByType<PotatoFarmingSystem>());
    }

    private void LateUpdate()
    {
        if (!player || !player.IsLocallyControlled)
        {
            SetFootContact(null, default, false);
            airborneSeconds = 0f;
            return;
        }
        if (!player.IsGrounded)
        {
            ObserveGroundContact(null, default, false, Time.deltaTime);
            return;
        }
        if (grounds == null) grounds = FindObjectsByType<GroundGrid>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Vector3 foot = character ? new Vector3(transform.position.x, character.bounds.min.y, transform.position.z) : transform.position;
        foreach (var ground in grounds)
        {
            if (!ground || !ground.isActiveAndEnabled || Mathf.Abs(foot.y - ground.Origin.y) > maximumGroundHeight) continue;
            var cell = ground.WorldToCell(foot);
            if (!ground.ContainsCell(cell)) continue;
            ObserveGroundContact(ground, cell, true, Time.deltaTime);
            return;
        }
        SetFootContact(null, default, false);
    }

    public bool ObserveGroundContact(GroundGrid ground, Vector2Int cell, bool grounded, float deltaSeconds)
    {
        if (!grounded)
        {
            // CharacterController can flicker off the ground for a frame while crossing a soil mound.
            // Preserve this contact; real jumps still reset after sustained time in the air.
            airborneSeconds += Mathf.Max(0f, deltaSeconds);
            if (airborneSeconds >= airborneResetSeconds) SetFootContact(null, default, false);
            return false;
        }
        airborneSeconds = 0f;
        return SetFootContact(ground, cell, true);
    }

    public bool SetFootContact(GroundGrid ground, Vector2Int cell, bool touching)
    {
        if (!touching || !ground || !ground.ContainsCell(cell))
        {
            contactedGround = null;
            return false;
        }
        if (contactedGround == ground && contactedCell == cell) return false;
        contactedGround = ground;
        contactedCell = cell;
        if (!farmingSystem || !farmingSystem.TryGetFarmingTile(ground, cell, out var crop) || crop.IsDead) return false;
        float previousHealth = crop.CurrentHealth;
        if (!farmingSystem.TryDamage(ground, cell, 10f)) return false;
        Debug.Log($"[Potato Trample] Ground='{ground.name}', Cell={cell}, 발로 밟힘 -10, HP={previousHealth:0.#} → {crop.CurrentHealth:0.#}", this);
        return true;
    }

    private void OnDisable()
    {
        SetFootContact(null, default, false);
        airborneSeconds = 0f;
    }
}
