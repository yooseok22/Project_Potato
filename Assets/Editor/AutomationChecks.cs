using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Supply topology and real farming integration, isolated from the user's scene.</summary>
public static class AutomationChecks
{
    [MenuItem("Tools/Project Potato/Validate Farm Automation")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run the deterministic checks outside Play mode.");
        if (!AutomationWorld.Offline) throw new InvalidOperationException("Disconnect multiplayer first.");
        var scene = EditorSceneManager.NewPreviewScene();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Automation check failed: " + message);
            checks++;
        }
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var root = new GameObject("AutomationChecks"); SceneManager.MoveGameObjectToScene(root, scene);
            var world = root.AddComponent<AutomationWorld>();
            AutomationBlock Block(AutomationMachineKind kind, float x, float z = 0)
            {
                // Same factory as construction, but no ground-placement dependency in topology checks.
                var block = AutomationMachineFactory.Create(kind, 2, root.transform);
                block.transform.position = new Vector3(x, 0, z); world.Blocks.Add(block); return block;
            }
            AutomationLine Connect(AutomationBlock source, AutomationBlock target)
            {
                var line = world.BeginLine(source);
                Check(line && world.Connect(line, target), "connect " + source.Kind + " to " + target.Kind);
                return line;
            }
            var generator = Block(AutomationMachineKind.Generator, 0, -2);
            var tank = Block(AutomationMachineKind.WaterTank, 2, -2);
            var sprinkler = Block(AutomationMachineKind.Sprinkler, 0);
            Check(generator.OutletCount == 3 && tank.OutletCount == 4, "source outlet counts");
            world.RebuildPower(); Check(!sprinkler.IsOperating, "unconnected sprinkler stops");
            var power = Connect(generator, sprinkler); world.RebuildPower();
            Check(!sprinkler.IsOperating && sprinkler.PowerFraction == 1, "water also required");
            var water = Connect(tank, sprinkler); world.RebuildPower();
            Check(sprinkler.IsOperating, "both supplies activate sprinkler");
            var bad = world.BeginLine(tank);
            Check(!world.Connect(bad, generator), "water rejected by generator"); world.RemoveLine(bad);
            var duplicate = world.BeginLine(tank);
            Check(!world.Connect(duplicate, sprinkler), "duplicate inlet rejected"); world.RemoveLine(duplicate);
            world.RemoveLine(power); world.RebuildPower(); Check(!sprinkler.IsOperating, "disconnect power stops sprinkler");
            power = Connect(generator, sprinkler);
            world.RemoveLine(water); world.RebuildPower(); Check(!sprinkler.IsOperating, "disconnect water stops sprinkler");
            water = Connect(tank, sprinkler);

            var cropRoot = new GameObject("Crops"); cropRoot.transform.SetParent(root.transform);
            var grid = cropRoot.AddComponent<GroundGrid>(); typeof(GroundGrid).GetMethod("Awake", flags).Invoke(grid, null);
            var clock = cropRoot.AddComponent<WorldClock>(); var farming = cropRoot.AddComponent<PotatoFarmingSystem>();
            sprinkler.transform.position = grid.CellCenter(new Vector2Int(25, 25));
            generator.transform.position = sprinkler.transform.position + Vector3.back * 2;
            tank.transform.position = sprinkler.transform.position + Vector3.back * 4;
            var left = new Vector2Int(24, 25); var right = new Vector2Int(26, 25); var forward = new Vector2Int(25, 27);
            foreach (var cell in new[] { left, right, forward }) Check(farming.TryStartFarming(grid, cell, clock), "register crop " + cell);
            world.RebuildPower();
            var waterCrops = typeof(AutomationBlock).GetMethod("WaterCrops", flags);
            Check((int)waterCrops.Invoke(sprinkler, new object[] { farming, grid }) == 2, "waters both sides of T bar");
            Check(grid.IsWatered(left) && grid.IsWatered(right) && !grid.IsWatered(forward), "strip coverage");
            farming.TryGetFarmingTile(grid, left, out var crop);
            Check(crop.IsWatered, "watering starts existing growth timer");
            Check((int)waterCrops.Invoke(sprinkler, new object[] { farming, grid }) == 0, "repeat watering does not reset crop");
            sprinkler.SetSprinklerPassThroughUnlocked(true);
            Check(sprinkler.GetComponent<BoxCollider>().isTrigger && !sprinkler.GetComponent<UnityEngine.AI.NavMeshObstacle>().enabled,
                "upgrade permits players and navigation while retaining targeting");
            sprinkler.SetSprinklerPassThroughUnlocked(false);
            Check(!sprinkler.GetComponent<BoxCollider>().isTrigger, "pillar blocks by default");

            world.Disconnect(sprinkler);
            generator.transform.position = new Vector3(0, 0, -2);
            var railA = Block(AutomationMachineKind.Conveyor, 0, 0);
            var railB = Block(AutomationMachineKind.Conveyor, 0, 2);
            var railC = Block(AutomationMachineKind.Conveyor, 0, 4);
            Check(AutomationWorld.AreAdjacentRails(railA, railB) && !AutomationWorld.AreAdjacentRails(railA, railC), "cardinal adjacency only");
            Connect(generator, railA); world.RebuildPower();
            Check(railA.PowerFraction == 1 && railB.PowerFraction < railA.PowerFraction && railC.PowerFraction < railB.PowerFraction,
                "power attenuates along rail chain");
            Check(railB.TransportSpeed > railC.TransportSpeed && railC.TransportSpeed > 0, "low power reduces speed");
            var reinforcement = Connect(generator, railC); world.RebuildPower();
            Check(railC.PowerFraction == 1, "downstream supply restores speed");
            world.RemoveLine(reinforcement);
            var output = new SerializedObject(generator); output.FindProperty("generatorOutput").floatValue = 15; output.ApplyModifiedPropertiesWithoutUndo();
            world.RebuildPower(); Check(Mathf.Abs(railA.PowerFraction - 0.5f) < 0.001f, "rail count shares generator capacity");
            var repeated = Connect(generator, railC); world.RebuildPower();
            Check(Mathf.Abs(railA.PowerFraction - 0.5f) < 0.001f, "second cord cannot duplicate one generator's capacity");
            world.RemoveLine(repeated);
            var secondGenerator = Block(AutomationMachineKind.Generator, 2, 4);
            var secondSettings = new SerializedObject(secondGenerator); secondSettings.FindProperty("generatorOutput").floatValue = 15;
            secondSettings.ApplyModifiedPropertiesWithoutUndo();
            Connect(secondGenerator, railC); world.RebuildPower();
            Check(railA.PowerFraction == 1 && railC.PowerFraction == 1, "distinct generators add capacity and reinforce weak endpoints");
            world.RemoveBlock(secondGenerator);
            world.RemoveBlock(railB); world.RebuildPower();
            Check(railC.PowerFraction == 0, "gap interrupts supply");

            var slots = Enumerable.Range(0, 3).Select(_ => world.BeginLine(tank)).ToArray();
            Check(slots.All(line => line), "tank offers remaining four outlets");
            var fourth = world.BeginLine(tank); Check(fourth && !world.BeginLine(tank), "fifth hose rejected");
            var lengthLine = slots[0];
            Check(!lengthLine.TryPin(tank.PortPosition + Vector3.right * 17), "hose maximum enforced while pinning");
            Check(lengthLine.TryPin(tank.PortPosition + Vector3.left * 7), "pin accepted inside budget");
            var farSprinkler = Block(AutomationMachineKind.Sprinkler, tank.transform.position.x + 7, tank.transform.position.z);
            Check(!world.Connect(lengthLine, farSprinkler), "route via anchor exceeds length despite nearby endpoints");
            lengthLine.RemoveLastAnchor(); Check(world.Connect(lengthLine, farSprinkler), "unpin frees length");
            Check(lengthLine.GetComponentsInChildren<Collider>().Length == 0, "lines can be walked over");
            var source = Block(AutomationMachineKind.Generator, 30);
            var reserved = Enumerable.Range(0, 3).Select(_ => world.BeginLine(source)).ToArray();
            Check(reserved.All(line => line) && !world.BeginLine(source), "fourth power cord rejected");
            world.RemoveLine(reserved[0]); Check(world.BeginLine(source), "cancel returns outlet");
            world.RemoveBlock(source); Check(!world.Lines.Any(line => line && line.Source == source), "demolition cleans up connected lines");
            var longSource = Block(AutomationMachineKind.Generator, 38, -2);
            var largeOutput = new SerializedObject(longSource); largeOutput.FindProperty("generatorOutput").floatValue = 200;
            largeOutput.ApplyModifiedPropertiesWithoutUndo();
            var longRails = Enumerable.Range(0, 16).Select(index => Block(AutomationMachineKind.Conveyor, 40, index * 2)).ToArray();
            Connect(longSource, longRails[0]); world.RebuildPower();
            Check(longRails[14].PowerFraction > 0 && longRails[15].PowerFraction == 0, "power expires after 15 rail hops");
            longRails[0].gameObject.SetActive(false); world.RebuildPower();
            Check(longRails.All(rail => rail.PowerFraction == 0), "disabled input cannot conduct power");
            Check(!world.CanPlace(railA.transform.position, 2), "occupied machine footprint rejected");
            Debug.Log($"[Automation] {checks} deterministic checks passed.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
