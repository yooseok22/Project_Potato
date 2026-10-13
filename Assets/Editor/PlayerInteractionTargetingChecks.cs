using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerInteractionTargetingChecks
{
    [MenuItem("Tools/Project Potato/Validate Player Interaction Targeting")]
    public static void RunValidation()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run this check outside Play mode.");
        var originalScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Interaction targeting check failed: " + message);
            checks++;
        }
        try
        {
            var fixture = new GameObject("InteractionTargetingFixture");
            fixture.transform.position = Vector3.one * 100f;
            var player = new GameObject("Player");
            player.transform.SetParent(fixture.transform, false);
            var interaction = player.AddComponent<PlayerInteractionController>();
            var camera = new GameObject("AimCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.SetParent(fixture.transform, false);
            camera.transform.localPosition = new Vector3(0f, 0f, -1.3f);
            camera.nearClipPlane = 0.01f;
            var settings = new SerializedObject(interaction);
            settings.FindProperty("aimCamera").objectReferenceValue = camera;
            settings.FindProperty("range").floatValue = 3f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            BoxCollider Box(string name, Transform parent, float z, float size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.forward * z;
                var box = go.AddComponent<BoxCollider>();
                box.size = Vector3.one * size;
                return box;
            }
            Box("PlayerChild", player.transform, 0.7f, 0.15f);
            var target = Box("Target", fixture.transform, 1.5f, 0.5f);
            var method = typeof(PlayerInteractionController).GetMethod("TryRaycastTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Collider Hit()
            {
                Physics.SyncTransforms();
                object[] args = { default(RaycastHit) };
                return (bool)method.Invoke(interaction, args) ? ((RaycastHit)args[0]).collider : null;
            }
            Physics.SyncTransforms();
            Check(Physics.Raycast(camera.ViewportPointToRay(new Vector3(0.5f, 0.5f)), out var raw, 3f) &&
                  raw.collider == player.GetComponent<CharacterController>(), "fixture reproduces self obstruction");
            Check(Hit() == target, "ignore own character and child colliders");
            var obstacle = Box("Wall", fixture.transform, 1f, 0.15f);
            Check(Hit() == obstacle, "closest external wall still blocks target");
            target.gameObject.layer = 8;
            settings.Update();
            settings.FindProperty("interactionMask").intValue = 1 << 8;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Check(Hit() == target, "respect interaction layer mask");
            settings.Update();
            settings.FindProperty("range").floatValue = 0.1f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Check(Hit() == null, "respect interaction range");
            settings.Update();
            settings.FindProperty("range").floatValue = 3f;
            settings.FindProperty("interactionMask").intValue = -1;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var nearest = Box("Nearest", fixture.transform, 0.5f, 0.02f);
            for (int i = 1; i < 30; i++) Box("DenseTarget" + i, fixture.transform, 0.5f + i * 0.04f, 0.02f);
            var denseHit = Hit();
            Check(denseHit == nearest, "dense hit sets keep nearest external target; got " + denseHit?.name);
            var buffer = (RaycastHit[])typeof(PlayerInteractionController).GetField("targetHits", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interaction);
            Check(buffer.Length > 16, "full hit buffer grows instead of dropping targets");
            Debug.Log($"[PlayerInteractionTargetingChecks] PASS: {checks} checks.");
        }
        finally
        {
            SceneManager.SetActiveScene(originalScene);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
