#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using TheBazaar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal sealed class MonsterLockShowcaseRuntime : MonoBehaviour
{
    private readonly MonsterLockShowcaseController _controller = new MonsterLockShowcaseController();
    private readonly FixedAnchorStrategy _anchorStrategy = new FixedAnchorStrategy(
        MonsterPreviewDefaults.DefaultAnchorPose
    );
    private readonly PreviewBoardPresentation _presentation =
        MonsterPreviewDefaults.CreateShowcasePresentation();
    private readonly MonsterPreviewDebugTuner _tuner;

    private MonsterPreviewController _overlayController;
    private Card _lockedCard;
    private bool _closeOnNextClickArmed;
    public static MonsterLockShowcaseRuntime Instance { get; private set; }

    public bool IsPreviewActive => _lockedCard != null;

    public FixedAnchorStrategy AnchorStrategy => _anchorStrategy;

    public PreviewBoardPresentation Presentation => _presentation;

    public MonsterPreviewDebugTuner DebugTuner => _tuner;

    public MonsterLockShowcaseRuntime()
    {
        _tuner = new MonsterPreviewDebugTuner(_anchorStrategy, _presentation);
    }

    private void Awake()
    {
        Instance = this;
        _overlayController = GetComponent<MonsterPreviewController>();
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Awake overlayControllerFound={_overlayController != null}"
        );
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private void Update()
    {
        if (!IsPreviewActive || !_closeOnNextClickArmed)
            return;

        var mouse = Mouse.current;
        if (mouse == null)
            return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            TryConsumeNextClickToClosePreview(
                isLeftClick: true,
                isRightClick: false,
                reason: "next global left click"
            );
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            TryConsumeNextClickToClosePreview(
                isLeftClick: false,
                isRightClick: true,
                reason: "next global right click"
            );
        }
    }

    public bool HandleLockToggle(Card card)
    {
        if (_overlayController == null)
            return false;

        if (
            TryConsumeNextClickToClosePreview(
                isLeftClick: false,
                isRightClick: true,
                reason: "next right click"
            )
        )
        {
            return true;
        }

        if (IsPreviewActive)
        {
            HideOverlay("right click toggle");
            return true;
        }

        var isShowcaseCard = card != null && IsShowcaseCard(card);
        var isMonsterCard = card != null && IsMonsterSourceCard(card);
        if (!_controller.ShouldShowForLock(card?.TemplateId, isShowcaseCard, isMonsterCard))
            return false;

        if (!TryBuildPreview(card, out var cards, out var skillCards, out var source))
            return false;

        _lockedCard = card;
        _anchorStrategy.SetPose(MonsterPreviewDefaults.DefaultAnchorPose);
        CopyPresentation(MonsterPreviewDefaults.CreateShowcasePresentation(), _presentation);
        _overlayController.ShowRequest(
            CreateShowcaseRequest(
                cards,
                skillCards,
                card?.Template?.InternalName ?? source,
                source
            )
        );
        _closeOnNextClickArmed = true;
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Activated BPP showcase mode source={source} card={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} items={cards.Count} skills={skillCards.Count}"
        );
        return true;
    }

    public bool TryConsumeNextClickToClosePreview(PointerEventData.InputButton? button, string reason)
    {
        return TryConsumeNextClickToClosePreview(
            isLeftClick: button == PointerEventData.InputButton.Left,
            isRightClick: button == PointerEventData.InputButton.Right,
            reason: reason
        );
    }

    private void HideOverlay(string reason)
    {
        _lockedCard = null;
        _closeOnNextClickArmed = false;
        if (_overlayController == null)
            return;

        _overlayController.ClearCards();
        _overlayController.HidePreview();
        BppLog.Info("MonsterLockShowcaseRuntime", $"Hiding preview: {reason}");
    }

    private static bool IsShowcaseCard(Card card)
    {
        var controller = Data.CardAndSkillLookup?.GetCardController(card);
        return controller != null && controller.GetComponent<ShowcaseCardMarker>() != null;
    }

    public bool ShouldInterceptLockToggle(Card card)
    {
        if (_overlayController == null)
            return false;

        var isShowcaseCard = card != null && IsShowcaseCard(card);
        var isMonsterCard = card != null && IsMonsterSourceCard(card);
        return _controller.ShouldInterceptLockToggle(
            IsPreviewActive,
            card != null,
            isShowcaseCard,
            isMonsterCard
        );
    }

    private static bool IsMonsterSourceCard(Card card)
    {
        if (card == null || !ModState.IsInGameRun)
            return false;

        if (MonsterDatabase.TryGetByEncounterId(card.TemplateId.ToString(), out _))
            return true;

        return FindEncounterPreview(card) != null;
    }

    private static bool TryBuildPreview(
        Card card,
        out List<PreviewCardSpec> cards,
        out List<PreviewCardSpec> skillCards,
        out string source
    )
    {
        cards = new List<PreviewCardSpec>();
        skillCards = new List<PreviewCardSpec>();
        source = string.Empty;

        if (card == null || !ModState.IsInGameRun)
            return false;

        if (MonsterDatabase.TryGetByEncounterId(card.TemplateId.ToString(), out var monster))
        {
            var previewModel = MonsterDatabasePreviewDataSource.BuildModel(monster, "monster_db");
            cards = new List<PreviewCardSpec>(previewModel.ItemCards);
            skillCards = new List<PreviewCardSpec>(previewModel.SkillCards);
            source = $"monster_db:{monster.EncounterShortId}";
            return cards.Count > 0 || skillCards.Count > 0;
        }

        var preview = FindEncounterPreview(card);
        if (preview == null)
            return false;

        cards = EncounterPreviewSpecConverter.BuildCachedSpecs(preview.BoardCards);
        skillCards = EncounterPreviewSpecConverter.BuildCachedSpecs(preview.Skills);
        source = "encounter_tracker_cache";
        return cards.Count > 0 || skillCards.Count > 0;
    }

    private static RunInfo.MonsterPreview FindEncounterPreview(Card card)
    {
        var previews = ModState.EncounterMonsterPreviews;
        if (previews == null || previews.Count == 0)
            return null;

        var internalName = card.Template?.InternalName;
        foreach (var preview in previews)
        {
            if (preview == null)
                continue;

            if (preview.EncounterTemplateId == card.TemplateId)
                return preview;

            if (
                !string.IsNullOrWhiteSpace(internalName)
                && string.Equals(
                    preview.EncounterName,
                    internalName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return preview;
            }
        }

        return null;
    }

    private PreviewBoardRequest CreateShowcaseRequest(
        IReadOnlyList<PreviewCardSpec> cards,
        IReadOnlyList<PreviewCardSpec> skillCards,
        string title,
        string source
    )
    {
        var dataSource = new InMemoryPreviewDataSource();
        dataSource.SetCards(cards, skillCards);
        dataSource.SetMetadata(title, new Dictionary<string, string> { ["source"] = source });

        return new PreviewBoardRequest
        {
            DataSource = dataSource,
            AnchorStrategy = _anchorStrategy,
            Presentation = _presentation,
            Debug = new PreviewBoardDebugOptions(),
        };
    }

    private bool TryConsumeNextClickToClosePreview(bool isLeftClick, bool isRightClick, string reason)
    {
        if (
            !_controller.ShouldConsumeNextClickToClosePreview(
                IsPreviewActive,
                _closeOnNextClickArmed,
                isLeftClick,
                isRightClick
            )
        )
        {
            return false;
        }

        HideOverlay(reason);
        return true;
    }


    private static void CopyPresentation(
        PreviewBoardPresentation source,
        PreviewBoardPresentation destination
    )
    {
        destination.Visible = source.Visible;
        destination.DebugEnabled = source.DebugEnabled;
        destination.LocalOffset = source.LocalOffset;
        destination.CardScale = source.CardScale;
        destination.CardSpacing = source.CardSpacing;
        destination.BoardSize = source.BoardSize;
        destination.SkillBoardWidth = source.SkillBoardWidth;
        destination.BoardThickness = source.BoardThickness;
        destination.BorderThickness = source.BorderThickness;
        destination.BorderHeight = source.BorderHeight;
    }
}
