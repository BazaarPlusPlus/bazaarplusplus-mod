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
    private int _closeOnNextClickArmedFrame = -1;
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
        _closeOnNextClickArmedFrame = Time.frameCount;
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Activated BPP showcase mode source={source} card={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} items={cards.Count} skills={skillCards.Count} armedFrame={_closeOnNextClickArmedFrame}"
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
        _closeOnNextClickArmedFrame = -1;
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
            cards = PreviewCardSpecFilter.FilterLocallyRenderable(previewModel.ItemCards);
            skillCards = PreviewCardSpecFilter.FilterLocallyRenderable(previewModel.SkillCards);
            source = $"monster_db:{monster.EncounterShortId}";
            LogFilteredPreviewCounts(
                card,
                source,
                previewModel.ItemCards?.Count ?? 0,
                cards.Count,
                previewModel.SkillCards?.Count ?? 0,
                skillCards.Count
            );
            return cards.Count > 0 || skillCards.Count > 0;
        }

        var preview = FindEncounterPreview(card);
        if (preview == null)
            return false;

        var cachedCards = EncounterPreviewSpecConverter.BuildCachedSpecs(preview.BoardCards);
        var cachedSkillCards = EncounterPreviewSpecConverter.BuildCachedSpecs(preview.Skills);
        cards = PreviewCardSpecFilter.FilterLocallyRenderable(cachedCards);
        skillCards = PreviewCardSpecFilter.FilterLocallyRenderable(cachedSkillCards);
        source = "encounter_tracker_cache";
        LogFilteredPreviewCounts(
            card,
            source,
            cachedCards.Count,
            cards.Count,
            cachedSkillCards.Count,
            skillCards.Count
        );
        return cards.Count > 0 || skillCards.Count > 0;
    }

    private static void LogFilteredPreviewCounts(
        Card card,
        string source,
        int originalItemCount,
        int filteredItemCount,
        int originalSkillCount,
        int filteredSkillCount
    )
    {
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Preview filter source={source} encounter={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} items={filteredItemCount}/{originalItemCount} skills={filteredSkillCount}/{originalSkillCount}"
        );
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
        var presentation = ClonePresentation(_presentation);
        if (!presentation.Visible)
        {
            BppLog.Warn(
                "MonsterLockShowcaseRuntime",
                $"Showcase presentation was hidden before request creation; forcing visible source={source} title={title}"
            );
            presentation.Visible = true;
        }

        return new PreviewBoardRequest
        {
            DataSource = dataSource,
            AnchorStrategy = _anchorStrategy,
            Presentation = presentation,
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

        if (!NextClickCloseFrameGate.CanConsume(_closeOnNextClickArmedFrame, Time.frameCount))
        {
            BppLog.Debug(
                "MonsterLockShowcaseRuntime",
                $"Ignored close consume in armed frame reason={reason} armedFrame={_closeOnNextClickArmedFrame} currentFrame={Time.frameCount}"
            );
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

    private static PreviewBoardPresentation ClonePresentation(PreviewBoardPresentation presentation)
    {
        presentation ??= new PreviewBoardPresentation();
        return new PreviewBoardPresentation
        {
            Visible = presentation.Visible,
            DebugEnabled = presentation.DebugEnabled,
            LocalOffset = presentation.LocalOffset,
            CardScale = presentation.CardScale,
            CardSpacing = presentation.CardSpacing,
            BoardSize = presentation.BoardSize,
            SkillBoardWidth = presentation.SkillBoardWidth,
            BoardThickness = presentation.BoardThickness,
            BorderThickness = presentation.BorderThickness,
            BorderHeight = presentation.BorderHeight,
        };
    }
}
