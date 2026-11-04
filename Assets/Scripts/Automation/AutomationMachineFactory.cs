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
            var head = new GameObject("RotatingHead").transform;
            head.SetParent(root.transform, false); head.localPosition = Vector3.up * 1.3f;
            var bar = factory.Box("TBar", new Vector3(0, 1.3f, 0), new Vector3(size * 0.8f, 0.12f, 0.12f), body);
            bar.transform.SetParent(head, false); bar.transform.localPosition = Vector3.zero;
            factory.Box("Status", new Vector3(0, 0.7f, -0.1f), Vector3.one * 0.13f, indicator);
            var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up * 0.65f; collider.size = new Vector3(0.65f, 1.4f, 0.65f);
            var obstacle = root.AddComponent<UnityEngine.AI.NavMeshObstacle>(); obstacle.carving = true;
            obstacle.center = Vector3.up * 0.65f; obstacle.size = collider.size;
            left = factory.Spray("LeftWater", new Vector3(-size * 0.4f, 1.3f, 0), Vector3.left);
            right = factory.Spray("RightWater", new Vector3(size * 0.4f, 1.3f, 0), Vector3.right);
            left.transform.SetParent(head, false); left.transform.localPosition = Vector3.left * size * 0.4f;
            right.transform.SetParent(head, false); right.transform.localPosition = Vector3.right * size * 0.4f;
        }
        else if (block.IsRobotStation)
        {
            factory.Box("ChargingPad", new Vector3(0, 0.08f, 0), new Vector3(size * 0.85f, 0.16f, size * 0.85f), body);
            factory.Box("PowerSocket", new Vector3(0, 0.45f, -size * 0.36f), new Vector3(0.5f, 0.7f, 0.2f), dark);
            factory.Box("Status", new Vector3(0, 0.7f, -size * 0.42f), Vector3.one * 0.12f, indicator);
            var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up * 0.08f; collider.size = new Vector3(size * 0.85f, 0.16f, size * 0.85f);
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

    public Transform CreateRobotVisual(FarmRobotRole role, FarmRobotSize size)
    {
        var robot = new GameObject("Robot_" + role).transform; robot.SetParent(transform, false);
        var paint = Material(role == FarmRobotRole.Seeder ? new Color(0.9f, 0.65f, 0.15f) :
            role == FarmRobotRole.Mulching ? new Color(0.25f, 0.7f, 0.55f) : new Color(0.35f, 0.55f, 0.9f));
        var dark = Material(new Color(0.05f, 0.08f, 0.1f));
        GameObject Part(string name, Vector3 position, Vector3 scale, Material material)
        {
            var part = Box(name, position, scale, material); part.transform.SetParent(robot, false); return part;
        }
        float visualSize = size == FarmRobotSize.Large ? 1.2f : size == FarmRobotSize.Medium ? 1f : 0.8f;
        Part("Body", new Vector3(0, 0.3f, 0), new Vector3(0.55f * visualSize, 0.4f, 0.65f * visualSize), paint);
        Part("Front", new Vector3(0, 0.4f, 0.33f * visualSize), new Vector3(0.25f, 0.1f, 0.04f), dark);
        foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            Part(role == FarmRobotRole.SprayerDrone ? "Rotor" : "Wheel", new Vector3(x * 0.36f * visualSize, role == FarmRobotRole.SprayerDrone ? 0.55f : 0.12f, z * 0.23f * visualSize),
                role == FarmRobotRole.SprayerDrone ? new Vector3(0.45f, 0.04f, 0.12f) : new Vector3(0.12f, 0.22f, 0.24f), dark);
        var collider = robot.gameObject.AddComponent<BoxCollider>(); collider.center = Vector3.up * 0.3f; collider.size = new Vector3(0.56f, 0.5f, 0.56f);
        var body = robot.gameObject.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        robot.gameObject.AddComponent<FarmRobotBody>();
        if (role == FarmRobotRole.SprayerDrone)
        {
            var spray = Spray("PesticideSpray", Vector3.zero, Vector3.down);
            spray.transform.SetParent(robot, false); spray.transform.localPosition = Vector3.down * 0.05f;
            spray.transform.localRotation = Quaternion.LookRotation(Vector3.down);
            var main = spray.main; main.startColor = new Color(0.5f, 1f, 0.6f, 0.6f); main.startSpeed = 2;
        }
        return robot;
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
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission; emission.rateOverTime = 35;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 12; shape.radius = 0.02f;
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
        material.color = new Color(0.35f, 0.75f, 1f, 0.8f); materials.Add(material);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        return particles;
    }
    private void OnDestroy() { foreach (var material in materials) AutomationWorld.DestroyOwned(material); }
}
