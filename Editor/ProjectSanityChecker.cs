#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pre-Android-APK sanity check: card JSON, UIManager refs, card/audio pool assets, Player Settings.
/// Menu: Tools → Run Pre-Build Check
/// </summary>
public sealed class ProjectSanityChecker : EditorWindow
{
    private const string PrimaryCardJsonRelative = "Resources/Cards/event_cards.json";
    private const string CardsFolderRelative = "Resources/Cards";
    private const int MinimumAndroidTargetApi = 34;

    private static readonly string[] DefaultCompanyPackageFragments =
    {
        "com.Company.",
        "com.DefaultCompany.",
        "com.unity3d.",
        "com.UnityCompany."
    };

    private static readonly string[] RequiredUiManagerSliders =
    {
        "religionSlider",
        "peopleSlider",
        "armySlider",
        "wealthSlider"
    };

    private static readonly string[] RequiredUiManagerTmp =
    {
        "scenarioText",
        "leftChoiceText",
        "rightChoiceText",
        "yearsRuledText",
        "deathMessageText",
        "gameOverYearsText"
    };

    private static readonly string[] OptionalUiManagerTmp =
    {
        "religionSignText",
        "peopleSignText",
        "armySignText",
        "wealthSignText",
        "longestReignText",
        "eraText"
    };

    private static readonly string[] AudioManagerPoolProperties =
    {
        "cardDrawPool",
        "swipeLeftPool",
        "swipeRightPool",
        "buttonClickPool",
        "gameOverPool"
    };

    private static readonly string[] AudioManagerLegacyClips =
    {
        "cardDrawSfx",
        "swipeLeftSfx",
        "swipeRightSfx",
        "buttonClickSfx",
        "gameOverSfx"
    };

    private readonly List<CheckItem> results = new List<CheckItem>(128);
    private Vector2 scroll;
    private int passCount;
    private int failCount;
    private int warnCount;
    private bool hasRun;
    private bool showPasses = true;

    private enum Severity
    {
        Pass,
        Warn,
        Fail
    }

    private struct CheckItem
    {
        public Severity Severity;
        public string Category;
        public string Message;
    }

    [MenuItem("Tools/Run Pre-Build Check")]
    public static void RunFromMenu()
    {
        var window = GetWindow<ProjectSanityChecker>("Pre-Build Check");
        window.minSize = new Vector2(560f, 420f);
        window.Show();
        window.RunAllChecks();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Re-run Checks", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                RunAllChecks();

            showPasses = GUILayout.Toggle(showPasses, "Show Passes", EditorStyles.toolbarButton, GUILayout.Width(100f));

            GUILayout.FlexibleSpace();

            if (hasRun)
            {
                GUILayout.Label(
                    $"Pass {passCount}  ·  Warn {warnCount}  ·  Fail {failCount}",
                    EditorStyles.toolbarButton);
            }
        }

        EditorGUILayout.Space(6f);

        if (!hasRun)
        {
            EditorGUILayout.HelpBox(
                "Click Re-run Checks (or use Tools → Run Pre-Build Check) before shipping an Android APK.",
                MessageType.Info);
            return;
        }

        DrawSummaryBanner();
        EditorGUILayout.Space(4f);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        string lastCategory = null;
        for (int i = 0; i < results.Count; i++)
        {
            CheckItem item = results[i];
            if (!showPasses && item.Severity == Severity.Pass)
                continue;

            if (item.Category != lastCategory)
            {
                lastCategory = item.Category;
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(item.Category, EditorStyles.boldLabel);
            }

            DrawResultRow(item);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSummaryBanner()
    {
        MessageType type = failCount > 0 ? MessageType.Error
            : warnCount > 0 ? MessageType.Warning
            : MessageType.Info;

        string headline = failCount > 0
            ? "FAILED — fix the issues below before building the Android APK."
            : warnCount > 0
                ? "PASSED WITH WARNINGS — review warnings before release."
                : "PASSED — project looks ready for an Android APK build.";

        EditorGUILayout.HelpBox(headline, type);
    }

    private static void DrawResultRow(CheckItem item)
    {
        Color prev = GUI.color;
        switch (item.Severity)
        {
            case Severity.Pass:
                GUI.color = new Color(0.55f, 0.9f, 0.55f);
                break;
            case Severity.Warn:
                GUI.color = new Color(1f, 0.85f, 0.35f);
                break;
            case Severity.Fail:
                GUI.color = new Color(1f, 0.45f, 0.45f);
                break;
        }

        string mark = item.Severity switch
        {
            Severity.Pass => "PASS",
            Severity.Warn => "WARN",
            _ => "FAIL"
        };

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label(mark, EditorStyles.miniBoldLabel, GUILayout.Width(40f));
        GUI.color = prev;
        EditorGUILayout.LabelField(item.Message, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void RunAllChecks()
    {
        results.Clear();
        passCount = failCount = warnCount = 0;
        hasRun = true;

        try
        {
            EditorUtility.DisplayProgressBar("Pre-Build Check", "Validating card JSON…", 0.1f);
            CheckCardJsonFiles();

            EditorUtility.DisplayProgressBar("Pre-Build Check", "Validating UIManager…", 0.35f);
            CheckUiManagerReferences();

            EditorUtility.DisplayProgressBar("Pre-Build Check", "Validating audio & sprites…", 0.6f);
            CheckCardPoolAssets();
            CheckAudioManagerPools();

            EditorUtility.DisplayProgressBar("Pre-Build Check", "Validating Android Player Settings…", 0.85f);
            CheckAndroidPlayerSettings();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        for (int i = 0; i < results.Count; i++)
        {
            switch (results[i].Severity)
            {
                case Severity.Pass: passCount++; break;
                case Severity.Warn: warnCount++; break;
                case Severity.Fail: failCount++; break;
            }
        }

        string summary =
            $"Pre-Build Check complete — Pass:{passCount} Warn:{warnCount} Fail:{failCount}";
        if (failCount > 0)
            Debug.LogError(summary + "\n" + BuildConsoleDump(Severity.Fail));
        else if (warnCount > 0)
            Debug.LogWarning(summary + "\n" + BuildConsoleDump(Severity.Warn));
        else
            Debug.Log(summary);

        Repaint();
    }

    private string BuildConsoleDump(Severity minimum)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            CheckItem item = results[i];
            if (item.Severity < minimum)
                continue;
            sb.Append('[').Append(item.Severity).Append("] ")
                .Append(item.Category).Append(": ")
                .AppendLine(item.Message);
        }

        return sb.ToString();
    }

    private void Add(Severity severity, string category, string message)
    {
        results.Add(new CheckItem
        {
            Severity = severity,
            Category = category,
            Message = message
        });
    }

    // ─── Card JSON ───────────────────────────────────────────────────────────

    private void CheckCardJsonFiles()
    {
        const string cat = "Card JSON";
        string cardsFolder = ResolveProjectPath(CardsFolderRelative);
        string primaryPath = ResolveProjectPath(PrimaryCardJsonRelative);

        if (!Directory.Exists(cardsFolder))
        {
            Add(Severity.Fail, cat, $"Cards folder missing: {CardsFolderRelative}");
            return;
        }

        if (!File.Exists(primaryPath))
        {
            Add(Severity.Fail, cat, $"Primary card database missing: {PrimaryCardJsonRelative}");
            return;
        }

        Add(Severity.Pass, cat, $"Primary database found ({PrimaryCardJsonRelative}).");

        string[] jsonFiles = Directory.GetFiles(cardsFolder, "*.json", SearchOption.TopDirectoryOnly);
        if (jsonFiles.Length == 0)
        {
            Add(Severity.Fail, cat, "No .json files under Resources/Cards.");
            return;
        }

        int validatedFiles = 0;

        for (int f = 0; f < jsonFiles.Length; f++)
        {
            string path = jsonFiles[f];
            string fileName = Path.GetFileName(path);
            bool isSample = fileName.IndexOf(".sample.", StringComparison.OrdinalIgnoreCase) >= 0
                || fileName.EndsWith(".sample.json", StringComparison.OrdinalIgnoreCase);

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Add(Severity.Fail, cat, $"Could not read {fileName}: {e.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                Add(isSample ? Severity.Warn : Severity.Fail, cat, $"{fileName} is empty.");
                continue;
            }

            CardDatabase database;
            try
            {
                database = CardLoader.ParseDatabase(json);
            }
            catch (Exception e)
            {
                Add(Severity.Fail, cat, $"{fileName} failed to parse: {e.Message}");
                continue;
            }

            if (database == null)
            {
                Add(Severity.Fail, cat, $"{fileName} parsed to null database.");
                continue;
            }

            int cardCount = CountCards(database);
            if (cardCount == 0)
            {
                Add(isSample ? Severity.Warn : Severity.Fail, cat,
                    $"{fileName} contains no cards in baseDeck or unlockable pools.");
                continue;
            }

            validatedFiles++;
            Add(Severity.Pass, cat, $"{fileName}: valid JSON with {cardCount} card(s).");

            CheckDuplicateIdsInDatabase(database, fileName, cat, isSample ? Severity.Warn : Severity.Fail);
        }

        if (validatedFiles == 0)
            Add(Severity.Fail, cat, "No card JSON files validated successfully.");
        else
            Add(Severity.Pass, cat, $"Validated {validatedFiles} card JSON file(s).");
    }

    private void CheckDuplicateIdsInDatabase(
        CardDatabase database,
        string fileName,
        string category,
        Severity failSeverity)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var cards = new List<(string Source, Card Card)>(64);

        if (database.baseDeck != null)
        {
            for (int i = 0; i < database.baseDeck.Length; i++)
                cards.Add(("baseDeck", database.baseDeck[i]));
        }

        if (database.unlockablePools != null)
        {
            for (int p = 0; p < database.unlockablePools.Length; p++)
            {
                UnlockableCardPool pool = database.unlockablePools[p];
                if (pool?.cards == null)
                    continue;

                string poolLabel = string.IsNullOrWhiteSpace(pool.id) ? $"pool[{p}]" : pool.id;
                for (int i = 0; i < pool.cards.Length; i++)
                    cards.Add((poolLabel, pool.cards[i]));
            }
        }

        int emptyIds = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i].Card;
            if (card == null)
            {
                Add(failSeverity, category, $"{fileName}: null card entry in {cards[i].Source}.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.id))
            {
                emptyIds++;
                continue;
            }

            if (seen.TryGetValue(card.id, out string firstSource))
            {
                Add(failSeverity, category,
                    $"{fileName}: duplicate card id '{card.id}' ({firstSource} and {cards[i].Source}).");
            }
            else
            {
                seen[card.id] = cards[i].Source;
            }

            if (string.IsNullOrWhiteSpace(card.scenarioText))
            {
                Add(Severity.Warn, category,
                    $"{fileName}: card '{card.id}' has empty scenarioText.");
            }
        }

        if (emptyIds > 0)
        {
            Add(failSeverity, category, $"{fileName}: {emptyIds} card(s) missing id.");
        }
        else if (seen.Count > 0)
        {
            Add(Severity.Pass, category, $"{fileName}: {seen.Count} unique card id(s).");
        }
    }

    private static int CountCards(CardDatabase database)
    {
        int count = 0;
        if (database.baseDeck != null)
            count += database.baseDeck.Length;

        if (database.unlockablePools != null)
        {
            for (int i = 0; i < database.unlockablePools.Length; i++)
            {
                UnlockableCardPool pool = database.unlockablePools[i];
                if (pool?.cards != null)
                    count += pool.cards.Length;
            }
        }

        return count;
    }

    // ─── UIManager ───────────────────────────────────────────────────────────

    private void CheckUiManagerReferences()
    {
        const string cat = "UIManager";
        var targets = FindComponentsInProject<UIManager>();

        if (targets.Count == 0)
        {
            Add(Severity.Fail, cat,
                "No UIManager found in open scenes, project scenes, or prefabs. Assign one before shipping.");
            return;
        }

        Add(Severity.Pass, cat, $"Found {targets.Count} UIManager instance(s) to validate.");

        for (int t = 0; t < targets.Count; t++)
        {
            UIManager ui = targets[t].Component;
            string context = targets[t].Context;
            if (ui == null)
                continue;

            var so = new SerializedObject(ui);
            int requiredOk = 0;
            int requiredTotal = RequiredUiManagerSliders.Length + RequiredUiManagerTmp.Length;

            for (int i = 0; i < RequiredUiManagerSliders.Length; i++)
            {
                if (CheckObjectReference(so, RequiredUiManagerSliders[i], cat, context, required: true))
                    requiredOk++;
            }

            for (int i = 0; i < RequiredUiManagerTmp.Length; i++)
            {
                if (CheckObjectReference(so, RequiredUiManagerTmp[i], cat, context, required: true))
                    requiredOk++;
            }

            for (int i = 0; i < OptionalUiManagerTmp.Length; i++)
                CheckObjectReference(so, OptionalUiManagerTmp[i], cat, context, required: false);

            if (requiredOk == requiredTotal)
            {
                Add(Severity.Pass, cat,
                    $"{context}: all required Slider + TextMeshProUGUI references assigned ({requiredOk}/{requiredTotal}).");
            }
        }
    }

    /// <returns>True when the reference is assigned.</returns>
    private bool CheckObjectReference(
        SerializedObject so,
        string propertyName,
        string category,
        string context,
        bool required)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            Add(Severity.Warn, category, $"{context}: property '{propertyName}' not found (script renamed?).");
            return false;
        }

        if (prop.propertyType != SerializedPropertyType.ObjectReference)
        {
            Add(Severity.Warn, category, $"{context}: '{propertyName}' is not an object reference.");
            return false;
        }

        if (prop.objectReferenceValue != null)
            return true;

        if (required)
            Add(Severity.Fail, category, $"{context}: {propertyName} is unassigned (null).");
        else
            Add(Severity.Warn, category, $"{context}: optional {propertyName} is unassigned.");

        return false;
    }

    // ─── Card pool sprites / audio ───────────────────────────────────────────

    private void CheckCardPoolAssets()
    {
        const string cat = "Card Pool Assets";
        string primaryPath = ResolveProjectPath(PrimaryCardJsonRelative);
        if (!File.Exists(primaryPath))
        {
            Add(Severity.Fail, cat, "Skipped — primary card JSON missing.");
            return;
        }

        string json = File.ReadAllText(primaryPath);
        CardDatabase database = CardLoader.ParseDatabase(json);
        if (database == null)
        {
            Add(Severity.Fail, cat, "Could not parse primary card database for asset checks.");
            return;
        }

        int portraitOk = 0;
        int voiceOk = 0;
        int portraitChecked = 0;
        int voiceChecked = 0;
        int missing = 0;

        void CheckCard(Card card, string poolLabel)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.id))
                return;

            if (!string.IsNullOrWhiteSpace(card.portraitResourcePath))
            {
                portraitChecked++;
                Sprite sprite = Resources.Load<Sprite>(card.portraitResourcePath);
                if (sprite == null)
                {
                    missing++;
                    Add(Severity.Fail, cat,
                        $"[{poolLabel}] '{card.id}': missing Sprite at Resources/{card.portraitResourcePath}");
                }
                else
                {
                    portraitOk++;
                }
            }

            if (!string.IsNullOrWhiteSpace(card.voiceResourcePath))
            {
                voiceChecked++;
                AudioClip clip = Resources.Load<AudioClip>(card.voiceResourcePath);
                if (clip == null)
                {
                    missing++;
                    Add(Severity.Fail, cat,
                        $"[{poolLabel}] '{card.id}': missing AudioClip at Resources/{card.voiceResourcePath}");
                }
                else
                {
                    voiceOk++;
                }
            }
        }

        if (database.baseDeck != null)
        {
            for (int i = 0; i < database.baseDeck.Length; i++)
                CheckCard(database.baseDeck[i], "baseDeck");
        }

        if (database.unlockablePools != null)
        {
            for (int p = 0; p < database.unlockablePools.Length; p++)
            {
                UnlockableCardPool pool = database.unlockablePools[p];
                if (pool?.cards == null)
                    continue;

                string label = string.IsNullOrWhiteSpace(pool.id) ? $"unlockablePools[{p}]" : pool.id;
                for (int i = 0; i < pool.cards.Length; i++)
                    CheckCard(pool.cards[i], label);
            }
        }

        if (portraitChecked == 0 && voiceChecked == 0)
        {
            Add(Severity.Warn, cat,
                "No portraitResourcePath / voiceResourcePath assigned on any card. Depth FX may look empty.");
            return;
        }

        if (missing == 0)
        {
            Add(Severity.Pass, cat,
                $"All assigned card assets OK ({portraitOk}/{portraitChecked} portraits, {voiceOk}/{voiceChecked} voices).");
        }
    }

    // ─── AudioManager SoundPools ─────────────────────────────────────────────

    private void CheckAudioManagerPools()
    {
        const string cat = "Audio Pools";
        var targets = FindComponentsInProject<AudioManager>();

        if (targets.Count == 0)
        {
            Add(Severity.Warn, cat,
                "No AudioManager found in scenes/prefabs. Runtime may create one — assign clips on a prefab for builds.");
            return;
        }

        for (int t = 0; t < targets.Count; t++)
        {
            AudioManager audio = targets[t].Component;
            string context = targets[t].Context;
            if (audio == null)
                continue;

            var so = new SerializedObject(audio);

            for (int i = 0; i < AudioManagerPoolProperties.Length; i++)
            {
                string poolName = AudioManagerPoolProperties[i];
                SerializedProperty pool = so.FindProperty(poolName);
                if (pool == null)
                {
                    Add(Severity.Warn, cat, $"{context}: SoundPool '{poolName}' property missing.");
                    continue;
                }

                SerializedProperty clips = pool.FindPropertyRelative("clips");
                int assigned = CountAssignedObjectRefs(clips);
                int size = clips != null ? clips.arraySize : 0;

                string legacyName = i < AudioManagerLegacyClips.Length
                    ? AudioManagerLegacyClips[i]
                    : null;
                SerializedProperty legacy = legacyName != null ? so.FindProperty(legacyName) : null;
                bool hasLegacy = legacy != null && legacy.objectReferenceValue != null;

                if (assigned > 0)
                {
                    int nullSlots = size - assigned;
                    if (nullSlots > 0)
                    {
                        Add(Severity.Fail, cat,
                            $"{context}: {poolName}.clips has {nullSlots} missing (null) entry(ies).");
                    }
                    else
                    {
                        Add(Severity.Pass, cat,
                            $"{context}: {poolName} has {assigned} AudioClip variation(s).");
                    }
                }
                else if (hasLegacy)
                {
                    Add(Severity.Pass, cat,
                        $"{context}: {poolName} empty — legacy '{legacyName}' will be used as fallback.");
                }
                else
                {
                    Add(Severity.Fail, cat,
                        $"{context}: {poolName} has no clips and '{legacyName}' is unassigned.");
                }
            }
        }
    }

    private static int CountAssignedObjectRefs(SerializedProperty arrayProp)
    {
        if (arrayProp == null || !arrayProp.isArray)
            return 0;

        int count = 0;
        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            SerializedProperty element = arrayProp.GetArrayElementAtIndex(i);
            if (element != null && element.objectReferenceValue != null)
                count++;
        }

        return count;
    }

    // ─── Android Player Settings ─────────────────────────────────────────────

    private void CheckAndroidPlayerSettings()
    {
        const string cat = "Android Player Settings";

        string packageName = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        if (string.IsNullOrWhiteSpace(packageName))
        {
            Add(Severity.Fail, cat, "Package Name (applicationId) is empty.");
        }
        else if (!LooksLikeValidPackageName(packageName))
        {
            Add(Severity.Fail, cat,
                $"Package Name '{packageName}' is invalid. Use reverse-DNS form (e.g. com.studio.reigns).");
        }
        else if (IsDefaultCompanyPackage(packageName))
        {
            Add(Severity.Fail, cat,
                $"Package Name '{packageName}' still looks like a Unity default. Set a real applicationId before shipping.");
        }
        else
        {
            Add(Severity.Pass, cat, $"Package Name set: {packageName}");
        }

        AndroidSdkVersions targetSdk = PlayerSettings.Android.targetSdkVersion;
        if (targetSdk == AndroidSdkVersions.AndroidApiLevelAuto)
        {
            Add(Severity.Pass, cat,
                "Target API Level is Automatic (recommended — uses highest installed SDK).");
        }
        else
        {
            int level = (int)targetSdk;
            if (level < MinimumAndroidTargetApi)
            {
                Add(Severity.Fail, cat,
                    $"Target API Level is {level}. Google Play requires ≥ {MinimumAndroidTargetApi} (or Automatic).");
            }
            else
            {
                Add(Severity.Pass, cat, $"Target API Level is {level} (≥ {MinimumAndroidTargetApi}).");
            }
        }

        AndroidSdkVersions minSdk = PlayerSettings.Android.minSdkVersion;
        int minLevel = (int)minSdk;
        if (minLevel > 0 && minLevel < 22)
        {
            Add(Severity.Warn, cat,
                $"Min API Level is {minLevel}. Consider API 23+ for modern permission / vibration APIs.");
        }
        else
        {
            Add(Severity.Pass, cat, $"Min API Level is {(minLevel > 0 ? minLevel.ToString() : minSdk.ToString())}.");
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Add(Severity.Warn, cat,
                $"Active build target is {EditorUserBuildSettings.activeBuildTarget}, not Android. Switch platform before APK build.");
        }
        else
        {
            Add(Severity.Pass, cat, "Active build target is Android.");
        }
    }

    private static bool LooksLikeValidPackageName(string packageName)
    {
        if (packageName.Contains(" ") || packageName.StartsWith(".") || packageName.EndsWith("."))
            return false;

        string[] parts = packageName.Split('.');
        if (parts.Length < 2)
            return false;

        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i]))
                return false;
            if (!char.IsLetter(parts[i][0]) && parts[i][0] != '_')
                return false;
        }

        return true;
    }

    private static bool IsDefaultCompanyPackage(string packageName)
    {
        for (int i = 0; i < DefaultCompanyPackageFragments.Length; i++)
        {
            if (packageName.StartsWith(DefaultCompanyPackageFragments[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ─── Project discovery helpers ───────────────────────────────────────────

    private struct LocatedComponent<T> where T : Component
    {
        public T Component;
        public string Context;
    }

    private static List<LocatedComponent<T>> FindComponentsInProject<T>() where T : Component
    {
        var found = new List<LocatedComponent<T>>();
        var seen = new HashSet<int>();

        // Open / loaded scenes first.
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
                CollectComponents(roots[r], $"Scene '{scene.name}'", found, seen);
        }

        // Prefabs.
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            CollectComponents(prefab, $"Prefab '{path}'", found, seen);
        }

        // Scenes on disk (skip already loaded to avoid double work / dirtying).
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                loadedPaths.Add(scene.path);
        }

        for (int i = 0; i < sceneGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
            if (loadedPaths.Contains(path))
                continue;

            Scene opened = default;
            try
            {
                opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }
            catch (Exception)
            {
                continue;
            }

            if (!opened.IsValid())
                continue;

            GameObject[] roots = opened.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
                CollectComponents(roots[r], $"Scene '{path}'", found, seen);

            EditorSceneManager.CloseScene(opened, true);
        }

        return found;
    }

    private static void CollectComponents<T>(
        GameObject root,
        string context,
        List<LocatedComponent<T>> found,
        HashSet<int> seen) where T : Component
    {
        T[] components = root.GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component == null)
                continue;

            int id = component.GetInstanceID();
            if (!seen.Add(id))
                continue;

            found.Add(new LocatedComponent<T>
            {
                Component = component,
                Context = $"{context} / {GetHierarchyPath(component.transform)}"
            });
        }
    }

    private static string GetHierarchyPath(Transform t)
    {
        if (t == null)
            return "(null)";

        var stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }

        return string.Join("/", stack);
    }

    private static string ResolveProjectPath(string relativePath)
    {
        // Project root = parent of /Assets when present; otherwise Application.dataPath parent.
        string dataPath = Application.dataPath.Replace('\\', '/');
        string projectRoot = Directory.GetParent(dataPath)?.FullName ?? dataPath;

        // This repo sometimes keeps Scripts/Resources beside Assets — also try dataPath itself.
        string candidate = Path.Combine(projectRoot, relativePath);
        if (File.Exists(candidate) || Directory.Exists(candidate))
            return candidate;

        candidate = Path.Combine(dataPath, relativePath);
        if (File.Exists(candidate) || Directory.Exists(candidate))
            return candidate;

        // Walk up a level if Resources lives next to the Unity project folder.
        string sibling = Path.Combine(Directory.GetParent(projectRoot)?.FullName ?? projectRoot, relativePath);
        if (File.Exists(sibling) || Directory.Exists(sibling))
            return sibling;

        return Path.Combine(projectRoot, relativePath);
    }
}
#endif
