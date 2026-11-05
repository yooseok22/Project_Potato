using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>A temporary play fixture. No scene or player save files are written.</summary>
public static class FarmRobotPlayChecks
{
    private static GameObject fixture;
    private static FarmRobotStation seeder, drone, detour;
    private static GroundGrid grid;
    private static double began;
    private static bool stopped;
    private static bool seederLeft, droneLeft, detourLeft;
    private static bool seederHome, droneHome, detourHome;
    private static GameObject returnBlocker;
    private static bool detouredOffLine;
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [InitializeOnLoadMethod]
    private static void RestorePendingRun()
    {
        if (!SessionState.GetBool("FarmRobotPlayCheckPending", false)) return;
        began = EditorApplication.timeSinceStartup; EditorApplication.update -= Update; EditorApplication.update += Update;
    }

    [MenuItem("Tools/Project Potato/Run Farm Robot Play Checks")]
    public static void Begin()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before running the temporary robot fixture.");
        EditorApplication.update -= Update;
        fixture = null; stopped = false; seederLeft = droneLeft = detourLeft = false;
        returnBlocker = null; detouredOffLine = false;
        seederHome = droneHome = detourHome = false;
        began = EditorApplication.timeSinceStartup;
        SessionState.SetBool("FarmRobotPlayCheckPending", true);
        EditorApplication.update += Update;
        EditorApplication.isPlaying = true;
    }
    private static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPaused) return;
        if (!Application.isPlaying)
        {
            if (EditorApplication.timeSinceStartup - began > 15)
            { SessionState.SetBool("FarmRobotPlayCheckPending", false); EditorApplication.update -= Update; }
            return;
        }
        try
        {
            if (!fixture)
            {
                if (!AutomationWorld.Instance) return;
                Build(); began = EditorApplication.timeSinceStartup; return;
            }
            if (!seeder || !drone || !detour) throw new Exception("Robot fixture was unexpectedly removed.");
            seederLeft |= !seeder.IsAtDock; droneLeft |= !drone.IsAtDock; detourLeft |= !detour.IsAtDock;
            if (detour.WorkedThisTrip >= 3 && !returnBlocker)
            {
                returnBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube); returnBlocker.name = "RobotReturnDetourObstacle";
                returnBlocker.transform.SetParent(fixture.transform);
                returnBlocker.transform.position = grid.CellCenter(new Vector2Int(20, 6)) + Vector3.up * 0.7f;
                returnBlocker.transform.localScale = new Vector3(1.5f, 1.4f, 1.5f); Physics.SyncTransforms();
            }
            var movingBody = detour.transform.Find("Robot_Seeder");
            if (returnBlocker && movingBody && Mathf.Abs(movingBody.position.x - detour.transform.position.x) > 0.5f) detouredOffLine = true;
            seederHome |= seederLeft && seeder.IsAtDock;
            droneHome |= droneLeft && drone.IsAtDock;
            detourHome |= detourLeft && detour.IsAtDock && !string.IsNullOrEmpty(detour.LastNotice) && detouredOffLine;
            if (seederHome && droneHome && detourHome)
            {
                if (!grid.HasPotato(new Vector2Int(8, 8)) || grid.HasPotato(new Vector2Int(9, 8)) || !grid.HasPotato(new Vector2Int(9, 9)) ||
                    !grid.HasPotato(new Vector2Int(10, 9)) || !grid.HasPotato(new Vector2Int(8, 9)) ||
                    grid.HasPotato(new Vector2Int(5, 6)))
                    throw new Exception("Live seeding did not respect blue and red route sections.");
                var farming = fixture.GetComponent<PotatoFarmingSystem>();
                if (!farming.TryGetFarmingTile(grid, new Vector2Int(12, 8), out var crop) || !crop.HasBugPrevention)
                    throw new Exception("Live drone did not apply prevention.");
                if (!grid.HasPotato(new Vector2Int(20, 12)) || detour.WorkedThisTrip != 4 ||
                    grid.HasPotato(new Vector2Int(20, 7)) || grid.HasPotato(new Vector2Int(20, 9)) || grid.HasPotato(new Vector2Int(20, 11)) ||
                    grid.HasPotato(new Vector2Int(19, 8)))
                    throw new Exception("Robot failed to continue beyond installed machines, or worked on an obstacle/detour tile.");
                Debug.Log("[Farm Robots Play] PASS: generator/tank/sprinkler detours continue work, occupied cells skipped, detours never plant, detached branching range, charging, drone prevention and return-home detour.");
                End();
            }
            else if (EditorApplication.timeSinceStartup - began > 45)
                throw new Exception($"Live robot timeout: seeder={seeder.State}/{seeder.WorkedThisTrip}, drone={drone.State}/{drone.WorkedThisTrip}, detour={detour.State}/{detour.LastNotice}.");
        }
        catch (Exception error) { Debug.LogException(error); End(); }
    }
    private static void End()
    {
        if (stopped) return; stopped = true;
        SessionState.SetBool("FarmRobotPlayCheckPending", false);
        EditorApplication.update -= Update; EditorApplication.isPlaying = false;
    }
    private static void Build()
    {
        var world = AutomationWorld.Instance;
        fixture = new GameObject("TemporaryRobotPlayChecks");
        grid = fixture.AddComponent<GroundGrid>();
        var groundFields = new SerializedObject(grid); groundFields.FindProperty("origin").vector3Value = new Vector3(200, 200, 200); groundFields.ApplyModifiedPropertiesWithoutUndo();
        var clock = fixture.AddComponent<WorldClock>(); var farming = fixture.AddComponent<PotatoFarmingSystem>();
        var fields = new SerializedObject(farming); fields.FindProperty("ground").objectReferenceValue = grid; fields.FindProperty("clock").objectReferenceValue = clock;
        fields.FindProperty("bugSpawnChance").floatValue = 0; fields.ApplyModifiedPropertiesWithoutUndo();
        typeof(AutomationWorld).GetMethod("ResolveScene", Flags).Invoke(world, null);
        // This disposable configuration never changes the saved production tuning asset.
        var settings = ScriptableObject.CreateInstance<FarmRobotSettings>(); settings.hideFlags = HideFlags.DontSave;
        settings.small.chargeSeconds = 1; settings.small.moveSpeed = 6; settings.small.workSeconds = 0.05f;
        typeof(AutomationWorld).GetField("robotSettings", Flags).SetValue(world, settings);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "RobotFixtureFloor"; floor.transform.SetParent(fixture.transform);
        floor.transform.position = new Vector3(240, 199.7f, 240); floor.transform.localScale = new Vector3(100, 0.6f, 100);
        FarmRobotStation Station(AutomationMachineKind kind, Vector2Int cell)
        {
            var block = world.Place(kind, grid.CellCenter(cell), Quaternion.identity);
            if (!block) throw new Exception("Fixture station placement rejected.");
            var source = world.Place(AutomationMachineKind.Generator, grid.CellCenter(cell + Vector2Int.left * 2), Quaternion.identity);
            if (!source || !world.Connect(world.BeginLine(source), block)) throw new Exception("Fixture power connection failed.");
            return block.GetComponent<FarmRobotStation>();
        }
        seeder = Station(AutomationMachineKind.SeederStation, new Vector2Int(5, 5));
        var route = new FarmRobotRoute();
        foreach (var cell in new[] { new Vector2Int(8, 8), new Vector2Int(9, 8), new Vector2Int(9, 9), new Vector2Int(10, 9), new Vector2Int(8, 9) })
            route.TryAdd(cell, grid.ContainsCell);
        route.Toggle(new Vector2Int(9, 8)); seeder.ConfigureRoute(route);
        drone = Station(AutomationMachineKind.DroneStation, new Vector2Int(12, 5));
        farming.TryRobotPlant(grid, new Vector2Int(12, 8));
        drone.ConfigureAreas(new[] { new FarmDroneArea { minimum = new Vector2Int(11, 7), maximum = new Vector2Int(13, 9) } });
        detour = Station(AutomationMachineKind.SeederStation, new Vector2Int(20, 5));
        var detourRoute = new FarmRobotRoute(); detourRoute.TrySetEnd(new Vector2Int(20, 6), new Vector2Int(20, 12), grid.ContainsCell); detour.ConfigureRoute(detourRoute);
        foreach (var entry in new[] { (AutomationMachineKind.Generator, 7), (AutomationMachineKind.WaterTank, 9), (AutomationMachineKind.Sprinkler, 11) })
            if (!world.Place(entry.Item1, grid.CellCenter(new Vector2Int(20, entry.Item2)), Quaternion.identity))
                throw new Exception("Fixture machine obstacle placement failed.");
        Physics.SyncTransforms(); world.RebuildPower();
    }
}
