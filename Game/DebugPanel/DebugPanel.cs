#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.EncounterTracking;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal sealed class DebugPanel : MonoBehaviour
{
    private const float RefreshInterval = 0.1f;
    private const float WindowWidth = 520f;

    private static readonly GUIStyle HeaderStyle = new GUIStyle();
    private static readonly GUIStyle StatusStyle = new GUIStyle();
    private static readonly GUIStyle SectionStyle = new GUIStyle();
    private static readonly GUIStyle KeyStyle = new GUIStyle();
    private static readonly GUIStyle LabelStyle = new GUIStyle();
    private static readonly GUIStyle ValueStyle = new GUIStyle();
    private static readonly GUIStyle MutedStyle = new GUIStyle();
    private static readonly GUIStyle ToolbarButtonStyle = new GUIStyle();
    private static readonly GUIStyle EntryStyle = new GUIStyle();
    private static bool _stylesInitialized;

    private readonly DebugPanelState _panelState = new DebugPanelState();
    private Vector2 _scroll = Vector2.zero;
    private PanelSnapshot _snapshot = PanelSnapshot.Empty;
    private float _nextRefreshTime;

    public static bool IsVisible { get; private set; }

    private sealed class PanelSnapshot
    {
        public static readonly PanelSnapshot Empty = new PanelSnapshot();

        public RunSummary Run;
        public List<EncounterSection> EncounterSections = new List<EncounterSection>();
        public List<ReplayEntry> Replays = new List<ReplayEntry>();
        public string ActiveBattleId;
    }

    private sealed class RunSummary
    {
        public string Hero;
        public string Day;
        public string WinLoss;
        public string State;
        public string EncounterId;
    }

    private sealed class EncounterSection
    {
        public string Key;
        public string Title;
        public List<EncounterEntry> Entries = new List<EncounterEntry>();
        public int MatchedMonsterRows;
    }

    private sealed class EncounterEntry
    {
        public string Key;
        public string Name;
        public string Tier;
        public string Enchant;
        public string CardId;
        public RunInfo.MonsterPreview Preview;
    }

    private sealed class ReplayEntry
    {
        public string BattleId;
        public string SavedAt;
        public string Label;
    }

    private void OnDisable()
    {
        IsVisible = false;
    }

    private void Update()
    {
        if (BppHotkeyService.WasPressedThisFrame(KeyBindings.Toggle.DebugPanel))
        {
            IsVisible = !IsVisible;
            if (IsVisible)
                RefreshSnapshot(force: true);
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (!IsVisible)
            return;

        if (BppHotkeyService.WasPressedThisFrame(KeyBindings.DebugPanel.SelectSummary))
            SelectSection(DebugPanelSection.Summary);
        else if (BppHotkeyService.WasPressedThisFrame(KeyBindings.DebugPanel.SelectRun))
            SelectSection(DebugPanelSection.Run);
        else if (BppHotkeyService.WasPressedThisFrame(KeyBindings.DebugPanel.SelectEncounters))
            SelectSection(DebugPanelSection.Encounters);
        else if (BppHotkeyService.WasPressedThisFrame(KeyBindings.DebugPanel.SelectReplays))
            SelectSection(DebugPanelSection.Replays);

        if (BppHotkeyService.WasPressedThisFrame(KeyBindings.DebugPanel.ToggleViewMode))
            _panelState.ToggleViewMode();

        if (Time.unscaledTime >= _nextRefreshTime)
            RefreshSnapshot(force: false);
    }

    private void OnGUI()
    {
        if (!IsVisible)
            return;

        InitStyles();

        var windowRect = new Rect(10, 10, WindowWidth, Screen.height - 20);
        GUI.Box(windowRect, "");
        GUILayout.BeginArea(
            new Rect(
                windowRect.x + 8,
                windowRect.y + 8,
                windowRect.width - 16,
                windowRect.height - 16
            )
        );

        DrawToolbar();
        GUILayout.Space(8);

        _scroll = GUILayout.BeginScrollView(_scroll);
        if (_panelState.ShowAllSections)
            DrawAllSections();
        else
            DrawSection(_panelState.ActiveSection);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawToolbar()
    {
        GUILayout.Label("Bazaar++ Debug Panel", HeaderStyle);
        GUILayout.Label(
            $"Mode: {(_panelState.ShowAllSections ? "All Sections" : _panelState.ActiveSection.ToString())}",
            StatusStyle
        );
        GUILayout.Label(
            $"[F2] Toggle  [1-4] Sections  [Tab] {(_panelState.ShowAllSections ? "Single" : "All")}",
            MutedStyle
        );
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        DrawSectionButton("1 Summary", DebugPanelSection.Summary);
        DrawSectionButton("2 Run", DebugPanelSection.Run);
        DrawSectionButton("3 Encounters", DebugPanelSection.Encounters);
        DrawSectionButton("4 Replays", DebugPanelSection.Replays);
        GUILayout.EndHorizontal();
    }

    private void DrawSectionButton(string label, DebugPanelSection section)
    {
        var wasEnabled = GUI.enabled;
        GUI.enabled = _panelState.ActiveSection != section || _panelState.ShowAllSections;
        if (GUILayout.Button(label, ToolbarButtonStyle))
            SelectSection(section);
        GUI.enabled = wasEnabled;
    }

    private void DrawAllSections()
    {
        DrawSection(DebugPanelSection.Summary);
        DrawSection(DebugPanelSection.Run);
        DrawSection(DebugPanelSection.Encounters);
        DrawSection(DebugPanelSection.Replays);
    }

    private void DrawSection(DebugPanelSection section)
    {
        switch (section)
        {
            case DebugPanelSection.Summary:
                DrawSummarySection();
                break;
            case DebugPanelSection.Run:
                DrawRunSection();
                break;
            case DebugPanelSection.Encounters:
                DrawEncounterSections();
                break;
            case DebugPanelSection.Replays:
                DrawReplaySection();
                break;
        }
    }

    private void DrawSummarySection()
    {
        DrawSectionHeader("SUMMARY");
        var keyboard = Keyboard.current;
        DrawRow("Hero", _snapshot.Run?.Hero ?? "-");
        DrawRow("Day", _snapshot.Run?.Day ?? "-");
        DrawRow("W/L", _snapshot.Run?.WinLoss ?? "-");
        DrawRow("State", _snapshot.Run?.State ?? "-");
        DrawRow("Encounter ID", _snapshot.Run?.EncounterId ?? "-");
        DrawRow(
            "Enchant Key",
            $"{BppHotkeyService.GetBindingDisplay(BppHotkeyActionId.HoldEnchantPreview)} [{(BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview, keyboard) ? "held" : "up")}]"
        );
        DrawRow(
            "Upgrade Key",
            $"{BppHotkeyService.GetBindingDisplay(BppHotkeyActionId.HoldUpgradePreview)} [{(BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview, keyboard) ? "held" : "up")}]"
        );
        DrawRow(
            "Ctrl Raw",
            keyboard == null
                ? "n/a"
                : $"L={keyboard.leftCtrlKey.isPressed} R={keyboard.rightCtrlKey.isPressed}"
        );
        DrawRow(
            "Shift Raw",
            keyboard == null
                ? "n/a"
                : $"L={keyboard.leftShiftKey.isPressed} R={keyboard.rightShiftKey.isPressed}"
        );
    }

    private void DrawRunSection()
    {
        DrawSectionHeader("RUN");
        DrawRow("Hero", _snapshot.Run?.Hero ?? "-");
        DrawRow("Day", _snapshot.Run?.Day ?? "-");
        DrawRow("W/L", _snapshot.Run?.WinLoss ?? "-");
        DrawRow("State", _snapshot.Run?.State ?? "-");
        DrawRow("Encounter", _snapshot.Run?.EncounterId ?? "-");
    }

    private void DrawReplaySection()
    {
        DrawSectionHeader("REPLAYS");

        var runtime = CombatReplayRuntime.Instance;
        if (runtime == null)
        {
            GUILayout.Label("Combat replay runtime unavailable.", MutedStyle);
            return;
        }

        var canReplaySavedCombats = runtime.CanReplaySavedCombats(out var replayRestrictionReason);
        var previousEnabled = GUI.enabled;
        GUI.enabled = canReplaySavedCombats;
        if (GUILayout.Button("Replay Latest", ToolbarButtonStyle))
            runtime.ReplayLatest();
        GUI.enabled = previousEnabled;

        DrawRow(
            "Active Replay",
            string.IsNullOrWhiteSpace(_snapshot.ActiveBattleId) ? "-" : _snapshot.ActiveBattleId
        );

        if (!canReplaySavedCombats)
            GUILayout.Label(replayRestrictionReason, MutedStyle);

        if (_snapshot.Replays.Count == 0)
        {
            GUILayout.Label("No saved combat replays.", MutedStyle);
            return;
        }

        foreach (var replay in _snapshot.Replays)
        {
            previousEnabled = GUI.enabled;
            GUI.enabled = canReplaySavedCombats;
            if (GUILayout.Button($"Replay {replay.Label}", EntryStyle))
                runtime.ReplaySaved(replay.BattleId);
            GUI.enabled = previousEnabled;

            GUILayout.Label($"Saved {replay.SavedAt}", MutedStyle);
        }
    }

    private void DrawEncounterSections()
    {
        foreach (var section in _snapshot.EncounterSections)
        {
            DrawSectionHeader(section.Title.ToUpperInvariant());

            if (section.Entries.Count == 0)
            {
                GUILayout.Label("(none)", MutedStyle);
                continue;
            }

            foreach (var entry in section.Entries)
                DrawEncounterEntry(entry);

            if (section.MatchedMonsterRows > 0)
            {
                GUILayout.Label(
                    $"Matched monster preview: {section.MatchedMonsterRows}/{section.Entries.Count}",
                    MutedStyle
                );
            }
        }
    }

    private void DrawEncounterEntry(EncounterEntry entry)
    {
        var expanded = _panelState.IsEncounterExpanded(entry.Key);
        var matched = entry.Preview != null ? "matched" : "unmatched";
        var toggleLabel = expanded ? "[-]" : "[+]";
        if (
            GUILayout.Button(
                $"{toggleLabel} {entry.Name}  T{entry.Tier}  {entry.Enchant}  {entry.CardId}  {matched}",
                EntryStyle
            )
        )
        {
            _panelState.ToggleEncounter(entry.Key);
        }

        if (!expanded)
            return;

        GUILayout.BeginVertical("box");
        GUILayout.Space(2);
        DrawRow("Tier", entry.Tier);
        DrawRow("Enchant", entry.Enchant);
        DrawRow("CardID", entry.CardId);

        if (entry.Preview == null)
        {
            GUILayout.Label("Monster preview unavailable.", MutedStyle);
            GUILayout.EndVertical();
            return;
        }

        if (!string.IsNullOrEmpty(entry.Preview.EncounterName))
            DrawRow("Encounter Name", entry.Preview.EncounterName);
        if (!string.IsNullOrEmpty(entry.Preview.Title))
            DrawRow("Title", entry.Preview.Title);
        if (entry.Preview.EncounterId != Guid.Empty)
            DrawRow("Encounter Id", entry.Preview.EncounterShortId);

        var levelText = entry.Preview.CombatLevel.HasValue
            ? entry.Preview.CombatLevel.Value.ToString()
            : "?";
        var goldText = entry.Preview.RewardGold.HasValue
            ? entry.Preview.RewardGold.Value.ToString()
            : "?";
        var xpText = entry.Preview.RewardXp.HasValue
            ? entry.Preview.RewardXp.Value.ToString()
            : "?";
        var sandText = !entry.Preview.SandstormEnabled.HasValue
            ? "?"
            : (entry.Preview.SandstormEnabled.Value ? "on" : "off");
        DrawRow("Combat", $"lvl={levelText}, reward={goldText}g/{xpText}xp, sand={sandText}");

        if (!string.IsNullOrEmpty(entry.Preview.MonsterTemplateId))
            DrawRow("Monster Tpl", entry.Preview.MonsterTemplateId);

        DrawPreviewCardList("Board", entry.Preview.BoardCards);
        DrawPreviewCardList("Skills", entry.Preview.Skills);
        GUILayout.Space(2);
        GUILayout.EndVertical();
    }

    private void DrawPreviewCardList(string title, List<RunInfo.MonsterPreviewCard> values)
    {
        if (values == null)
        {
            GUILayout.Label($"{title}: unavailable", MutedStyle);
            return;
        }

        GUILayout.Label($"{title} ({values.Count})", LabelStyle);
        if (values.Count == 0)
        {
            GUILayout.Label("    - (none)", MutedStyle);
            return;
        }

        foreach (var value in values)
        {
            if (value == null)
                continue;

            var label = string.IsNullOrWhiteSpace(value.SourceName)
                ? value.TemplateId
                : $"{value.SourceName} ({value.TemplateId})";
            GUILayout.Label(
                $"    - {label}  T{value.Tier}  size={value.Size}  {value.Enchant}",
                ValueStyle
            );
        }
    }

    private void DrawSectionHeader(string title)
    {
        GUILayout.Space(10);
        GUILayout.Label(title, SectionStyle);
        GUILayout.Space(6);
    }

    private void DrawRow(string key, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(key, KeyStyle, GUILayout.Width(148f));
        GUILayout.Label(value ?? "-", ValueStyle);
        GUILayout.EndHorizontal();
        GUILayout.Space(2);
    }

    private void SelectSection(DebugPanelSection section)
    {
        _panelState.SelectSection(section);
        _panelState.ShowOnlySelectedSection();
        _scroll = Vector2.zero;
    }

    private void RefreshSnapshot(bool force)
    {
        if (!force && Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + RefreshInterval;

        try
        {
            _snapshot = BuildSnapshot();
        }
        catch (Exception ex)
        {
            BppLog.Warn("DebugPanel", $"Rebuild failed: {ex.Message}");
        }
    }

    private PanelSnapshot BuildSnapshot()
    {
        var selectionSnapshot = EncounterTracker.SelectionQuery.GetSnapshot();
        var snapshot = new PanelSnapshot
        {
            Run = BuildRunSummary(),
            Replays = BuildReplayEntries(),
            ActiveBattleId = CombatReplayRuntime.Instance?.ActiveBattleId ?? string.Empty,
        };

        snapshot.EncounterSections.Add(
            BuildEncounterSection(
                "map",
                "Available Encounters (map)",
                selectionSnapshot.AvailableEncounters,
                selectionSnapshot.EncounterMonsterPreviews
            )
        );
        snapshot.EncounterSections.Add(
            BuildEncounterSection(
                "choice",
                "Current Encounter Choices",
                selectionSnapshot.CurrentEncounterChoices,
                selectionSnapshot.EncounterMonsterPreviews
            )
        );
        return snapshot;
    }

    private List<ReplayEntry> BuildReplayEntries()
    {
        var runtime = CombatReplayRuntime.Instance;
        if (runtime == null)
            return new List<ReplayEntry>();

        return runtime
            .ListRecentBattles()
            .Take(10)
            .Select(record => new ReplayEntry
            {
                BattleId = record.BattleId,
                SavedAt = record.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Label = BuildReplayLabel(record),
            })
            .ToList();
    }

    private static string BuildReplayLabel(PvpBattleManifest record)
    {
        var runText = record.RunId ?? "unknown-run";
        var dayHour =
            record.Day.HasValue && record.Hour.HasValue
                ? $"D{record.Day.Value} H{record.Hour.Value}"
                : "D? H?";
        var opponent = string.IsNullOrWhiteSpace(record.Participants.OpponentName)
            ? "Unknown Opponent"
            : record.Participants.OpponentName;
        return $"{dayHour}  {opponent}  {runText}";
    }

    private RunSummary BuildRunSummary()
    {
        var run = Data.Run;
        var state = Data.CurrentState;
        return new RunSummary
        {
            Hero = run?.Player?.Hero.ToString() ?? "-",
            Day = run?.Day.ToString() ?? "-",
            WinLoss = run != null ? $"{run.Victories} / {run.Losses}" : "-",
            State = state?.StateName.ToString() ?? "-",
            EncounterId = Data.CurrentEncounterId?.ToString() ?? "-",
        };
    }

    private EncounterSection BuildEncounterSection(
        string sectionKey,
        string title,
        List<RunInfo.CardInfo> cards,
        List<RunInfo.MonsterPreview> monsterPreviews
    )
    {
        var section = new EncounterSection { Key = sectionKey, Title = title };

        if (cards == null || cards.Count == 0)
            return section;

        var previewByTemplateId = new Dictionary<Guid, RunInfo.MonsterPreview>();
        var previewByName = new Dictionary<string, RunInfo.MonsterPreview>();
        if (monsterPreviews != null)
        {
            foreach (var preview in monsterPreviews)
            {
                if (preview.EncounterTemplateId != Guid.Empty)
                    previewByTemplateId[preview.EncounterTemplateId] = preview;
                if (!string.IsNullOrEmpty(preview.EncounterName))
                    previewByName[preview.EncounterName] = preview;
            }
        }

        foreach (var card in cards)
        {
            var name = card.Name ?? card.TemplateId.ToString("N")[..8];
            var entry = new EncounterEntry
            {
                Key = $"{sectionKey}:{card.TemplateId:N}",
                Name = name,
                Tier = card.Tier.ToString(),
                Enchant =
                    string.IsNullOrEmpty(card.Enchant) || card.Enchant == "None"
                        ? "-"
                        : card.Enchant,
                CardId = card.TemplateId.ToString("N")[..8],
            };

            if (!previewByTemplateId.TryGetValue(card.TemplateId, out entry.Preview))
                previewByName.TryGetValue(name, out entry.Preview);

            if (entry.Preview != null)
                section.MatchedMonsterRows++;

            section.Entries.Add(entry);
        }

        return section;
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
    }

    private static void InitStyles()
    {
        if (_stylesInitialized)
            return;

        HeaderStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        HeaderStyle.fontStyle = FontStyle.Bold;
        HeaderStyle.fontSize = 18;

        StatusStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
        StatusStyle.fontSize = 14;

        SectionStyle.normal.textColor = new Color(0.75f, 0.9f, 1f);
        SectionStyle.fontStyle = FontStyle.Bold;
        SectionStyle.fontSize = 15;

        KeyStyle.normal.textColor = new Color(0.72f, 0.88f, 1f);
        KeyStyle.fontSize = 14;
        KeyStyle.fontStyle = FontStyle.Bold;

        LabelStyle.normal.textColor = Color.white;
        LabelStyle.fontSize = 14;

        ValueStyle.normal.textColor = Color.white;
        ValueStyle.fontSize = 14;
        ValueStyle.wordWrap = true;

        MutedStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        MutedStyle.fontSize = 12;
        MutedStyle.wordWrap = true;

        CopyButtonStyle(ToolbarButtonStyle);
        ToolbarButtonStyle.alignment = TextAnchor.MiddleLeft;
        ToolbarButtonStyle.fontSize = 13;
        ToolbarButtonStyle.padding = new RectOffset(8, 8, 6, 6);

        CopyButtonStyle(EntryStyle);
        EntryStyle.alignment = TextAnchor.MiddleLeft;
        EntryStyle.fontSize = 14;
        EntryStyle.padding = new RectOffset(10, 8, 8, 8);
        EntryStyle.wordWrap = true;

        _stylesInitialized = true;
    }

    private static void CopyButtonStyle(GUIStyle target)
    {
        var buttonStyle = GUI.skin.button;
        target.normal = buttonStyle.normal;
        target.hover = buttonStyle.hover;
        target.active = buttonStyle.active;
        target.focused = buttonStyle.focused;
        target.onNormal = buttonStyle.onNormal;
        target.onHover = buttonStyle.onHover;
        target.onActive = buttonStyle.onActive;
        target.onFocused = buttonStyle.onFocused;
        target.border = buttonStyle.border;
        target.margin = buttonStyle.margin;
        target.overflow = buttonStyle.overflow;
        target.padding = buttonStyle.padding;
    }
}
