#pragma warning disable CS0436
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal class DebugOverlay : MonoBehaviour
{
    private bool _visible = false;
    private Vector2 _scroll = Vector2.zero;

    private static readonly GUIStyle _headerStyle = new GUIStyle();
    private static readonly GUIStyle _labelStyle = new GUIStyle();
    private static bool _stylesInitialized = false;

    // Cached lines snapshot
    private readonly List<Line> _lines = new List<Line>();
    private float _nextRefreshTime;
    private const float RefreshInterval = 0.1f;

    private struct Line
    {
        public string Text;
        public bool IsHeader;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f2Key.wasPressedThisFrame)
            _visible = !_visible;
    }

    private void OnGUI()
    {
        if (!_visible)
            return;

        InitStyles();

        // Refresh data snapshot at a lower frequency to avoid heavy per-frame work
        if (Time.unscaledTime >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.unscaledTime + RefreshInterval;
            RebuildLines();
        }

        var windowRect = new Rect(10, 10, 420, Screen.height - 20);
        GUI.Box(windowRect, "");
        GUILayout.BeginArea(
            new Rect(
                windowRect.x + 8,
                windowRect.y + 8,
                windowRect.width - 16,
                windowRect.height - 16
            )
        );
        _scroll = GUILayout.BeginScrollView(_scroll);

        foreach (var line in _lines)
        {
            if (line.IsHeader)
            {
                GUILayout.Space(4);
                GUILayout.Label(line.Text, _headerStyle);
                GUILayout.Space(2);
            }
            else
            {
                GUILayout.Label(line.Text, _labelStyle);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void RebuildLines()
    {
        _lines.Clear();

        Header("BazaarPlusPlus  [F2 toggle]");

        // Run basics
        try
        {
            var run = TheBazaar.Data.Run;
            var state = TheBazaar.Data.CurrentState;
            if (run != null)
            {
                Header("Run");
                Row("Hero", run.Player?.Hero.ToString());
                Row("Day", run.Day.ToString());
                Row("W/L", $"{run.Victories} / {run.Losses}");
                Row("State", state?.StateName.ToString());
                Row("Encounter", TheBazaar.Data.CurrentEncounterId?.ToString() ?? "-");
            }
        }
        catch { }

        // Available encounters (map path) with monster preview
        DrawEncounterList(
            "Available Encounters (map)",
            ModState.AvailableEncounters,
            ModState.EncounterMonsterPreviews
        );

        // Current encounter choices (inside encounter)
        DrawCardList("Current Encounter Choices", ModState.CurrentEncounterChoices);
    }

    private void DrawEncounterList(
        string title,
        List<RunInfo.CardInfo> cards,
        List<RunInfo.MonsterPreview> monsterPreviews
    )
    {
        Header(title);
        if (cards == null || cards.Count == 0)
        {
            Label("  (none)");
            return;
        }

        // Build a lookup by EncounterName for quick access
        var previewMap = new Dictionary<string, RunInfo.MonsterPreview>();
        if (monsterPreviews != null)
            foreach (var mp in monsterPreviews)
                if (!string.IsNullOrEmpty(mp.EncounterName))
                    previewMap[mp.EncounterName] = mp;

        foreach (var card in cards)
        {
            var name = card.Name ?? card.TemplateId.ToString("N")[..8];
            var tier = card.Tier.ToString();
            var enchant =
                string.IsNullOrEmpty(card.Enchant) || card.Enchant == "None"
                    ? ""
                    : $" [{card.Enchant}]";
            Label($"  • {name}  T{tier}{enchant}");

            // If this is a combat encounter with monster data, show inline
            if (card.Name != null && previewMap.TryGetValue(card.Name, out var preview))
            {
                if (preview.Items == null && preview.Skills == null)
                {
                    Label("      [no-data-now]");
                }
                else
                {
                    if (preview.Items != null && preview.Items.Count > 0)
                        Label("      items: " + string.Join(", ", preview.Items));
                    if (preview.Skills != null && preview.Skills.Count > 0)
                        Label("      skills: " + string.Join(", ", preview.Skills));
                }
            }
        }
    }

    private void DrawCardList(string title, List<RunInfo.CardInfo> cards)
    {
        Header(title);
        if (cards == null || cards.Count == 0)
        {
            Label("  (none)");
            return;
        }
        foreach (var card in cards)
        {
            var name = card.Name ?? card.TemplateId.ToString("N")[..8];
            var tier = card.Tier.ToString();
            var enchant =
                string.IsNullOrEmpty(card.Enchant) || card.Enchant == "None"
                    ? ""
                    : $" [{card.Enchant}]";
            Label($"  • {name}  T{tier}{enchant}");
        }
    }

    private void Header(string text)
    {
        _lines.Add(new Line { Text = text, IsHeader = true });
    }

    private void Row(string key, string value)
    {
        Label($"  {key}: {value ?? "-"}");
    }

    private void Label(string text)
    {
        _lines.Add(new Line { Text = text, IsHeader = false });
    }

    private static void InitStyles()
    {
        if (_stylesInitialized)
            return;

        _headerStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        _headerStyle.fontStyle = FontStyle.Bold;
        _headerStyle.fontSize = 13;

        _labelStyle.normal.textColor = Color.white;
        _labelStyle.fontSize = 12;

        _stylesInitialized = true;
    }
}
