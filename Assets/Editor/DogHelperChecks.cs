using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DogHelperChecks
{
    [MenuItem("Tools/Project Potato/Validate Helper Dog")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var random = UnityEngine.Random.state;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Check(bool value, string message)
        { if (!value) throw new Exception("Dog check failed: " + message); }
        void UpdateDog(DogHelperController dog)
        {
            typeof(DogHelperController).GetField("nextSearchAt", flags).SetValue(dog, 0f);
            typeof(DogHelperController).GetField("nextActionAt", flags).SetValue(dog, 0f);
            typeof(DogHelperController).GetMethod("Update", flags).Invoke(dog, null);
        }
        try
        {
            var root = new GameObject("DogChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var grid = root.AddComponent<GroundGrid>();
            typeof(GroundGrid).GetMethod("Awake", flags).Invoke(grid, null);
            var clock = root.AddComponent<WorldClock>();
            var farming = root.AddComponent<PotatoFarmingSystem>();
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", flags).SetValue(farming, 1f);
            var cell = new Vector2Int(2, 2);
            farming.TryStartFarming(grid, cell, clock);
            farming.TryWater(grid, cell);
            clock.AdvanceBy(80);
            var dogObject = new GameObject("Dog");
            SceneManager.MoveGameObjectToScene(dogObject, scene);
            dogObject.transform.position = grid.CellCenter(cell);
            var recoveryObject = new GameObject("DogRecoveryPoint");
            SceneManager.MoveGameObjectToScene(recoveryObject, scene);
            recoveryObject.transform.position = dogObject.transform.position;
            var dog = dogObject.AddComponent<DogHelperController>();
            typeof(DogHelperController).GetMethod("Awake", flags).Invoke(dog, null);
            dog.Initialize(farming, recoveryObject.transform);
            Check(dog.CurrentHealth == 50f && dog.MaxHealth == 50f &&
                dog.RecoveryDurationSeconds == 180f, "dog starts with 50 HP and a three-minute recovery");
            int barks = 0;
            dog.Barked += () => barks++;
            UpdateDog(dog);
            farming.TryGetFarmingTile(grid, cell, out var crop);
            Check(!crop.HasBugs && barks == 1 && !crop.HasAbnormalStatus, "dog barks away crop bugs");
            UpdateDog(dog);
            Check(crop.IsSoilCovered && grid.IsSoilCoveredPotato(cell), "dog covers two-leaf crop");
            clock.AdvanceBy(180);
            Check(crop.CurrentHealth == 100 && !crop.HasAbnormalStatus && grid.IsSoilCoveredPotato(cell),
                "covered visual and protection persist without a harvest coordinator");
            var boarObject = new GameObject("Boar");
            SceneManager.MoveGameObjectToScene(boarObject, scene);
            boarObject.transform.position = dog.transform.position + Vector3.right;
            var boar = boarObject.AddComponent<BoarController>();
            typeof(BoarController).GetMethod("Awake", flags).Invoke(boar, null);
            boar.Initialize(farming, new[] { grid }, grid);
            UpdateDog(dog);
            Check(boar.MaxHealth == 30f && boar.CurrentHealth == 15f &&
                dog.State == DogHelperState.ChasingBoar,
                "dog prioritizes boar and deals 15 damage");
            Check((Transform)typeof(BoarController).GetField("playerTarget", flags).GetValue(boar) == dog.transform,
                "boar retaliates against dog");
            typeof(BoarController).GetMethod("BeginCharge", flags).Invoke(boar, null);
            var chargeDestination = (Vector3)typeof(BoarController)
                .GetField("directDestination", flags).GetValue(boar);
            Check(boar.State == BoarState.Charging &&
                Vector3.Dot((chargeDestination - boar.transform.position).normalized,
                    (dog.transform.position - boar.transform.position).normalized) > 0.99f,
                "boar charge is aimed at the attacking dog");
            var collider = dogObject.AddComponent<CapsuleCollider>();
            var hitbox = boar.GetComponent<BoarChargeHitbox>();
            hitbox.SetCharging(true);
            typeof(BoarChargeHitbox).GetMethod("TryStun", flags).Invoke(hitbox, new object[] { collider, dog.transform.position });
            Check(dog.IsStunned && dog.CurrentHealth == 40f, "charging boar deals 10 damage and stuns dog");
            typeof(BoarController).GetField("stateEndsAt", flags).SetValue(boar, -1f);
            typeof(BoarController).GetMethod("UpdateCharging", flags).Invoke(boar, null);
            Check(boar.State == BoarState.SeekingCrop &&
                typeof(BoarController).GetField("playerTarget", flags).GetValue(boar) == null,
                "boar performs one retaliation charge and returns to farming");
            UpdateDog(dog);
            Check(boar.CurrentHealth == 15f, "stunned dog cannot bark damage");
            typeof(DogHelperController).GetField("stunEndsAt", flags).SetValue(dog, -1f);
            UpdateDog(dog);
            Check(!dog.IsStunned && boar.IsDead && boar.CurrentHealth == 0f &&
                boar.State == BoarState.Fleeing,
                "second dog attack reduces boar to zero HP and makes it flee");
            Check(dog.TryTakeDamage(40f) && dog.IsDead && dog.State == DogHelperState.Recovering,
                "zero-HP dog returns to and starts recovery at its assigned point");
            typeof(DogHelperController).GetField("recoveryStartedAt", flags).SetValue(dog, Time.time - 90f);
            typeof(DogHelperController).GetMethod("Update", flags).Invoke(dog, null);
            Check(Mathf.Abs(dog.CurrentHealth - 25f) < 0.1f && dog.State == DogHelperState.Recovering,
                "dog reaches half health after half the recovery time");
            typeof(DogHelperController).GetField("recoveryStartedAt", flags).SetValue(dog, Time.time - 181f);
            typeof(DogHelperController).GetMethod("Update", flags).Invoke(dog, null);
            Check(dog.CurrentHealth == 50f && dog.State == DogHelperState.Idle && !dog.IsDead,
                "dog fully recovers after three minutes");
            Debug.Log("Helper dog checks passed.");
        }
        finally { EditorSceneManager.CloseScene(scene, true); UnityEngine.Random.state = random; }
    }
}
