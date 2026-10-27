using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoPackingBoxChecks
{
    [MenuItem("Tools/Project Potato/Validate Packing Box")]
    public static void RunValidation()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run validation outside Play mode.");
        var originalScene = SceneManager.GetActiveScene();
        var source = originalScene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PotatoPackingBox>(true)).Single();
        var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Packing box check failed: " + message);
            checks++;
        }
        try
        {
            var box = UnityEngine.Object.Instantiate(source);
            box.name = "PackingBoxCheckFixture";
            SceneManager.MoveGameObjectToScene(box.gameObject, testScene);
            box.transform.SetPositionAndRotation(new Vector3(1000f, 1000f, 1000f), Quaternion.Euler(0f, 37f, 0f));
            var player = new GameObject("PackingCheckPlayer").AddComponent<PlayerInteractionController>();
            SceneManager.MoveGameObjectToScene(player.gameObject, testScene);
            typeof(PlayerInteractionController).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, box);
            var driver = box.GetComponent<PackingBoxAnimationDriver>();
            var potatoes = box.transform.Find("Potato Millionaire Potato Pack Location");
            var starch = box.transform.Find("Potato Millionaire Starch Pack Location");
            var body = box.transform.Find("B_Box Pelvis/B_Box/B_Box Body");
            var walls = body.GetComponentsInChildren<Transform>(true).Where(t => t != body).ToArray();
            driver.SetPose(driver.FoldLength, 0f);
            var foldedRotations = walls.Select(t => t.localRotation).ToArray();
            driver.SetPose(0f, 0f);
            var flatColliderSize = box.GetComponent<BoxCollider>().size;

            bool HoldsFoldedWalls() => walls.Select((t, i) => Quaternion.Angle(t.localRotation, foldedRotations[i]) < 0.02f).All(v => v);
            Transform[] Slots(Transform group, string prefix) => group.Cast<Transform>().Where(t => t.name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(t => int.Parse(t.name.Substring(prefix.Length))).ToArray();
            var potatoSlots = Slots(potatoes, "FBX_Potato Pack Location ");
            var starchSlots = Slots(starch, "FBX_Starch Pack Location ");
            GameObject Ghost() => (GameObject)typeof(PotatoPackingBox).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(box);

            Check(potatoes.parent == box.transform && starch.parent == box.transform, "both content groups are direct box children");
            Check(potatoSlots.Length == 8 && starchSlots.Length == 20, "authored capacities are 8 and 20");
            Check(driver.FoldLength > 0f && driver.PackingLength > 0f, "both imported animations are connected");

            void RunMode(BoxPackingContent mode, Transform group, Transform otherGroup, Transform[] slots)
            {
                box.ResetPackaging();
                player.SelectPackingContent(BoxPackingContent.None);
                Check(!potatoes.gameObject.activeSelf && !starch.gameObject.activeSelf, "both groups start hidden");
                Check(potatoSlots.Concat(starchSlots).All(t => !t.gameObject.activeSelf), "all real locations start hidden");
                Check(!box.PreviewLocation && !box.IsFolded, "no preview before folding");
                Check(box.CanInteract(player), "empty box can be folded before selecting contents");
                box.Interact(player);
                box.Interact(player);
                Check(box.IsBusy && box.PackedCount == 0 && box.PackingPulseCount == 0, "first E only folds and duplicate E is ignored");
                box.AdvanceAnimation(driver.FoldLength);
                Check(box.IsFolded && box.IsBusy && box.PackingPulseCount == 1, "fold completion automatically starts one packing pulse");
                Check(HoldsFoldedWalls(), "automatic packing pulse holds folded walls");
                box.AdvanceAnimation(driver.PackingLength + 0.001f);
                Check(!box.IsBusy && !box.CanInteract(player), "selection is required before adding contents");
                Check(box.GetComponent<BoxCollider>().size.x < flatColliderSize.x && box.GetComponent<BoxCollider>().size.y > flatColliderSize.y,
                    "interaction collider follows the assembled box footprint");

                box.SetHighlighted(true);
                Check(!box.PreviewLocation, "no ghost without a selection");
                var other = mode == BoxPackingContent.Potato ? BoxPackingContent.Starch : BoxPackingContent.Potato;
                player.SelectPackingContent(other);
                Check(box.PreviewLocation && box.LockedContent == BoxPackingContent.None, "selection can change before the first item");
                player.SelectPackingContent(mode);
                Check(player.ConsumesPackingSelectionInput && box.PreviewLocation == slots[0], "number selection chooses the correct first ghost");
                Check(!group.gameObject.activeSelf && !otherGroup.gameObject.activeSelf, "preview does not activate real content groups");
                var ghost = Ghost();
                var ghostRenderer = ghost.GetComponentInChildren<MeshRenderer>();
                var actualRenderer = slots[0].GetComponent<MeshRenderer>();
                Check(ghostRenderer.GetComponent<MeshFilter>().sharedMesh == slots[0].GetComponent<MeshFilter>().sharedMesh,
                    "ghost uses the actual item silhouette");
                Check(Vector3.Distance(ghostRenderer.transform.position, actualRenderer.transform.position) < 0.001f &&
                      Quaternion.Angle(ghostRenderer.transform.rotation, actualRenderer.transform.rotation) < 0.02f,
                    "ghost retains exact slot position and orientation on a rotated box");
                Color color = ghostRenderer.sharedMaterial.GetColor("_BaseColor");
                Check(color.g > color.r && color.g > color.b && color.a > 0f && color.a < 0.4f,
                    "preview is faint translucent green");
                Check(ghost.GetComponentsInChildren<Collider>(true).Length == 0 &&
                      ghost.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 2),
                    "preview cannot intercept E raycasts or physics");
                box.SetHighlighted(false);
                Check(!box.PreviewLocation && !ghost.activeSelf, "looking away hides the ghost");
                box.SetHighlighted(true);
                Check(box.PreviewLocation == slots[0], "looking back restores the next ghost");

                for (int i = 0; i < slots.Length; i++)
                {
                    Check(box.CanInteract(player), "correct selected content can be added");
                    box.Interact(player);
                    Check(box.PackedCount == i + 1 && box.LockedContent == mode, "one E adds exactly one item and fixes the content type");
                    Check(group.gameObject.activeSelf && !otherGroup.gameObject.activeSelf, "only the chosen group becomes visible");
                    Check(slots.Where((t, index) => t.gameObject.activeSelf != (index <= i)).Count() == 0,
                        "locations activate in numeric order, including Location 10 through 20");
                    Check(box.PackingPulseCount == i + 2, "each added item starts one packing pulse");
                    Check(box.PreviewLocation == (i + 1 < slots.Length ? slots[i + 1] : null), "ghost advances once and disappears when full");
                    box.Interact(player);
                    Check(box.PackedCount == i + 1, "E during the pulse cannot add a second item");
                    box.AdvanceAnimation(driver.PackingLength * 0.2f);
                    Check(body.localScale.y > 1.05f && HoldsFoldedWalls(), "authored pulse squashes the assembled box without unfolding");
                    box.AdvanceAnimation(driver.PackingLength);
                    Check(HoldsFoldedWalls() && Vector3.Distance(body.localScale, Vector3.one) < 0.001f,
                        "pulse finishes with folded walls and original scale");
                    if (i == 0)
                    {
                        player.SelectPackingContent(other);
                        Check(!box.CanInteract(player), "opposite content is rejected after the first item");
                        box.Interact(player);
                        Check(box.PackedCount == 1 && box.LockedContent == mode && !otherGroup.gameObject.activeSelf,
                            "switching selection cannot mix or replace contents");
                        player.SelectPackingContent(mode);
                    }
                }
                Check(box.IsComplete && !box.IsBusy && box.PackedCount == slots.Length, "full box completes after its final packing pulse");
                Check(!box.PreviewLocation && !box.CanInteract(player), "completed box hides preview and blocks extra contents");
                box.Interact(player);
                box.AdvanceAnimation(100f);
                Check(box.PackedCount == slots.Length && box.PackingPulseCount == slots.Length + 1 && HoldsFoldedWalls(),
                    "completed box stays folded and never overfills or repeats packing");
            }

            RunMode(BoxPackingContent.Potato, potatoes, starch, potatoSlots);
            RunMode(BoxPackingContent.Starch, starch, potatoes, starchSlots);
            box.ResetPackaging();
            player.SelectPackingContent(BoxPackingContent.Starch);
            box.Interact(player);
            box.AdvanceAnimation(100f);
            Check(box.IsFolded && !box.IsBusy && box.PackingPulseCount == 1 && box.PackedCount == 0,
                "a long frame completes folding and its pulse without inserting an item");
            box.SetHighlighted(true);
            box.SetPreviewSelection(BoxPackingContent.Starch);
            Check(box.PreviewLocation == starchSlots[0], "starch preview is available after long-frame folding");
            box.enabled = false;
            // Non-ExecuteAlways behaviours do not receive lifecycle callbacks in Edit mode.
            typeof(PotatoPackingBox).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(box, null);
            Check(!box.PreviewLocation && !Ghost() && !box.CanInteract(player), "disabling the box removes its transient preview");
            Debug.Log($"[PotatoPackingBoxChecks] PASS: {checks} checks (8 potatoes, 20 starch packs, animation retention, preview and content locking).");
        }
        finally
        {
            SceneManager.SetActiveScene(originalScene);
            EditorSceneManager.CloseScene(testScene, true);
        }
    }
}
