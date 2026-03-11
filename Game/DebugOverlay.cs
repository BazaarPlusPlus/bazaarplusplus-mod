#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using TheBazaar;
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

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.unscaledTime + RefreshInterval;
            RebuildLines();
        }

        DrawInfoPanel();
    }

    private void DrawInfoPanel()
    {
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
        Label("Preview: [F3] toggle, [F4] source, arrows move X/Z, PgUp/PgDn move Y, </> rotate, R reset");
        Label("Layout: 1/2 width, 3/4 depth, 5/6 spacing, -/= scale, K/L plate, ;/' border, N/M border height");

        try
        {
            DrawPreviewDebugState();

            var run = Data.Run;
            var state = Data.CurrentState;
            if (run != null)
            {
                Header("Run");
                Row("Hero", run.Player?.Hero.ToString());
                Row("Day", run.Day.ToString());
                Row("W/L", $"{run.Victories} / {run.Losses}");
                Row("State", state?.StateName.ToString());
                Row("Encounter", Data.CurrentEncounterId?.ToString() ?? "-");
            }
        }
        catch (Exception ex)
        {
            ModState.Logger?.LogWarning($"[DebugOverlay] Rebuild failed: {ex.Message}");
        }

        DrawEncounterList(
            "Available Encounters (map)",
            ModState.AvailableEncounters,
            ModState.EncounterMonsterPreviews
        );

        DrawEncounterList(
            "Current Encounter Choices",
            ModState.CurrentEncounterChoices,
            ModState.EncounterMonsterPreviews
        );
    }

    private void DrawPreviewDebugState()
    {
        var previewDebug = GetComponent<OverlayDebugController>();
        if (previewDebug == null || !previewDebug.TryGetDebugState(out var state))
            return;

        Header("Preview Debug");
        Row("Data Source", state.DataSource);
        Row("Encounter ID", state.EncounterId);
        Row("Monster Title", state.MonsterTitle);
        Row("Visible", state.Visible ? "on" : "off");
        Row("World Pos", FormatVector3(state.AnchorPosition));
        Row("World Rot", FormatVector3(state.AnchorRotationEuler));
        Row("Local Offset", FormatVector3(state.LocalOffset));
        Row("Board Size", $"{state.BoardSize.x:F2} x {state.BoardSize.y:F2}");
        Row("Card Spacing X", state.CardSpacingX.ToString("F2"));
        Row("Card Scale", state.CardScale.ToString("F2"));
        Row("Plate Thickness", state.BoardThickness.ToString("F2"));
        Row("Border Thickness", state.BorderThickness.ToString("F2"));
        Row("Border Height", state.BorderHeight.ToString("F2"));
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

        var previewByTemplateId = new Dictionary<Guid, RunInfo.MonsterPreview>();
        var previewByName = new Dictionary<string, RunInfo.MonsterPreview>();
        if (monsterPreviews != null)
        {
            foreach (var mp in monsterPreviews)
            {
                if (mp.EncounterTemplateId != Guid.Empty)
                    previewByTemplateId[mp.EncounterTemplateId] = mp;
                if (!string.IsNullOrEmpty(mp.EncounterName))
                    previewByName[mp.EncounterName] = mp;
            }
        }

        var matchedMonsterRows = 0;

        foreach (var card in cards)
        {
            var name = card.Name ?? card.TemplateId.ToString("N")[..8];
            var tier = card.Tier.ToString();
            var cardId = card.TemplateId.ToString("N")[..8];
            var enchant =
                string.IsNullOrEmpty(card.Enchant) || card.Enchant == "None" ? "-" : card.Enchant;

            Label($"  - Name: {name}");
            Label($"    Tier: {tier}");
            Label($"    Enchant: {enchant}");
            Label($"    CardID: {cardId}");

            RunInfo.MonsterPreview preview = null;
            if (!previewByTemplateId.TryGetValue(card.TemplateId, out preview))
                previewByName.TryGetValue(name, out preview);

            if (preview == null)
                continue;

            matchedMonsterRows++;

            if (!string.IsNullOrEmpty(preview.EncounterName))
                Label($"      id: {preview.EncounterName}");

            var levelText = preview.CombatLevel.HasValue
                ? preview.CombatLevel.Value.ToString()
                : "?";
            var goldText = preview.RewardGold.HasValue ? preview.RewardGold.Value.ToString() : "?";
            var xpText = preview.RewardXp.HasValue ? preview.RewardXp.Value.ToString() : "?";
            var sandText = !preview.SandstormEnabled.HasValue
                ? "?"
                : (preview.SandstormEnabled.Value ? "on" : "off");
            Label($"      combat: lvl={levelText}, reward={goldText}g/{xpText}xp, sand={sandText}");

            if (!string.IsNullOrEmpty(preview.MonsterTemplateId))
                Label($"      monsterTpl: {preview.MonsterTemplateId}");

            if (preview.Items == null && preview.Skills == null)
            {
                Label("      db: unavailable (encounter preview not migrated to monsters_bazaardb yet)");
                continue;
            }

            if (preview.Items != null)
            {
                Label($"      items ({preview.Items.Count}):");
                if (preview.Items.Count == 0)
                {
                    Label("        - (none)");
                }
                else
                {
                    foreach (var item in preview.Items)
                        Label($"        - {item}");
                }
            }

            if (preview.Skills != null)
            {
                Label($"      skills ({preview.Skills.Count}):");
                if (preview.Skills.Count == 0)
                {
                    Label("        - (none)");
                }
                else
                {
                    foreach (var skill in preview.Skills)
                        Label($"        - {skill}");
                }
            }
        }

        if (matchedMonsterRows > 0)
            Label($"  monster preview matched: {matchedMonsterRows}/{cards.Count}");
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

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
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
