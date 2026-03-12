#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class MonsterLockShowcaseRuntime : MonoBehaviour
{
    private static readonly Rect FallbackHole = new Rect(0.52f, 0.18f, 0.40f, 0.22f);
    private const float SkillRegionYOffset = 1.15f;
    private const float SkillRegionZOffset = 1.15f;
    private const float HorizontalPadding = 0.03f;
    private const float VerticalPaddingTop = 0.04f;
    private const float VerticalPaddingBottom = 0.03f;

    private static readonly System.Reflection.PropertyInfo CurrentTooltipControllerProperty =
        AccessTools.Property(typeof(TooltipParentComponent), "CardTooltipController");

    private readonly MonsterLockShowcaseController _controller = new MonsterLockShowcaseController();
    private readonly LockCanvasHoleOverlay _holeOverlay = new LockCanvasHoleOverlay();
    private readonly FixedAnchorStrategy _anchorStrategy = new FixedAnchorStrategy(
        MonsterPreviewDefaults.DefaultAnchorPose
    );
    private readonly PreviewBoardPresentation _presentation =
        MonsterPreviewDefaults.CreateShowcasePresentation();
    private readonly MonsterPreviewDebugTuner _tuner;

    private MonsterPreviewController _overlayController;
    private Card _lockedCard;

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
        _overlayController = GetComponent<MonsterPreviewController>();
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Awake overlayControllerFound={_overlayController != null}"
        );
    }

    private void OnEnable()
    {
        Events.TooltipLock.AddListener(OnTooltipLock, this);
        Events.TooltipUnlock.AddListener(OnTooltipUnlock, this);
    }

    private void OnDisable()
    {
        Events.TooltipLock.RemoveListener(OnTooltipLock);
        Events.TooltipUnlock.RemoveListener(OnTooltipUnlock);
    }

    private void Update()
    {
        if (!IsPreviewActive)
            return;

        var tooltipController = GetCurrentTooltipController();
        if (tooltipController != null)
            _holeOverlay.Apply(tooltipController, CalculatePreviewHole(_anchorStrategy, _presentation));
    }

    private void OnTooltipLock()
    {
        BppLog.Info("MonsterLockShowcaseRuntime", "OnTooltipLock fired");
        if (_overlayController == null)
        {
            BppLog.Info("MonsterLockShowcaseRuntime", "OnTooltipLock aborted because overlay controller is null");
            return;
        }

        if (!ModState.IsInGameRun)
        {
            HideOverlay("ignoring tooltip lock outside of an active run");
            return;
        }

        var tooltipController = GetCurrentTooltipController();
        var card = tooltipController?.CurrentCard;
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Tooltip current card templateId={card?.TemplateId} name={card?.Template?.InternalName ?? "null"} tooltipControllerFound={tooltipController != null}"
        );
        if (!_controller.ShouldShowForLock(card?.TemplateId, card != null && IsShowcaseCard(card)))
        {
            HideOverlay("locked tooltip had no supported current card");
            return;
        }

        if (!TryBuildPreview(card, out var cards, out var skillCards, out var source))
        {
            HideOverlay(
                $"no encounter preview data for card={card?.Template?.InternalName ?? card?.TemplateId.ToString() ?? "null"}"
            );
            return;
        }

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
        _holeOverlay.Apply(tooltipController, CalculatePreviewHole(_anchorStrategy, _presentation));
        BppLog.Debug(
            "MonsterLockShowcaseRuntime",
            $"Showing preview source={source} card={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} items={cards.Count} skills={skillCards.Count}"
        );
    }

    private void OnTooltipUnlock()
    {
        BppLog.Info("MonsterLockShowcaseRuntime", "OnTooltipUnlock fired");
        if (_lockedCard == null)
            return;

        var tooltipController = GetCurrentTooltipController();
        var currentCard = tooltipController?.CurrentCard;
        var shouldHide = _controller.ShouldHideForUnlock(
            currentCard?.TemplateId,
            currentCard != null && IsShowcaseCard(currentCard)
        );
        if (!shouldHide)
        {
            BppLog.Debug("MonsterLockShowcaseRuntime", "Ignoring unlock caused by showcase card hover");
            return;
        }

        HideOverlay("tooltip unlocked");
    }

    private void HideOverlay(string reason)
    {
        _lockedCard = null;
        _holeOverlay.Clear();
        if (_overlayController == null)
            return;

        _overlayController.ClearCards();
        _overlayController.HidePreview();
        BppLog.Info("MonsterLockShowcaseRuntime", $"Hiding preview: {reason}");
    }

    private static CardTooltipController GetCurrentTooltipController()
    {
        var tooltipParent = Data.TooltipParentComponent;
        if (tooltipParent == null || CurrentTooltipControllerProperty == null)
            return null;

        return CurrentTooltipControllerProperty.GetValue(tooltipParent) as CardTooltipController;
    }

    private static bool IsShowcaseCard(Card card)
    {
        var controller = Data.CardAndSkillLookup?.GetCardController(card);
        return controller != null && controller.GetComponent<ShowcaseCardMarker>() != null;
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

        cards = BuildLegacySpecs(preview.Items);
        skillCards = BuildLegacySpecs(preview.Skills);
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

    private static List<PreviewCardSpec> BuildLegacySpecs(List<string> templateIds)
    {
        var specs = new List<PreviewCardSpec>();
        if (templateIds == null)
            return specs;

        foreach (var templateId in templateIds)
        {
            if (!Guid.TryParse(templateId, out var parsed))
                continue;

            specs.Add(
                new PreviewCardSpec
                {
                    TemplateId = parsed.ToString(),
                    Tier = 0,
                    Size = 1,
                    Enchant = "None",
                }
            );
        }

        return specs;
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

    private static Rect CalculatePreviewHole(
        FixedAnchorStrategy anchorStrategy,
        PreviewBoardPresentation presentation
    )
    {
        var camera = Camera.main;
        if (camera == null)
            return FallbackHole;

        if (!anchorStrategy.TryResolve(out var pose) || pose == null)
            pose = MonsterPreviewDefaults.DefaultAnchorPose;

        var boardCenter = pose.Position + pose.Rotation * presentation.LocalOffset;
        var halfBoardWidth = presentation.BoardSize.x * 0.5f;
        var halfBoardDepth = presentation.BoardSize.y * 0.5f;
        var boardTopY = boardCenter.y + 0.8f;
        var boardBottomY = boardCenter.y - 0.35f;

        var skillCenter =
            pose.Position
            + pose.Rotation
                * (presentation.LocalOffset + new Vector3(0f, SkillRegionYOffset, SkillRegionZOffset));
        var halfSkillWidth = presentation.BoardSize.x * 0.25f;
        var skillTopY = skillCenter.y + 0.65f;
        var skillBottomY = skillCenter.y - 0.45f;

        var worldPoints = new[]
        {
            new Vector3(boardCenter.x - halfBoardWidth, boardTopY, boardCenter.z - halfBoardDepth),
            new Vector3(boardCenter.x + halfBoardWidth, boardTopY, boardCenter.z - halfBoardDepth),
            new Vector3(boardCenter.x - halfBoardWidth, boardBottomY, boardCenter.z + halfBoardDepth),
            new Vector3(boardCenter.x + halfBoardWidth, boardBottomY, boardCenter.z + halfBoardDepth),
            new Vector3(skillCenter.x - halfSkillWidth, skillTopY, skillCenter.z),
            new Vector3(skillCenter.x + halfSkillWidth, skillTopY, skillCenter.z),
            new Vector3(skillCenter.x - halfSkillWidth, skillBottomY, skillCenter.z),
            new Vector3(skillCenter.x + halfSkillWidth, skillBottomY, skillCenter.z),
        };

        var minX = 1f;
        var maxX = 0f;
        var minY = 1f;
        var maxY = 0f;
        var hasPoint = false;

        foreach (var worldPoint in worldPoints)
        {
            var viewportPoint = camera.WorldToViewportPoint(worldPoint);
            if (viewportPoint.z <= 0f)
                continue;

            hasPoint = true;
            minX = Mathf.Min(minX, viewportPoint.x);
            maxX = Mathf.Max(maxX, viewportPoint.x);
            minY = Mathf.Min(minY, viewportPoint.y);
            maxY = Mathf.Max(maxY, viewportPoint.y);
        }

        if (!hasPoint)
            return FallbackHole;

        var left = Mathf.Clamp01(minX - HorizontalPadding);
        var right = Mathf.Clamp01(maxX + HorizontalPadding);
        var top = Mathf.Clamp01(1f - maxY - VerticalPaddingTop);
        var bottom = Mathf.Clamp01(1f - minY + VerticalPaddingBottom);
        var width = Mathf.Clamp01(right - left);
        var height = Mathf.Clamp01(bottom - top);

        if (width <= 0.01f || height <= 0.01f)
            return FallbackHole;

        var computedHole = new Rect(left, top, width, height);
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Computed preview hole=({computedHole.xMin:0.###},{computedHole.yMin:0.###},{computedHole.width:0.###},{computedHole.height:0.###}) from active showcase state"
        );
        return computedHole;
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
        destination.BoardThickness = source.BoardThickness;
        destination.BorderThickness = source.BorderThickness;
        destination.BorderHeight = source.BorderHeight;
    }
}
