#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using Newtonsoft.Json;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal class DebugOverlay : MonoBehaviour
{
    private bool _visible = false;
    private bool _showcaseVisible = true;
    private Vector2 _scroll = Vector2.zero;

    private static readonly GUIStyle _headerStyle = new GUIStyle();
    private static readonly GUIStyle _labelStyle = new GUIStyle();
    private static readonly GUIStyle _showcaseTitleStyle = new GUIStyle();
    private static bool _stylesInitialized = false;

    private readonly List<Line> _lines = new List<Line>();
    private readonly List<RunInfo.CardInfo> _showcaseCards = new List<RunInfo.CardInfo>();
    private readonly List<Card> _handCardsForLayout = new List<Card>();
    private readonly List<GameObject> _showcaseEntities = new List<GameObject>();
    private readonly List<GameObject> _showcaseAnchors = new List<GameObject>();
    private readonly List<ShowcaseCardJson> _showcaseInputCards = new List<ShowcaseCardJson>();

    private string _showcaseJsonInput = "[]";
    private string _showcaseSignature = string.Empty;
    private bool _showcaseDirty = true;
    private bool _showcaseSyncInFlight = false;
    private int _showcaseSyncVersion = 0;
    private string _showcaseStatus = "idle";

    private MethodInfo _instantiateCardMethod;
    private object _spawnSection;

    private float _nextRefreshTime;
    private const float RefreshInterval = 0.1f;

    private struct Line
    {
        public string Text;
        public bool IsHeader;
    }

    private sealed class ShowcaseCardJson
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
        {
            _visible = !_visible;
            if (!_visible)
                HideShowcaseEntities();
            else
                _showcaseDirty = true;
        }

        if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
        {
            _showcaseVisible = !_showcaseVisible;
            if (!_showcaseVisible)
                HideShowcaseEntities();
            else
                _showcaseDirty = true;
        }

        if (_visible && _showcaseVisible && _showcaseDirty && !_showcaseSyncInFlight)
        {
            _showcaseSyncVersion++;
            _ = SyncShowcaseEntitiesAsync(_showcaseSyncVersion);
        }
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
        DrawShowcasePanel();
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

    private void DrawShowcasePanel()
    {
        if (!_visible || !_showcaseVisible)
            return;

        var x = 440f;
        var y = 10f;
        var width = Mathf.Max(260f, Screen.width - x - 10f);
        var height = 106f;
        var panel = new Rect(x, y, width, height);
        GUI.Box(panel, "");

        var innerX = panel.x + 10f;
        var innerY = panel.y + 8f;
        var innerW = panel.width - 20f;

        GUI.Label(
            new Rect(innerX, innerY, innerW, 22f),
            $"Showcase (JSON Input)  [F3 toggle]  Count: {_showcaseInputCards.Count}",
            _showcaseTitleStyle
        );

        GUI.Label(
            new Rect(innerX, innerY + 24f, innerW, 20f),
            "Render only from minimal JSON list. Hover enabled, click/drag disabled.",
            _labelStyle
        );

        GUI.Label(
            new Rect(innerX, innerY + 44f, innerW, 20f),
            $"status: {(_showcaseSyncInFlight ? "syncing..." : _showcaseStatus)}",
            _labelStyle
        );

        GUI.Label(
            new Rect(innerX, innerY + 64f, innerW, 20f),
            $"json bytes: {_showcaseJsonInput.Length}",
            _labelStyle
        );
    }

    private void RebuildLines()
    {
        _lines.Clear();
        _showcaseCards.Clear();
        _handCardsForLayout.Clear();

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

                var handCards = GameDataReader.GetItemsAsCards(run.Player?.Hand);
                _showcaseCards.AddRange(GameDataReader.GetCardInfo(handCards));
                _handCardsForLayout.AddRange(handCards);

                _showcaseJsonInput = BuildMinimalJsonFromCards(handCards);
                _showcaseInputCards.Clear();
                _showcaseInputCards.AddRange(ParseMinimalJson(_showcaseJsonInput));
            }
        }
        catch (Exception ex)
        {
            _showcaseStatus = $"rebuild error: {ex.GetType().Name}";
            ModState.Logger?.LogWarning($"[DebugOverlay] Rebuild failed: {ex.Message}");
        }

        if (!string.Equals(_showcaseJsonInput, _showcaseSignature, StringComparison.Ordinal))
        {
            _showcaseSignature = _showcaseJsonInput;
            _showcaseDirty = true;
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

    private static string BuildMinimalJsonFromCards(List<Card> cards)
    {
        var list = new List<ShowcaseCardJson>();
        if (cards != null)
        {
            foreach (var card in cards)
            {
                if (card == null || card.Type != ECardType.Item)
                    continue;

                list.Add(
                    new ShowcaseCardJson
                    {
                        TemplateId = card.TemplateId.ToString(),
                        Tier = (int)card.Tier,
                        Enchant = (card as ItemCard)?.Enchantment?.ToString() ?? "None",
                        Attributes = card.Attributes?.ToDictionary(kv => (int)kv.Key, kv => kv.Value) ?? new Dictionary<int, int>(),
                    }
                );
            }
        }

        return JsonConvert.SerializeObject(list, Formatting.None);
    }

    private static List<ShowcaseCardJson> ParseMinimalJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ShowcaseCardJson>();

        try
        {
            return JsonConvert.DeserializeObject<List<ShowcaseCardJson>>(json)
                ?? new List<ShowcaseCardJson>();
        }
        catch
        {
            return new List<ShowcaseCardJson>();
        }
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

        _showcaseTitleStyle.normal.textColor = new Color(0.8f, 0.95f, 1f);
        _showcaseTitleStyle.fontStyle = FontStyle.Bold;
        _showcaseTitleStyle.fontSize = 13;

        _stylesInitialized = true;
    }

    private async Task SyncShowcaseEntitiesAsync(int version)
    {
        _showcaseSyncInFlight = true;
        try
        {
            HideShowcaseEntities();

            if (!_visible || !_showcaseVisible || _showcaseInputCards.Count == 0)
            {
                _showcaseStatus = "no json cards";
                return;
            }

            Services.TryGet<AssetLoader>(out var assetLoader);
            if (assetLoader == null)
            {
                _showcaseStatus = "asset loader missing";
                return;
            }

            if (!EnsureInstantiateApi(assetLoader))
            {
                _showcaseStatus = "InstantiateCardAsync api missing";
                return;
            }

            var staticData = await Data.GetStatic();
            var spawnedCount = 0;

            for (var i = 0; i < _showcaseInputCards.Count; i++)
            {
                if (version != _showcaseSyncVersion)
                    return;

                var input = _showcaseInputCards[i];
                var runtimeCard = BuildRuntimeCardFromJson(input, staticData);
                if (runtimeCard == null)
                    continue;

                var anchor = CreateShowcaseAnchor(i);
                if (anchor == null)
                {
                    _showcaseStatus = "anchor create failed";
                    continue;
                }

                GameObject spawned = null;
                try
                {
                    spawned = await InstantiateCardForShowcaseAsync(assetLoader, runtimeCard, anchor);
                }
                catch (Exception ex)
                {
                    _showcaseStatus = $"instantiate error: {ex.GetType().Name}";
                    ModState.Logger?.LogWarning(
                        $"[DebugOverlay] Showcase instantiate failed: {ex.Message}"
                    );
                }

                if (spawned == null)
                    continue;

                if (version != _showcaseSyncVersion)
                {
                    Destroy(spawned);
                    return;
                }

                spawned.AddComponent<ShowcaseCardMarker>();
                if (spawned.TryGetComponent<CardController>(out var cardController))
                {
                    cardController.EnableMovement(false);
                    cardController.ShowCard(true);
                }

                _showcaseEntities.Add(spawned);
                spawnedCount++;
            }

            ApplyFixedAdjacentLayout();
            _showcaseStatus = $"spawned {spawnedCount}/{_showcaseInputCards.Count}";
        }
        finally
        {
            if (version == _showcaseSyncVersion)
                _showcaseDirty = false;
            _showcaseSyncInFlight = false;
        }
    }

    private static ItemCard BuildRuntimeCardFromJson(ShowcaseCardJson input, object staticData)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.TemplateId))
            return null;

        if (!Guid.TryParse(input.TemplateId, out var templateId))
            return null;

        var template = GetTemplateById(staticData, templateId) as ITCard;
        if (template == null)
            return null;

        var card = new ItemCard
        {
            InstanceId = InstanceId.New("ppshow"),
            TemplateId = templateId,
            Template = template,
            Tier = (ETier)Mathf.Clamp(input.Tier, 0, 5),
            Size = template.Size,
            Type = ECardType.Item,
            Attributes = new Dictionary<ECardAttributeType, int>(),
            Tags = new HashSet<ECardTag>(),
            HiddenTags = new HashSet<EHiddenTag>(),
            Heroes = new HashSet<EHero>(),
            Owner = null,
            Section = null,
            LeftSocketId = null,
        };

        if (
            !string.IsNullOrWhiteSpace(input.Enchant)
            && !string.Equals(input.Enchant, "None", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(input.Enchant, out EEnchantmentType enchantType)
        )
        {
            card.Enchantment = enchantType;
        }


        if (input.Attributes != null)
        {
            foreach (var kv in input.Attributes)
            {
                if (Enum.IsDefined(typeof(ECardAttributeType), kv.Key))
                    card.Attributes[(ECardAttributeType)kv.Key] = kv.Value;
            }
        }

        return card;
    }
    private static object GetTemplateById(object staticData, Guid templateId)
    {
        if (staticData == null)
            return null;

        var method = staticData
            .GetType()
            .GetMethod("GetCardById", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Guid) }, null);
        if (method == null)
            return null;

        return method.Invoke(staticData, new object[] { templateId });
    }
    private bool EnsureInstantiateApi(AssetLoader loader)
    {
        if (_instantiateCardMethod != null && _spawnSection != null)
            return true;

        _instantiateCardMethod = loader
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, "InstantiateCardAsync", StringComparison.Ordinal))
                    return false;
                var ps = m.GetParameters();
                return ps.Length == 3
                    && ps[0].ParameterType == typeof(Card)
                    && ps[1].ParameterType == typeof(GameObject)
                    && ps[2].ParameterType.IsEnum;
            });

        if (_instantiateCardMethod == null)
            return false;

        var sectionType = _instantiateCardMethod.GetParameters()[2].ParameterType;
        if (!sectionType.IsEnum)
            return false;

        var names = Enum.GetNames(sectionType);
        if (names.Contains("Opponent"))
            _spawnSection = Enum.Parse(sectionType, "Opponent");
        else if (names.Contains("Storage"))
            _spawnSection = Enum.Parse(sectionType, "Storage");
        else
            _spawnSection = Enum.ToObject(sectionType, 0);

        return _spawnSection != null;
    }

    private async Task<GameObject> InstantiateCardForShowcaseAsync(
        AssetLoader loader,
        Card card,
        GameObject parent
    )
    {
        if (_instantiateCardMethod == null || _spawnSection == null)
            return null;

        var taskObj = _instantiateCardMethod.Invoke(loader, new object[] { card, parent, _spawnSection });
        if (taskObj is Task<GameObject> task)
            return await task;

        if (taskObj is Task nonGenericTask)
        {
            await nonGenericTask;
            var resultProp = nonGenericTask.GetType().GetProperty("Result");
            return resultProp?.GetValue(nonGenericTask) as GameObject;
        }

        return null;
    }

    private GameObject CreateShowcaseAnchor(int index)
    {
        var slot = new GameObject($"BazaarPP_ShowcaseAnchor_{index}");
        _showcaseAnchors.Add(slot);
        slot.transform.localScale = Vector3.one * 0.6f;
        return slot;
    }

    private void ApplyFixedAdjacentLayout()
    {
        if (_showcaseAnchors.Count == 0)
            return;

        if (!TryGetShowcaseRegion(out var leftEdge, out var rotation))
            return;

        var right = (rotation * Vector3.right).normalized;
        var gap = 0.0f;
        var edge = 0f;

        foreach (var anchor in _showcaseAnchors)
        {
            if (anchor == null || anchor.transform.childCount == 0)
                continue;

            var cardObj = anchor.transform.GetChild(0).gameObject;
            var half = GetCardHalfWidth(cardObj, right);
            if (half <= 0.001f)
                half = 0.18f;

            var center = edge + half;
            anchor.transform.position = leftEdge + right * center;
            anchor.transform.rotation = rotation;

            edge = center + half + gap;
        }
    }

    private bool TryGetShowcaseRegion(out Vector3 leftEdge, out Quaternion rotation)
    {

        var handControllers = new List<CardController>();
        foreach (var handCard in _handCardsForLayout)
        {
            var ctrl = handCard == null ? null : Data.CardAndSkillLookup.GetCardController(handCard);
            if (ctrl != null)
                handControllers.Add(ctrl);
        }

        if (handControllers.Count > 0)
        {
            handControllers.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            var rightMost = handControllers[handControllers.Count - 1];
            leftEdge = rightMost.transform.position + rightMost.transform.right * 0.42f;
            rotation = rightMost.transform.rotation;
            return true;
        }

        leftEdge = Vector3.zero;
        rotation = Quaternion.identity;

        var cam = Camera.main;
        if (cam == null)
            return false;

        leftEdge = cam.ViewportToWorldPoint(new Vector3(0.70f, 0.22f, 7.2f));
        rotation = Quaternion.LookRotation((cam.transform.position - leftEdge).normalized, Vector3.up)
            * Quaternion.Euler(0f, 180f, 0f);
        return true;
    }

    private static float GetCardHalfWidth(GameObject obj, Vector3 axis)
    {
        if (obj == null)
            return 0f;

        var n = axis.normalized;
        var cardController = obj.GetComponent<CardController>();
        var box = cardController?.BoxCollider != null
            ? cardController.BoxCollider
            : obj.GetComponentInChildren<BoxCollider>(includeInactive: true);

        if (box != null)
        {
            var b = box.bounds;
            var e = b.extents;
            return Mathf.Abs(n.x) * e.x + Mathf.Abs(n.y) * e.y + Mathf.Abs(n.z) * e.z;
        }

        var renderers = obj.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers == null || renderers.Length == 0)
            return 0f;

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var ext = bounds.extents;
        return Mathf.Abs(n.x) * ext.x + Mathf.Abs(n.y) * ext.y + Mathf.Abs(n.z) * ext.z;
    }

    private void HideShowcaseEntities()
    {
        foreach (var entity in _showcaseEntities)
        {
            if (entity != null)
                Destroy(entity);
        }
        _showcaseEntities.Clear();

        foreach (var anchor in _showcaseAnchors)
        {
            if (anchor != null)
                Destroy(anchor);
        }
        _showcaseAnchors.Clear();
    }

    private void OnDestroy()
    {
        HideShowcaseEntities();
    }
}










