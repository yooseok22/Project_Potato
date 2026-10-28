using System.Collections.Generic;
using UnityEngine;

/// <summary>Replaceable primitive block art, generated without modifying imported props.</summary>
public sealed class AutomationMachineFactory : MonoBehaviour
{
    private readonly List<Material> materials = new();
    public static AutomationBlock Create(AutomationMachineKind kind, float size, Transform parent)
    {
        var root = new GameObject("Automation_" + kind);
        root.transform.SetParent(parent, false);
        var block = root.AddComponent<AutomationBlock>(); block.Configure(kind, size);
        var factory = root.AddComponent<AutomationMachineFactory>();
        var body = factory.Material(kind switch
        {
            AutomationMachineKind.Generator => new Color(0.8f, 0.49f, 0.12f),
            AutomationMachineKind.WaterTank => new Color(0.12f, 0.42f, 0.65f),
            _ => new Color(0.26f, 0.33f, 0.33f)
        });
        var dark = factory.Material(new Color(0.08f, 0.1f, 0.11f));
        var indicator = factory.Material(new Color(0.9f, 0.25f, 0.12f));
        ParticleSystem left = null, right = null;
        if (kind == AutomationMachineKind.Conveyor)
        {
            factory.Box("Belt", new Vector3(0, 0.12f, 0), new Vector3(size, 0.24f, size), dark);
            for (int index = 0; index < 7; index++)
                factory.Box("Slat", new Vector3(0, 0.245f, ((index + 0.5f) / 7f - 0.5f) * size), new Vector3(size * 0.92f, 0.015f, 0.06f), body);
            factory.Box("Direction", new Vector3(0, 0.26f, size * 0.14f), new Vector3(0.12f, 0.015f, size * 0.38f), indicator);
            factory.Box("ArrowLeft", new Vector3(-0.13f, 0.26f, size * 0.25f), new Vector3(0.10f, 0.015f, 0.38f), indicator).transform.localRotation = Quaternion.Euler(0, -45, 0);
            factory.Box("ArrowRight", new Vector3(0.13f, 0.26f, size * 0.25f), new Vector3(0.10f, 0.015f, 0.38f), indicator).transform.localRotation = Quaternion.Euler(0, 45, 0);
            var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up * 0.12f; collider.size = new Vector3(size, 0.24f, size);
            var rigidbody = root.AddComponent<Rigidbody>(); rigidbody.isKinematic = true; rigidbody.useGravity = false;
            root.AddComponent<AutomationConveyor>();
        }
        else if (kind == AutomationMachineKind.Sprinkler)
        {
            factory.Box("Base", new Vector3(0, 0.08f, 0), new Vector3(0.65f, 0.16f, 0.65f), body);
            factory.Box("Pillar", new Vector3(0, 0.65f, 0), new Vector3(0.15f, 1.3f, 0.15f), body);
            factory.Box("TBar", new Vector3(0, 1.3f, 0), new Vector3(size * 0.8f, 0.12f, 0.12f), body);
            factory.Box("Status", new Vector3(0, 0.7f, -0.1f), Vector3.one * 0.13f, indicator);
            var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up * 0.65f; collider.size = new Vector3(0.65f, 1.4f, 0.65f);
            var obstacle = root.AddComponent<UnityEngine.AI.NavMeshObstacle>(); obstacle.carving = true;
            obstacle.center = Vector3.up * 0.65f; obstacle.size = collider.size;
            left = factory.Spray("LeftWater", new Vector3(-size * 0.4f, 1.3f, 0), Vector3.left);
            right = factory.Spray("RightWater", new Vector3(size * 0.4f, 1.3f, 0), Vector3.right);
        }
        else
        {
            factory.Box("Housing", new Vector3(0, 0.55f, 0), new Vector3(size * 0.8f, 1.1f, size * 0.8f), body);
            factory.Box("Status", new Vector3(0, 0.9f, -size * 0.405f), new Vector3(0.35f, 0.12f, 0.03f), dark);
            for (int index = 0; index < block.OutletCount; index++)
                factory.Box("Outlet_" + index, new Vector3((index - (block.OutletCount - 1) * 0.5f) * 0.3f, 0.5f, -size * 0.42f), Vector3.one * 0.16f, dark);
            var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up * 0.55f; collider.size = new Vector3(size * 0.8f, 1.1f, size * 0.8f);
            var obstacle = root.AddComponent<UnityEngine.AI.NavMeshObstacle>(); obstacle.carving = true;
            obstacle.center = collider.center; obstacle.size = collider.size;
        }
        block.BindVisuals(indicator, left, right);
        return block;
    }

    private Material Material(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        material.color = color; materials.Add(material); return material;
    }
    private GameObject Box(string name, Vector3 position, Vector3 size, Material material)
    {
        // A mesh-only primitive avoids stray colliders when toggling sprinkler passage.
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        go.transform.localPosition = position; go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
    private ParticleSystem Spray(string name, Vector3 position, Vector3 direction)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        go.transform.localPosition = position; go.transform.localRotation = Quaternion.LookRotation(direction + Vector3.up * 0.1f);
        var particles = go.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main; main.startLifetime = 0.9f; main.startSpeed = 6f; main.startSize = 0.04f;
        main.gravityModifier = 0.8f; main.startColor = new Color(0.4f, 0.8f, 1f, 0.8f); main.playOnAwake = false;
        var emission = particles.emission; emission.rateOverTime = 35;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 12; shape.radius = 0.02f;
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
        material.color = new Color(0.35f, 0.75f, 1f, 0.8f); materials.Add(material);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        return particles;
    }
    private void OnDestroy() { foreach (var material in materials) AutomationWorld.DestroyOwned(material); }
}
