using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BoarSystemChecks
{
    [MenuItem("Tools/Project Potato/Validate Boar System")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Boar check failed: " + message);
            checks++;
        }

        try
        {
            var root = new GameObject("BoarChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var ground = root.AddComponent<GroundGrid>();
            typeof(GroundGrid).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(ground, null);
            var clock = root.AddComponent<WorldClock>();
            var farming = root.AddComponent<PotatoFarmingSystem>();
            var firstCell = new Vector2Int(2, 2);
            var secondCell = new Vector2Int(3, 2);
            var thirdCell = new Vector2Int(3, 3);
            var firstCrop = StartCrop(ground, farming, clock, firstCell);
            StartCrop(ground, farming, clock, secondCell);
            StartCrop(ground, farming, clock, thirdCell);
            Check(firstCrop != null, "crop is registered and watered");

            var boarObject = new GameObject("Boar");
            SceneManager.MoveGameObjectToScene(boarObject, scene);
            boarObject.transform.position = ground.CellCenter(firstCell);
            boarObject.AddComponent<BoarChargeHitbox>();
            var boar = boarObject.AddComponent<BoarController>();
            typeof(BoarController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(boar, null);
            boar.Initialize(farming, new[] { ground }, ground);

            Check(Mathf.Approximately(GetField<float>(boar, "cropAttackInterval"), 3f),
                "crop attacks are spaced three seconds apart");
            Invoke(boar, "UpdateSeekingCrop");
            Check(boar.State == BoarState.AttackingCrop, "nearest crop is reached and targeted");
            SetField(boar, "nextCropAttackAt", 0f);
            Invoke(boar, "UpdateAttackingCrop");
            Check(firstCrop.CurrentHealth == 65f, "boar deals 35 crop damage");
            SetField(boar, "nextCropAttackAt", 0f);
            Invoke(boar, "UpdateAttackingCrop");
            SetField(boar, "nextCropAttackAt", 0f);
            Invoke(boar, "UpdateAttackingCrop");
            Check(firstCrop.IsDead && !farming.TryGetFarmingTile(ground, firstCell, out _) &&
                ground.GetCellState(firstCell) == GroundCellState.BoarDugField,
                "destroyed crop becomes a dug field and leaves farming data");
            Check(boar.DugTileCount == 1 && boar.State == BoarState.SeekingCrop &&
                GetField<PotatoFarmingSystem.FarmingTile>(boar, "cropTarget") != null,
                "after the first crop, another crop in the same field is selected");

            Check(boar.MaxHealth == 30f && boar.TryTakeDamage(10f) && boar.CurrentHealth == 20f &&
                boar.State == BoarState.ChasingPlayer,
                "projectile damage starts one retaliation");
            var attacker = new GameObject("Attacker");
            SceneManager.MoveGameObjectToScene(attacker, scene);
            attacker.transform.position = boar.transform.position + Vector3.forward;
            SetField(boar, "playerTarget", attacker.transform);
            Invoke(boar, "BeginCharge");
            SetField(boar, "stateEndsAt", -1f);
            Invoke(boar, "UpdateCharging");
            Check(boar.State == BoarState.SeekingCrop &&
                GetField<Transform>(boar, "playerTarget") == null &&
                GetField<PotatoFarmingSystem.FarmingTile>(boar, "cropTarget") != null,
                "one charge completes retaliation and resumes crop work");

            var nextCrop = GetField<PotatoFarmingSystem.FarmingTile>(boar, "cropTarget");
            boarObject.transform.position = nextCrop.Ground.CellCenter(nextCrop.Cell);
            Invoke(boar, "UpdateSeekingCrop");
            SetField(boar, "dugTileCount", 49);
            for (int hit = 0; hit < 3; hit++)
            {
                SetField(boar, "nextCropAttackAt", 0f);
                Invoke(boar, "UpdateAttackingCrop");
            }
            Check(boar.DugTileCount == 50 && boar.State == BoarState.Fleeing &&
                farming.GetFarmingTiles().Length == 1,
                "the boar flees after 50 crops even when another crop remains");

            var emptyGroundObject = new GameObject("EmptyGround");
            SceneManager.MoveGameObjectToScene(emptyGroundObject, scene);
            var emptyGround = emptyGroundObject.AddComponent<GroundGrid>();
            typeof(GroundGrid).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(emptyGround, null);
            var hungryBoarObject = new GameObject("HungryBoar");
            SceneManager.MoveGameObjectToScene(hungryBoarObject, scene);
            hungryBoarObject.AddComponent<BoarChargeHitbox>();
            var hungryBoar = hungryBoarObject.AddComponent<BoarController>();
            typeof(BoarController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(hungryBoar, null);
            hungryBoar.Initialize(farming, new[] { emptyGround }, emptyGround);
            Check(hungryBoar.State == BoarState.Fleeing,
                "a boar leaves toward the field edge when no crop is available");

            var lethalBoarObject = new GameObject("LethalBoar");
            SceneManager.MoveGameObjectToScene(lethalBoarObject, scene);
            lethalBoarObject.AddComponent<BoarChargeHitbox>();
            var lethalBoar = lethalBoarObject.AddComponent<BoarController>();
            typeof(BoarController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(lethalBoar, null);
            lethalBoar.Initialize(farming, new[] { ground }, ground);
            Check(lethalBoar.TryTakeDamage(100f) && lethalBoar.IsDead && lethalBoar.State == BoarState.Fleeing,
                "lethal damage starts fleeing");
            Debug.Log($"[BoarSystemChecks] PASS: {checks} checks.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Invoke(BoarController boar, string method)
        => typeof(BoarController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(boar, null);

    private static void SetField(BoarController boar, string field, object value)
        => typeof(BoarController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(boar, value);

    private static T GetField<T>(BoarController boar, string field)
        => (T)typeof(BoarController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(boar);

    private static PotatoFarmingSystem.FarmingTile StartCrop(GroundGrid ground,
        PotatoFarmingSystem farming, WorldClock clock, Vector2Int cell)
    {
        ground.TrySetCellState(cell, GroundCellState.PlantingHole);
        if (!farming.TryStartFarming(ground, cell, clock) || !farming.TryWater(ground, cell))
            return null;
        farming.TryGetFarmingTile(ground, cell, out var crop);
        return crop;
    }
}
