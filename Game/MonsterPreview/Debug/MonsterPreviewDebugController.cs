#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewDebugController : MonoBehaviour
{
    private const string DefaultAnchorPath = "Game/=== BoardAnchor ===/BoardBase(Clone)/PlayerPortrait";
    private const float RefreshInterval = 0.2f;
    private const float DefaultMoveStep = 0.5f;
    private const float FineMoveStep = 0.1f;
    private const float RotationStep = 5f;
    private const float SizeStep = 0.25f;
    private const float ThicknessStep = 0.02f;
    private const float ScaleStep = 0.05f;

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
    private readonly PreviewBoardDebugOptions _debugOptions = new PreviewBoardDebugOptions
    {
        Enabled = true,
        ShowAnchorPoint = true,
        ShowItemSlots = true,
        ShowSkillSlots = true,
        ShowCardBounds = true,
        ShowLabels = true,
    };

    private void Awake()
    {
        _overlayController = GetComponent<MonsterPreviewController>();
        _anchorStrategy = new FixedAnchorStrategy();
        _presentation = new PreviewBoardPresentation();

        if (_overlayController != null)
        {
            _overlayController.SetAnchorStrategy(_anchorStrategy);
            _overlayController.SetPresentation(ClonePresentation(_presentation));
            _overlayController.SetDebugOptions(_debugOptions);
            _overlayController.SetVisible(false);
        }

        SeedAnchorFromBoardPortrait();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || _overlayController == null)
            return;

        if (keyboard.f3Key.wasPressedThisFrame)
        {
            if (!_anchorSeeded)
                SeedAnchorFromBoardPortrait();

            _overlayController.SetVisible(!_overlayController.Visible);
            if (_overlayController.Visible)
                SyncPreviewData();
            BppLog.Debug("MonsterPreviewDebugController", $"Preview visible={_overlayController.Visible}");
        }

        if (keyboard.f4Key.wasPressedThisFrame)
        {
            _useMonsterDatabase = !_useMonsterDatabase;
            _lastCardSignature = string.Empty;
            _lastSkillSignature = string.Empty;
            SyncPreviewData();
            BppLog.Debug(
                "MonsterPreviewDebugController",
                $"Preview data source={(_useMonsterDatabase ? "monster_db" : "player_hand")}"
            );
        }

        if (!_overlayController.Visible)
            return;

        HandleAnchorControls(keyboard);
        HandleLayoutControls(keyboard);

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            _nextRefreshTime = Time.unscaledTime + RefreshInterval;
            SyncPreviewData();
        }
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

    private void HandleAnchorControls(Keyboard keyboard)
    {
        var moved = false;
        var moveStep = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
            ? FineMoveStep
            : DefaultMoveStep;

        var position = _anchorStrategy.Position;
        if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            position.x -= moveStep;
            moved = true;
        }
        if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            position.x += moveStep;
            moved = true;
        }
        if (keyboard.upArrowKey.wasPressedThisFrame)
        {
            position.z += moveStep;
            moved = true;
        }
        if (keyboard.downArrowKey.wasPressedThisFrame)
        {
            position.z -= moveStep;
            moved = true;
        }
        if (keyboard.pageUpKey.wasPressedThisFrame)
        {
            position.y += moveStep;
            moved = true;
        }
        if (keyboard.pageDownKey.wasPressedThisFrame)
        {
            position.y -= moveStep;
            moved = true;
        }
        if (moved)
            _anchorStrategy.Position = position;

        var rotation = _anchorStrategy.Rotation;
        if (keyboard.commaKey.wasPressedThisFrame)
        {
            rotation = Quaternion.Euler(0f, -RotationStep, 0f) * rotation;
            moved = true;
        }
        if (keyboard.periodKey.wasPressedThisFrame)
        {
            rotation = Quaternion.Euler(0f, RotationStep, 0f) * rotation;
            moved = true;
        }
        if (rotation != _anchorStrategy.Rotation)
            _anchorStrategy.Rotation = rotation;

        if (keyboard.rKey.wasPressedThisFrame)
        {
            SeedAnchorFromBoardPortrait();
            moved = true;
        }

        if (moved)
        {
            BppLog.Debug(
                "MonsterPreviewDebugController",
                $"Anchor pos={_anchorStrategy.Position} rot={_anchorStrategy.Rotation.eulerAngles}"
            );
        }
    }

    private void HandleLayoutControls(Keyboard keyboard)
    {
        var layoutChanged = false;
        var boardSize = _presentation.BoardSize;
        var cardSpacing = _presentation.CardSpacing;

        if (keyboard.digit1Key.wasPressedThisFrame)
        {
            if (!DebugPanel.IsVisible)
            {
                boardSize.x = Mathf.Max(1f, boardSize.x - SizeStep);
                layoutChanged = true;
            }
        }
        if (keyboard.digit2Key.wasPressedThisFrame)
        {
            if (!DebugPanel.IsVisible)
            {
                boardSize.x += SizeStep;
                layoutChanged = true;
            }
        }
        if (keyboard.digit3Key.wasPressedThisFrame)
        {
            if (!DebugPanel.IsVisible)
            {
                boardSize.y = Mathf.Max(1f, boardSize.y - SizeStep);
                layoutChanged = true;
            }
        }
        if (keyboard.digit4Key.wasPressedThisFrame)
        {
            if (!DebugPanel.IsVisible)
            {
                boardSize.y += SizeStep;
                layoutChanged = true;
            }
        }
        if (keyboard.digit5Key.wasPressedThisFrame)
        {
            cardSpacing.x = Mathf.Max(0.2f, cardSpacing.x - SizeStep);
            layoutChanged = true;
        }
        if (keyboard.digit6Key.wasPressedThisFrame)
        {
            cardSpacing.x += SizeStep;
            layoutChanged = true;
        }
        if (keyboard.minusKey.wasPressedThisFrame)
        {
            _presentation.CardScale = Vector3.one
                * Mathf.Max(0.1f, _presentation.CardScale.x - ScaleStep);
            layoutChanged = true;
        }
        if (keyboard.equalsKey.wasPressedThisFrame)
        {
            _presentation.CardScale = Vector3.one * (_presentation.CardScale.x + ScaleStep);
            layoutChanged = true;
        }
        if (keyboard.kKey.wasPressedThisFrame)
        {
            _presentation.BoardThickness = Mathf.Max(0.01f, _presentation.BoardThickness - ThicknessStep);
            layoutChanged = true;
        }
        if (keyboard.lKey.wasPressedThisFrame)
        {
            _presentation.BoardThickness += ThicknessStep;
            layoutChanged = true;
        }
        if (keyboard.semicolonKey.wasPressedThisFrame)
        {
            _presentation.BorderThickness = Mathf.Max(0.01f, _presentation.BorderThickness - ThicknessStep);
            layoutChanged = true;
        }
        if (keyboard.quoteKey.wasPressedThisFrame)
        {
            _presentation.BorderThickness += ThicknessStep;
            layoutChanged = true;
        }
        if (keyboard.nKey.wasPressedThisFrame)
        {
            _presentation.BorderHeight = Mathf.Max(0.01f, _presentation.BorderHeight - ThicknessStep);
            layoutChanged = true;
        }
        if (keyboard.mKey.wasPressedThisFrame)
        {
            _presentation.BorderHeight += ThicknessStep;
            layoutChanged = true;
        }

        if (!layoutChanged)
            return;

        _presentation.BoardSize = boardSize;
        _presentation.CardSpacing = cardSpacing;
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

    private void SeedAnchorFromBoardPortrait()
    {
        var anchorTransform = FindDefaultAnchorTransform();
        if (anchorTransform != null)
        {
            _anchorStrategy.Position = anchorTransform.position;
            _anchorStrategy.Rotation = anchorTransform.rotation;
            _anchorSeeded = true;
            BppLog.Debug(
                "MonsterPreviewDebugController",
                $"Seeded anchor from {DefaultAnchorPath}: {_anchorStrategy.Position}"
            );
            return;
        }

        var camera = Camera.main;
        if (camera == null)
            return;

        _anchorStrategy.Position = camera.transform.position + camera.transform.forward * 12f;
        _anchorStrategy.Rotation = Quaternion.identity;
        _anchorSeeded = true;
        BppLog.Warn(
            "MonsterPreviewDebugController",
            $"Default anchor '{DefaultAnchorPath}' not found, fell back to camera seed: {_anchorStrategy.Position}"
        );
    }

    private static Transform FindDefaultAnchorTransform()
    {
        return GameObject.Find(DefaultAnchorPath)?.transform;
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
}
