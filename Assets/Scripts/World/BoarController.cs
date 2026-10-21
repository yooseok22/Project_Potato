using System;
using System.Collections.Generic;
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
    [SerializeField, Min(0.1f)] private float cropAttackInterval = 3f;
    [SerializeField, Min(0.1f)] private float cropAttackDistance = 1.6f;
    [SerializeField, Min(1)] private int maximumDugTiles = 50;
    [Tooltip("After the first crop, prefer another crop within this many cells to create a winding path.")]
    [SerializeField, Min(1)] private int windingSearchRadius = 6;
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
    private GroundGrid huntingGround;
    private NavMeshAgent agent;
    private BoarChargeHitbox chargeHitbox;
    private PotatoFarmingSystem.FarmingTile cropTarget;
    private Transform playerTarget;
    private DogHelperController dogTarget;
    private Vector3 directDestination;
    private float nextCropAttackAt;
    private float nextChargeAt;
    private float stateEndsAt;
    private bool hasDestination;
    private Quaternion modelRotationOffset;
    private Vector2 previousCropDirection;
    private int dugTileCount;

    public float CurrentHealth { get; private set; }
    public float MaxHealth => maxHealth;
    public bool IsDead => CurrentHealth <= 0f;
    public BoarState State { get; private set; }
    public int DugTileCount => dugTileCount;
    public int MaximumDugTiles => Mathf.Max(1, maximumDugTiles);
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
        => Initialize(system, grounds, null);

    public void Initialize(PotatoFarmingSystem system, GroundGrid[] grounds, GroundGrid spawnGround)
    {
        farmingSystem = system;
        allowedGrounds = grounds ?? Array.Empty<GroundGrid>();
        huntingGround = spawnGround;
        dugTileCount = 0;
        previousCropDirection = Vector2.zero;
        CurrentHealth = Mathf.Max(1f, maxHealth);
        State = BoarState.SeekingCrop;
        if (!FindNearestCrop()) BeginSatisfiedFlee();
    }

    public bool TryTakeDamage(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f || IsDead) return false;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        if (IsDead) BeginFlee();
        else BeginPlayerAggro();
        return true;
    }

    public bool TryTakeDamageFromDog(float amount, DogHelperController dog)
    {
        if (!dog || dog.IsStunned || State == BoarState.Fleeing || IsDead ||
            float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return false;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        dogTarget = dog;
        playerTarget = dog.transform;
        if (IsDead) BeginFlee();
        else if (State != BoarState.Charging)
        {
            cropTarget = null;
            chargeHitbox.SetCharging(false);
            State = BoarState.ChasingPlayer;
        }
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
            if (!FindNextCrop())
            {
                BeginSatisfiedFlee();
                return;
            }
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
            if (!FindNextCrop()) BeginSatisfiedFlee();
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
        dugTileCount++;
        cropTarget = null;
        if (dugTileCount >= MaximumDugTiles)
        {
            BeginSatisfiedFlee();
            return;
        }
        State = BoarState.SeekingCrop;
        if (!FindWindingCrop(targetGround, targetCell)) BeginSatisfiedFlee();
    }

    private void BeginPlayerAggro()
    {
        dogTarget = null;
        playerTarget = FindNearestPlayer();
        cropTarget = null;
        chargeHitbox.SetCharging(false);
        State = BoarState.ChasingPlayer;
    }

    private void UpdateChasingPlayer()
    {
        if (dogTarget && dogTarget.IsStunned)
        {
            dogTarget = null;
            playerTarget = null;
        }
        if (!playerTarget) playerTarget = FindNearestPlayer();
        if (!playerTarget)
        {
            State = BoarState.SeekingCrop;
            if (!FindNextCrop()) BeginSatisfiedFlee();
            return;
        }
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

    private void BeginSatisfiedFlee()
    {
        chargeHitbox.SetCharging(false);
        cropTarget = null;
        State = BoarState.Fleeing;
        GroundGrid ground = huntingGround;
        if (!ground)
        {
            BeginFlee();
            return;
        }

        Vector2Int current = ground.WorldToCell(transform.position);
        current.x = Mathf.Clamp(current.x, 0, ground.Width - 1);
        current.y = Mathf.Clamp(current.y, 0, ground.Depth - 1);
        int west = current.x;
        int east = ground.Width - 1 - current.x;
        int south = current.y;
        int north = ground.Depth - 1 - current.y;
        int nearest = Mathf.Min(west, east, south, north);
        Vector2Int edge = current;
        if (nearest == west) edge.x = 0;
        else if (nearest == east) edge.x = ground.Width - 1;
        else if (nearest == south) edge.y = 0;
        else edge.y = ground.Depth - 1;

        Vector3 destination = ground.CellCenter(edge);
        float travelTime = HorizontalDistance(transform.position, destination) / Mathf.Max(0.1f, fleeSpeed);
        stateEndsAt = Time.time + Mathf.Max(fleeDuration, travelTime + 3f);
        SetDestination(destination, fleeSpeed, 0.25f);
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
            if (!IsValidCrop(crop) || !IsAllowedGround(crop.Ground)
                || huntingGround && crop.Ground != huntingGround) continue;
            float distance = (crop.Ground.CellCenter(crop.Cell) - transform.position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            cropTarget = crop;
        }
        if (cropTarget != null)
        {
            huntingGround = cropTarget.Ground;
            Vector3 direction = cropTarget.Ground.CellCenter(cropTarget.Cell) - transform.position;
            previousCropDirection = new Vector2(direction.x, direction.z).normalized;
        }
        return cropTarget != null;
    }

    private bool FindNextCrop()
    {
        if (dugTileCount == 0) return FindNearestCrop();
        if (!huntingGround) return false;
        return FindWindingCrop(huntingGround, huntingGround.WorldToCell(transform.position));
    }

    private bool FindWindingCrop(GroundGrid ground, Vector2Int fromCell)
    {
        cropTarget = null;
        var localCandidates = new List<PotatoFarmingSystem.FarmingTile>();
        var allCandidates = new List<PotatoFarmingSystem.FarmingTile>();
        foreach (var crop in farmingSystem.GetFarmingTiles())
        {
            if (!IsValidCrop(crop) || crop.Ground != ground) continue;
            allCandidates.Add(crop);
            Vector2Int delta = crop.Cell - fromCell;
            int cellDistance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            if (cellDistance <= Mathf.Max(1, windingSearchRadius)) localCandidates.Add(crop);
        }

        var candidates = localCandidates.Count > 0 ? localCandidates : allCandidates;
        float bestScore = float.PositiveInfinity;
        foreach (var crop in candidates)
        {
            Vector2 delta = crop.Cell - fromCell;
            float distance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            Vector2 direction = delta.sqrMagnitude > 0f ? delta.normalized : Vector2.zero;
            // Penalize both straight continuation and direct backtracking. Sideways and diagonal
            // turns plus random jitter produce an irregular, winding top-down footprint.
            float straightness = previousCropDirection.sqrMagnitude > 0f
                ? Mathf.Abs(Vector2.Dot(previousCropDirection, direction)) : 0f;
            float score = distance + straightness * 2.5f + UnityEngine.Random.Range(0f, 2.75f);
            if (score >= bestScore) continue;
            bestScore = score;
            cropTarget = crop;
        }

        if (cropTarget == null) return false;
        Vector2 selectedDirection = cropTarget.Cell - fromCell;
        if (selectedDirection.sqrMagnitude > 0f) previousCropDirection = selectedDirection.normalized;
        return true;
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
        foreach (var player in FindObjectsByType<PlayerStunController>())
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
