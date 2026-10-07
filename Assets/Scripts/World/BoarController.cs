using System;
using UnityEngine;
using UnityEngine.AI;

public enum BoarState : byte
{
    SeekingCrop,
    AttackingCrop,
    ChasingPlayer,
    Charging,
    Fleeing
}

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent), typeof(BoarChargeHitbox))]
public sealed class BoarController : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [Header("Crop Attack")]
    [SerializeField, Min(0f)] private float cropDamage = 35f;
    [SerializeField, Min(0.1f)] private float cropAttackInterval = 1f;
    [SerializeField, Min(0.1f)] private float cropAttackDistance = 1.6f;
    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float searchSpeed = 3f;
    [SerializeField, Min(0.1f)] private float chaseSpeed = 4f;
    [SerializeField, Min(0.1f)] private float chargeSpeed = 8f;
    [SerializeField, Min(0.1f)] private float fleeSpeed = 6f;
    [SerializeField, Min(1f)] private float fleeDistance = 25f;
    [SerializeField, Min(0.1f)] private float fleeDuration = 5f;
    [Header("Charge")]
    [SerializeField, Min(0.1f)] private float chargeStartDistance = 8f;
    [SerializeField, Min(0.1f)] private float chargeDuration = 1.2f;
    [SerializeField, Min(0.1f)] private float chargeCooldown = 3f;

    private PotatoFarmingSystem farmingSystem;
    private GroundGrid[] allowedGrounds = Array.Empty<GroundGrid>();
    private NavMeshAgent agent;
    private BoarChargeHitbox chargeHitbox;
    private PotatoFarmingSystem.FarmingTile cropTarget;
    private Transform playerTarget;
    private Vector3 directDestination;
    private float nextCropAttackAt;
    private float nextChargeAt;
    private float stateEndsAt;
    private bool hasDestination;
    private Quaternion modelRotationOffset;

    public float CurrentHealth { get; private set; }
    public float MaxHealth => maxHealth;
    public bool IsDead => CurrentHealth <= 0f;
    public BoarState State { get; private set; }
    public event Action<BoarController> Despawned;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        chargeHitbox = GetComponent<BoarChargeHitbox>();
        modelRotationOffset = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f))
            * transform.rotation;
        CurrentHealth = Mathf.Max(1f, maxHealth);
        agent.stoppingDistance = cropAttackDistance;
        agent.updateRotation = false;
        chargeHitbox.SetCharging(false);
    }

    public void Initialize(PotatoFarmingSystem system, GroundGrid[] grounds)
    {
        farmingSystem = system;
        allowedGrounds = grounds ?? Array.Empty<GroundGrid>();
        CurrentHealth = Mathf.Max(1f, maxHealth);
        State = BoarState.SeekingCrop;
        FindNearestCrop();
    }

    public bool TryTakeDamage(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f || IsDead) return false;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        if (IsDead) BeginFlee();
        else BeginPlayerAggro();
        return true;
    }

    private void Update()
    {
        if (!farmingSystem && State != BoarState.Fleeing) return;
        switch (State)
        {
            case BoarState.SeekingCrop: UpdateSeekingCrop(); break;
            case BoarState.AttackingCrop: UpdateAttackingCrop(); break;
            case BoarState.ChasingPlayer: UpdateChasingPlayer(); break;
            case BoarState.Charging: UpdateCharging(); break;
            case BoarState.Fleeing: UpdateFleeing(); break;
        }
        UpdateDirectMovement();
        if (agent && agent.enabled && agent.isOnNavMesh && agent.desiredVelocity.sqrMagnitude > 0.001f)
            Face(transform.position + agent.desiredVelocity);
    }

    private void UpdateSeekingCrop()
    {
        if (!IsValidCrop(cropTarget))
        {
            if (!FindNearestCrop()) return;
        }
        Vector3 destination = cropTarget.Ground.CellCenter(cropTarget.Cell);
        SetDestination(destination, searchSpeed, cropAttackDistance);
        if (HorizontalDistance(transform.position, destination) <= cropAttackDistance)
        {
            State = BoarState.AttackingCrop;
            nextCropAttackAt = Time.time;
            StopMoving();
        }
    }

    private void UpdateAttackingCrop()
    {
        if (!IsValidCrop(cropTarget))
        {
            State = BoarState.SeekingCrop;
            FindNearestCrop();
            return;
        }
        Vector3 targetPosition = cropTarget.Ground.CellCenter(cropTarget.Cell);
        if (HorizontalDistance(transform.position, targetPosition) > cropAttackDistance * 1.35f)
        {
            State = BoarState.SeekingCrop;
            return;
        }
        Face(targetPosition);
        if (Time.time < nextCropAttackAt) return;
        nextCropAttackAt = Time.time + cropAttackInterval;
        GroundGrid targetGround = cropTarget.Ground;
        Vector2Int targetCell = cropTarget.Cell;
        farmingSystem.TryDamage(targetGround, targetCell, cropDamage);
        if (!cropTarget.IsDead) return;
        farmingSystem.StopFarming(targetGround, targetCell);
        targetGround.TrySetCellState(targetCell, GroundCellState.BoarDugField);
        cropTarget = null;
        State = BoarState.SeekingCrop;
        FindNearestCrop();
    }

    private void BeginPlayerAggro()
    {
        playerTarget = FindNearestPlayer();
        cropTarget = null;
        chargeHitbox.SetCharging(false);
        State = BoarState.ChasingPlayer;
    }

    private void UpdateChasingPlayer()
    {
        if (!playerTarget) playerTarget = FindNearestPlayer();
        if (!playerTarget) return;
        float distance = HorizontalDistance(transform.position, playerTarget.position);
        SetDestination(playerTarget.position, chaseSpeed, 1f);
        if (distance <= chargeStartDistance && Time.time >= nextChargeAt) BeginCharge();
    }

    private void BeginCharge()
    {
        State = BoarState.Charging;
        stateEndsAt = Time.time + chargeDuration;
        nextChargeAt = stateEndsAt + chargeCooldown;
        chargeHitbox.SetCharging(true);
        Vector3 direction = playerTarget
            ? playerTarget.position - transform.position : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
        SetDestination(transform.position + direction.normalized * chargeSpeed * chargeDuration,
            chargeSpeed, 0f);
    }

    private void UpdateCharging()
    {
        if (Time.time < stateEndsAt) return;
        chargeHitbox.SetCharging(false);
        State = BoarState.ChasingPlayer;
    }

    private void BeginFlee()
    {
        chargeHitbox.SetCharging(false);
        State = BoarState.Fleeing;
        stateEndsAt = Time.time + fleeDuration;
        Transform threat = playerTarget ? playerTarget : FindNearestPlayer();
        Vector3 away = threat ? transform.position - threat.position : -transform.forward;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f) away = transform.forward;
        SetDestination(transform.position + away.normalized * fleeDistance, fleeSpeed, 0f);
    }

    private void UpdateFleeing()
    {
        if (Time.time < stateEndsAt && !ReachedDestination()) return;
        Despawned?.Invoke(this);
        Destroy(gameObject);
    }

    private bool FindNearestCrop()
    {
        cropTarget = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var crop in farmingSystem.GetFarmingTiles())
        {
            if (!IsValidCrop(crop) || !IsAllowedGround(crop.Ground)) continue;
            float distance = (crop.Ground.CellCenter(crop.Cell) - transform.position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            cropTarget = crop;
        }
        return cropTarget != null;
    }

    private bool IsValidCrop(PotatoFarmingSystem.FarmingTile crop)
        => crop != null && crop.Ground && !crop.IsDead &&
            farmingSystem.TryGetFarmingTile(crop.Ground, crop.Cell, out var current) &&
            ReferenceEquals(current, crop);

    private bool IsAllowedGround(GroundGrid target)
    {
        if (allowedGrounds == null || allowedGrounds.Length == 0) return true;
        foreach (var candidate in allowedGrounds) if (candidate == target) return true;
        return false;
    }

    private Transform FindNearestPlayer()
    {
        Transform nearest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var player in FindObjectsByType<PlayerStunController>(FindObjectsSortMode.None))
        {
            float distance = (player.transform.position - transform.position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            nearest = player.transform;
        }
        return nearest;
    }

    private void SetDestination(Vector3 destination, float speed, float stoppingDistance)
    {
        destination.y = transform.position.y;
        directDestination = destination;
        hasDestination = true;
        if (agent && agent.enabled && agent.isOnNavMesh)
        {
            agent.speed = speed;
            agent.stoppingDistance = stoppingDistance;
            agent.isStopped = false;
            agent.SetDestination(destination);
        }
    }

    private void UpdateDirectMovement()
    {
        if (!hasDestination || agent && agent.enabled && agent.isOnNavMesh) return;
        float speed = State == BoarState.Charging ? chargeSpeed
            : State == BoarState.Fleeing ? fleeSpeed
            : State == BoarState.ChasingPlayer ? chaseSpeed : searchSpeed;
        Vector3 next = Vector3.MoveTowards(transform.position, directDestination, speed * Time.deltaTime);
        Face(next);
        transform.position = next;
    }

    private void StopMoving()
    {
        hasDestination = false;
        if (agent && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
    }

    private bool ReachedDestination()
    {
        if (agent && agent.enabled && agent.isOnNavMesh)
            return !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;
        return !hasDestination || HorizontalDistance(transform.position, directDestination) <= 0.1f;
    }

    private void Face(Vector3 point)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * modelRotationOffset;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDestroy()
    {
        if (chargeHitbox) chargeHitbox.SetCharging(false);
    }
}
