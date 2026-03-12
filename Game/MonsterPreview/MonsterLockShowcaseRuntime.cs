#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using TheBazaar;
using UnityEngine;

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

    public bool HandleLockToggle(Card card)
    {
        if (_overlayController == null)
            return false;

        if (IsPreviewActive)
        {
            HideOverlay("right click toggle");
            return true;
        }

        if (!_controller.ShouldShowForLock(card?.TemplateId, card != null && IsShowcaseCard(card)))
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
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Activated BPP showcase mode source={source} card={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} items={cards.Count} skills={skillCards.Count}"
        );
        return true;
    }

    private void HideOverlay(string reason)
    {
        _lockedCard = null;
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
