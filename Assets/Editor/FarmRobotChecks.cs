using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FarmRobotChecks
{
    [MenuItem("Tools/Project Potato/Validate Robot Regression")]
    public static void RunRegression()
    {
        Run(); AutomationChecks.Run(); SaveGameChecks.Run();
        Debug.Log("[Farm Robots] Robot, automation and save regression checks passed.");
    }

    [MenuItem("Tools/Project Potato/Create or Select Robot Settings")]
    public static void SelectSettings()
    {
        const string path = "Assets/Resources/Automation/FarmRobotSettings.asset";
        var asset = AssetDatabase.LoadAssetAtPath<FarmRobotSettings>(path);
        if (!asset)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Automation")) AssetDatabase.CreateFolder("Assets/Resources", "Automation");
            asset = ScriptableObject.CreateInstance<FarmRobotSettings>(); AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets();
        }
        Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
    }

    [MenuItem("Tools/Project Potato/Validate Farm Robots")]
    public static void Run()
    {
        if (Application.isPlaying || !AutomationWorld.Offline) throw new InvalidOperationException("Stop Play mode and disconnect multiplayer before checking.");
        var scene = EditorSceneManager.NewPreviewScene();
        FarmRobotSettings settings = null;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Robot check failed: " + message); checks++; }
        try
        {
            var root = new GameObject("RobotChecks"); SceneManager.MoveGameObjectToScene(root, scene);
            var grid = root.AddComponent<GroundGrid>();
            var gridFields = new SerializedObject(grid); gridFields.FindProperty("origin").vector3Value = new Vector3(200, 200, 200); gridFields.ApplyModifiedPropertiesWithoutUndo();
            typeof(GroundGrid).GetMethod("Awake", flags).Invoke(grid, null);
            var clock = root.AddComponent<WorldClock>(); var farm = root.AddComponent<PotatoFarmingSystem>();
            var farmFields = new SerializedObject(farm); farmFields.FindProperty("clock").objectReferenceValue = clock;
            farmFields.FindProperty("ground").objectReferenceValue = grid; farmFields.FindProperty("bugSpawnChance").floatValue = 1; farmFields.ApplyModifiedPropertiesWithoutUndo();
            var world = root.AddComponent<AutomationWorld>();
            typeof(AutomationWorld).GetMethod("Awake", flags).Invoke(world, null);
            typeof(AutomationWorld).GetProperty("Grids").SetValue(world, new[] { grid });
            typeof(AutomationWorld).GetProperty("FarmingSystems").SetValue(world, new[] { farm });
            settings = ScriptableObject.CreateInstance<FarmRobotSettings>();
            typeof(AutomationWorld).GetField("robotSettings", flags).SetValue(world, settings);
            Check(settings.small.maxWorkCells == 10 && settings.medium.maxWorkCells == 25 && settings.large.maxWorkCells == 60, "approved work quotas");
            Check(settings.small.chargeSeconds == 60 && settings.medium.chargeSeconds == 90 && settings.large.chargeSeconds == 120, "approved charge durations");

            // A sloped ridge represents a tilled tile surface; validate the same helper used by the build preview.
            var groundRenderer = root.AddComponent<GroundTileRenderer>();
            var ridge = new GameObject("TilledSoilRidge"); ridge.transform.SetParent(root.transform);
            var tilledCell = new Vector2Int(30, 30); grid.TryDig(tilledCell);
            ridge.transform.position = grid.CellCenter(tilledCell); ridge.transform.rotation = Quaternion.Euler(35, 0, 0);
            var ridgeCollider = ridge.AddComponent<BoxCollider>(); ridgeCollider.size = new Vector3(2, 0.1f, 2);
            Check(ridgeCollider.Raycast(new Ray(grid.CellCenter(tilledCell) + Vector3.up * 2, Vector3.down), out var ridgeHit, 4) &&
                ridgeHit.normal.y < 0.9f, "tilled ridge reproduces non-flat surface");
            var placement = typeof(AutomationPlayerController).GetMethod("TryPlacementSurface", BindingFlags.Static | BindingFlags.NonPublic);
            object[] placementArgs = { world, AutomationMachineKind.Sprinkler, ridgeHit, Vector3.zero };
            Check((bool)placement.Invoke(null, placementArgs) && (Vector3)placementArgs[3] == grid.CellCenter(tilledCell),
                "sprinkler preview accepts tilled ridges at logical grid height");
            var fieldSprinkler = world.Place(AutomationMachineKind.Sprinkler, (Vector3)placementArgs[3], Quaternion.identity);
            Check(fieldSprinkler && grid.GetCellState(tilledCell) == GroundCellState.FieldSoil,
                "sprinkler can be placed on tilled soil without changing soil state");
            Check(!world.Place(AutomationMachineKind.Sprinkler, (Vector3)placementArgs[3], Quaternion.identity), "tilled placement still rejects overlapping machines");
            UnityEngine.Object.DestroyImmediate(fieldSprinkler.gameObject);
            UnityEngine.Object.DestroyImmediate(ridge); UnityEngine.Object.DestroyImmediate(groundRenderer);

            var dock = new Vector2Int(5, 5); var start = new Vector2Int(8, 10); var route = new FarmRobotRoute();
            Check(route.TryAdd(start, grid.ContainsCell) && route.Count == 1, "first work tile can be anywhere, independently of dock");
            Check(!route.TryAdd(start + new Vector2Int(1, 1), grid.ContainsCell), "diagonal-only tile connection rejected");
            Check(route.TryAdd(start + Vector2Int.up, grid.ContainsCell) && route.TryAdd(start + Vector2Int.up * 2, grid.ContainsCell) && route.Count == 3,
                "each click adds one face-adjacent tile");
            Check(route.Cells[0] == start && route.Cells.All(cell => cell.x == 8) &&
                Enumerable.Range(1, route.Count - 1).All(index => (route.Cells[index] - route.Cells[index - 1]).sqrMagnitude == 1),
                "work tiles meet face to face without gaps");
            Check(route.Toggle(new Vector2Int(8, 11)) && !route.WorksAt(1), "red transit section");
            var copy = route.Copy(); copy.Toggle(new Vector2Int(8, 11));
            Check(!route.WorksAt(1) && copy.WorksAt(1), "cancelled route edits cannot mutate committed route");
            Check(!route.TrySetEnd(start, new Vector2Int(8, 100), grid.ContainsCell) && route.Count == 3, "invalid endpoint preserves route");
            var shaped = new FarmRobotRoute();
            var shapeCells = new[] { start, start + Vector2Int.up, start + new Vector2Int(1, 1), start + new Vector2Int(2, 1),
                start + Vector2Int.right, start + Vector2Int.left, start + Vector2Int.down };
            Check(shapeCells.All(cell => shaped.TryAdd(cell, grid.ContainsCell)), "corners, loops and branches can all be placed");
            Check(!shaped.TryAdd(start + new Vector2Int(4, 4), grid.ContainsCell) && shaped.Count == shapeCells.Length,
                "disconnected additions leave range intact");
            Check(shaped.TryAdd(start, grid.ContainsCell) && shaped.Count == shapeCells.Length, "clicking existing tile does not duplicate it");
            var walk = shaped.BuildRoundTrip();
            Check(walk.First().Cell == start && walk.Last().Cell == start && walk.Select(visit => visit.Cell).Distinct().Count() == shapeCells.Length,
                "branch traversal covers every tile and returns to first tile");
            Check(walk.Count == shapeCells.Length * 2 - 1 && Enumerable.Range(1, walk.Count - 1)
                .All(index => (walk[index].Cell - walk[index - 1].Cell).sqrMagnitude == 1), "loops terminate with only face-to-face travel steps");
            shaped.RemoveLast(); Check(shaped.Count == shapeCells.Length - 1 && !shaped.Cells.Contains(shapeCells.Last()), "undo removes only last placed tile");

            var badDock = new Vector2Int(3, 3); grid.TryDig(badDock);
            Check(!world.Place(AutomationMachineKind.SeederStation, grid.CellCenter(badDock), Quaternion.identity), "dock rejected on field soil");
            var stationBlock = world.Place(AutomationMachineKind.SeederStation, grid.CellCenter(dock), Quaternion.identity);
            Check(stationBlock && stationBlock.IsRobotStation && stationBlock.Accepts(AutomationSupplyKind.Electricity) &&
                !stationBlock.Accepts(AutomationSupplyKind.Water), "dock requires electricity and rejects hoses");
            var station = stationBlock.GetComponent<FarmRobotStation>();
            Check(station.ConfigureRoute(shaped), "station accepts bent and branching ranges without straight endpoint restriction");
            Check(station.ConfigureRoute(route), "detached range accepted even when offset from dock on both axes");
            var simulate = typeof(FarmRobotStation).GetMethod("Simulate", flags);
            void Tick(FarmRobotStation robot, float seconds) => simulate.Invoke(robot, new object[] { seconds });
            Tick(station, 120); Check(station.Charge01 == 0 && station.IsAtDock, "unpowered dock cannot charge or launch");
            var generator = world.Place(AutomationMachineKind.Generator, grid.CellCenter(new Vector2Int(3, 5)), Quaternion.identity);
            var cable = world.BeginLine(generator); Check(world.Connect(cable, stationBlock), "generator connects to charging station"); world.RebuildPower();
            Tick(station, 30); Check(Mathf.Abs(station.Charge01 - 0.5f) < 0.001f && station.State == FarmRobotState.Charging, "small robot half-charged at 30 seconds");
            station.BeingConfigured = true; Tick(station, 30); Check(station.Charge01 == 1 && station.IsAtDock, "route editing prevents automatic departure");
            station.BeingConfigured = false; typeof(FarmRobotStation).GetField("nextDecision", flags).SetValue(station, 0f);
            // Test faster movement without altering the approved production defaults.
            settings.small.moveSpeed = 20; settings.small.workSeconds = 0.01f; settings.small.maxWorkCells = 1;
            bool departed = false;
            for (int step = 0; step < 100; step++)
            {
                Tick(station, 0.1f); departed |= !station.IsAtDock;
                if (departed && station.IsAtDock) break;
            }
            Check(departed && station.IsAtDock && station.Charge01 == 0, "one trip returns to initial dock and requires a fresh charge");
            Check(grid.HasPotato(new Vector2Int(8, 10)) && !grid.HasPotato(new Vector2Int(8, 11)) && !grid.HasPotato(new Vector2Int(8, 12)),
                "quota counts successful work; red section and over-quota cell remain unplanted");
            Check(!grid.HasPotato(new Vector2Int(5, 6)) && !grid.HasPotato(dock), "dock access and return travel never work outside selected range");
            Check(station.WorkedThisTrip == 1, "outbound and return never consume work twice for the same crop");
            bool neighborsPrepared = true;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    if (x != 0 || y != 0)
                        neighborsPrepared &= grid.GetCellState(new Vector2Int(8 + x, 10 + y)) == GroundCellState.FieldSoil;
            Check(neighborsPrepared, "seeding tills all eight neighbors outside the route, including red cells, without using extra quota");
            typeof(FarmRobotStation).GetField("nextDecision", flags).SetValue(station, 0f);
            for (int step = 0; step < 100; step++) { Tick(station, step == 0 ? 60 : 0.1f); if (step > 0 && station.IsAtDock) break; }
            Check(grid.HasPotato(new Vector2Int(8, 12)) && !grid.HasPotato(new Vector2Int(8, 11)), "next charged trip processes remaining work without planting red section");
            Check(grid.GetSoilCoverLayers(new Vector2Int(8, 10)) == 1, "seeder digs, plants and covers soil");

            var edgeCrop = Vector2Int.zero;
            Check(farm.TryRobotPlant(grid, edgeCrop) && grid.GetCellState(Vector2Int.right) == GroundCellState.FieldSoil &&
                grid.GetCellState(Vector2Int.up) == GroundCellState.FieldSoil && grid.GetCellState(Vector2Int.one) == GroundCellState.FieldSoil,
                "edge seeding prepares only valid neighboring cells");
            grid.TryWater(edgeCrop); farm.TryWater(grid, edgeCrop);
            farm.TryGetFarmingTile(grid, edgeCrop, out var preservedCrop);
            grid.TrySetCellState(new Vector2Int(2, 0), GroundCellState.PlantingHole);
            Check(farm.TryRobotPlant(grid, Vector2Int.one) && grid.HasPotato(edgeCrop) && grid.IsWatered(edgeCrop) &&
                farm.TryGetFarmingTile(grid, edgeCrop, out var sameCrop) && ReferenceEquals(preservedCrop, sameCrop) &&
                grid.GetCellState(new Vector2Int(2, 0)) == GroundCellState.PlantingHole,
                "neighbor preparation preserves existing crops, watering and planting holes");
            Check(!farm.TryRobotPlant(grid, edgeCrop) && grid.IsWatered(edgeCrop), "rejected planting does not modify existing crop");

            var mulching = world.Place(AutomationMachineKind.MulchingStation, grid.CellCenter(new Vector2Int(10, 5)), Quaternion.identity).GetComponent<FarmRobotStation>();
            Check(farm.CanRobotMulch(grid, new Vector2Int(8, 10)) && farm.TryApplyMulchingFilm(grid, new Vector2Int(8, 10)) &&
                !farm.CanRobotMulch(grid, new Vector2Int(8, 10)), "mulching reuses early-stage rule and never repeats completed work");

            var drone = world.Place(AutomationMachineKind.DroneStation, grid.CellCenter(new Vector2Int(15, 5)), Quaternion.identity).GetComponent<FarmRobotStation>();
            var seederBody = station.GetComponentInChildren<FarmRobotBody>();
            var droneBody = drone.GetComponentInChildren<FarmRobotBody>();
            // Preview scenes do not run normal MonoBehaviour lifecycle callbacks.
            typeof(FarmRobotBody).GetMethod("OnEnable", flags).Invoke(seederBody, null);
            typeof(FarmRobotBody).GetMethod("OnEnable", flags).Invoke(droneBody, null);
            Check(Physics.GetIgnoreCollision(seederBody.GetComponent<Collider>(), droneBody.GetComponent<Collider>()),
                "robot body pairs ignore physical collision across robot types");
            var blocksMovement = typeof(FarmRobotStation).GetMethod("BlocksMovement", flags);
            Check(!(bool)blocksMovement.Invoke(station, new object[] { droneBody.GetComponent<Collider>() }),
                "another robot is not treated as a route obstacle");
            Check((bool)blocksMovement.Invoke(station, new object[] { drone.GetComponent<Collider>() }) &&
                (bool)blocksMovement.Invoke(station, new object[] { generator.GetComponent<Collider>() }),
                "charging docks and other machines still block robot movement");
            var area = new FarmDroneArea { minimum = new Vector2Int(14, 7), maximum = new Vector2Int(16, 9) };
            Check(drone.ValidArea(area) && drone.ConfigureAreas(new[] { area, area, area, area }), "drone accepts four bounded square areas");
            Check(!drone.ConfigureAreas(new[] { area, area, area, area, area }), "fifth drone area rejected");
            Check(!drone.ValidArea(new FarmDroneArea { minimum = new Vector2Int(14, 7), maximum = new Vector2Int(18, 11) }), "small drone area size limit");
            Check(!drone.ValidArea(new FarmDroneArea { minimum = new Vector2Int(40, 40), maximum = new Vector2Int(40, 40) }), "drone designation range enforced");
            var protectedCell = new Vector2Int(15, 8); farm.TryRobotPlant(grid, protectedCell); grid.TryWater(protectedCell); farm.TryWater(grid, protectedCell);
            Check(farm.TryPreventBugs(grid, protectedCell, 180) && !farm.TryPreventBugs(grid, protectedCell, 180), "preventive spray applies once while protection lasts");
            clock.AdvanceBy(80); farm.TryGetFarmingTile(grid, protectedCell, out var protectedCrop);
            Check(!protectedCrop.HasBugs && protectedCrop.HasBugPrevention, "protection prevents guaranteed bug spawn");
            Check(!farm.CanRobotMulch(grid, protectedCell), "mulching excludes crops beyond sprout stage");
            var expired = new Vector2Int(17, 8); farm.TryRobotPlant(grid, expired); grid.TryWater(expired); farm.TryWater(grid, expired);
            Check(farm.TryPreventBugs(grid, expired, 1), "short prevention for expiry check"); clock.AdvanceBy(80);
            farm.TryGetFarmingTile(grid, expired, out var expiredCrop);
            Check(expiredCrop.HasBugs && !farm.TryPreventBugs(grid, expired, 180), "expired prevention does not suppress bugs or cure existing infestation");

            Debug.Log($"[Farm Robots] {checks} checks passed.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); if (settings) UnityEngine.Object.DestroyImmediate(settings); }
    }
}
