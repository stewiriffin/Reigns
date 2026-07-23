using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Serializable snapshot of an in-progress reign.
/// </summary>
[Serializable]
public class GameSaveData
{
    public int yearsRuled;
    public int religion;
    public int people;
    public int army;
    public int wealth;
    public StatusEffect[] statusEffects;
    public string[] inventoryItemIds;
    public string currentCardId;
    public bool secondChanceUsedThisRun;
    public FactionLoyaltySave[] factionLoyalties;
    public StoryArcProgressSave[] storyArcs;
}

/// <summary>
/// Auto-saves the active run to JSON on pause/quit, and restores it on launch.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    private const string SaveFileName = "reigns_run_save.json";

    [SerializeField] private GameManager gameManager;

    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            SaveGame();
    }

    private void OnApplicationQuit()
    {
        SaveGame();
    }

#if UNITY_EDITOR
    // Editor stop-play does not always fire OnApplicationQuit reliably on all versions.
    private void OnDestroy()
    {
        if (!Application.isPlaying)
            return;

        SaveGame();
    }
#endif

    public bool HasSaveFile()
    {
        return File.Exists(SavePath);
    }

    /// <summary>
    /// Writes the current run to disk (no-op if there is nothing meaningful to save).
    /// </summary>
    public void SaveGame()
    {
        if (DailyChallengeManager.Instance != null && DailyChallengeManager.Instance.IsDailyRunActive)
            return;

        if (gameManager == null || !gameManager.CanAutoSave)
            return;

        GameSaveData data = gameManager.CaptureSaveData();
        if (data == null || string.IsNullOrWhiteSpace(data.currentCardId))
            return;

        try
        {
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(SavePath, json);
            Debug.Log($"SaveManager: Saved run to {SavePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveManager: Failed to save — {e.Message}");
        }
    }

    /// <summary>
    /// Loads save JSON if present. Returns null when missing or corrupt.
    /// </summary>
    public GameSaveData LoadGame()
    {
        if (!HasSaveFile())
            return null;

        try
        {
            string json = File.ReadAllText(SavePath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            GameSaveData data = JsonUtility.FromJson<GameSaveData>(json);
            if (data == null || string.IsNullOrWhiteSpace(data.currentCardId))
            {
                Debug.LogWarning("SaveManager: Save file was empty or incomplete.");
                return null;
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveManager: Failed to load — {e.Message}");
            return null;
        }
    }

    public void DeleteSave()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
                Debug.Log("SaveManager: Deleted run save.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveManager: Could not delete save — {e.Message}");
        }
    }
}
