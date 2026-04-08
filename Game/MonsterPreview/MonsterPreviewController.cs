#pragma warning disable CS0436
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewController : MonoBehaviour
{
    private const string SurfaceName = "MonsterPreviewBoard";
    private readonly List<PreviewCardSpec> _cards = new List<PreviewCardSpec>();
    private readonly List<PreviewCardSpec> _skillCards = new List<PreviewCardSpec>();

    private MonsterPreviewOverlayCoordinator _coordinator;
    private IBoardRenderTarget _renderTarget;
    private IBoardAnchorStrategy _anchorStrategy;
    private PreviewBoardPresentation _presentation;
    private PreviewBoardDebugOptions _debugOptions = new PreviewBoardDebugOptions();
    private bool _visible;

    public bool Visible => _visible;

    private void Awake()
    {
        _presentation = new PreviewBoardPresentation();
        EnsureRenderPipeline("awake");
        BppLog.Info(
            "MonsterPreviewController",
            $"Awake completed; renderTarget={_renderTarget?.GetType().Name ?? "null"} coordinatorCreated={_coordinator != null}"
        );
    }

    private void LateUpdate()
    {
        if (!_visible)
        {
            _coordinator?.SetVisible(false);
            return;
        }

        EnsureRenderPipeline("late_update_visible");
        _coordinator?.Tick();
    }

    public void SetAnchorStrategy(IBoardAnchorStrategy anchorStrategy)
    {
        _anchorStrategy = anchorStrategy;
        if (_visible)
            EnsureRenderPipeline("set_anchor_strategy");
        _coordinator?.SetAnchorStrategy(anchorStrategy);
        BppLog.Debug(
            "MonsterPreviewController",
            $"Anchor strategy set: {anchorStrategy?.GetType().Name ?? "null"}"
        );
    }

    public void SetPresentation(PreviewBoardPresentation presentation)
    {
        _presentation = presentation ?? new PreviewBoardPresentation();
        if (_visible)
            EnsureRenderPipeline("set_presentation");
        _coordinator?.SetPresentation(_presentation);
        BppLog.Debug(
            "MonsterPreviewController",
            $"Presentation updated: size={_presentation.BoardSize}, offset={_presentation.LocalOffset}, spacing={_presentation.CardSpacing}, scale={_presentation.CardScale}"
        );
    }

    public void SetCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        _cards.Clear();
        if (cards != null)
            _cards.AddRange(CloneCards(cards));

        BppLog.Debug(
            "MonsterPreviewController",
            $"SetCards count={_cards.Count}, visible={_visible}"
        );
        if (_visible)
            EnsureRenderPipeline("set_cards");
        _coordinator?.SetCards(_cards);
    }

    public void SetSkillCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        _skillCards.Clear();
        if (cards != null)
            _skillCards.AddRange(CloneCards(cards));

        BppLog.Debug(
            "MonsterPreviewController",
            $"SetSkillCards count={_skillCards.Count}, visible={_visible}"
        );
        if (_visible)
            EnsureRenderPipeline("set_skill_cards");
        _coordinator?.SetSkillCards(_skillCards);
    }

    public void SetDebugOptions(PreviewBoardDebugOptions debugOptions)
    {
        _debugOptions = debugOptions ?? new PreviewBoardDebugOptions();
        if (_visible)
            EnsureRenderPipeline("set_debug_options");
        _coordinator?.SetDebugOptions(_debugOptions);
    }

    public void ShowRequest(PreviewBoardRequest request)
    {
        EnsureRenderPipeline("show_request");
        _visible = request?.Presentation?.Visible ?? false;
        BppLog.Info(
            "MonsterPreviewController",
            $"ShowRequest visible={_visible} dataSource={request?.DataSource?.GetType().Name ?? "null"} initialSignature={request?.InitialModel?.Signature ?? string.Empty} hasAnchor={request?.AnchorStrategy != null} presentationVisible={request?.Presentation?.Visible ?? false}"
        );
        _coordinator?.ShowRequest(request);
    }

    public void HidePreview()
    {
        BppLog.Info("MonsterPreviewController", "HidePreview called");
        SetVisible(false);
    }

    public void ClearCards()
    {
        _cards.Clear();
        _skillCards.Clear();
        BppLog.Debug("MonsterPreviewController", "ClearCards");
        _coordinator?.ClearCards();
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            BppLog.Debug("MonsterPreviewController", $"SetVisible ignored: already {visible}");
            return;
        }

        _visible = visible;
        BppLog.Info("MonsterPreviewController", $"SetVisible visible={_visible}");

        if (!_visible)
        {
            _coordinator?.SetVisible(false);
            return;
        }

        EnsureRenderPipeline("set_visible_true");
        _coordinator?.SetVisible(true);
        if (_anchorStrategy != null)
            _coordinator?.SetAnchorStrategy(_anchorStrategy);
        _coordinator?.SetPresentation(_presentation);
        _coordinator?.SetDebugOptions(_debugOptions);
        _coordinator?.SetCards(_cards);
        _coordinator?.SetSkillCards(_skillCards);
    }

    public void Refresh()
    {
        EnsureRenderPipeline("refresh");
        _coordinator?.Refresh();
    }

    private void OnDestroy()
    {
        _renderTarget?.Dispose();
        _renderTarget = null;
        _coordinator = null;
    }

    private static List<PreviewCardSpec> CloneCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        return cards
            .Select(card => new PreviewCardSpec
            {
                TemplateId = card.TemplateId,
                Tier = card.Tier,
                SourceName = card.SourceName,
                Enchant = card.Enchant,
                Size = card.Size,
                Attributes =
                    card.Attributes != null
                        ? new Dictionary<int, int>(card.Attributes)
                        : new Dictionary<int, int>(),
            })
            .ToList();
    }

    private void EnsureRenderPipeline(string reason)
    {
        if (_renderTarget != null && _renderTarget.IsAlive && _coordinator != null)
            return;

        if (_renderTarget != null && !_renderTarget.IsAlive)
        {
            BppLog.Warn(
                "MonsterPreviewController",
                $"Render pipeline was dead; recreating reason={reason}"
            );
        }

        _renderTarget?.Dispose();
        _renderTarget = PreviewBoardRenderTargetFactory.Create(SurfaceName);
        _coordinator = new MonsterPreviewOverlayCoordinator(_renderTarget);
        _coordinator.SetPresentation(_presentation ?? new PreviewBoardPresentation());
        _coordinator.SetDebugOptions(_debugOptions ?? new PreviewBoardDebugOptions());
        if (_anchorStrategy != null)
            _coordinator.SetAnchorStrategy(_anchorStrategy);

        if (_cards.Count > 0)
            _coordinator.SetCards(_cards);
        if (_skillCards.Count > 0)
            _coordinator.SetSkillCards(_skillCards);

        if (!_visible)
            _coordinator.SetVisible(false);

        BppLog.Info(
            "MonsterPreviewController",
            $"Render pipeline ready reason={reason} renderTarget={_renderTarget?.GetType().Name ?? "null"} visible={_visible}"
        );
    }
}
