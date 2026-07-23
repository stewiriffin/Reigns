#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hidden mobile debug console. Triple-tap the top-left corner to toggle.
/// Stripped from non-development player builds.
/// </summary>
public class DebugConsole : MonoBehaviour
{
    private const float CornerSize = 120f;
    private const float TripleTapWindow = 0.55f;
    private const int TripleTapCount = 3;

    [SerializeField] private GameManager gameManager;
    [SerializeField] private KingdomStats kingdomStats;
    [SerializeField] private SaveManager saveManager;

    private bool panelVisible;
    private readonly List<float> cornerTapTimes = new List<float>(4);
    private Vector2 scroll;
    private string cardIdInput = "plague_in_the_capital";
    private DeathCause selectedDeath = DeathCause.ArmyEmpty;
    private string statusMessage = string.Empty;

    private GUIStyle boxStyle;
    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private bool stylesReady;
    private int windowId;

    private void Awake()
    {
        windowId = GetInstanceID();

        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();
        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();
        if (saveManager == null)
            saveManager = FindObjectOfType<SaveManager>();
    }

    private void Update()
    {
        DetectCornerTripleTap();
    }

    private void DetectCornerTripleTap()
    {
        bool tapped = false;
        Vector2 screenPos = Vector2.zero;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                tapped = true;
                screenPos = touch.position;
            }
        }
        else if (Input.GetMouseButtonDown(0))
        {
            tapped = true;
            screenPos = Input.mousePosition;
        }

        if (!tapped)
            return;

        // Top-left in screen space (y grows upward).
        Rect corner = new Rect(0f, Screen.height - CornerSize, CornerSize, CornerSize);
        if (!corner.Contains(screenPos))
        {
            cornerTapTimes.Clear();
            return;
        }

        float now = Time.unscaledTime;
        cornerTapTimes.Add(now);
        cornerTapTimes.RemoveAll(t => now - t > TripleTapWindow);

        if (cornerTapTimes.Count >= TripleTapCount)
        {
            cornerTapTimes.Clear();
            panelVisible = !panelVisible;
            statusMessage = panelVisible ? "Debug panel opened." : "Debug panel closed.";
        }
    }

    private void OnGUI()
    {
        if (!panelVisible)
            return;

        EnsureStyles();

        float width = Mathf.Min(560f, Screen.width - 24f);
        float height = Mathf.Min(720f, Screen.height - 24f);
        Rect area = new Rect(12f, 12f, width, height);

        GUI.ModalWindow(windowId, area, DrawWindow, "Reigns Debug Console", boxStyle);
    }

    private void DrawWindow(int id)
    {
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("Triple-tap top-left to close.", labelStyle);
        if (!string.IsNullOrEmpty(statusMessage))
            GUILayout.Label(statusMessage, labelStyle);

        DrawStatsSection();
        DrawCardJumpSection();
        DrawDeathSection();
        DrawProgressSection();

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 48));
    }

    private void DrawStatsSection()
    {
        GUILayout.Space(8);
        GUILayout.Label("Set Stats", labelStyle);

        if (kingdomStats == null)
        {
            GUILayout.Label("KingdomStats missing.", labelStyle);
            return;
        }

        GUILayout.Label(
            $"R:{kingdomStats.Religion}  P:{kingdomStats.People}  A:{kingdomStats.Army}  W:{kingdomStats.Wealth}",
            labelStyle);

        DrawStatRow(StatType.Religion, "Religion");
        DrawStatRow(StatType.People, "People");
        DrawStatRow(StatType.Army, "Army");
        DrawStatRow(StatType.Wealth, "Wealth");
    }

    private void DrawStatRow(StatType stat, string label)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(90));
        if (GUILayout.Button("0", buttonStyle))
            SetStat(stat, 0);
        if (GUILayout.Button("50", buttonStyle))
            SetStat(stat, 50);
        if (GUILayout.Button("100", buttonStyle))
            SetStat(stat, 100);
        GUILayout.EndHorizontal();
    }

    private void SetStat(StatType stat, int value)
    {
        if (kingdomStats == null)
            return;

        kingdomStats.DebugSetStat(stat, value);
        gameManager?.DebugRefreshHud();
        statusMessage = $"Set {stat} = {value}";
    }

    private void DrawCardJumpSection()
    {
        GUILayout.Space(12);
        GUILayout.Label("Jump to Card ID", labelStyle);
        cardIdInput = GUILayout.TextField(cardIdInput ?? string.Empty, buttonStyle);

        if (GUILayout.Button("Jump to Card", buttonStyle))
        {
            if (gameManager != null && gameManager.DebugJumpToCard(cardIdInput))
                statusMessage = $"Jumped to '{cardIdInput}'.";
            else
                statusMessage = $"Card not found: '{cardIdInput}'.";
        }
    }

    private void DrawDeathSection()
    {
        GUILayout.Space(12);
        GUILayout.Label("Force Death State", labelStyle);

        string[] names = Enum.GetNames(typeof(DeathCause));
        int current = Mathf.Max(0, Array.IndexOf(names, selectedDeath.ToString()));
        current = Mathf.Clamp(current, 1, names.Length - 1); // skip None in buttons

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", buttonStyle, GUILayout.Width(40)))
            current = Mathf.Max(1, current - 1);
        GUILayout.Label(names[current], labelStyle);
        if (GUILayout.Button(">", buttonStyle, GUILayout.Width(40)))
            current = Mathf.Min(names.Length - 1, current + 1);
        GUILayout.EndHorizontal();

        if (Enum.TryParse(names[current], out DeathCause parsed))
            selectedDeath = parsed;

        if (GUILayout.Button("Trigger Death", buttonStyle))
        {
            if (selectedDeath == DeathCause.None)
            {
                statusMessage = "Pick a non-None death cause.";
                return;
            }

            gameManager?.DebugForceDeath(selectedDeath);
            statusMessage = $"Forced death: {selectedDeath}";
        }
    }

    private void DrawProgressSection()
    {
        GUILayout.Space(12);
        GUILayout.Label("Progress", labelStyle);

        if (GUILayout.Button("Clear Saved Progress (PlayerPrefs.DeleteAll)", buttonStyle))
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            saveManager?.DeleteSave();
            statusMessage = "PlayerPrefs cleared + run save deleted.";
        }

        if (GUILayout.Button("Close Panel", buttonStyle))
            panelVisible = false;
    }

    private void EnsureStyles()
    {
        if (stylesReady)
            return;

        boxStyle = new GUIStyle(GUI.skin.window)
        {
            fontSize = 28,
            padding = new RectOffset(16, 16, 16, 16)
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 26,
            fixedHeight = 52,
            alignment = TextAnchor.MiddleCenter
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            wordWrap = true
        };

        stylesReady = true;
    }
}
#endif
