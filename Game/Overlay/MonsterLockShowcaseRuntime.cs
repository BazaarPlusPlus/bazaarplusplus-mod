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
    private static readonly Vector3 FixedPreviewPosition = new Vector3(4f, 1f, -5f);
    private static readonly Quaternion FixedPreviewRotation = Quaternion.identity;
    private static readonly Rect FixedHole = new Rect(0.52f, 0.18f, 0.40f, 0.22f);

    private static readonly System.Reflection.PropertyInfo CurrentTooltipControllerProperty =
        AccessTools.Property(typeof(TooltipParentComponent), "CardTooltipController");

    private readonly MonsterLockShowcaseController _controller = new MonsterLockShowcaseController();
    private readonly LockCanvasHoleOverlay _holeOverlay = new LockCanvasHoleOverlay();

    private MonsterPreviewOverlayController _overlayController;
    private FixedWorldAnchorSource _anchorSource;
    private Card _lockedCard;

    private void Awake()
    {
        _overlayController = GetComponent<MonsterPreviewOverlayController>();
        _anchorSource = new FixedWorldAnchorSource
        {
            Position = FixedPreviewPosition,
            Rotation = FixedPreviewRotation,
        };
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

    private void OnTooltipLock()
    {
        if (_overlayController == null)
            return;

        var tooltipController = GetCurrentTooltipController();
        var card = tooltipController?.CurrentCard;
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
        ApplyFixedLayout();
        _overlayController.SetAnchorSource(_anchorSource);
        _overlayController.SetCards(cards);
        _overlayController.SetSkillCards(skillCards);
        _overlayController.SetVisible(true);
        _holeOverlay.Apply(tooltipController, FixedHole);
        BppLog.Debug(
            "MonsterLockShowcaseRuntime",
            $"Showing preview source={source} card={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} items={cards.Count} skills={skillCards.Count}"
        );
    }

    private void OnTooltipUnlock()
    {
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
        _overlayController.SetVisible(false);
        BppLog.Debug("MonsterLockShowcaseRuntime", $"Hiding preview: {reason}");
    }

    private void ApplyFixedLayout()
    {
        var layout = new PreviewBoardLayout
        {
            BoardSize = new Vector2(8.25f, 2.75f),
            LocalOffset = new Vector3(0f, 0.1f, 0f),
            CardSpacing = new Vector3(1.1f, 0f, 0f),
            CardScale = Vector3.one * 0.5f,
            BoardThickness = 0.02f,
            BorderThickness = 0.04f,
            BorderHeight = 0.04f,
        };

        _overlayController.SetLayout(layout);
        BppLog.Debug(
            "MonsterLockShowcaseRuntime",
            $"Applied fixed layout: anchor={FixedPreviewPosition} rotation={FixedPreviewRotation.eulerAngles} size={layout.BoardSize}"
        );
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

        if (card == null)
            return false;

        if (MonsterDatabase.TryGetByEncounterId(card.TemplateId.ToString(), out var monster))
        {
            cards = MonsterPreviewSpecBuilder.Build(monster);
            skillCards = SkillPreviewSpecBuilder.Build(monster);
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
}
