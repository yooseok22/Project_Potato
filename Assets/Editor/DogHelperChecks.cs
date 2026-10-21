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
            var dog = dogObject.AddComponent<DogHelperController>();
            typeof(DogHelperController).GetMethod("Awake", flags).Invoke(dog, null);
            dog.Initialize(farming);
            dog.transform.position = grid.CellCenter(cell);
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
            Check(boar.CurrentHealth == 90 && dog.State == DogHelperState.ChasingBoar,
                "dog prioritizes boar and bark damages it");
            Check((Transform)typeof(BoarController).GetField("playerTarget", flags).GetValue(boar) == dog.transform,
                "boar retaliates against dog");
            var collider = dogObject.AddComponent<CapsuleCollider>();
            var hitbox = boar.GetComponent<BoarChargeHitbox>();
            hitbox.SetCharging(true);
            typeof(BoarChargeHitbox).GetMethod("TryStun", flags).Invoke(hitbox, new object[] { collider, dog.transform.position });
            Check(dog.IsStunned, "charging boar stuns dog");
            UpdateDog(dog);
            Check(boar.CurrentHealth == 90, "stunned dog cannot bark damage");
            typeof(DogHelperController).GetField("stunEndsAt", flags).SetValue(dog, -1f);
            UpdateDog(dog);
            Check(!dog.IsStunned && boar.CurrentHealth == 80, "dog resumes helping after stun");
            Debug.Log("Helper dog checks passed.");
        }
        finally { EditorSceneManager.CloseScene(scene, true); UnityEngine.Random.state = random; }
    }
}
