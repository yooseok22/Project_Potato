using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoStarchProcessingChecks
{
    private const string BlenderPath = "Assets/Fbx/Food Processing/Potato Millionaire Strach Blender 1.fbx";
    private const string MakerPath = "Assets/Fbx/Food Processing/Potato Millionaire Strach Maker 1.fbx";
    private const string DropSourcePath = "Assets/Fbx/Fbx/Potato Millionaire Strach Maker 1.fbx";
    private const string SetupRootName = "PotatoStarchTestSetup";
    private static readonly string[] BlenderClips = { "Anim_Blender Lid Open", "Anim_Blender Lid Close", "Anim_Blender Start", "Anim_Blender Working", "Anim_Blender End" };
    private static readonly string[] MakerClips = { "Anim_Starch Water Basket Use", "Anim_Starch Water Basket Working", "Anim_Starch Dry Basket Use", "Anim_Starch Maker H1-1 Use", "Anim_Starch Maker H2-1 Use", "Anim_Starch Water Basket Drop", "Anim_Starch Dry Basket Drop" };

    [MenuItem("Tools/Project Potato/Setup Potato Starch Test")]
    public static void SetupTestScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene = SceneManager.GetActiveScene();
        var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        GameObject Find(string name) => objects.FirstOrDefault(t => t.name == name)?.gameObject;
        var blenderProp = Find("Potato Millionaire Strach Blender 1");
        var makerProp = Find("Potato Millionaire Strach Maker 1");
        if (!blenderProp || !makerProp) throw new InvalidOperationException("Add the new Blender and Maker FBX instances to the current scene first.");
        var clock = objects.Select(t => t.GetComponent<WorldClock>()).FirstOrDefault(c => c);
        if (!clock) throw new InvalidOperationException("The scene needs WorldClock.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Connect animated starch props");
        var root = Find(SetupRootName);
        if (!root) { root = new GameObject(SetupRootName); Undo.RegisterCreatedObjectUndo(root, "Create starch setup"); }
        // Only remove generated placeholder objects; imported props retain their placement.
        foreach (Transform child in root.transform.Cast<Transform>().ToArray())
            if (child.name == "StarchBlender_Test" || child.name == "StarchSieve_Test" || child.name == "OutdoorDryingGround_Test")
                Undo.DestroyObjectImmediate(child.gameObject);
        Undo.SetTransformParent(blenderProp.transform, root.transform, "Connect blender");
        Undo.SetTransformParent(makerProp.transform, root.transform, "Connect maker");
        ConfigureStation(root, blenderProp, makerProp, clock, true);
        var bundle = objects.FirstOrDefault(t => t.name == "FBX_Starch Packaging Bundle");
        if (!bundle) throw new InvalidOperationException("The scene needs FBX_Starch Packaging Bundle.");
        // Bring the existing packaging display beside the test equipment if it is still at its export location.
        var packagingRoot = bundle.parent;
        if (Vector3.Distance(bundle.position, makerProp.transform.position) > 20f)
        {
            Undo.RecordObject(packagingRoot, "Place packaging beside starch station");
            var bounds = BoundsOf(bundle.GetComponentsInChildren<Renderer>());
            var target = makerProp.transform.position + makerProp.transform.right * 2.5f;
            packagingRoot.position += target - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(packagingRoot);
        }
        Component<StarchPackagingBundle>(bundle.gameObject, true);
        Box(bundle.gameObject, BoundsOf(bundle.GetComponentsInChildren<Renderer>()), true);
        Outline(bundle.gameObject, bundle.GetComponentsInChildren<Renderer>(), true);
        var packagingAnimator = packagingRoot.GetComponent<Animator>();
        if (packagingAnimator) { Undo.RecordObject(packagingAnimator, "Disable unused packaging animation"); packagingAnimator.enabled = false; Dirty(packagingAnimator); }
        foreach (var player in objects.Select(t => t.GetComponent<PlayerInteractionController>()).Where(p => p))
            Component<StarchPackagingInventory>(player.gameObject, true);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("[PotatoStarch] 새 믹서기·재배기 연결 및 씬 저장 완료. 믹서기에서 [E]로 시작하세요.", root);
    }

    private static T Component<T>(GameObject go, bool undo) where T : Component
        => go.GetComponent<T>() ? go.GetComponent<T>() : undo ? Undo.AddComponent<T>(go) : go.AddComponent<T>();

    private static Transform Child(Transform parent, string name, bool undo)
    {
        var found = parent.Find(name);
        if (found) return found;
        var go = new GameObject(name);
        if (undo) Undo.RegisterCreatedObjectUndo(go, "Create starch anchor");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static StarchPropAnimationDriver Driver(GameObject prop, string assetPath, string[] names,
        StarchAnimationId firstId, bool undo)
    {
        var driver = Component<StarchPropAnimationDriver>(prop, undo);
        var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().ToArray();
        var poses = new List<StarchPropAnimationDriver.ClipPose>();
        if (undo) Undo.RecordObjects(prop.GetComponentsInChildren<Transform>(true).Cast<UnityEngine.Object>().Concat(new UnityEngine.Object[] { driver, prop.GetComponent<Animator>() }).ToArray(), "Set starch initial pose");
        for (int index = 0; index < names.Length; index++)
        {
            var clip = clips.SingleOrDefault(c => c.name == names[index]);
            if (!clip && names[index] == "Anim_Starch Dry Basket Drop")
                clip = AssetDatabase.LoadAllAssetsAtPath(DropSourcePath).OfType<AnimationClip>().Single(c => c.name == names[index]);
            if (!clip) throw new InvalidOperationException("Missing starch clip: " + names[index]);
            if (clip.isLooping) throw new InvalidOperationException("Starch clips must not loop: " + clip.name);
            var tracks = new Dictionary<string, StarchPropAnimationDriver.TransformTrack>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform)) throw new InvalidOperationException("Unsupported clip binding: " + binding.type);
                if (!tracks.TryGetValue(binding.path, out var track))
                {
                    var target = prop.transform.Find(binding.path);
                    if (!target) throw new InvalidOperationException("Missing animation target: " + binding.path);
                    track = new StarchPropAnimationDriver.TransformTrack { target = target };
                    tracks.Add(binding.path, track);
                }
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                switch (binding.propertyName)
                {
                    case "m_LocalPosition.x": track.px = curve; break;
                    case "m_LocalPosition.y": track.py = curve; break;
                    case "m_LocalPosition.z": track.pz = curve; break;
                    case "m_LocalRotation.x": track.rx = curve; break;
                    case "m_LocalRotation.y": track.ry = curve; break;
                    case "m_LocalRotation.z": track.rz = curve; break;
                    case "m_LocalRotation.w": track.rw = curve; break;
                    case "m_LocalScale.x": track.sx = curve; break;
                    case "m_LocalScale.y": track.sy = curve; break;
                    case "m_LocalScale.z": track.sz = curve; break;
                    default: throw new InvalidOperationException("Unsupported curve: " + binding.propertyName);
                }
            }
            poses.Add(new StarchPropAnimationDriver.ClipPose { id = firstId + index, source = clip, tracks = tracks.Values.ToArray() });
        }
        driver.Configure(prop.GetComponent<Animator>(), poses.ToArray());
        Dirty(driver);
        Dirty(prop.GetComponent<Animator>());
        return driver;
    }
    private static void Dirty(UnityEngine.Object value) { if (value) EditorUtility.SetDirty(value); }
    private static Bounds BoundsOf(IEnumerable<Renderer> renderers)
    {
        var list = renderers.ToArray();
        var bounds = list[0].bounds;
        foreach (var renderer in list.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }
    private static void Box(GameObject go, Bounds world, bool undo)
    {
        var box = Component<BoxCollider>(go, undo);
        if (undo) Undo.RecordObject(box, "Set starch interaction collider");
        // Props use upright, axis-aligned scene placement; transform all corners for rotated variants.
        var local = new Bounds(go.transform.InverseTransformPoint(world.center), Vector3.zero);
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            local.Encapsulate(go.transform.InverseTransformPoint(new Vector3(x == 0 ? world.min.x : world.max.x,
                y == 0 ? world.min.y : world.max.y, z == 0 ? world.min.z : world.max.z)));
        box.center = local.center;
        box.size = local.size + Vector3.one * 0.015f;
        Dirty(box);
    }
    private static void Outline(GameObject go, Renderer[] renderers, bool undo)
    {
        var outline = Component<InteractionOutline>(go, undo);
        var serialized = new SerializedObject(outline);
        var array = serialized.FindProperty("targetRenderers");
        array.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Dirty(outline);
    }
    private static ProcessingCarryItem Carry(GameObject prop, string name, Transform sourceRoot,
        SkinnedMeshRenderer[] renderers, float heldSize, bool undo)
    {
        var root = Child(prop.transform, name, undo);
        if (undo) Undo.RecordObject(root, "Set carry home");
        root.SetPositionAndRotation(sourceRoot.position, prop.transform.rotation);
        root.localScale = Vector3.one;
        var rig = Component<ProcessingCarryRig>(root.gameObject, undo);
        rig.Configure(sourceRoot, renderers);
        var carry = Component<ProcessingCarryItem>(root.gameObject, undo);
        var bounds = BoundsOf(renderers);
        float dimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        carry.ConfigureHeldPose(new Vector3(0f, 0.02f, 0.12f), Vector3.zero,
            prop.transform.lossyScale * (heldSize / Mathf.Max(0.01f, dimension)));
        if (name == "DryingTray_Carry")
            carry.ConfigureHeldPose(new Vector3(-0.22f, -0.12f, 0.32f), new Vector3(-25f, 0f, 0f),
                prop.transform.lossyScale * (heldSize / Mathf.Max(0.01f, dimension)));
        if (name == "WaterBasket_Carry")
            carry.ConfigureHeldPose(new Vector3(-0.12f, 0.04f, 0.28f), Vector3.zero,
                prop.transform.lossyScale * (heldSize / Mathf.Max(0.01f, dimension)));
        Box(root.gameObject, bounds, undo);
        Outline(root.gameObject, renderers, undo);
        Dirty(rig); Dirty(carry);
        return carry;
    }
    private static PotatoStarchProcessor ConfigureStation(GameObject root, GameObject blenderProp,
        GameObject makerProp, WorldClock clock, bool undo)
    {
        var blenderDriver = Driver(blenderProp, BlenderPath, BlenderClips, StarchAnimationId.BlenderOpen, undo);
        var makerDriver = Driver(makerProp, MakerPath, MakerClips, StarchAnimationId.BasketFill, undo);
        SkinnedMeshRenderer Renderer(GameObject prop, string name) => prop.transform.Find(name).GetComponent<SkinnedMeshRenderer>();
        var head = Renderer(blenderProp, "FBX_Blender Head");
        var lid = Renderer(blenderProp, "FBX_Blender Lid");
        var trayMesh = Renderer(makerProp, "FBX_Starch Dry Basket");
        var waterMesh = Renderer(makerProp, "FBX_Starch Water Basket");
        var blenderBone = blenderProp.transform.Find("B_Blender Pelvis/B_Blender All/B_Blender Head");
        var trayBone = makerProp.transform.Find("B_Starch Maker Pelvis/B_Starch Maker.001/B_Starch Dry Basket");
        var waterBone = makerProp.transform.Find("B_Starch Maker Pelvis/B_Starch Maker.001/B_Starch Water Basket");
        var blenderCarry = Carry(blenderProp, "BlenderContainer_Carry", blenderBone, new[] { head, lid }, 0.48f, undo);
        var trayCarry = Carry(makerProp, "DryingTray_Carry", trayBone, new[] { trayMesh }, 0.62f, undo);
        var waterCarry = Carry(makerProp, "WaterBasket_Carry", waterBone, new[] { waterMesh }, 0.55f, undo);
        var blender = Component<StarchBlender>(blenderProp, undo);
        var sieve = Component<StarchSieve>(makerProp, undo);
        var tray = Component<StarchDryingTray>(trayCarry.gameObject, undo);
        var waterBasket = Component<StarchWaterBasket>(waterCarry.gameObject, undo);
        waterBasket.ConfigureCollider(waterBone.Find("B_Starch Water Basket"));
        var makerBounds = BoundsOf(makerProp.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r != trayMesh));
        var trayBounds = trayMesh.bounds;
        var interactionBounds = makerBounds;
        var bottom = interactionBounds.min;
        // Keep the lower open frame free of a solid interaction box so the visible
        // front lip of the dry tray can be aimed at from standing height.
        bottom.y = Mathf.Max(trayBounds.max.y + 0.025f,
            Renderer(makerProp, "FBX_Starch Water Basket").bounds.min.y);
        interactionBounds.SetMinMax(bottom, interactionBounds.max);
        Box(makerProp, interactionBounds, undo);
        Box(blenderProp, BoundsOf(blenderProp.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r != head && r != lid)), undo);
        Outline(blenderProp, blenderProp.GetComponentsInChildren<SkinnedMeshRenderer>().Cast<Renderer>().ToArray(), undo);
        Outline(makerProp, makerProp.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r != trayMesh && r != waterMesh).Cast<Renderer>().ToArray(), undo);
        var anchor = Child(makerProp.transform, "DryingAnchor", undo);
        if (undo) Undo.RecordObject(anchor, "Position top drying anchor");
        // The imported Dry Basket Drop starts precisely on the upper support ledges.
        // Use the same pose for carrying/placement so rejoining the rig has no visible snap.
        var animatedTrayBone = trayBone.Find("B_Starch Dry Basket");
        Vector3 lowerBonePosition = animatedTrayBone.position;
        makerDriver.SetTime(StarchAnimationId.DryBasketDrop, 0d);
        Vector3 supportOffset = animatedTrayBone.position - lowerBonePosition;
        anchor.SetPositionAndRotation(trayCarry.transform.position + supportOffset, makerProp.transform.rotation);
        makerDriver.SetTime(StarchAnimationId.DryBasketDrop, makerDriver.Length(StarchAnimationId.DryBasketDrop));
        anchor.localScale = Vector3.one;
        var processor = Component<PotatoStarchProcessor>(root, undo);
        if (undo) Undo.RecordObject(processor, "Configure starch station");
        processor.Configure(clock, blender, sieve, tray, blenderDriver, makerDriver, anchor, waterBasket);
        Dirty(processor); Dirty(blender); Dirty(sieve); Dirty(tray); Dirty(waterBasket);
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            Dirty(component);
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        return processor;
    }

    [MenuItem("Tools/Project Potato/Validate Potato Starch Processing")]
    public static void RunValidation()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Starch check failed: " + message);
            checks++;
        }
        try
        {
            var root = new GameObject("StarchValidation");
            SceneManager.MoveGameObjectToScene(root, scene);
            var blender = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BlenderPath), root.transform);
            var maker = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MakerPath), root.transform);
            var clock = root.AddComponent<WorldClock>();
            clock.Speed = 1f;
            clock.IsPaused = false;
            var p = ConfigureStation(root, blender, maker, clock, false);
            var playerObject = Child(root.transform, "PackagingPlayer", false).gameObject;
            var player = playerObject.AddComponent<PlayerInteractionController>();
            var items = playerObject.AddComponent<PlayerItemController>();
            // Preview scenes do not run PlayerItemController.Start, so supply its normal hand anchor.
            var handAnchor = Child(playerObject.transform, "HandAnchor", false);
            handAnchor.position = new Vector3(4f, 2f, -1f);
            typeof(PlayerItemController).GetField("<HeldItemAnchor>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(items, handAnchor);
            var waterBasket = maker.GetComponentInChildren<StarchWaterBasket>();
            var waterRenderer = maker.transform.Find("FBX_Starch Water Basket").GetComponent<SkinnedMeshRenderer>();
            var initialWaterVertices = WorldVertices(waterRenderer);
            var held = Child(root.transform, "HeldObject", false).gameObject.AddComponent<ProcessingCarryItem>();
            var packagingProp = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Fbx/Food Processing/Potato Millionaire Starch Pack.fbx"), root.transform);
            var bundle = packagingProp.transform.Find("FBX_Starch Packaging Bundle").gameObject.AddComponent<StarchPackagingBundle>();
            var inventory = playerObject.AddComponent<StarchPackagingInventory>();
            Check(inventory.WrapperCount == 0 && !items.HasItem(PlayerItemSlot.Hands), "initially no wrapper");
            Check(!p.TryTakeWaterBasket(player), "installed water basket cannot be taken before drying");
            Check(Math.Abs(p.GrindingDuration - 3.6000002d) < 0.001, "original blender duration");
            Check(Math.Abs(p.DrainingDuration - 3.3d) < 0.001, "original drain duration");
            Check(!p.TryStartGrinding(), "start requires lid/load phase");
            for (int batch = 1; batch <= 5; batch++)
            {
                Check(p.TryLoadBlender(), "load batch " + batch);
                Check(!p.TryLoadBlender() && !p.TryTakeBlender(null), "opening blocks duplicate load and pickup");
                clock.AdvanceBy(p.BlenderAnimation.Length(StarchAnimationId.BlenderOpen));
                Check(p.BlenderState == StarchBlenderState.LidClosing, "open complete");
                clock.AdvanceBy(p.BlenderAnimation.Length(StarchAnimationId.BlenderClose));
                Check(p.BlenderState == StarchBlenderState.LoadedReady, "closed waits for E");
                Check(p.TryStartGrinding(), "start batch");
                clock.AdvanceBy(p.GrindingDuration - 0.01d);
                Check(p.BlenderState == StarchBlenderState.Ending && !p.TryTakeBlender(null), "end incomplete blocks carry");
                clock.AdvanceBy(0.01d);
                Check(p.TryTakeBlender(null) && p.TryPourBatch(null), "carry and pour");
                Check(!p.TryPourBatch(null) && p.SieveFillCount == batch, "no double pour");
                double segment = p.MakerAnimation.Length(StarchAnimationId.BasketFill) / 5d;
                clock.AdvanceBy(segment * 0.5d);
                float expected = (float)((batch - 0.5d) * segment);
                Check(Math.Abs(p.MakerAnimation.HeldTime(StarchAnimationId.BasketFill) - expected) < 0.0001, "continuous partial fill");
                clock.AdvanceBy(segment * 0.5d);
                Check(p.SieveState == (batch < 5 ? StarchSieveState.Filling : StarchSieveState.Draining), "segment end");
                Check(p.TryReturnBlender(null), "return empty blender");
            }
            Check(!p.TryLoadBlender() && !p.TryTakeWetTray(null), "full/draining blocks work");
            clock.AdvanceBy(p.DrainingDuration - 0.01d);
            Check(!p.TryTakeWetTray(null), "both drain clips must finish");
            clock.AdvanceBy(0.01d);
            Check(p.TryTakeWetTray(null), "full wet tray ready");
            var trayCarry = maker.GetComponentInChildren<StarchDryingTray>().CarryItem;
            var trayRig = trayCarry.GetComponent<ProcessingCarryRig>();
            var trayRenderer = maker.transform.Find("FBX_Starch Dry Basket").GetComponent<SkinnedMeshRenderer>();
            trayRig.Detach();
            Check(!p.TryPlaceTrayOnMaker(null), "cannot bypass handles/drop");
            Check(p.TryUseMaker(null) && !p.TryUseMaker(null), "one input starts both handles and locks input");
            clock.AdvanceBy(p.HandlesDuration * 0.5d);
            Check(p.MakerAnimation.HeldTime(StarchAnimationId.HandleOne) > 0f &&
                Math.Abs(p.MakerAnimation.HeldTime(StarchAnimationId.HandleOne) - p.MakerAnimation.HeldTime(StarchAnimationId.HandleTwo)) < 0.0001, "both handles advance together");
            clock.AdvanceBy(p.HandlesDuration * 0.5d);
            Check(p.SieveState == StarchSieveState.Dropping && !p.TryPlaceTrayOnMaker(null), "automatic drop, no early placement");
            Check(p.MakerAnimation.HeldTime(StarchAnimationId.HandleOne) == 0f && p.MakerAnimation.HeldTime(StarchAnimationId.HandleTwo) == 0f, "both handles reset");
            clock.AdvanceBy(p.MakerAnimation.Length(StarchAnimationId.BasketDrop));
            Check(p.TryPlaceTrayOnMaker(null), "place on maker");
            trayCarry.transform.SetParent(p.DryingAnchor, false);
            trayCarry.transform.localPosition = Vector3.zero;
            trayCarry.transform.localRotation = Quaternion.identity;
            Check(Math.Abs(p.DryingAnchor.localPosition.y - 1.537123f) < 0.005f, "tray sits on support ledges instead of above corner posts");
            waterBasket.RefreshCollider();
            Check(p.WaterBasketState == StarchWaterBasketState.Lowered &&
                waterBasket.GetComponent<BoxCollider>().bounds.center.y < 1f, "pickup collider follows the lowered water basket");
            Check(!p.TryTakeWaterBasket(player), "water basket stays below until drying completes");
            clock.IsPaused = true;
            clock.Tick(20d);
            Check(p.DryingSecondsRemaining == 150, "pause stops drying");
            clock.IsPaused = false;
            clock.AdvanceBy(149.99d);
            Check(!p.TryStartPackaging(player) && !p.TryUseMaker(null), "no early packaging or dry drop");
            clock.AdvanceBy(0.01d);
            Check(p.TrayState == StarchTrayState.ReadyToPack && p.RemainingPackCount == 5 && inventory.StarchPackCount == 0,
                "drying enables five packs on top without automatic reward");
            Check(!p.TryTakeWaterBasket(player) && !p.TryUseMaker(player), "starch must be exhausted before cleanup");
            var topTrayPosition = trayCarry.transform.position;
            var starchBone = trayRenderer.bones.Single(t => t.name == "B_Starch Dry");
            float fullStarchHeight = starchBone.localPosition.y;
            Check(!p.TryStartPackaging(player), "bare hands cannot package");
            items.TryHoldInHands(held);
            Check(!bundle.CanInteract(player) && !p.TryStartPackaging(player), "other held objects block wrapper and packaging");
            items.TryReleaseFromHands(held);
            bundle.Interact(player);
            Check(inventory.WrapperCount == 1 && inventory.IsHoldingWrapper(player) && items.HasItem(PlayerItemSlot.Hands),
                "wrapper pickup creates a visible hand item");
            Check(!bundle.CanInteract(player), "cannot stack wrappers in occupied hands");
            Check(maker.GetComponent<StarchSieve>().CanInteract(player), "maker collider also accepts packaging aimed at the upper tray");
            Check(!p.TryReturnWaterBasket(player) && !p.TryLoadBlender(), "no return or new production before cleanup");
            items.SelectSlot(PlayerItemSlot.Hoe);
            Check(!p.TryStartPackaging(player) && inventory.WrapperCount == 1, "tools block packaging without consuming wrapper");
            items.SelectSlot(PlayerItemSlot.Hands);
            maker.GetComponent<StarchSieve>().Interact(player);
            Check(p.TrayState == StarchTrayState.Packing, "held wrapper begins packaging through maker collider");
            items.SelectSlot(PlayerItemSlot.Hoe);
            clock.AdvanceBy(0.1d);
            Check(p.TrayState == StarchTrayState.ReadyToPack && inventory.WrapperCount == 1 && inventory.StarchPackCount == 0, "changing tools cancels and restores exactly one held wrapper");
            items.SelectSlot(PlayerItemSlot.Hands);
            for (int pack = 1; pack <= 5; pack++)
            {
                if (pack > 1)
                {
                    Check(!p.TryStartPackaging(player), "no wrapper means no pack");
                    bundle.Interact(player);
                }
                Check(p.TryStartPackaging(player) && inventory.WrapperCount == 0, "reserve exactly one wrapper");
                Check(!p.TryTakeWaterBasket(player), "cannot take basket while packaging");
                Check(!p.TryStartPackaging(player) && !bundle.CanInteract(player), "busy input cannot duplicate or take extra wrappers");
                clock.IsPaused = true;
                clock.Tick(20d);
                Check(inventory.StarchPackCount == pack - 1, "pause stops packaging");
                clock.IsPaused = false;
                clock.AdvanceBy(2.99d);
                Check(inventory.StarchPackCount == pack - 1 && p.TrayState == StarchTrayState.Packing, "no pack before three seconds");
                clock.AdvanceBy(0.01d);
                Check(inventory.StarchPackCount == pack && p.TotalProducedPackCount == pack && p.LastProducedPackCount == 1, "three seconds awards exactly one pack");
                Check(!items.HasItem(PlayerItemSlot.Hands) && !inventory.IsHoldingWrapper(player), "finished wrapper frees hands");
                Check((trayCarry.transform.position - topTrayPosition).sqrMagnitude < 0.00001f && trayRig.IsDetached,
                    "tray remains on top throughout packaging");
                Check(starchBone.localPosition.y < fullStarchHeight && p.RemainingPackCount == 5 - pack,
                    "detached starch mesh and count shrink on completion");
                clock.AdvanceBy(30d);
                Check(inventory.StarchPackCount == pack, "waits for a fresh E; never auto-repeats");
            }
            Check(!p.TryStartPackaging(player) && !p.TryLoadBlender() && p.TrayState == StarchTrayState.EmptyOnTop && p.CanTakeWaterBasket,
                "last pack leaves empty tray on top and unlocks cleanup without resetting");
            items.TryHoldInHands(held);
            Check(!p.TryTakeWaterBasket(player), "occupied hands block water basket pickup");
            items.TryReleaseFromHands(held);
            Check(waterBasket.CanInteract(player) && p.TryTakeWaterBasket(player) &&
                p.WaterBasketState == StarchWaterBasketState.Carried && waterBasket.CarryItem.IsCarriedBy(player), "pick up actual lower water basket into player's hand");
            Check(waterBasket.GetComponent<ProcessingCarryRig>().IsDetached && !waterBasket.GetComponent<BoxCollider>().enabled,
                "water basket mesh follows independent hand rig and disables collider");
            Check(!p.TryTakeWaterBasket(player) && !p.TryReturnWaterBasket(player), "no duplicate pickup or early return");
            var otherPlayer = Child(root.transform, "OtherPlayer", false).gameObject.AddComponent<PlayerInteractionController>();
            otherPlayer.gameObject.AddComponent<PlayerItemController>();
            Check(!p.TryUseMaker(otherPlayer), "another player cannot operate the basket carrier's handles");
            items.SelectSlot(PlayerItemSlot.Hoe);
            Check(!p.TryUseMaker(player), "water basket must be selected in hands to use handles");
            items.SelectSlot(PlayerItemSlot.Hands);
            var carriedWaterVertices = WorldVertices(waterRenderer);
            var upperVertices = WorldVertices(trayRenderer);
            Check(p.TryUseMaker(player) && !p.TryUseMaker(player), "one dry input starts both handles");
            clock.AdvanceBy(p.HandlesDuration * 0.5d);
            Check(p.MakerAnimation.HeldTime(StarchAnimationId.HandleOne) > 0f &&
                Math.Abs(p.MakerAnimation.HeldTime(StarchAnimationId.HandleOne) - p.MakerAnimation.HeldTime(StarchAnimationId.HandleTwo)) < 0.0001, "dry handles run simultaneously too");
            clock.AdvanceBy(p.HandlesDuration * 0.5d);
            Check(p.SieveState == StarchSieveState.DryDropping && !trayRig.IsDetached, "dry drop rejoins source rig");
            Check(upperVertices.Zip(WorldVertices(trayRenderer), (a, b) => (a - b).sqrMagnitude).All(d => d < 0.00001f), "rejoining at the top has no visible position jump");
            Check(!p.TryStartPackaging(player), "no packaging during fall");
            Check(!p.TryReturnWaterBasket(player), "return blocked until dry drop ends");
            clock.AdvanceBy(p.MakerAnimation.Length(StarchAnimationId.DryBasketDrop) * 0.5d);
            Check(WorldVertices(trayRenderer).Average(v => v.y) < upperVertices.Average(v => v.y) - 0.1f, "visible mesh really falls");
            clock.AdvanceBy(p.MakerAnimation.Length(StarchAnimationId.DryBasketDrop) * 0.5d);
            Check(p.TrayState == StarchTrayState.EmptyOnTop && p.RemainingPackCount == 0, "landed empty tray has no renewed starch");
            Check(p.SieveState == StarchSieveState.WaitingForBasketReturn && !p.TryStartPackaging(player), "must explicitly return water basket after empty tray falls");
            Check(carriedWaterVertices.Zip(WorldVertices(waterRenderer), (a, b) => (a - b).sqrMagnitude).All(d => d < 0.00001f),
                "dry tray drop leaves carried water basket in the player's hand");
            var landedTrayVertices = WorldVertices(trayRenderer);
            Check(!p.TryReturnWaterBasket(otherPlayer), "another player cannot return the carried basket");
            Check(p.TryUseMaker(player), "fresh E returns water basket after drop");
            Check(p.WaterBasketState == StarchWaterBasketState.Docked && p.SieveState == StarchSieveState.Filling &&
                !items.HasItem(PlayerItemSlot.Hands) && !waterBasket.GetComponent<ProcessingCarryRig>().IsDetached,
                "water basket returns to upper rig and frees hands");
            Check(p.RemainingPackCount == 0 && inventory.StarchPackCount == 5 &&
                landedTrayVertices.Zip(WorldVertices(trayRenderer), (a, b) => (a - b).sqrMagnitude).All(d => d < 0.00001f),
                "reset preserves lower empty tray pose and earned packs");
            Check(initialWaterVertices.Zip(WorldVertices(waterRenderer), (a, b) => (a - b).sqrMagnitude).All(d => d < 0.00001f),
                "water basket and contents return to original empty upper pose");
            Check(!p.TryReturnWaterBasket(player) && p.SieveFillCount == 0 && p.TrayState == StarchTrayState.InstalledEmpty,
                "fresh E resets exactly once after empty drop");
            Check(p.TryLoadBlender(), "second cycle can start");
            clock.AdvanceBy(100d);
            Check(p.BlenderState == StarchBlenderState.LoadedReady, "time skip stops at input gate");
            Check(p.TryStartGrinding(), "second cycle start");
            clock.AdvanceBy(100d);
            Check(p.BlenderState == StarchBlenderState.GroundReady, "time skip consumes automatic clips");
            Check(!root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c && c.GetType().Name == "OutdoorDryingSurface"), "no outdoor drying");
            ValidateRig(blender.transform.Find("BlenderContainer_Carry").GetComponent<ProcessingCarryRig>(),
                blender.transform.Find("FBX_Blender Head").GetComponent<SkinnedMeshRenderer>(), Check);
            p.MakerAnimation.SetTime(StarchAnimationId.TrayFill, p.MakerAnimation.Length(StarchAnimationId.TrayFill));
            ValidateRig(maker.transform.Find("DryingTray_Carry").GetComponent<ProcessingCarryRig>(),
                maker.transform.Find("FBX_Starch Dry Basket").GetComponent<SkinnedMeshRenderer>(), Check);
            ValidateRig(waterBasket.GetComponent<ProcessingCarryRig>(), waterRenderer, Check);
            Debug.Log($"[PotatoStarchProcessingChecks] PASS: {checks} checks. Full cycle, water basket pickup/ownership/return, top-tray packaging with held wrappers, empty-tray cleanup, simultaneous handles and carry rigs.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static Vector3[] WorldVertices(SkinnedMeshRenderer renderer)
    {
        var mesh = new Mesh();
        try
        {
            renderer.BakeMesh(mesh);
            return mesh.vertices.Select(renderer.transform.TransformPoint).ToArray();
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
    private static void ValidateRig(ProcessingCarryRig rig, SkinnedMeshRenderer renderer, Action<bool, string> check)
    {
        var before = new Mesh();
        var after = new Mesh();
        try
        {
            renderer.BakeMesh(before);
            var originalBones = renderer.bones;
            var originalParent = renderer.transform.parent;
            rig.Detach();
            renderer.BakeMesh(after);
            check(renderer.transform.parent == rig.transform && !renderer.bones.Intersect(originalBones).Any(), "renderer follows independent carry bones");
            var a = before.vertices;
            var b = after.vertices;
            check(a.Length == b.Length && a.Zip(b, (x, y) => (x - y).sqrMagnitude).All(d => d < 0.00001f), "detachment preserves skinned pose");
            rig.transform.position += new Vector3(2f, 1f, 0f);
            renderer.BakeMesh(after);
            check(a.Zip(after.vertices, (x, y) => (x - y).sqrMagnitude).All(d => d < 0.00001f), "carried mesh pose remains unchanged");
            rig.Restore();
            check(renderer.transform.parent == originalParent && renderer.bones.SequenceEqual(originalBones), "restore original bones and parent");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(before);
            UnityEngine.Object.DestroyImmediate(after);
        }
    }
}
