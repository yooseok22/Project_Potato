using System;
using UnityEngine;
using UnityEngine.AI;

public enum DogHelperState
{
    Idle,
    CoveringSoil,
    ChasingBugs,
    ChasingBoar,
    Stunned,
    ReturningToRecovery,
    Recovering
}

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class DogHelperController : MonoBehaviour, IDamageable
{
    [SerializeField] private PotatoFarmingSystem farmingSystem;
    [Header("Health and Recovery")]
    [SerializeField, Min(1f)] private float maxHealth = 50f;
    [SerializeField] private Transform recoveryPoint;
    [SerializeField, Min(1f)] private float recoveryDurationSeconds = 180f;
    [SerializeField, Min(0.1f)] private float recoveryArrivalDistance = 0.75f;
    [Header("Helper Behaviour")]
    [SerializeField, Min(1f)] private float detectionRange = 30f;
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
    [SerializeField, Min(0.1f)] private float workDistance = 1.5f;
    [SerializeField, Min(0.1f)] private float barkDistance = 3f;
    [SerializeField, Min(0.1f)] private float barkInterval = 1f;
    [SerializeField, Min(0f)] private float barkDamage = 15f;
    [SerializeField, Min(0.1f)] private float stunDuration = 3f;
    [SerializeField] private AudioSource barkAudio;
    [SerializeField] private AudioClip barkClip;
    private NavMeshAgent agent;
    private float nextActionAt;
    private float stunEndsAt;
    private float nextSearchAt;
    private float recoveryStartedAt;
    private Vector3 fallbackRecoveryPosition;
    private BoarController boarTarget;
    private PotatoFarmingSystem.FarmingTile cropTarget;
    public DogHelperState State { get; private set; }
    public bool IsStunned => State == DogHelperState.Stunned;
    public bool IsRecovering => State == DogHelperState.ReturningToRecovery || State == DogHelperState.Recovering;
    public float CurrentHealth { get; private set; }
    public float MaxHealth => Mathf.Max(1f, maxHealth);
    public bool IsDead => CurrentHealth <= 0f;
    public float RecoveryDurationSeconds => Mathf.Max(1f, recoveryDurationSeconds);
    public event Action Barked;
    public event Action<float, float> HealthChanged;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!barkAudio) barkAudio = GetComponent<AudioSource>();
        if (!farmingSystem) farmingSystem = FindAnyObjectByType<PotatoFarmingSystem>();
        fallbackRecoveryPosition = transform.position;
        CurrentHealth = MaxHealth;
    }

    public void Initialize(PotatoFarmingSystem system) => Initialize(system, null);

    public void Initialize(PotatoFarmingSystem system, Transform assignedRecoveryPoint)
    {
        farmingSystem = system;
        if (assignedRecoveryPoint) recoveryPoint = assignedRecoveryPoint;
        fallbackRecoveryPosition = recoveryPoint ? recoveryPoint.position : transform.position;
        SetHealth(MaxHealth);
        State = DogHelperState.Idle;
    }

    public bool TryTakeDamage(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f || IsDead || IsRecovering)
            return false;
        SetHealth(CurrentHealth - amount);
        if (IsDead) BeginRecovery();
        return true;
    }

    public bool TryStun()
    {
        if (IsStunned || IsRecovering || IsDead) return false;
        State = DogHelperState.Stunned;
        stunEndsAt = Time.time + stunDuration;
        boarTarget = null;
        cropTarget = null;
        StopMoving();
        return true;
    }

    private void Update()
    {
        if (State == DogHelperState.ReturningToRecovery)
        {
            if (MoveTo(RecoveryPosition(), recoveryArrivalDistance)) StartRecovery();
            return;
        }
        if (State == DogHelperState.Recovering)
        {
            UpdateRecovery();
            return;
        }
        if (IsStunned)
        {
            if (Time.time < stunEndsAt) return;
            State = DogHelperState.Idle;
            nextSearchAt = 0f;
        }
        // Threats preempt field work; keep searches bounded instead of scanning every frame.
        if (Time.time >= nextSearchAt)
        {
            nextSearchAt = Time.time + 0.25f;
            SelectTask();
        }
        if (State == DogHelperState.ChasingBoar)
        {
            if (!boarTarget || boarTarget.IsDead || boarTarget.State == BoarState.Fleeing)
            { ClearTask(); return; }
            if (MoveTo(boarTarget.transform.position, barkDistance) && Time.time >= nextActionAt)
            {
                nextActionAt = Time.time + barkInterval;
                Bark();
                boarTarget.TryTakeDamageFromDog(barkDamage, this);
            }
            return;
        }
        if (State == DogHelperState.CoveringSoil || State == DogHelperState.ChasingBugs)
        {
            if (!ValidCrop(cropTarget) || !(State == DogHelperState.CoveringSoil ? NeedsCover(cropTarget) : cropTarget.HasBugs))
            { ClearTask(); return; }
            if (!MoveTo(cropTarget.Ground.CellCenter(cropTarget.Cell), workDistance)) return;
            if (Time.time < nextActionAt) return;
            nextActionAt = Time.time + barkInterval;
            if (State == DogHelperState.CoveringSoil)
                farmingSystem.TryCoverSoil(cropTarget.Ground, cropTarget.Cell);
            else
            {
                if (farmingSystem.TryRemoveBugs(cropTarget.Ground, cropTarget.Cell)) Bark();
            }
            ClearTask();
        }
    }

    private void SelectTask()
    {
        boarTarget = null;
        float best = detectionRange * detectionRange;
        foreach (var boar in FindObjectsByType<BoarController>(FindObjectsSortMode.None))
        {
            if (boar.IsDead || boar.State == BoarState.Fleeing) continue;
            float distance = (boar.transform.position - transform.position).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            boarTarget = boar;
        }
        if (boarTarget) { State = DogHelperState.ChasingBoar; return; }
        cropTarget = null;
        if (farmingSystem)
        {
            // Bugs have a short deadline, so deal with them before routine soil work.
            foreach (bool bugs in new[] { true, false })
            {
                best = detectionRange * detectionRange;
                foreach (var crop in farmingSystem.GetFarmingTiles())
                {
                    if (!ValidCrop(crop) || !(bugs ? crop.HasBugs : NeedsCover(crop))) continue;
                    float distance = (crop.Ground.CellCenter(crop.Cell) - transform.position).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    cropTarget = crop;
                }
                if (cropTarget != null)
                { State = bugs ? DogHelperState.ChasingBugs : DogHelperState.CoveringSoil; return; }
            }
        }
        ClearTask();
    }

    private bool ValidCrop(PotatoFarmingSystem.FarmingTile crop)
        => farmingSystem && crop != null && crop.Ground && !crop.IsDead &&
           farmingSystem.TryGetFarmingTile(crop.Ground, crop.Cell, out var current) && ReferenceEquals(current, crop);

    private static bool NeedsCover(PotatoFarmingSystem.FarmingTile crop)
        => !crop.IsGrowthPaused && crop.Stage == PotatoGrowthStage.TwoLargeLeaves && !crop.IsSoilCovered &&
           !crop.IsMulchingFilmApplied && crop.Ground.CanCoverPotato(crop.Cell);

    private bool MoveTo(Vector3 position, float distance)
    {
        if (Vector3.Distance(transform.position, position) <= distance)
        {
            StopMoving();
            var direction = position - transform.position;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
            return true;
        }
        // Wait for the existing runtime NavMesh baker; never walk through geometry as a fallback.
        if (!agent || !agent.enabled || !agent.isOnNavMesh) return false;
        agent.speed = moveSpeed;
        agent.stoppingDistance = distance * 0.8f;
        agent.isStopped = false;
        if (!agent.pathPending) agent.SetDestination(position);
        return false;
    }

    private void StopMoving()
    {
        if (agent && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
    }

    private Vector3 RecoveryPosition() => recoveryPoint ? recoveryPoint.position : fallbackRecoveryPosition;

    private void BeginRecovery()
    {
        boarTarget = null;
        cropTarget = null;
        StopMoving();
        State = DogHelperState.ReturningToRecovery;
        if (Vector3.Distance(transform.position, RecoveryPosition()) <= recoveryArrivalDistance)
            StartRecovery();
    }

    private void StartRecovery()
    {
        StopMoving();
        State = DogHelperState.Recovering;
        recoveryStartedAt = Time.time;
    }

    private void UpdateRecovery()
    {
        float progress = Mathf.Clamp01((Time.time - recoveryStartedAt) / RecoveryDurationSeconds);
        SetHealth(MaxHealth * progress);
        if (progress < 1f) return;
        State = DogHelperState.Idle;
        nextSearchAt = 0f;
    }

    private void SetHealth(float value)
    {
        float next = Mathf.Clamp(value, 0f, MaxHealth);
        if (Mathf.Approximately(CurrentHealth, next)) return;
        CurrentHealth = next;
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    private void ClearTask()
    {
        State = DogHelperState.Idle;
        cropTarget = null;
        boarTarget = null;
        StopMoving();
    }

    private void Bark()
    {
        if (barkAudio && barkClip) barkAudio.PlayOneShot(barkClip);
        Barked?.Invoke();
    }

    private void OnDisable() => StopMoving();
}
