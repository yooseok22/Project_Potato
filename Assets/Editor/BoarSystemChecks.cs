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
            var cell = new Vector2Int(2, 2);
            ground.TrySetCellState(cell, GroundCellState.PlantingHole);
            Check(farming.TryStartFarming(ground, cell, clock), "crop is registered");
            Check(farming.TryWater(ground, cell), "crop starts growing");
            farming.TryGetFarmingTile(ground, cell, out var crop);

            var boarObject = new GameObject("Boar");
            SceneManager.MoveGameObjectToScene(boarObject, scene);
            boarObject.transform.position = ground.CellCenter(cell);
            boarObject.AddComponent<BoarChargeHitbox>();
            var boar = boarObject.AddComponent<BoarController>();
            typeof(BoarController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(boar, null);
            boar.Initialize(farming, new[] { ground });

            Invoke(boar, "UpdateSeekingCrop");
            Check(boar.State == BoarState.AttackingCrop, "nearest crop is reached and targeted");
            SetField(boar, "nextCropAttackAt", 0f);
            Invoke(boar, "UpdateAttackingCrop");
            Check(crop.CurrentHealth == 65f, "boar deals 35 crop damage");
            SetField(boar, "nextCropAttackAt", 0f);
            Invoke(boar, "UpdateAttackingCrop");
            SetField(boar, "nextCropAttackAt", 0f);
            Invoke(boar, "UpdateAttackingCrop");
            Check(crop.IsDead && !farming.TryGetFarmingTile(ground, cell, out _) &&
                ground.GetCellState(cell) == GroundCellState.BoarDugField,
                "destroyed crop becomes a dug field and leaves farming data");
            Check(boar.TryTakeDamage(25f) && boar.CurrentHealth == 75f &&
                boar.State == BoarState.ChasingPlayer,
                "projectile damage makes a living boar aggressive");
            Check(boar.TryTakeDamage(100f) && boar.IsDead && boar.State == BoarState.Fleeing,
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
}
