#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using Newtonsoft.Json;
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

    private MonsterPreviewOverlay _monsterPreview;

    private struct Line
    {
        public string Text;
        public bool IsHeader;
    }

    private sealed class MinimalCardJson
    {
        [JsonProperty("t")]
        public string TemplateId { get; set; } = string.Empty;

        [JsonProperty("r")]
        public int Tier { get; set; }

        [JsonProperty("e")]
        public string Enchant { get; set; } = "None";

        [JsonProperty("a")]
        public Dictionary<int, int> Attributes { get; set; } = new Dictionary<int, int>();
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

        try
        {
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

                // Feed MonsterPreviewOverlay with hand cards
                _monsterPreview ??= GetComponent<MonsterPreviewOverlay>();
                if (_monsterPreview != null)
                {
                    var handCards = GameDataReader.GetItemsAsCards(run.Player?.Hand);
                    _monsterPreview.SetJson(BuildMinimalJson(handCards));
                }
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

    private static string BuildMinimalJson(List<Card> cards)
    {
        var list = new List<MinimalCardJson>();
        if (cards != null)
        {
            foreach (var card in cards)
            {
                if (card == null || card.Type != ECardType.Item)
                    continue;

                list.Add(
                    new MinimalCardJson
                    {
                        TemplateId = card.TemplateId.ToString(),
                        Tier = (int)card.Tier,
                        Enchant = (card as ItemCard)?.Enchantment?.ToString() ?? "None",
                        Attributes =
                            card.Attributes?.ToDictionary(kv => (int)kv.Key, kv => kv.Value)
                            ?? new Dictionary<int, int>(),
                    }
                );
            }
        }

        return JsonConvert.SerializeObject(list, Formatting.None);
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
                Label("      db: missing (BazaarPlusPlus_monsters.json)");
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
