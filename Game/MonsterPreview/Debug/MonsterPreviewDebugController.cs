#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewDebugController : MonoBehaviour
{
    private const float RefreshInterval = 0.2f;
    private const float MoveStep = 0.5f;
    private const float RotationStep = 5f;
    private const float SizeStep = 0.25f;
    private const float ThicknessStep = 0.02f;
    private const float ScaleStep = 0.05f;
    private const float WidgetWidth = 92f;
    private const float WidgetButtonHeight = 30f;
    private const float PanelWidth = 260f;
    private const float PanelPadding = 12f;
    private static readonly Color WidgetButtonColor = new Color(0.10f, 0.13f, 0.18f, 0.96f);
    private static readonly Color WidgetButtonActiveColor = new Color(0.16f, 0.34f, 0.29f, 0.98f);
    private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.13f, 0.96f);
    private static readonly Color ActionButtonColor = new Color(0.15f, 0.20f, 0.28f, 1f);
    private static readonly Color StepperButtonColor = new Color(0.18f, 0.24f, 0.32f, 1f);

    private static readonly GUIStyle WidgetButtonStyle = new GUIStyle();
    private static readonly GUIStyle PanelStyle = new GUIStyle();
    private static readonly GUIStyle TitleStyle = new GUIStyle();
    private static readonly GUIStyle SubtitleStyle = new GUIStyle();
    private static readonly GUIStyle SectionStyle = new GUIStyle();
    private static readonly GUIStyle StepperButtonStyle = new GUIStyle();
    private static readonly GUIStyle ActionButtonStyle = new GUIStyle();
    private static readonly GUIStyle RowLabelStyle = new GUIStyle();
    private static readonly GUIStyle ValueStyle = new GUIStyle();
    private static bool _stylesInitialized;

    internal struct DebugState
    {
        public string DataSource;
        public string EncounterId;
        public string MonsterTitle;
        public bool Visible;
        public Vector3 AnchorPosition;
        public Vector3 AnchorRotationEuler;
        public Vector3 LocalOffset;
        public Vector2 BoardSize;
        public float CardSpacingX;
        public float CardScale;
        public float BoardThickness;
        public float BorderThickness;
        public float BorderHeight;
    }

    private MonsterPreviewController _overlayController;
    private FixedAnchorStrategy _anchorStrategy;
    private PreviewBoardPresentation _presentation;
    private string _lastCardSignature = string.Empty;
    private string _lastSkillSignature = string.Empty;
    private float _nextRefreshTime;
    private bool _anchorSeeded;
    private bool _useMonsterDatabase = true;
    private const string DefaultEncounterId = "4a4542cd";
    private string _activeEncounterId = string.Empty;
    private string _activeMonsterTitle = string.Empty;
    private bool _widgetExpanded;
    private readonly PreviewBoardDebugOptions _debugOptions = new PreviewBoardDebugOptions
    {
        Enabled = true,
        ShowAnchorPoint = true,
        ShowItemSlots = true,
        ShowSkillSlots = true,
        ShowCardBounds = true,
        ShowLabels = true,
    };
    private MonsterPreviewDebugTuner _tuner;

    private void Awake()
    {
        _overlayController = GetComponent<MonsterPreviewController>();
        _anchorStrategy = new FixedAnchorStrategy();
        _presentation = MonsterPreviewDefaults.CreateDebugPresentation();
        _tuner = new MonsterPreviewDebugTuner(_anchorStrategy, _presentation);

        if (_overlayController != null)
        {
            _overlayController.SetAnchorStrategy(_anchorStrategy);
            _overlayController.SetPresentation(ClonePresentation(_presentation));
            _overlayController.SetDebugOptions(_debugOptions);
            _overlayController.SetVisible(false);
        }

        SeedAnchorToDefaultPose();
    }

    private void Update()
    {
        if (_overlayController == null || !_overlayController.Visible)
            return;

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.unscaledTime + RefreshInterval;
            SyncPreviewData();
        }
    }

    private void OnGUI()
    {
        if (_overlayController == null)
            return;

        InitStyles();

        var buttonRect = new Rect(
            Screen.width - WidgetWidth - 16f,
            16f,
            WidgetWidth,
            WidgetButtonHeight
        );
        var buttonLabel = _overlayController.Visible ? "Preview ON" : "Preview";
        if (
            DrawTintedButton(
                buttonRect,
                buttonLabel,
                WidgetButtonStyle,
                _overlayController.Visible ? WidgetButtonActiveColor : WidgetButtonColor
            )
        )
            _widgetExpanded = !_widgetExpanded;

        if (!_widgetExpanded)
            return;

        var panelRect = new Rect(
            Screen.width - PanelWidth - 16f,
            buttonRect.yMax + 8f,
            PanelWidth,
            376f
        );
        DrawTintedBox(panelRect, PanelStyle, PanelColor);

        GUILayout.BeginArea(
            new Rect(
                panelRect.x + PanelPadding,
                panelRect.y + PanelPadding,
                panelRect.width - (PanelPadding * 2f),
                panelRect.height - (PanelPadding * 2f)
            )
        );

        DrawWidgetHeader();
        GUILayout.Space(10f);
        DrawActionButtons();
        GUILayout.Space(10f);
        DrawAnchorSection();
        GUILayout.Space(10f);
        DrawLayoutSection();
        GUILayout.EndArea();
    }

    public bool TryGetDebugState(out DebugState state)
    {
        if (_overlayController == null || _anchorStrategy == null || _presentation == null)
        {
            state = default;
            return false;
        }

        state = new DebugState
        {
            DataSource = _useMonsterDatabase ? "monster_db" : "player_hand",
            EncounterId = string.IsNullOrEmpty(_activeEncounterId) ? "-" : _activeEncounterId,
            MonsterTitle = string.IsNullOrEmpty(_activeMonsterTitle) ? "-" : _activeMonsterTitle,
            Visible = _overlayController.Visible,
            AnchorPosition = _anchorStrategy.Position,
            AnchorRotationEuler = _anchorStrategy.Rotation.eulerAngles,
            LocalOffset = _presentation.LocalOffset,
            BoardSize = _presentation.BoardSize,
            CardSpacingX = _presentation.CardSpacing.x,
            CardScale = _presentation.CardScale.x,
            BoardThickness = _presentation.BoardThickness,
            BorderThickness = _presentation.BorderThickness,
            BorderHeight = _presentation.BorderHeight,
        };
        return true;
    }

    private void DrawWidgetHeader()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Monster Preview", TitleStyle);
        GUILayout.FlexibleSpace();
        if (
            DrawTintedButton(
                "x",
                StepperButtonStyle,
                StepperButtonColor,
                GUILayout.Width(26f),
                GUILayout.Height(22f)
            )
        )
            _widgetExpanded = false;
        GUILayout.EndHorizontal();

        GUILayout.Label(
            $"{(_overlayController.Visible ? "Visible" : "Hidden")}  |  {(_useMonsterDatabase ? "Monster DB" : "Player Hand")}",
            SubtitleStyle
        );
        GUILayout.Label(
            $"{(string.IsNullOrEmpty(_activeMonsterTitle) ? "No target" : _activeMonsterTitle)}",
            SubtitleStyle
        );
    }

    private void DrawActionButtons()
    {
        GUILayout.BeginHorizontal();
        if (
            DrawTintedButton(
                _overlayController.Visible ? "Hide" : "Show",
                ActionButtonStyle,
                ActionButtonColor
            )
        )
            TogglePreview();
        if (DrawTintedButton("Source", ActionButtonStyle, ActionButtonColor))
            ToggleDataSource();
        if (DrawTintedButton("Sync", ActionButtonStyle, ActionButtonColor))
            SyncPreviewData();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (DrawTintedButton("Reset Anchor", ActionButtonStyle, ActionButtonColor))
            ResetAnchor();
        GUILayout.EndHorizontal();
    }

    private void DrawAnchorSection()
    {
        GUILayout.Label("ANCHOR", SectionStyle);
        DrawStepperRow(
            "X",
            _anchorStrategy.Position.x.ToString("F1"),
            () => MoveAnchor(new Vector3(-MoveStep, 0f, 0f)),
            () => MoveAnchor(new Vector3(MoveStep, 0f, 0f))
        );
        DrawStepperRow(
            "Y",
            _anchorStrategy.Position.y.ToString("F1"),
            () => MoveAnchor(new Vector3(0f, -MoveStep, 0f)),
            () => MoveAnchor(new Vector3(0f, MoveStep, 0f))
        );
        DrawStepperRow(
            "Z",
            _anchorStrategy.Position.z.ToString("F1"),
            () => MoveAnchor(new Vector3(0f, 0f, -MoveStep)),
            () => MoveAnchor(new Vector3(0f, 0f, MoveStep))
        );
        DrawStepperRow(
            "Yaw",
            _anchorStrategy.Rotation.eulerAngles.y.ToString("F0"),
            () => RotateAnchor(-RotationStep),
            () => RotateAnchor(RotationStep)
        );
    }

    private void DrawLayoutSection()
    {
        GUILayout.Label("LAYOUT", SectionStyle);
        DrawStepperRow(
            "Width",
            _presentation.BoardSize.x.ToString("F2"),
            () => AdjustLayout(() => _tuner.AdjustBoardWidth(-SizeStep)),
            () => AdjustLayout(() => _tuner.AdjustBoardWidth(SizeStep))
        );
        DrawStepperRow(
            "Height",
            _presentation.BoardSize.y.ToString("F2"),
            () => AdjustLayout(() => _tuner.AdjustBoardHeight(-SizeStep)),
            () => AdjustLayout(() => _tuner.AdjustBoardHeight(SizeStep))
        );
        DrawStepperRow(
            "Gap",
            _presentation.CardSpacing.x.ToString("F2"),
            () => AdjustLayout(() => _tuner.AdjustSpacingX(-SizeStep)),
            () => AdjustLayout(() => _tuner.AdjustSpacingX(SizeStep))
        );
        DrawStepperRow(
            "Scale",
            _presentation.CardScale.x.ToString("F2"),
            () => AdjustLayout(() => _tuner.AdjustCardScale(-ScaleStep)),
            () => AdjustLayout(() => _tuner.AdjustCardScale(ScaleStep))
        );
        DrawStepperRow(
            "Plate",
            _presentation.BoardThickness.ToString("F2"),
            () => AdjustLayout(() => _tuner.AdjustBoardThickness(-ThicknessStep)),
            () => AdjustLayout(() => _tuner.AdjustBoardThickness(ThicknessStep))
        );
        DrawStepperRow(
            "Border",
            _presentation.BorderThickness.ToString("F2"),
            () => AdjustLayout(() => _tuner.AdjustBorderThickness(-ThicknessStep)),
            () => AdjustLayout(() => _tuner.AdjustBorderThickness(ThicknessStep))
        );
        DrawStepperRow(
            "Lip",
            _presentation.BorderHeight.ToString("F2"),
            () => AdjustLayout(() => _tuner.AdjustBorderHeight(-ThicknessStep)),
            () => AdjustLayout(() => _tuner.AdjustBorderHeight(ThicknessStep))
        );
    }

    private void DrawStepperRow(string label, string value, Action decrement, Action increment)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, RowLabelStyle, GUILayout.Width(50f));
        if (
            DrawTintedButton(
                "-",
                StepperButtonStyle,
                StepperButtonColor,
                GUILayout.Width(28f),
                GUILayout.Height(24f)
            )
        )
            decrement();
        GUILayout.Label(value, ValueStyle, GUILayout.Width(58f));
        if (
            DrawTintedButton(
                "+",
                StepperButtonStyle,
                StepperButtonColor,
                GUILayout.Width(28f),
                GUILayout.Height(24f)
            )
        )
            increment();
        GUILayout.EndHorizontal();
    }

    private void TogglePreview()
    {
        if (!_anchorSeeded)
            SeedAnchorToDefaultPose();

        _overlayController.SetVisible(!_overlayController.Visible);
        if (_overlayController.Visible)
        {
            _nextRefreshTime = Time.unscaledTime + RefreshInterval;
            SyncPreviewData();
        }

        BppLog.Debug("MonsterPreviewDebugController", $"Preview visible={_overlayController.Visible}");
    }

    private void ToggleDataSource()
    {
        _useMonsterDatabase = !_useMonsterDatabase;
        _lastCardSignature = string.Empty;
        _lastSkillSignature = string.Empty;
        if (_overlayController.Visible)
            SyncPreviewData();
        BppLog.Debug(
            "MonsterPreviewDebugController",
            $"Preview data source={(_useMonsterDatabase ? "monster_db" : "player_hand")}"
        );
    }

    private void ResetAnchor()
    {
        SeedAnchorToDefaultPose();
        BppLog.Debug(
            "MonsterPreviewDebugController",
            $"Anchor pos={_anchorStrategy.Position} rot={_anchorStrategy.Rotation.eulerAngles}"
        );
    }

    private void MoveAnchor(Vector3 delta)
    {
        _tuner.MoveAnchor(delta);
        BppLog.Debug(
            "MonsterPreviewDebugController",
            $"Anchor pos={_anchorStrategy.Position} rot={_anchorStrategy.Rotation.eulerAngles}"
        );
    }

    private void RotateAnchor(float delta)
    {
        _tuner.RotateAnchorY(delta);
        BppLog.Debug(
            "MonsterPreviewDebugController",
            $"Anchor pos={_anchorStrategy.Position} rot={_anchorStrategy.Rotation.eulerAngles}"
        );
    }

    private void AdjustLayout(Action adjustment)
    {
        adjustment();
        ApplyLayout();
        BppLog.Debug(
            "MonsterPreviewDebugController",
            $"Layout size={_presentation.BoardSize} offset={_presentation.LocalOffset} spacingX={_presentation.CardSpacing.x:F2} scale={_presentation.CardScale.x:F2} boardT={_presentation.BoardThickness:F2} borderT={_presentation.BorderThickness:F2} borderH={_presentation.BorderHeight:F2}"
        );
    }

    private void SyncPreviewData()
    {
        if (_useMonsterDatabase)
        {
            SyncCardsFromMonsterDatabase();
            return;
        }

        SyncCardsFromHand();
    }

    private void SyncCardsFromMonsterDatabase()
    {
        _activeEncounterId = DefaultEncounterId;

        if (!MonsterDatabase.TryGetByEncounterId(DefaultEncounterId, out var monster))
        {
            _activeMonsterTitle = string.Empty;
            BppLog.Warn("MonsterPreviewDebugController", $"Monster DB miss encounterId={DefaultEncounterId}");
            _overlayController.SetCards(new List<PreviewCardSpec>());
            _overlayController.SetSkillCards(new List<PreviewCardSpec>());
            return;
        }

        _activeMonsterTitle = monster.Title;
        var previewModel = MonsterDatabasePreviewDataSource.BuildModel(monster, "monster_db");
        var specs = previewModel.ItemCards.ToList();
        var skillSpecs = previewModel.SkillCards.ToList();
        BppLog.Debug(
            "MonsterPreviewDebugController",
            $"Monster DB hit encounterId={DefaultEncounterId} key={monster.EncounterKey} shortId={monster.EncounterShortId} title={monster.Title} boardCards={monster.BoardCards.Count} skillCards={monster.Skills.Count} previewCards={specs.Count}"
        );
        var signature = BuildSignature(specs);
        var skillSignature = BuildSignature(skillSpecs);
        if (signature == _lastCardSignature)
        {
            if (skillSignature == _lastSkillSignature)
            {
                BppLog.Debug(
                    "MonsterPreviewDebugController",
                    $"Monster preview signature unchanged encounterId={DefaultEncounterId}"
                );
                return;
            }
        }

        _lastCardSignature = signature;
        _lastSkillSignature = skillSignature;
        _overlayController.SetCards(specs);
        _overlayController.SetSkillCards(skillSpecs);
    }

    private void SyncCardsFromHand()
    {
        _activeEncounterId = string.Empty;
        _activeMonsterTitle = string.Empty;

        var handCards = GameDataReader.GetItemsAsCards(Data.Run?.Player?.Hand);
        var specs = BuildCardSpecs(handCards);
        var skillSpecs = BuildSkillSpecs(Data.Run?.Player?.Skills);
        var signature = BuildSignature(specs);
        var skillSignature = BuildSignature(skillSpecs);
        if (signature == _lastCardSignature)
        {
            if (skillSignature == _lastSkillSignature)
            {
                BppLog.Debug("MonsterPreviewDebugController", "Player hand preview signature unchanged");
                return;
            }
        }

        _lastCardSignature = signature;
        _lastSkillSignature = skillSignature;
        BppLog.Debug(
            "MonsterPreviewDebugController",
            $"Player hand preview cards={specs.Count} skills={skillSpecs.Count}"
        );
        _overlayController.SetCards(specs);
        _overlayController.SetSkillCards(skillSpecs);
    }

    private void SeedAnchorToDefaultPose()
    {
        _anchorStrategy.Position = MonsterPreviewDefaults.DefaultAnchorPose.Position;
        _anchorStrategy.Rotation = MonsterPreviewDefaults.DefaultAnchorPose.Rotation;
        _anchorSeeded = true;
        BppLog.Debug(
            "MonsterPreviewDebugController",
            $"Seeded anchor to fixed preview pose: {_anchorStrategy.Position}"
        );
    }

    private void ApplyLayout()
    {
        _overlayController?.SetPresentation(ClonePresentation(_presentation));
    }

    private static List<PreviewCardSpec> BuildCardSpecs(List<Card> cards)
    {
        var specs = new List<PreviewCardSpec>();
        if (cards == null)
            return specs;

        foreach (var card in cards)
        {
            if (card == null || card.Type != ECardType.Item)
                continue;

            specs.Add(
                new PreviewCardSpec
                {
                    TemplateId = card.TemplateId.ToString(),
                    Tier = (int)card.Tier,
                    SourceName = card.Template?.InternalName ?? string.Empty,
                    Enchant = (card as ItemCard)?.Enchantment?.ToString() ?? "None",
                    Size = Math.Max(1, (int)card.Size),
                    Attributes = card.Attributes?.ToDictionary(kv => (int)kv.Key, kv => kv.Value)
                        ?? new Dictionary<int, int>(),
                }
            );
        }

        return specs;
    }

    private static List<PreviewCardSpec> BuildSkillSpecs(IEnumerable<SkillCard> skills)
    {
        var specs = new List<PreviewCardSpec>();
        if (skills == null)
            return specs;

        foreach (var skill in skills)
        {
            if (skill == null || skill.Type != ECardType.Skill)
                continue;

            specs.Add(
                new PreviewCardSpec
                {
                    TemplateId = skill.TemplateId.ToString(),
                    Tier = (int)skill.Tier,
                    SourceName = skill.Template?.InternalName ?? string.Empty,
                    Size = 1,
                    Enchant = "None",
                    Attributes = skill.Attributes?.ToDictionary(kv => (int)kv.Key, kv => kv.Value)
                        ?? new Dictionary<int, int>(),
                }
            );
        }

        return specs;
    }

    private static string BuildSignature(IReadOnlyList<PreviewCardSpec> cards)
    {
        return string.Join(
            "|",
            cards.Select(card =>
                string.Join(
                    ";",
                    card.TemplateId,
                    card.SourceName ?? string.Empty,
                    card.Tier.ToString(),
                    card.Size.ToString(),
                    card.Enchant ?? "None",
                    string.Join(
                        ",",
                        card.Attributes
                            .OrderBy(kv => kv.Key)
                            .Select(kv => $"{kv.Key}:{kv.Value}")
                    )
                )
            )
        );
    }

    private static PreviewBoardPresentation ClonePresentation(PreviewBoardPresentation presentation)
    {
        return new PreviewBoardPresentation
        {
            Visible = presentation.Visible,
            DebugEnabled = presentation.DebugEnabled,
            LocalOffset = presentation.LocalOffset,
            CardSpacing = presentation.CardSpacing,
            CardScale = presentation.CardScale,
            BoardSize = presentation.BoardSize,
            BoardThickness = presentation.BoardThickness,
            BorderThickness = presentation.BorderThickness,
            BorderHeight = presentation.BorderHeight,
        };
    }

    private static void InitStyles()
    {
        if (_stylesInitialized)
            return;

        WidgetButtonStyle.normal.background = Texture2D.whiteTexture;
        WidgetButtonStyle.normal.textColor = new Color(0.95f, 0.97f, 1f);
        WidgetButtonStyle.fontSize = 12;
        WidgetButtonStyle.fontStyle = FontStyle.Bold;
        WidgetButtonStyle.alignment = TextAnchor.MiddleCenter;
        WidgetButtonStyle.padding = new RectOffset(10, 10, 6, 6);
        WidgetButtonStyle.border = new RectOffset(10, 10, 10, 10);

        PanelStyle.normal.background = Texture2D.whiteTexture;
        PanelStyle.border = new RectOffset(14, 14, 14, 14);

        TitleStyle.normal.textColor = new Color(0.96f, 0.97f, 0.99f);
        TitleStyle.fontSize = 14;
        TitleStyle.fontStyle = FontStyle.Bold;

        SubtitleStyle.normal.textColor = new Color(0.70f, 0.76f, 0.84f);
        SubtitleStyle.fontSize = 11;

        SectionStyle.normal.textColor = new Color(0.82f, 0.87f, 0.94f);
        SectionStyle.fontSize = 11;
        SectionStyle.fontStyle = FontStyle.Bold;

        StepperButtonStyle.normal.background = Texture2D.whiteTexture;
        StepperButtonStyle.normal.textColor = new Color(0.94f, 0.96f, 0.99f);
        StepperButtonStyle.fontSize = 12;
        StepperButtonStyle.fontStyle = FontStyle.Bold;
        StepperButtonStyle.alignment = TextAnchor.MiddleCenter;
        StepperButtonStyle.margin = new RectOffset(0, 4, 2, 2);

        ActionButtonStyle.normal.background = Texture2D.whiteTexture;
        ActionButtonStyle.normal.textColor = new Color(0.94f, 0.96f, 0.99f);
        ActionButtonStyle.fontSize = 11;
        ActionButtonStyle.fontStyle = FontStyle.Bold;
        ActionButtonStyle.alignment = TextAnchor.MiddleCenter;
        ActionButtonStyle.padding = new RectOffset(8, 8, 6, 6);
        ActionButtonStyle.margin = new RectOffset(0, 6, 0, 4);

        RowLabelStyle.normal.textColor = new Color(0.84f, 0.89f, 0.96f);
        RowLabelStyle.fontSize = 11;
        RowLabelStyle.alignment = TextAnchor.MiddleLeft;

        ValueStyle.normal.textColor = new Color(0.98f, 0.98f, 0.99f);
        ValueStyle.fontSize = 11;
        ValueStyle.alignment = TextAnchor.MiddleCenter;

        _stylesInitialized = true;
    }

    private static void DrawTintedBox(Rect rect, GUIStyle style, Color color)
    {
        var previousColor = GUI.color;
        GUI.color = color;
        GUI.Box(rect, GUIContent.none, style);
        GUI.color = previousColor;
    }

    private static bool DrawTintedButton(
        Rect rect,
        string label,
        GUIStyle style,
        Color color
    )
    {
        var previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = color;
        var clicked = GUI.Button(rect, label, style);
        GUI.backgroundColor = previousBackground;
        return clicked;
    }

    private static bool DrawTintedButton(
        string label,
        GUIStyle style,
        Color color,
        params GUILayoutOption[] options
    )
    {
        var previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = color;
        var clicked = GUILayout.Button(label, style, options);
        GUI.backgroundColor = previousBackground;
        return clicked;
    }
}
