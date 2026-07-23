#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.UI;

/// <summary>
/// Mobile UI optimization tools: canvas split, raycast cleanup, card sprite atlas.
/// </summary>
public static class MobileUiOptimizationMenus
{
    private const string AtlasAssetPath = "Assets/Atlases/CardPortraits.spriteatlas";

    // -------------------------------------------------------------------------
    // 1) Split Canvas
    // -------------------------------------------------------------------------

    [MenuItem("Reigns/Mobile UI/Split Selected Canvas Into Static + Dynamic")]
    public static void SplitSelectedCanvas()
    {
        Canvas source = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<Canvas>()
            : null;

        if (source == null)
        {
            EditorUtility.DisplayDialog(
                "Split Canvas",
                "Select your root Canvas GameObject in the Hierarchy, then run this again.",
                "OK");
            return;
        }

        if (EditorUtility.DisplayDialog(
                "Split Canvas",
                "This duplicates the selected Canvas into StaticCanvas + DynamicCanvas, " +
                "moves interactive/moving UI under Dynamic, and leaves backgrounds/static text on Static.\n\n" +
                "Review the result — move any misclassified children by hand.",
                "Split",
                "Cancel") == false)
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        GameObject sourceGo = source.gameObject;
        string baseName = sourceGo.name;

        // Dynamic keeps the original (preserves scene references on card/sliders).
        Undo.RecordObject(sourceGo, "Rename Dynamic Canvas");
        sourceGo.name = "DynamicCanvas";

        GameObject staticGo = Object.Instantiate(sourceGo);
        Undo.RegisterCreatedObjectUndo(staticGo, "Create Static Canvas");
        staticGo.name = "StaticCanvas";
        staticGo.transform.SetParent(sourceGo.transform.parent, false);
        staticGo.transform.SetSiblingIndex(sourceGo.transform.GetSiblingIndex());

        // Strip dynamic-looking children from Static; strip static-looking from Dynamic.
        ClassifyAndPrune(staticGo.transform, keepDynamic: false);
        ClassifyAndPrune(sourceGo.transform, keepDynamic: true);

        var helper = sourceGo.GetComponent<CanvasSplitHelper>();
        if (helper == null)
            helper = Undo.AddComponent<CanvasSplitHelper>(sourceGo);

        SerializedObject so = new SerializedObject(helper);
        so.FindProperty("staticCanvas").objectReferenceValue = staticGo.GetComponent<Canvas>();
        so.FindProperty("dynamicCanvas").objectReferenceValue = source;
        so.ApplyModifiedPropertiesWithoutUndo();
        helper.ApplyRecommendedSettings();

        // Static canvas should not keep a raycaster.
        var staticRaycaster = staticGo.GetComponent<GraphicRaycaster>();
        if (staticRaycaster != null)
            Undo.DestroyObjectImmediate(staticRaycaster);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = sourceGo;
        Debug.Log(
            "Reigns: Canvas split complete.\n" +
            "• StaticCanvas — backgrounds / static text (no GraphicRaycaster)\n" +
            "• DynamicCanvas — card, sliders, buttons (rebuilds independently)\n" +
            "Move any remaining children that were misclassified.");
    }

    private static void ClassifyAndPrune(Transform root, bool keepDynamic)
    {
        // Work on a copy of children because we destroy/reparent while iterating.
        var children = new List<Transform>();
        for (int i = 0; i < root.childCount; i++)
            children.Add(root.GetChild(i));

        foreach (Transform child in children)
        {
            if (child == null)
                continue;

            bool isDynamic = LooksDynamic(child);
            bool shouldKeep = keepDynamic ? isDynamic : !isDynamic;

            if (!shouldKeep)
                Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static bool LooksDynamic(Transform t)
    {
        string n = t.name.ToLowerInvariant();

        // Name heuristics for this Reigns layout.
        if (n.Contains("card") || n.Contains("swipe") || n.Contains("slider") ||
            n.Contains("stat") || n.Contains("hud") || n.Contains("choice") ||
            n.Contains("inventory") || n.Contains("particle") || n.Contains("button") ||
            n.Contains("play") || n.Contains("chance") || n.Contains("leaderboard"))
            return true;

        if (t.GetComponent<CardSwipeHandler>() != null ||
            t.GetComponentInChildren<CardSwipeHandler>(true) != null)
            return true;

        if (t.GetComponentInChildren<Slider>(true) != null)
            return true;

        if (t.GetComponentInChildren<Button>(true) != null)
            return true;

        if (t.GetComponentInChildren<ScrollRect>(true) != null)
            return true;

        // Safe-area root usually holds everything — keep on dynamic if it contains interactives.
        if (t.GetComponent<SafeAreaFitter>() != null &&
            t.GetComponentInChildren<Button>(true) != null)
            return true;

        return false;
    }

    // -------------------------------------------------------------------------
    // 2) Disable unused raycast targets
    // -------------------------------------------------------------------------

    [MenuItem("Reigns/Mobile UI/Disable Unused Raycast Targets In Scene")]
    public static void DisableUnusedRaycastTargets()
    {
        int changed = 0;
        int scanned = 0;

        // UnityEngine.UI.Image / RawImage / Text
        foreach (Graphic graphic in Object.FindObjectsOfType<Graphic>(true))
        {
            scanned++;
            if (!graphic.raycastTarget)
                continue;

            if (ShouldKeepRaycast(graphic.gameObject))
                continue;

            Undo.RecordObject(graphic, "Disable Raycast Target");
            graphic.raycastTarget = false;
            EditorUtility.SetDirty(graphic);
            changed++;
        }

        // TextMeshProUGUI
        foreach (TextMeshProUGUI tmp in Object.FindObjectsOfType<TextMeshProUGUI>(true))
        {
            scanned++;
            if (!tmp.raycastTarget)
                continue;

            if (ShouldKeepRaycast(tmp.gameObject))
                continue;

            Undo.RecordObject(tmp, "Disable TMP Raycast Target");
            tmp.raycastTarget = false;
            EditorUtility.SetDirty(tmp);
            changed++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog(
            "Raycast Targets",
            $"Scanned {scanned} graphics.\nDisabled raycastTarget on {changed} non-interactive Text/Image components.\n\n" +
            "Kept enabled on Buttons, Toggles, Sliders, ScrollRects, InputFields, and their child graphics.",
            "OK");
    }

    private static bool ShouldKeepRaycast(GameObject go)
    {
        // Keep if this object or a parent is an interactive Selectable / scroll view.
        if (go.GetComponent<Button>() != null ||
            go.GetComponent<Toggle>() != null ||
            go.GetComponent<Slider>() != null ||
            go.GetComponent<Scrollbar>() != null ||
            go.GetComponent<Dropdown>() != null ||
            go.GetComponent<InputField>() != null ||
            go.GetComponent<ScrollRect>() != null ||
            go.GetComponent<TMPro.TMP_InputField>() != null ||
            go.GetComponent<TMPro.TMP_Dropdown>() != null)
            return true;

        // Child graphic of a Button (targetGraphic / child text) must still raycast
        // if the Button uses it — typically the Image on the Button itself.
        Transform t = go.transform.parent;
        while (t != null)
        {
            if (t.GetComponent<Button>() != null ||
                t.GetComponent<Toggle>() != null ||
                t.GetComponent<Slider>() != null ||
                t.GetComponent<ScrollRect>() != null ||
                t.GetComponent<Scrollbar>() != null)
            {
                // Only the selectable's own Graphic (usually on same GO as Button) needs raycast.
                // Child labels under a Button can disable raycast — EventSystem hits the Button Image.
                if (t.gameObject == go)
                    return true;

                // Same GameObject as Button already returned true above.
                // For children of Button: safe to disable (Button Image receives clicks).
                return false;
            }

            t = t.parent;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // 3) Sprite Atlas for card portraits
    // -------------------------------------------------------------------------

    [MenuItem("Reigns/Mobile UI/Create / Refresh Card Sprite Atlas")]
    public static void CreateOrRefreshCardSpriteAtlas()
    {
        string atlasDir = Path.GetDirectoryName(AtlasAssetPath);
        if (!AssetDatabase.IsValidFolder("Assets/Atlases"))
        {
            if (!AssetDatabase.IsValidFolder("Assets"))
            {
                EditorUtility.DisplayDialog(
                    "Sprite Atlas",
                    "This Unity project has no Assets/ folder in the usual place.\n" +
                    "Open the full Unity project, then run this menu again.\n\n" +
                    "Manual steps:\n" +
                    "1. Create → 2D → Sprite Atlas\n" +
                    "2. Name it CardPortraits\n" +
                    "3. Objects for Packing → add your Characters portrait folder\n" +
                    "4. Enable Include in Build",
                    "OK");
                return;
            }

            AssetDatabase.CreateFolder("Assets", "Atlases");
        }

        SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasAssetPath);
        if (atlas == null)
        {
            atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, AtlasAssetPath);
        }

        var packing = new SpriteAtlasPackingSettings
        {
            blockOffset = 1,
            enableRotation = false,
            enableTightPacking = true,
            padding = 2
        };
        atlas.SetPackingSettings(packing);

        var texture = new SpriteAtlasTextureSettings
        {
            readable = false,
            generateMipMaps = false,
            sRGB = true,
            filterMode = FilterMode.Bilinear
        };
        atlas.SetTextureSettings(texture);

        // Prefer packing whole folders so new portraits are included automatically.
        var packables = new List<Object>();
        TryAddFolder(packables, "Assets/Resources/Characters");
        TryAddFolder(packables, "Assets/Characters");
        TryAddFolder(packables, "Resources/Characters");

        // Also add loose sprites under Resources if the folder asset isn't available.
        string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.IndexOf("Character", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                path.IndexOf("portrait", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                path.IndexOf("Card", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Object sprite = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (sprite != null)
                packables.Add(sprite);
        }

        // Clear + re-add for a clean refresh.
        Object[] existing = atlas.GetPackables();
        if (existing != null && existing.Length > 0)
            atlas.Remove(existing);

        if (packables.Count > 0)
            atlas.Add(packables.ToArray());

        EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Force pack so draw-call benefit is immediate in Editor.
        SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);

        EditorUtility.DisplayDialog(
            "Card Sprite Atlas",
            $"Atlas ready at:\n{AtlasAssetPath}\n\n" +
            $"Packables: {packables.Count}\n\n" +
            "All card portraits in this atlas share one texture → fewer draw calls " +
            "(often a single UI atlas batch when materials match).\n\n" +
            "Tip: keep portrait Images on the Dynamic Canvas and use the same UI material.",
            "OK");
    }

    private static void TryAddFolder(List<Object> packables, string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
            return;

        Object folder = AssetDatabase.LoadAssetAtPath<Object>(folderPath);
        if (folder != null)
            packables.Add(folder);
    }

    [MenuItem("Reigns/Mobile UI/Print Canvas Split Instructions")]
    public static void PrintCanvasSplitInstructions()
    {
        Debug.Log(
            "=== Reigns Canvas Split Guide ===\n" +
            "1. Select your main Canvas → Reigns/Mobile UI/Split Selected Canvas Into Static + Dynamic\n" +
            "2. StaticCanvas: backgrounds, frames, static titles (NO GraphicRaycaster)\n" +
            "3. DynamicCanvas: CardSwipeHandler card, sliders, buttons, inventory icons\n" +
            "4. Both CanvasScalers must match (1080x1920, Match 0.5)\n" +
            "5. Dynamic sortingOrder = Static + 1\n" +
            "6. Run Disable Unused Raycast Targets\n" +
            "7. Create/Refresh Card Sprite Atlas for portrait sprites\n" +
            "Result: static UI dirtying no longer rebuilds the moving card canvas.");
    }
}
#endif
