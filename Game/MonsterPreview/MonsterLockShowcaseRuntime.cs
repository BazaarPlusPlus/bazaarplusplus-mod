#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterLockShowcaseRuntime : MonoBehaviour
{
    private const string ShowcaseSurfaceName = "MonsterPreviewShowcaseSurface";
    private readonly MonsterLockShowcaseController _controller =
        new MonsterLockShowcaseController();
    private readonly FixedAnchorStrategy _anchorStrategy = new FixedAnchorStrategy(
        MonsterPreviewDefaults.DefaultAnchorPose
    );
    private readonly PreviewBoardPresentation _presentation =
        MonsterPreviewDefaults.CreateShowcasePresentation();
    private readonly MonsterPreviewDebugTuner _tuner;

    private IBoardRenderTarget _renderTarget;
    private PreviewBoardSession _session;
    private PreviewBoardRequest _activeRequest;
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
        EnsureRenderPipeline("awake");
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Awake renderTarget={_renderTarget?.GetType().Name ?? "null"} sessionCreated={_session != null}"
        );
    }

    private void OnDestroy()
    {
        _renderTarget?.Dispose();
        _renderTarget = null;
        _session = null;
        _activeRequest = null;
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

        if (IsPreviewActive && _activeRequest != null)
        {
            EnsureRenderPipeline("update_tick");
            _session.Show(_activeRequest);
            _session.Tick();
        }
    }

    public bool HandleLockToggle(Card card)
    {
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
        {
            BppLog.Debug(
                "MonsterLockShowcaseRuntime",
                $"HandleLockToggle ignored by controller card={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} showcase={isShowcaseCard} monster={isMonsterCard}"
            );
            return false;
        }

        if (!TryCreateShowcaseRequest(card, out var request, out var source))
            return false;

        _lockedCard = card;
        _anchorStrategy.SetPose(MonsterPreviewDefaults.DefaultAnchorPose);
        CopyPresentation(MonsterPreviewDefaults.CreateShowcasePresentation(), _presentation);
        EnsureRenderPipeline("handle_lock_toggle");
        _activeRequest = request;
        _session.Show(request);
        _session.Tick();
        _closeOnNextClickArmed = true;
        _closeOnNextClickArmedFrame = Time.frameCount;
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Activated showcase via PreviewBoardSurface source={source} card={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} requestDataSource={request.DataSource?.GetType().Name ?? "null"} armedFrame={_closeOnNextClickArmedFrame}"
        );
        return true;
    }

    public bool TryConsumeNextClickToClosePreview(
        PointerEventData.InputButton? button,
        string reason
    )
    {
        return TryConsumeNextClickToClosePreview(
            isLeftClick: button == PointerEventData.InputButton.Left,
            isRightClick: button == PointerEventData.InputButton.Right,
            reason: reason
        );
    }

    public void HandlePreviewModeChanged(bool useNativePreview)
    {
        if (!useNativePreview || !IsPreviewActive)
            return;

        HideOverlay("switched to native monster preview");
    }

    private void HideOverlay(string reason)
    {
        _lockedCard = null;
        _closeOnNextClickArmed = false;
        _closeOnNextClickArmedFrame = -1;
        _activeRequest = null;
        _session?.Hide();
        BppLog.Info("MonsterLockShowcaseRuntime", $"Hiding preview: {reason}");
    }

    private static bool IsShowcaseCard(Card card)
    {
        var controller = Data.CardAndSkillLookup?.GetCardController(card);
        return controller != null && controller.GetComponent<ShowcaseCardMarker>() != null;
    }

    public bool ShouldInterceptLockToggle(Card card)
    {
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
        if (card == null || !BppRuntimeHost.RunContext.IsInGameRun)
            return false;

        return BppRuntimeHost.MonsterCatalog.TryGetByEncounterId(card.TemplateId.ToString(), out _);
    }

    private PreviewBoardRequest CreateShowcaseRequest(
        IPreviewDataSource dataSource,
        PreviewBoardModel previewModel,
        string title,
        out string source
    )
    {
        previewModel ??= new PreviewBoardModel();
        var effectiveTitle = string.IsNullOrWhiteSpace(previewModel.Title)
            ? title
            : previewModel.Title;
        source =
            previewModel.Metadata != null
            && previewModel.Metadata.TryGetValue("source", out var metadataSource)
            && !string.IsNullOrWhiteSpace(metadataSource)
                ? metadataSource
                : "monster_db";
        var presentation = ClonePresentation(_presentation);
        if (!presentation.Visible)
        {
            BppLog.Warn(
                "MonsterLockShowcaseRuntime",
                $"Showcase presentation was hidden before request creation; forcing visible source={source} title={title}"
            );
            presentation.Visible = true;
        }

        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"CreateShowcaseRequest title={effectiveTitle} source={source} items={previewModel.ItemCards?.Count ?? 0} skills={previewModel.SkillCards?.Count ?? 0} signature={previewModel.Signature} dataSource={dataSource?.GetType().Name ?? "null"}"
        );

        return new PreviewBoardRequest
        {
            DataSource = dataSource,
            AnchorStrategy = _anchorStrategy,
            Presentation = presentation,
            Debug = new PreviewBoardDebugOptions(),
        };
    }

    private bool TryCreateShowcaseRequest(
        Card card,
        out PreviewBoardRequest request,
        out string source
    )
    {
        request = null;
        source = string.Empty;

        if (card == null)
        {
            BppLog.Warn(
                "MonsterLockShowcaseRuntime",
                "TryCreateShowcaseRequest aborted because card is null"
            );
            return false;
        }

        if (!BppRuntimeHost.RunContext.IsInGameRun)
        {
            BppLog.Warn(
                "MonsterLockShowcaseRuntime",
                $"TryCreateShowcaseRequest aborted because run context is not in-game card={card.Template?.InternalName ?? "-"} templateId={card.TemplateId}"
            );
            return false;
        }

        var encounterId = card.TemplateId.ToString();
        var dataSource = new MonsterDatabasePreviewDataSource(
            encounterId,
            $"lock_showcase:{card.Template?.InternalName ?? encounterId}"
        );
        if (!dataSource.TryBuild(out var previewModel) || previewModel == null)
        {
            BppLog.Warn(
                "MonsterLockShowcaseRuntime",
                $"TryCreateShowcaseRequest could not build renderable model card={card.Template?.InternalName ?? "-"} templateId={card.TemplateId} encounterId={encounterId}"
            );
            return false;
        }

        request = CreateShowcaseRequest(
            dataSource,
            previewModel,
            card.Template?.InternalName ?? encounterId,
            out source
        );
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"TryCreateShowcaseRequest prepared request card={card.Template?.InternalName ?? "-"} templateId={card.TemplateId} source={source} signature={previewModel.Signature}"
        );
        return true;
    }

    private bool TryConsumeNextClickToClosePreview(
        bool isLeftClick,
        bool isRightClick,
        string reason
    )
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

    private static IReadOnlyDictionary<string, string> BuildEncounterPreviewMetadata(
        RunInfo.MonsterPreview preview,
        string source
    )
    {
        return new Dictionary<string, string>
        {
            ["source"] = source ?? string.Empty,
            ["encounter"] = preview?.EncounterShortId ?? string.Empty,
            ["health"] = preview?.Health?.ToString() ?? string.Empty,
            ["reward_gold"] = preview?.RewardGold?.ToString() ?? string.Empty,
            ["reward_xp"] = preview?.RewardXp?.ToString() ?? string.Empty,
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

    private void EnsureRenderPipeline(string reason)
    {
        if (_renderTarget != null && _renderTarget.IsAlive && _session != null)
            return;

        if (_renderTarget != null && !_renderTarget.IsAlive)
        {
            BppLog.Warn(
                "MonsterLockShowcaseRuntime",
                $"Showcase render pipeline was dead; recreating reason={reason}"
            );
        }

        _renderTarget?.Dispose();
        _renderTarget = PreviewBoardRenderTargetFactory.Create(ShowcaseSurfaceName);
        _session = new PreviewBoardSession(_renderTarget);
        if (_activeRequest != null)
            _session.Show(_activeRequest);

        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Showcase render pipeline ready reason={reason} renderTarget={_renderTarget?.GetType().Name ?? "null"} surfaceName={ShowcaseSurfaceName}"
        );
    }
}
