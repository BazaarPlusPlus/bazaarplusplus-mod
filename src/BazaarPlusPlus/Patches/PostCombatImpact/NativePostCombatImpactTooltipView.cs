#nullable enable
#pragma warning disable CS0436
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PostCombatImpact;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PostCombatImpact.Ui;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.UI.Tooltips;
using TheBazaar.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Patches.PostCombatImpact;

internal sealed class NativePostCombatImpactTooltipView : IPostCombatImpactTooltipView
{
    private const float EntityPreviewHeight = 52f;
    private const float TooltipPreferredWidth = 660f;
    private const float TooltipReadableWidth = 360f;
    private const float TooltipGap = 18f;
    private const float CanvasMargin = 16f;
    private const float PlacementEpsilon = 0.5f;
    private const float VisibilityFadeDuration = 0.1f;
    private const float PanelTitleFontScale = 0.84f;
    private const float ModeLabelFontScale = 0.66f;
    private const float HeaderHintFontScale = 0.6f;
    private const float IdentityNameFontScale = 1f;
    private const float SummaryFontScale = 0.75f;
    private const float DisclosureFontScale = 0.62f;
    private const float GroupLabelFontScale = 1f;
    private const float GroupMetricFontScale = 1f;
    private const float TargetNameFontScale = 0.875f;
    private const float TargetMetricFontScale = 0.875f;
    private const float MetricColumnMinWidth = 112f;
    private const float MetricColumnPreferredWidth = 190f;
    private static readonly Color32 CausedAccentColor = new(242, 176, 70, 255);
    private static readonly Color32 ReceivedAccentColor = new(83, 197, 222, 255);
    private static readonly Color32 ShiftAccentColor = new(250, 211, 105, 255);
    private static readonly Color32 DisclosureColor = new(211, 190, 157, 255);
    private readonly INativeCardPreviewHost _previewHost;
    private readonly Vector3[] _worldCorners = new Vector3[4];
    private readonly List<LayoutElement> _metricColumns = [];
    private readonly List<INativeCardPreviewSession> _previewSessions = [];
    private readonly List<ImpactContentBlock> _causedBlocks = [];
    private readonly List<ImpactContentBlock> _receivedBlocks = [];
    private AuxiliaryTooltipController? _activeAuxiliary;
    private CardTooltipController? _activePrimary;
    private GameObject? _contentRoot;
    private GameObject? _causedRoot;
    private GameObject? _receivedRoot;
    private GameObject? _nativeBackgroundRoot;
    private TMP_Text? _causedMoreText;
    private TMP_Text? _receivedMoreText;
    private NativePreviewOwner? _previewOwner;
    private INativeCardPreviewScope? _previewScope;
    private CancellationTokenSource? _previewCancellation;
    private NativeAuxiliaryHostState? _preparedNativeHost;
    private CanvasGroupGate? _preparedPrimaryGate;
    private CanvasGroupGate? _preparedAuxiliaryGate;
    private RectTransform? _nativeAuxParentRect;
    private Coroutine? _visibilityFade;
    private float _currentTooltipWidth = TooltipPreferredWidth;
    private float _frameHorizontalBleed;
    private PairSide _pairSide;
    private bool _pairOverflowed;
    private bool _widthBelowReadable;
    private bool _topAlignmentAdjusted;
    private bool _overflowDegradedLogged;
    private bool _widthDegradedLogged;
    private bool _topAlignmentDegradedLogged;
    private bool _hidePending;
    private int _pendingPreviewCount;
    private int _renderGeneration;
    private CombatImpactPerspective _activePerspective = CombatImpactPerspective.Caused;

    internal NativePostCombatImpactTooltipView(INativeCardPreviewHost previewHost) =>
        _previewHost = previewHost ?? throw new ArgumentNullException(nameof(previewHost));

    public string Header => T("本场影响", "Combat Impact");

    public bool IsReadyToReveal => _pendingPreviewCount == 0;

    public bool IsContentActive => _contentRoot?.activeInHierarchy == true;

    public CombatImpactPerspective ActivePerspective => _activePerspective;

    public void PrepareNativePrimary(CardTooltipController primary)
    {
        if (primary == null)
            return;
        if (
            _preparedPrimaryGate != null
            && ReferenceEquals(_preparedPrimaryGate.Controller, primary)
        )
            return;

        if (_activePrimary != null || _hidePending)
        {
            var displacedAuxiliary = _activeAuxiliary;
            ConcealNativeAuxiliary(displacedAuxiliary);
            CleanupCustomContent(restoreNativeContentVisibility: false);
            if (displacedAuxiliary != null)
                Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
        }
        RestorePreparedPrimaryGate();
        var target = FindDescendant(primary.CanvasContentRectTransform, "Tooltip_Main");
        if (target != null)
            _preparedPrimaryGate = CanvasGroupGate.Create(primary, target.gameObject);
    }

    public void CancelPreparedNativePrimary(CardTooltipController primary)
    {
        if (
            _preparedPrimaryGate == null
            || !ReferenceEquals(_preparedPrimaryGate.Controller, primary)
        )
            return;

        if (ReferenceEquals(_activePrimary, primary))
            CleanupCustomContent(restoreNativeContentVisibility: false);
        else
            RestorePreparedPrimaryGate();
    }

    public void PrepareNativeAuxiliary(AuxiliaryTooltipController auxiliary)
    {
        if (_activeAuxiliary != null || _contentRoot != null)
            CleanupCustomContent(restoreNativeContentVisibility: false);
        RestorePreparedNativeHost();
        RestorePreparedAuxiliaryGate();
        _preparedNativeHost = NativeAuxiliaryHostState.Capture(auxiliary);
        if (auxiliary.auxParent != null)
        {
            _preparedAuxiliaryGate = CanvasGroupGate.Create(
                auxiliary,
                auxiliary.auxParent.gameObject,
                forceNonInteractive: true
            );
        }
    }

    public void CancelPreparedNativeAuxiliary(AuxiliaryTooltipController auxiliary)
    {
        if (
            _preparedNativeHost == null
            || !ReferenceEquals(_preparedNativeHost.Controller, auxiliary)
        )
            return;

        if (ReferenceEquals(_activeAuxiliary, auxiliary))
            CleanupCustomContent(restoreNativeContentVisibility: false);
        else
        {
            RestorePreparedNativeHost();
            RestorePreparedAuxiliaryGate();
        }
    }

    public bool Show(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        string entityName,
        CombatImpactSource? source,
        CombatImpactReceived? received,
        CombatImpactPerspective perspective
    )
    {
        if (
            auxiliary.auxParent == null
            || auxiliary.headerText == null
            || auxiliary.bodyText == null
            || !IsTypographyReadyForCurrentLocale()
        )
            return false;

        if (_activeAuxiliary != null || _contentRoot != null)
            CleanupCustomContent(restoreNativeContentVisibility: false);
        if (
            _preparedNativeHost == null
            || !ReferenceEquals(_preparedNativeHost.Controller, auxiliary)
        )
        {
            _preparedNativeHost = NativeAuxiliaryHostState.Capture(auxiliary);
        }

        if (
            _preparedPrimaryGate == null
            || !ReferenceEquals(_preparedPrimaryGate.Controller, primary)
        )
            PrepareNativePrimary(primary);
        if (
            _preparedAuxiliaryGate == null
            || !ReferenceEquals(_preparedAuxiliaryGate.Controller, auxiliary)
        )
        {
            _preparedAuxiliaryGate = CanvasGroupGate.Create(
                auxiliary,
                auxiliary.auxParent.gameObject,
                forceNonInteractive: true
            );
        }
        var generation = ++_renderGeneration;
        _hidePending = false;
        _activeAuxiliary = auxiliary;
        _activePrimary = primary;
        _activePerspective = perspective;
        auxiliary.headerText.gameObject.SetActive(false);
        auxiliary.bodyText.gameObject.SetActive(false);
        auxiliary.dividerParent?.SetActive(false);
        if (
            auxiliary.backgroundImage == null
            || primary.backgroundImage == null
            || primary.backgroundImage.sprite == null
        )
        {
            CleanupCustomContent(restoreNativeContentVisibility: false);
            return false;
        }
        auxiliary.backgroundImage.sprite = primary.backgroundImage.sprite;
        auxiliary.backgroundImage.enabled = primary.backgroundImage.enabled;
        PrepareNativePresentation(auxiliary);
        ApplyTooltipWidth(auxiliary, TooltipPreferredWidth);
        if (!TryCreateNativeBackground(auxiliary, primary))
        {
            CleanupCustomContent(restoreNativeContentVisibility: false);
            return false;
        }

        var root = CreateVertical("BppPostCombatImpactContent", auxiliary.auxParent.transform, 8f);
        _contentRoot = root.gameObject;
        var rootLayout = root.GetComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(0, 0, 14, 18);
        AddLayout(
            root.gameObject,
            preferredHeight: -1f,
            preferredWidth: _currentTooltipWidth,
            minWidth: _currentTooltipWidth
        );
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _currentTooltipWidth);
        var causedRoot = CreateVertical("ImpactCausedPerspective", root, 8f);
        var receivedRoot = CreateVertical("ImpactReceivedPerspective", root, 8f);
        _causedRoot = causedRoot.gameObject;
        _receivedRoot = receivedRoot.gameObject;
        _previewCancellation = new CancellationTokenSource();
        _previewOwner = new NativePreviewOwner(auxiliary.gameObject.layer);
        _previewScope = _previewHost.OpenScope(_previewOwner);
        BuildCaused(
            auxiliary.headerText,
            auxiliary.bodyText,
            causedRoot,
            entityName,
            source,
            generation
        );
        BuildReceived(
            auxiliary.headerText,
            auxiliary.bodyText,
            receivedRoot,
            entityName,
            received,
            generation
        );
        ApplyPerspectiveVisibility(perspective);

        ForceRebuildLayout(auxiliary);
        ApplyTooltipWidth(auxiliary, TooltipPreferredWidth);
        ForceRebuildLayout(auxiliary);
        ApplyNativeHeight(auxiliary);
        return true;
    }

    public bool SetPerspective(CombatImpactPerspective perspective, Transform anchor)
    {
        if (
            _activeAuxiliary == null
            || _activePrimary == null
            || _contentRoot == null
            || _causedRoot == null
            || _receivedRoot == null
        )
            return false;

        var auxiliary = _activeAuxiliary;
        var primary = _activePrimary;
        var previousPerspective = _activePerspective;
        var visibleAlpha = _preparedAuxiliaryGate?.Alpha ?? 1f;
        _preparedAuxiliaryGate?.SetAlpha(0f);
        try
        {
            _activePerspective = perspective;
            ApplyPerspectiveVisibility(perspective);
            ForceRebuildLayout(auxiliary);
            ApplyNativeHeight(auxiliary);
            if (Position(auxiliary, primary, anchor))
                return true;

            _activePerspective = previousPerspective;
            ApplyPerspectiveVisibility(previousPerspective);
            ForceRebuildLayout(auxiliary);
            ApplyNativeHeight(auxiliary);
            Position(auxiliary, primary, anchor);
            return false;
        }
        finally
        {
            // Perspective changes are an atomic content swap. Do not expose the intermediate
            // ContentSizeFitter passes that otherwise make the paired tooltips jump for a frame.
            _preparedAuxiliaryGate?.SetAlpha(visibleAlpha);
        }
    }

    public bool Position(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        Transform anchor
    )
    {
        if (
            !ReferenceEquals(_activeAuxiliary, auxiliary)
            || !ReferenceEquals(_activePrimary, primary)
            || _contentRoot == null
            || primary.RootCanvasComponent == null
        )
            return false;

        Canvas.ForceUpdateCanvases();
        TempoUIUtility.ForceRebuildRecursive(primary.PositioningRectTransform);
        TempoUIUtility.ForceRebuildRecursive(auxiliary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(primary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(auxiliary.PositioningRectTransform);
        ApplyNativeHeight(auxiliary);

        if (primary.RootCanvasComponent.transform is not RectTransform canvasRect)
            return false;

        var canvasBounds = Inset(canvasRect.rect, CanvasMargin);
        var primaryBounds = GetPrimaryVisibleBounds(primary, canvasRect);
        var availableRight = Mathf.Max(0f, canvasBounds.xMax - (primaryBounds.xMax + TooltipGap));
        var availableLeft = Mathf.Max(0f, primaryBounds.xMin - TooltipGap - canvasBounds.xMin);
        var canvasUnitsPerImpactUnit = CanvasUnitsPerLocalUnit(
            auxiliary.PositioningRectTransform,
            canvasRect
        );
        var preferredFrameWidth =
            (TooltipPreferredWidth + _frameHorizontalBleed) * canvasUnitsPerImpactUnit;
        if (availableRight + PlacementEpsilon >= preferredFrameWidth)
        {
            _pairSide = PairSide.ImpactRight;
        }
        else if (availableLeft + PlacementEpsilon >= preferredFrameWidth)
        {
            _pairSide = PairSide.ImpactLeft;
        }
        else if (availableRight >= availableLeft)
        {
            _pairSide = PairSide.ImpactRight;
        }
        else
        {
            _pairSide = PairSide.ImpactLeft;
        }

        var available = _pairSide == PairSide.ImpactRight ? availableRight : availableLeft;
        var availableContentWidth = available / canvasUnitsPerImpactUnit - _frameHorizontalBleed;
        ApplyTooltipWidth(
            auxiliary,
            Mathf.Min(TooltipPreferredWidth, Mathf.Max(1f, availableContentWidth))
        );
        Canvas.ForceUpdateCanvases();
        TempoUIUtility.ForceRebuildRecursive(auxiliary.PositioningRectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(auxiliary.PositioningRectTransform);
        ApplyNativeHeight(auxiliary);
        FitActiveContentToCanvas(auxiliary, canvasRect);

        var impactRect = auxiliary.backgroundImage.rectTransform;
        var impactBounds = GetCanvasLocalBounds(impactRect, canvasRect);
        var impactDelta =
            _pairSide == PairSide.ImpactRight
                ? new Vector2(
                    primaryBounds.xMax + TooltipGap - impactBounds.xMin,
                    primaryBounds.yMax - impactBounds.yMax
                )
                : new Vector2(
                    primaryBounds.xMin - TooltipGap - impactBounds.xMax,
                    primaryBounds.yMax - impactBounds.yMax
                );
        TranslateRect(auxiliary.PositioningRectTransform, canvasRect, impactDelta);

        impactBounds = GetCanvasLocalBounds(impactRect, canvasRect);
        var verticalAdjustment = ResolveVerticalAdjustment(impactBounds, canvasBounds);
        _topAlignmentAdjusted = Mathf.Abs(verticalAdjustment) > PlacementEpsilon;
        if (!Mathf.Approximately(verticalAdjustment, 0f))
        {
            var verticalDelta = new Vector2(0f, verticalAdjustment);
            TranslateRect(auxiliary.PositioningRectTransform, canvasRect, verticalDelta);
            impactDelta += verticalDelta;
            impactBounds = GetCanvasLocalBounds(impactRect, canvasRect);
        }

        var pairBounds = Union(primaryBounds, impactBounds);
        var screenBounds = canvasRect.rect;
        var collides =
            _pairSide == PairSide.ImpactRight
                ? impactBounds.xMin < primaryBounds.xMax + TooltipGap - PlacementEpsilon
                : impactBounds.xMax > primaryBounds.xMin - TooltipGap + PlacementEpsilon;
        _pairOverflowed =
            pairBounds.width > screenBounds.width
            || pairBounds.height > screenBounds.height
            || pairBounds.xMin < screenBounds.xMin - PlacementEpsilon
            || pairBounds.xMax > screenBounds.xMax + PlacementEpsilon
            || pairBounds.yMin < screenBounds.yMin - PlacementEpsilon
            || pairBounds.yMax > screenBounds.yMax + PlacementEpsilon
            || collides;
        _widthBelowReadable = _currentTooltipWidth < TooltipReadableWidth;
        LogPlacementDegradationOnce(
            _pairOverflowed,
            ref _overflowDegradedLogged,
            PostCombatImpactReasonCode.PairPlacementOverflowed
        );
        LogPlacementDegradationOnce(
            _widthBelowReadable,
            ref _widthDegradedLogged,
            PostCombatImpactReasonCode.PairPlacementTooNarrow
        );
        LogPlacementDegradationOnce(
            _topAlignmentAdjusted,
            ref _topAlignmentDegradedLogged,
            PostCombatImpactReasonCode.PairTopAlignmentAdjusted
        );
        return true;
    }

    public void Reveal()
    {
        if (_activeAuxiliary == null || _activePrimary == null || _contentRoot == null)
            return;

        _hidePending = false;
        StartVisibilityFade(targetAlpha: 1f, cleanupOnComplete: false);
    }

    public void Hide()
    {
        if (
            _activeAuxiliary == null
            && _contentRoot == null
            && _preparedPrimaryGate == null
            && _preparedAuxiliaryGate == null
        )
            return;

        if (_hidePending)
            return;
        _hidePending = true;
        if (_activePrimary != null)
            _activePrimary.SetLockedFlag(false);

        var visibleAlpha = Mathf.Max(
            _preparedPrimaryGate?.Alpha ?? 0f,
            _preparedAuxiliaryGate?.Alpha ?? 0f
        );
        if (visibleAlpha <= PlacementEpsilon || _activeAuxiliary == null)
        {
            CompleteAnimatedHide(_renderGeneration);
            return;
        }

        StartVisibilityFade(targetAlpha: 0f, cleanupOnComplete: true);
    }

    public bool OnNativeTooltipChanging(CardTooltipController controller)
    {
        if (!ReferenceEquals(_activePrimary, controller))
            return false;

        Hide();
        return true;
    }

    public bool OnNativeAuxiliaryTooltipShowing(AuxiliaryTooltipController controller)
    {
        if (!ReferenceEquals(_activeAuxiliary, controller))
        {
            RestorePreparedNativeHost(controller);
            return false;
        }

        CleanupCustomContent();
        return true;
    }

    public bool OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller)
    {
        if (!ReferenceEquals(_activeAuxiliary, controller))
            return false;

        // Restore the reusable host geometry for native fade-out, but keep its original content
        // inactive until the next real native show. Reactivating the header here lets a later
        // native fade/tween expose a detached title at this host's old paired position.
        ConcealNativeAuxiliary(controller);
        CleanupCustomContent(restoreNativeContentVisibility: false);
        return true;
    }

    private void StartVisibilityFade(float targetAlpha, bool cleanupOnComplete)
    {
        StopVisibilityFade();
        var auxiliary = _activeAuxiliary;
        if (auxiliary == null || !auxiliary.isActiveAndEnabled)
        {
            SetVisibilityAlpha(targetAlpha);
            if (cleanupOnComplete)
                CompleteAnimatedHide(_renderGeneration);
            return;
        }

        var generation = _renderGeneration;
        _visibilityFade = auxiliary.StartCoroutine(
            FadeVisibility(targetAlpha, cleanupOnComplete, generation)
        );
    }

    private System.Collections.IEnumerator FadeVisibility(
        float targetAlpha,
        bool cleanupOnComplete,
        int generation
    )
    {
        var primaryStart = _preparedPrimaryGate?.Alpha ?? targetAlpha;
        var auxiliaryStart = _preparedAuxiliaryGate?.Alpha ?? targetAlpha;
        var elapsed = 0f;
        while (elapsed < VisibilityFadeDuration)
        {
            if (generation != _renderGeneration)
            {
                _visibilityFade = null;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(elapsed / VisibilityFadeDuration);
            if (!cleanupOnComplete)
            {
                _preparedPrimaryGate?.SetAlpha(Mathf.Lerp(primaryStart, targetAlpha, progress));
            }
            _preparedAuxiliaryGate?.SetAlpha(Mathf.Lerp(auxiliaryStart, targetAlpha, progress));
            yield return null;
        }

        if (cleanupOnComplete)
        {
            _preparedPrimaryGate?.SetAlpha(0f);
            _preparedAuxiliaryGate?.SetAlpha(0f);
        }
        else
        {
            SetVisibilityAlpha(targetAlpha);
        }
        _visibilityFade = null;
        if (cleanupOnComplete)
            CompleteAnimatedHide(generation);
    }

    private void SetVisibilityAlpha(float alpha)
    {
        _preparedPrimaryGate?.SetAlpha(alpha);
        _preparedAuxiliaryGate?.SetAlpha(alpha);
    }

    private void StopVisibilityFade()
    {
        if (_visibilityFade == null)
            return;

        if (_activeAuxiliary != null)
            _activeAuxiliary.StopCoroutine(_visibilityFade);
        _visibilityFade = null;
    }

    private void CompleteAnimatedHide(int generation)
    {
        if (generation != _renderGeneration)
            return;

        ConcealNativeAuxiliary(_activeAuxiliary);
        _visibilityFade = null;
        if (!CleanupCustomContent(restoreNativeContentVisibility: false))
            return;
        Data.TooltipParentComponent?.HideAuxiliaryTooltipController();
    }

    private static void ConcealNativeAuxiliary(AuxiliaryTooltipController? auxiliary)
    {
        if (auxiliary?.tooltipCanvasGroup != null)
            auxiliary.tooltipCanvasGroup.alpha = 0f;
    }

    private bool CleanupCustomContent(bool restoreNativeContentVisibility = true)
    {
        var hadActiveContent =
            _activeAuxiliary != null
            || _contentRoot != null
            || _nativeBackgroundRoot != null
            || _previewScope != null
            || _preparedNativeHost != null
            || _preparedPrimaryGate != null
            || _preparedAuxiliaryGate != null;
        if (!hadActiveContent)
            return false;

        StopVisibilityFade();
        _renderGeneration++;
        DisposeNativePreviews();
        if (_contentRoot != null)
        {
            _contentRoot.SetActive(false);
            Object.Destroy(_contentRoot);
        }
        if (_nativeBackgroundRoot != null)
        {
            _nativeBackgroundRoot.SetActive(false);
            Object.Destroy(_nativeBackgroundRoot);
        }
        RestorePreparedNativeHost(restoreNativeContentVisibility);
        RestorePreparedAuxiliaryGate();
        RestorePreparedPrimaryGate();
        if (_activePrimary != null)
            _activePrimary.SetLockedFlag(false);

        _activeAuxiliary = null;
        _activePrimary = null;
        _contentRoot = null;
        _causedRoot = null;
        _receivedRoot = null;
        _nativeBackgroundRoot = null;
        _causedMoreText = null;
        _receivedMoreText = null;
        _previewOwner = null;
        _nativeAuxParentRect = null;
        _metricColumns.Clear();
        _causedBlocks.Clear();
        _receivedBlocks.Clear();
        _pendingPreviewCount = 0;
        _currentTooltipWidth = TooltipPreferredWidth;
        _frameHorizontalBleed = 0f;
        _pairSide = PairSide.None;
        _pairOverflowed = false;
        _widthBelowReadable = false;
        _topAlignmentAdjusted = false;
        _overflowDegradedLogged = false;
        _widthDegradedLogged = false;
        _topAlignmentDegradedLogged = false;
        _hidePending = false;
        _activePerspective = CombatImpactPerspective.Caused;
        return true;
    }

    private void PrepareNativePresentation(AuxiliaryTooltipController auxiliary)
    {
        _nativeAuxParentRect = auxiliary.auxParent.transform as RectTransform;
        if (_nativeAuxParentRect == null)
            return;

        var frameRect = auxiliary.backgroundImage.rectTransform;
        _frameHorizontalBleed = Mathf.Max(
            0f,
            frameRect.rect.width - _nativeAuxParentRect.rect.width
        );
        _nativeAuxParentRect.anchorMin = new Vector2(0.5f, 0.5f);
        _nativeAuxParentRect.anchorMax = new Vector2(0.5f, 0.5f);
        _nativeAuxParentRect.pivot = new Vector2(0.5f, 0.5f);
        // auxParent and the frame are sibling rects in the native Auxiliary Tooltip prefab.
        // Match the frame's center instead of mirroring its serialized offset; mirroring doubles
        // any inset and makes the left/right content padding visibly asymmetric.
        _nativeAuxParentRect.anchoredPosition = frameRect.anchoredPosition;
    }

    private void ApplyTooltipWidth(AuxiliaryTooltipController auxiliary, float contentWidth)
    {
        _currentTooltipWidth = Mathf.Max(1f, contentWidth);
        if (_contentRoot != null)
        {
            var contentRect = (RectTransform)_contentRoot.transform;
            var contentLayout = _contentRoot.GetComponent<LayoutElement>();
            contentLayout.preferredWidth = _currentTooltipWidth;
            contentLayout.minWidth = _currentTooltipWidth;
            contentRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                _currentTooltipWidth
            );
        }

        var metricWidth = Mathf.Clamp(
            _currentTooltipWidth * 0.3f,
            MetricColumnMinWidth,
            MetricColumnPreferredWidth
        );
        foreach (var metricColumn in _metricColumns)
            metricColumn.minWidth = metricWidth;

        _nativeAuxParentRect?.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            _currentTooltipWidth
        );
        auxiliary.PositioningRectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            _currentTooltipWidth + _frameHorizontalBleed
        );
    }

    private void ApplyNativeHeight(AuxiliaryTooltipController auxiliary)
    {
        var contentHeight = _nativeAuxParentRect?.rect.height ?? 0f;
        if (contentHeight <= 0f)
            return;

        auxiliary.PositioningRectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            contentHeight
        );
    }

    private void FitActiveContentToCanvas(
        AuxiliaryTooltipController auxiliary,
        RectTransform canvasRect
    )
    {
        var blocks =
            _activePerspective == CombatImpactPerspective.Caused ? _causedBlocks : _receivedBlocks;
        var moreText =
            _activePerspective == CombatImpactPerspective.Caused
                ? _causedMoreText
                : _receivedMoreText;
        if (moreText == null)
            return;

        foreach (var block in blocks)
            block.Restore();
        moreText.transform.parent.gameObject.SetActive(false);
        RebuildAfterHeightBudgetChange(auxiliary);

        var canvasBounds = Inset(canvasRect.rect, CanvasMargin);
        var hiddenCount = 0;
        for (var blockIndex = blocks.Count - 1; blockIndex >= 0; blockIndex--)
        {
            var block = blocks[blockIndex];
            for (var rowIndex = block.DetailRows.Count - 1; rowIndex >= 0; rowIndex--)
            {
                if (FitsCanvasHeight(auxiliary, canvasRect, canvasBounds))
                    return;

                block.DetailRows[rowIndex].SetActive(false);
                hiddenCount++;
                ShowMoreRow(moreText, hiddenCount);
                RebuildAfterHeightBudgetChange(auxiliary);
            }

            if (FitsCanvasHeight(auxiliary, canvasRect, canvasBounds))
                return;

            block.Root.SetActive(false);
            block.LeadingDivider?.SetActive(false);
            hiddenCount++;
            ShowMoreRow(moreText, hiddenCount);
            RebuildAfterHeightBudgetChange(auxiliary);
        }
    }

    private static void ShowMoreRow(TMP_Text moreText, int hiddenCount)
    {
        moreText.text = T($"另有 {hiddenCount} 项", $"+{hiddenCount} more");
        moreText.transform.parent.gameObject.SetActive(true);
    }

    private bool FitsCanvasHeight(
        AuxiliaryTooltipController auxiliary,
        RectTransform canvasRect,
        Rect canvasBounds
    ) =>
        GetCanvasLocalBounds(auxiliary.backgroundImage.rectTransform, canvasRect).height
        <= canvasBounds.height + PlacementEpsilon;

    private void RebuildAfterHeightBudgetChange(AuxiliaryTooltipController auxiliary)
    {
        ForceRebuildLayout(auxiliary);
        ApplyNativeHeight(auxiliary);
    }

    private static void ForceRebuildLayout(AuxiliaryTooltipController auxiliary)
    {
        // A newly activated nested ContentSizeFitter can expose its previous preferred height on
        // the first pass. Two bounded passes resolve the child and parent sizes in this frame.
        for (var pass = 0; pass < 2; pass++)
        {
            Canvas.ForceUpdateCanvases();
            TempoUIUtility.ForceRebuildRecursive(auxiliary.PositioningRectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(auxiliary.PositioningRectTransform);
        }
    }

    private void ReconcileVerticalPlacement()
    {
        if (
            _pairSide == PairSide.None
            || _activeAuxiliary == null
            || _activePrimary == null
            || _activePrimary.RootCanvasComponent?.transform is not RectTransform canvasRect
        )
            return;

        var canvasBounds = Inset(canvasRect.rect, CanvasMargin);
        var primaryBounds = GetPrimaryVisibleBounds(_activePrimary, canvasRect);
        var impactRect = _activeAuxiliary.backgroundImage.rectTransform;
        var impactBounds = GetCanvasLocalBounds(impactRect, canvasRect);
        var verticalDelta = primaryBounds.yMax - impactBounds.yMax;
        if (!Mathf.Approximately(verticalDelta, 0f))
        {
            TranslateRect(
                _activeAuxiliary.PositioningRectTransform,
                canvasRect,
                new Vector2(0f, verticalDelta)
            );
            impactBounds = GetCanvasLocalBounds(impactRect, canvasRect);
        }

        var containmentAdjustment = ResolveVerticalAdjustment(impactBounds, canvasBounds);
        _topAlignmentAdjusted = Mathf.Abs(containmentAdjustment) > PlacementEpsilon;
        if (!Mathf.Approximately(containmentAdjustment, 0f))
        {
            TranslateRect(
                _activeAuxiliary.PositioningRectTransform,
                canvasRect,
                new Vector2(0f, containmentAdjustment)
            );
            impactBounds = GetCanvasLocalBounds(impactRect, canvasRect);
        }

        var pairBounds = Union(primaryBounds, impactBounds);
        var screenBounds = canvasRect.rect;
        var collides =
            _pairSide == PairSide.ImpactRight
                ? impactBounds.xMin < primaryBounds.xMax + TooltipGap - PlacementEpsilon
                : impactBounds.xMax > primaryBounds.xMin - TooltipGap + PlacementEpsilon;
        _pairOverflowed =
            pairBounds.width > screenBounds.width
            || pairBounds.height > screenBounds.height
            || pairBounds.xMin < screenBounds.xMin - PlacementEpsilon
            || pairBounds.xMax > screenBounds.xMax + PlacementEpsilon
            || pairBounds.yMin < screenBounds.yMin - PlacementEpsilon
            || pairBounds.yMax > screenBounds.yMax + PlacementEpsilon
            || collides;
        LogPlacementDegradationOnce(
            _pairOverflowed,
            ref _overflowDegradedLogged,
            PostCombatImpactReasonCode.PairPlacementOverflowed
        );
        LogPlacementDegradationOnce(
            _topAlignmentAdjusted,
            ref _topAlignmentDegradedLogged,
            PostCombatImpactReasonCode.PairTopAlignmentAdjusted
        );
    }

    private bool TryCreateNativeBackground(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary
    )
    {
        var sourceContainer = primary.gradientImage?.transform.parent as RectTransform;
        var sourceMaskImage = sourceContainer?.GetComponent<Image>();
        var sourceMask = sourceContainer?.GetComponent<Mask>();
        var frameRect = auxiliary.backgroundImage?.rectTransform;
        if (
            sourceContainer == null
            || sourceMaskImage == null
            || sourceMask == null
            || frameRect == null
            || frameRect.parent == null
        )
            return false;

        var background = new GameObject(
            "BppPostCombatImpactNativeBackground",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Mask),
            typeof(LayoutElement)
        );
        var backgroundRect = (RectTransform)background.transform;
        backgroundRect.SetParent(frameRect.parent, worldPositionStays: false);
        CopyRectTransform(frameRect, backgroundRect);
        backgroundRect.SetSiblingIndex(frameRect.GetSiblingIndex());
        background.GetComponent<LayoutElement>().ignoreLayout = true;
        CopyImage(sourceMaskImage, background.GetComponent<Image>());
        background.GetComponent<Image>().enabled = true;
        var mask = background.GetComponent<Mask>();
        mask.enabled = sourceMask.enabled;
        mask.showMaskGraphic = sourceMask.showMaskGraphic;

        for (var childIndex = 0; childIndex < sourceContainer.childCount; childIndex++)
        {
            if (
                sourceContainer.GetChild(childIndex) is not RectTransform sourceRect
                || !sourceRect.TryGetComponent<Image>(out var sourceImage)
            )
                continue;

            var layer = new GameObject(
                $"BppPostCombatImpactNativeLayer_{childIndex}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            var layerRect = (RectTransform)layer.transform;
            layerRect.SetParent(backgroundRect, worldPositionStays: false);
            CopyRectTransform(sourceRect, layerRect);
            CopyImage(sourceImage, layer.GetComponent<Image>());
            layer.SetActive(sourceRect.gameObject.activeSelf);
        }

        _nativeBackgroundRoot = background;
        return true;
    }

    private static void CopyRectTransform(RectTransform source, RectTransform destination)
    {
        destination.anchorMin = source.anchorMin;
        destination.anchorMax = source.anchorMax;
        destination.pivot = source.pivot;
        destination.sizeDelta = source.sizeDelta;
        destination.anchoredPosition3D = source.anchoredPosition3D;
        destination.localRotation = source.localRotation;
        destination.localScale = source.localScale;
    }

    private static void CopyImage(Image source, Image destination)
    {
        destination.sprite = source.sprite;
        destination.material = source.material;
        destination.color = source.color;
        destination.type = source.type;
        destination.fillCenter = source.fillCenter;
        destination.fillMethod = source.fillMethod;
        destination.fillAmount = source.fillAmount;
        destination.fillClockwise = source.fillClockwise;
        destination.fillOrigin = source.fillOrigin;
        destination.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        destination.preserveAspect = source.preserveAspect;
        destination.useSpriteMesh = source.useSpriteMesh;
        destination.maskable = source.maskable;
        destination.raycastTarget = false;
        destination.enabled = source.enabled;
    }

    private void RestorePreparedNativeHost(
        bool restoreContentVisibility = true,
        AuxiliaryTooltipController? expectedController = null
    )
    {
        if (_preparedNativeHost == null)
            return;
        if (
            expectedController != null
            && !ReferenceEquals(_preparedNativeHost.Controller, expectedController)
        )
            return;

        _preparedNativeHost.Restore(restoreContentVisibility);
        if (restoreContentVisibility)
            _preparedNativeHost = null;
    }

    private void RestorePreparedNativeHost(AuxiliaryTooltipController expectedController) =>
        RestorePreparedNativeHost(
            restoreContentVisibility: true,
            expectedController: expectedController
        );

    private void RestorePreparedPrimaryGate()
    {
        _preparedPrimaryGate?.Restore();
        _preparedPrimaryGate = null;
    }

    private void RestorePreparedAuxiliaryGate()
    {
        _preparedAuxiliaryGate?.Restore();
        _preparedAuxiliaryGate = null;
    }

    private void DisposeNativePreviews()
    {
        if (_previewCancellation != null)
        {
            try
            {
                _previewCancellation.Cancel();
            }
            catch
            {
                // Continue deterministic native-preview release.
            }
            _previewCancellation.Dispose();
            _previewCancellation = null;
        }

        foreach (var session in _previewSessions)
        {
            try
            {
                session.Dispose();
            }
            catch
            {
                // Scope disposal below remains the final ownership boundary.
            }
        }
        _previewSessions.Clear();

        var scope = _previewScope;
        _previewScope = null;
        if (scope != null)
            _ = DisposeNativePreviewScope(scope);
    }

    private static async Task DisposeNativePreviewScope(INativeCardPreviewScope scope)
    {
        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                PostCombatImpactLogEvents.InteractionDegraded,
                ex,
                PostCombatImpactLogEvents.ReasonCode.Bind(
                    PostCombatImpactReasonCode.EntityPreviewUnavailable
                )
            );
        }
    }

    private void ApplyPerspectiveVisibility(CombatImpactPerspective perspective)
    {
        SetPerspectiveRoot(_causedRoot, perspective == CombatImpactPerspective.Caused);
        SetPerspectiveRoot(_receivedRoot, perspective == CombatImpactPerspective.Received);
    }

    private static void SetPerspectiveRoot(GameObject? root, bool visible)
    {
        if (root == null)
            return;

        root.SetActive(true);
        var canvasGroup = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        var layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        layout.ignoreLayout = !visible;
    }

    private void BuildCaused(
        TMP_Text headingTemplate,
        TMP_Text bodyTemplate,
        RectTransform root,
        string entityName,
        CombatImpactSource? source,
        int generation
    )
    {
        BuildPanelHeader(headingTemplate, bodyTemplate, root, CombatImpactPerspective.Caused);
        BuildIdentitySummary(
            bodyTemplate,
            root,
            entityName,
            source == null ? T("0 个效果", "0 effects") : CausedSummary(source)
        );
        AddDivider(root);

        if (source == null)
        {
            BuildEmptyState(
                bodyTemplate,
                root,
                T("本场未造成效果", "No effects caused this combat")
            );
        }
        else
        {
            for (var index = 0; index < source.Groups.Count; index++)
            {
                var divider = index > 0 ? AddDivider(root) : null;
                var block = BuildCausedGroup(bodyTemplate, root, source.Groups[index], generation);
                block.LeadingDivider = divider;
                _causedBlocks.Add(block);
            }
        }
        BuildDisclosures(
            bodyTemplate,
            root,
            CombatImpactMetricFormatter.CausedDisclosures(source, IsChinese())
        );
        _causedMoreText = BuildMoreRow(bodyTemplate, root);
    }

    private void BuildReceived(
        TMP_Text headingTemplate,
        TMP_Text bodyTemplate,
        RectTransform root,
        string entityName,
        CombatImpactReceived? received,
        int generation
    )
    {
        BuildPanelHeader(headingTemplate, bodyTemplate, root, CombatImpactPerspective.Received);
        BuildIdentitySummary(
            bodyTemplate,
            root,
            entityName,
            received == null
                ? T("0 个效果", "0 effects")
                : T(
                    $"受到 {received.EffectCount} 个效果",
                    $"{received.EffectCount} effects received"
                )
        );
        AddDivider(root);

        if (received == null)
        {
            BuildEmptyState(
                bodyTemplate,
                root,
                T("本场未受到效果", "No effects received this combat")
            );
        }
        else
        {
            for (var index = 0; index < received.Groups.Count; index++)
            {
                var divider = index > 0 ? AddDivider(root) : null;
                var block = BuildReceivedGroup(
                    bodyTemplate,
                    root,
                    received.Groups[index],
                    generation
                );
                block.LeadingDivider = divider;
                _receivedBlocks.Add(block);
            }
        }
        BuildDisclosures(
            bodyTemplate,
            root,
            CombatImpactMetricFormatter.ReceivedDisclosures(received, IsChinese())
        );
        _receivedMoreText = BuildMoreRow(bodyTemplate, root);
    }

    private void BuildPanelHeader(
        TMP_Text headingTemplate,
        TMP_Text bodyTemplate,
        RectTransform parent,
        CombatImpactPerspective perspective
    )
    {
        var header = CreateVertical("ImpactPanelHeader", parent, 2f);
        AddLayout(header.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        var title = CloneText(
            headingTemplate,
            header,
            Header,
            PanelTitleFontScale,
            flexibleWidth: 1f
        );
        title.alignment = TextAlignmentOptions.Left;
        var metadata = CreateHorizontal("ImpactHeaderMetadata", header, 12f, preferredHeight: 32f);
        var mode = CloneText(
            bodyTemplate,
            metadata,
            perspective == CombatImpactPerspective.Caused
                ? T("此卡造成", "CAUSED BY THIS CARD")
                : T("此卡受到", "RECEIVED BY THIS CARD"),
            ModeLabelFontScale,
            flexibleWidth: 1f
        );
        mode.alignment = TextAlignmentOptions.MidlineLeft;
        mode.color =
            perspective == CombatImpactPerspective.Caused ? CausedAccentColor : ReceivedAccentColor;

        var hintRoot = CreateHorizontal("ImpactShiftHint", metadata, 3f, preferredHeight: 32f);
        hintRoot.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
        var hintLead = CloneText(bodyTemplate, hintRoot, T("按", "Press"), HeaderHintFontScale);
        hintLead.alpha = 0.72f;
        var shiftKey = CloneText(bodyTemplate, hintRoot, "SHIFT", HeaderHintFontScale);
        shiftKey.color = ShiftAccentColor;
        var hintTail = CloneText(
            bodyTemplate,
            hintRoot,
            perspective == CombatImpactPerspective.Caused
                ? T("查看受到的效果", "to view effects received")
                : T("查看造成的效果", "to view effects caused"),
            HeaderHintFontScale
        );
        hintTail.alpha = 0.72f;
    }

    private static string CausedSummary(CombatImpactSource source)
    {
        var parts = new List<string>();
        if (source.UseCount > 0)
            parts.Add(T($"使用 {source.UseCount}", $"{source.UseCount} uses"));
        parts.Add(T($"{source.TotalCount} 个效果", $"{source.TotalCount} effects"));
        return string.Join(" · ", parts);
    }

    private void BuildIdentitySummary(
        TMP_Text bodyTemplate,
        RectTransform parent,
        string entityName,
        string summary
    )
    {
        var row = CreateVertical("ImpactIdentitySummary", parent, 2f);
        AddLayout(row.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        var name = CloneText(
            bodyTemplate,
            row,
            entityName.Replace('\n', ' '),
            IdentityNameFontScale,
            flexibleWidth: 1f
        );
        name.alignment = TextAlignmentOptions.Left;
        var detail = CloneText(bodyTemplate, row, summary, SummaryFontScale, flexibleWidth: 1f);
        detail.alignment = TextAlignmentOptions.Left;
        detail.alpha = 0.72f;
    }

    private static void BuildEmptyState(TMP_Text textTemplate, RectTransform parent, string message)
    {
        var empty = CloneText(
            textTemplate,
            parent,
            message,
            TargetNameFontScale,
            flexibleWidth: 1f
        );
        empty.alpha = 0.72f;
    }

    private ImpactContentBlock BuildCausedGroup(
        TMP_Text textTemplate,
        RectTransform parent,
        CombatImpactGroup group,
        int generation
    )
    {
        var groupRoot = CreateVertical("ImpactCausedGroup", parent, 4f);
        AddLayout(groupRoot.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        BuildGroupHeader(
            textTemplate,
            groupRoot,
            group.Kind,
            group.NativeAttributeKey,
            group.Surface,
            CombatImpactMetricFormatter.Group(group, IsChinese(), CriticalMarker())
        );
        var detailRows = new List<GameObject>();
        if (ShouldRenderTargetDetails(group))
        {
            foreach (var target in group.Targets)
            {
                detailRows.Add(
                    BuildEntityRow(
                        textTemplate,
                        groupRoot,
                        "ImpactTargetRow",
                        target.Entity,
                        CombatImpactMetricFormatter.Target(group, target, IsChinese()),
                        generation
                    )
                );
            }
        }
        return new ImpactContentBlock(groupRoot.gameObject, detailRows);
    }

    private static bool ShouldRenderTargetDetails(CombatImpactGroup group) =>
        CombatImpactTargetDetailPolicy.ShouldRender(
            group.Kind,
            group.NativeAttributeKey,
            group.Surface
        );

    private ImpactContentBlock BuildReceivedGroup(
        TMP_Text textTemplate,
        RectTransform parent,
        CombatImpactIncomingGroup group,
        int generation
    )
    {
        var groupRoot = CreateVertical("ImpactReceivedGroup", parent, 4f);
        AddLayout(groupRoot.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        BuildGroupHeader(
            textTemplate,
            groupRoot,
            group.Kind,
            group.NativeAttributeKey,
            group.Surface,
            CombatImpactMetricFormatter.IncomingGroup(group, IsChinese(), CriticalMarker())
        );
        var detailRows = new List<GameObject>();
        foreach (var source in group.Sources)
        {
            detailRows.Add(
                BuildEntityRow(
                    textTemplate,
                    groupRoot,
                    "ImpactSourceRow",
                    source.Entity,
                    CombatImpactMetricFormatter.IncomingSource(group, source, IsChinese()),
                    generation
                )
            );
        }
        return new ImpactContentBlock(groupRoot.gameObject, detailRows);
    }

    private void BuildGroupHeader(
        TMP_Text textTemplate,
        RectTransform parent,
        CombatImpactKind kind,
        string nativeAttributeKey,
        CombatImpactEventSurface surface,
        string metricText
    )
    {
        var header = CreateHorizontal("ImpactGroupHeader", parent, 10f, preferredHeight: 48f);
        var (label, iconKey) = ResolveEffect(kind, nativeAttributeKey, surface);
        var effectIcon =
            Data.TooltipTypography?.GetKeywordStringWithIconNoScale(
                iconKey,
                string.Empty,
                useNumberFont: false
            ) ?? string.Empty;
        var labelContent = string.IsNullOrWhiteSpace(effectIcon) ? label : $"{effectIcon} {label}";
        var labelText = CloneText(
            textTemplate,
            header,
            labelContent,
            GroupLabelFontScale,
            flexibleWidth: 1f
        );
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        var metric = CloneText(
            textTemplate,
            header,
            metricText,
            GroupMetricFontScale,
            minWidth: MetricColumnPreferredWidth
        );
        _metricColumns.Add(metric.GetComponent<LayoutElement>());
        metric.alignment = TextAlignmentOptions.MidlineRight;
    }

    private GameObject BuildEntityRow(
        TMP_Text textTemplate,
        RectTransform parent,
        string rowName,
        CombatImpactEntity entity,
        string metricText,
        int generation
    )
    {
        var row = CreateHorizontal(rowName, parent, 10f, preferredHeight: EntityPreviewHeight + 2f);
        BuildEntityIcon(row, entity, EntityPreviewHeight, generation);
        var name = CloneText(
            textTemplate,
            row,
            entity.Name,
            TargetNameFontScale,
            flexibleWidth: 1f
        );
        name.alignment = TextAlignmentOptions.MidlineLeft;
        name.alpha = 0.86f;
        var metric = CloneText(
            textTemplate,
            row,
            metricText,
            TargetMetricFontScale,
            minWidth: MetricColumnPreferredWidth
        );
        _metricColumns.Add(metric.GetComponent<LayoutElement>());
        metric.alignment = TextAlignmentOptions.MidlineRight;
        metric.alpha = 0.76f;
        return row.gameObject;
    }

    private static GameObject AddDivider(RectTransform parent)
    {
        var divider = new GameObject(
            "ImpactDivider",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(LayoutElement)
        );
        var rect = (RectTransform)divider.transform;
        rect.SetParent(parent, worldPositionStays: false);
        var image = divider.GetComponent<Image>();
        image.color = new Color32(112, 86, 43, 150);
        image.raycastTarget = false;
        AddLayout(divider, preferredHeight: 1f, flexibleWidth: 1f);
        return divider;
    }

    private static void BuildDisclosures(
        TMP_Text textTemplate,
        RectTransform parent,
        IReadOnlyList<string> disclosures
    )
    {
        if (disclosures.Count == 0)
            return;

        AddDivider(parent);
        var root = CreateVertical("ImpactDisclosures", parent, 2f);
        AddLayout(root.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        foreach (var disclosure in disclosures)
        {
            var text = CloneText(
                textTemplate,
                root,
                disclosure,
                DisclosureFontScale,
                flexibleWidth: 1f
            );
            text.alignment = TextAlignmentOptions.Left;
            text.color = DisclosureColor;
            text.alpha = 0.76f;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
        }
    }

    private static TMP_Text BuildMoreRow(TMP_Text textTemplate, RectTransform parent)
    {
        var root = CreateHorizontal("ImpactMoreRow", parent, 0f, preferredHeight: 32f);
        var text = CloneText(textTemplate, root, string.Empty, SummaryFontScale, flexibleWidth: 1f);
        text.alpha = 0.72f;
        root.gameObject.SetActive(false);
        return text;
    }

    private void BuildEntityIcon(
        RectTransform parent,
        CombatImpactEntity entity,
        float size,
        int generation
    )
    {
        var previewWidth = ResolveEntityPreviewWidth(entity, size);
        var columnObject = new GameObject(
            "ImpactEntityPreviewColumn",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(LayoutElement)
        );
        var columnRect = (RectTransform)columnObject.transform;
        columnRect.SetParent(parent, worldPositionStays: false);
        AddLayout(
            columnObject,
            preferredHeight: size,
            preferredWidth: previewWidth,
            minWidth: previewWidth
        );
        columnRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, previewWidth);
        columnRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);

        var slotObject = new GameObject("ImpactEntityPreview", typeof(RectTransform));
        var rect = (RectTransform)slotObject.transform;
        rect.SetParent(columnRect, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, previewWidth);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);

        if (entity.Hero.HasValue)
        {
            var artObject = new GameObject(
                "ImpactHeroPortrait",
                typeof(RectTransform),
                typeof(CanvasRenderer)
            );
            var artRect = (RectTransform)artObject.transform;
            artRect.SetParent(rect, worldPositionStays: false);
            artRect.anchorMin = Vector2.zero;
            artRect.anchorMax = Vector2.one;
            artRect.offsetMin = Vector2.zero;
            artRect.offsetMax = Vector2.zero;
            var image = artObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            _pendingPreviewCount++;
            _ = LoadHero(image, rect, entity.Hero.Value, generation);
            return;
        }

        if (
            entity.TemplateId == Guid.Empty
            || _previewOwner == null
            || _previewScope == null
            || _previewCancellation == null
        )
        {
            HidePreviewSlot(rect);
            return;
        }

        var subject = new NativeCardPreviewSubject
        {
            TemplateId = entity.TemplateId,
            Tier = entity.Tier,
            DisplaySpan = Mathf.Clamp(entity.DisplaySpan, 1, 3),
            EnchantmentType = entity.EnchantmentType,
            Attributes = entity.Attributes,
            InstanceIdPrefix = $"bpp-combat-impact-{generation}",
        };
        _previewOwner.Register(subject, rect);
        _pendingPreviewCount++;
        _ = LoadNativePreview(
            _previewScope,
            _previewOwner,
            subject,
            rect,
            generation,
            _previewCancellation.Token
        );
    }

    private static float ResolveEntityPreviewWidth(CombatImpactEntity entity, float height) =>
        entity.Hero.HasValue ? height : height * Mathf.Clamp(entity.DisplaySpan, 1, 3);

    private async Task LoadNativePreview(
        INativeCardPreviewScope scope,
        NativePreviewOwner owner,
        NativeCardPreviewSubject subject,
        RectTransform slot,
        int generation,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var outcome = await scope.AcquireAsync(subject, cancellationToken);
            var session = outcome.Session;
            if (
                generation != _renderGeneration
                || slot == null
                || cancellationToken.IsCancellationRequested
            )
            {
                session?.Dispose();
                return;
            }

            if (session == null)
            {
                HidePreviewSlot(slot);
                return;
            }

            var show = session.ShowArtworkOnly();
            if (
                show.Status
                is not NativePreviewActionStatus.Applied
                    and not NativePreviewActionStatus.AlreadyApplied
            )
            {
                session.Dispose();
                HidePreviewSlot(slot);
                return;
            }

            if (
                session.FitInto(slot, NativeCardPreviewHorizontalAlignment.Left)
                != NativeCardPreviewSlotFitResult.Applied
            )
            {
                session.Dispose();
                HidePreviewSlot(slot);
                return;
            }

            if (
                !NativeCardPreviewSlotFitter.TryAlignVisibleArtworkLeft(
                    session.Rect,
                    slot,
                    out var visibleWidth
                ) || !FitPreviewColumnToVisibleWidth(slot, visibleWidth)
            )
            {
                session.Dispose();
                HidePreviewSlot(slot);
                BppLog.WarnEvent(
                    PostCombatImpactLogEvents.InteractionDegraded,
                    PostCombatImpactLogEvents.ReasonCode.Bind(
                        PostCombatImpactReasonCode.EntityPreviewUnavailable
                    )
                );
                return;
            }

            _previewSessions.Add(session);
            owner.Reveal(session.Root);
        }
        catch (OperationCanceledException)
        {
            // Replacement and dismissal cancel compact native previews as one presentation.
        }
        catch (Exception ex)
        {
            HidePreviewSlot(slot);
            BppLog.WarnEvent(
                PostCombatImpactLogEvents.InteractionDegraded,
                ex,
                PostCombatImpactLogEvents.ReasonCode.Bind(
                    PostCombatImpactReasonCode.EntityPreviewUnavailable
                )
            );
        }
        finally
        {
            if (generation == _renderGeneration)
                _pendingPreviewCount = Mathf.Max(0, _pendingPreviewCount - 1);
        }
    }

    private async Task LoadHero(Image image, RectTransform slot, EHero hero, int generation)
    {
        try
        {
            var outcome = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
            if (generation == _renderGeneration && image != null && outcome?.Sprite != null)
            {
                image.sprite = outcome.Sprite;
                image.enabled = true;
            }
            else if (generation == _renderGeneration)
            {
                HidePreviewSlot(slot);
            }
        }
        catch (Exception ex)
        {
            if (generation == _renderGeneration)
                HidePreviewSlot(slot);
            BppLog.WarnEvent(
                PostCombatImpactLogEvents.InteractionDegraded,
                ex,
                PostCombatImpactLogEvents.ReasonCode.Bind(
                    PostCombatImpactReasonCode.EntityPreviewUnavailable
                )
            );
        }
        finally
        {
            if (generation == _renderGeneration)
                _pendingPreviewCount = Mathf.Max(0, _pendingPreviewCount - 1);
        }
    }

    private static void HidePreviewSlot(RectTransform slot)
    {
        if (slot == null)
            return;
        var column = slot.parent;
        if (column != null && column.name == "ImpactEntityPreviewColumn")
            column.gameObject.SetActive(false);
        else
            slot.gameObject.SetActive(false);
    }

    private static bool FitPreviewColumnToVisibleWidth(RectTransform slot, float visibleWidth)
    {
        if (
            slot == null
            || slot.parent is not RectTransform column
            || column.name != "ImpactEntityPreviewColumn"
            || visibleWidth <= 0f
        )
            return false;

        column.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, visibleWidth);
        if (column.TryGetComponent<LayoutElement>(out var layout))
        {
            layout.minWidth = visibleWidth;
            layout.preferredWidth = visibleWidth;
        }

        if (column.parent is RectTransform row)
            LayoutRebuilder.ForceRebuildLayoutImmediate(row);
        return true;
    }

    private static TMP_Text CloneText(
        TMP_Text template,
        Transform parent,
        string content,
        float fontScale,
        float flexibleWidth = 0f,
        float minWidth = -1f,
        bool autoSize = false,
        float minimumFontScale = 1f
    )
    {
        var text = Object.Instantiate(template, parent, worldPositionStays: false);
        text.name = "BppPostCombatImpactText";
        text.gameObject.SetActive(true);
        text.fontSize = Mathf.Max(1f, template.fontSize * fontScale);
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = Mathf.Max(1f, text.fontSize * minimumFontScale);
        text.enableAutoSizing = autoSize;
        text.color = new Color32(248, 238, 213, 255);
        text.alpha = 1f;
        text.margin = Vector4.zero;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.richText = content.Contains("<sprite", StringComparison.Ordinal);
        if (UnicodeFontCoverage.ContainsCjk(content))
        {
            var role =
                text.font?.name.IndexOf("Serif", StringComparison.OrdinalIgnoreCase) >= 0
                    ? NativeGameTypography.OwnedTextRole.Heading
                    : NativeGameTypography.OwnedTextRole.Body;
            var outcome = NativeGameTypography.PrepareOwnedText(role, out var preparation);
            if (
                outcome != NativeGameTypography.Outcome.Ready
                || preparation == null
                || preparation.Apply(text) != NativeGameTypography.Outcome.Applied
            )
                throw new InvalidOperationException(
                    $"Native {role} typography is not ready for CJK tooltip content."
                );
        }
        text.text = content;
        AddLayout(text.gameObject, preferredHeight: -1f, flexibleWidth, minWidth: minWidth);
        return text;
    }

    private static RectTransform CreateVertical(string name, Transform parent, float spacing)
    {
        var gameObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );
        var rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, worldPositionStays: false);
        var layout = gameObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        var fitter = gameObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rect;
    }

    private static RectTransform CreateHorizontal(
        string name,
        Transform parent,
        float spacing,
        float preferredHeight
    )
    {
        var gameObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(LayoutElement)
        );
        var rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, worldPositionStays: false);
        var layout = gameObject.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        AddLayout(gameObject, preferredHeight, flexibleWidth: 1f);
        return rect;
    }

    private static LayoutElement AddLayout(
        GameObject gameObject,
        float preferredHeight,
        float flexibleWidth = 0f,
        float preferredWidth = -1f,
        float minWidth = -1f
    )
    {
        var layout =
            gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = false;
        layout.preferredHeight = preferredHeight;
        layout.preferredWidth = preferredWidth;
        layout.minWidth = minWidth;
        layout.flexibleWidth = flexibleWidth;
        return layout;
    }

    private Rect GetCanvasLocalBounds(RectTransform rect, RectTransform canvasRect)
    {
        rect.GetWorldCorners(_worldCorners);
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var corner in _worldCorners)
        {
            var local = canvasRect.InverseTransformPoint(corner);
            minX = Mathf.Min(minX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxX = Mathf.Max(maxX, local.x);
            maxY = Mathf.Max(maxY, local.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private Rect GetPrimaryVisibleBounds(CardTooltipController primary, RectTransform canvasRect)
    {
        var bounds = GetPrimaryFrameBounds(primary, canvasRect);
        if (
            primary.cooldownClock != null
            && primary.cooldownClock.gameObject.activeInHierarchy
            && primary.cooldownClock.transform is RectTransform cooldownRect
        )
        {
            bounds = Union(bounds, GetCanvasLocalBounds(cooldownRect, canvasRect));
        }
        return bounds;
    }

    private Rect GetPrimaryFrameBounds(CardTooltipController primary, RectTransform canvasRect)
    {
        var bounds = GetCanvasLocalBounds(primary.PositioningRectTransform, canvasRect);
        if (primary.CanvasContentRectTransform != null)
        {
            bounds = Union(
                bounds,
                GetCanvasLocalBounds(primary.CanvasContentRectTransform, canvasRect)
            );
        }
        return bounds;
    }

    private static RectTransform? FindDescendant(Transform? root, string childName)
    {
        if (root == null)
            return null;
        for (var index = 0; index < root.childCount; index++)
        {
            var child = root.GetChild(index);
            if (
                string.Equals(child.name, childName, StringComparison.Ordinal)
                && child is RectTransform matching
            )
                return matching;
            var nested = FindDescendant(child, childName);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static float CanvasUnitsPerLocalUnit(RectTransform rect, RectTransform canvasRect)
    {
        var origin = canvasRect.InverseTransformPoint(rect.TransformPoint(Vector3.zero));
        var horizontalUnit = canvasRect.InverseTransformPoint(rect.TransformPoint(Vector3.right));
        return Mathf.Max(
            0.0001f,
            Vector2.Distance(
                new Vector2(origin.x, origin.y),
                new Vector2(horizontalUnit.x, horizontalUnit.y)
            )
        );
    }

    private static float ResolveVerticalAdjustment(Rect rect, Rect bounds)
    {
        if (rect.height > bounds.height + PlacementEpsilon)
            return bounds.yMax - rect.yMax;

        var adjustment = rect.yMax > bounds.yMax ? bounds.yMax - rect.yMax : 0f;
        if (rect.yMin + adjustment < bounds.yMin)
            adjustment += bounds.yMin - (rect.yMin + adjustment);
        return adjustment;
    }

    private static void LogPlacementDegradationOnce(
        bool degraded,
        ref bool wasLogged,
        PostCombatImpactReasonCode reasonCode
    )
    {
        if (!degraded)
        {
            wasLogged = false;
            return;
        }
        if (wasLogged)
            return;

        wasLogged = true;
        BppLog.WarnEvent(
            PostCombatImpactLogEvents.InteractionDegraded,
            PostCombatImpactLogEvents.ReasonCode.Bind(reasonCode)
        );
    }

    private static void TranslateRect(
        RectTransform rect,
        RectTransform canvasRect,
        Vector2 canvasLocalDelta
    )
    {
        if (canvasLocalDelta == Vector2.zero)
            return;

        rect.position += canvasRect.TransformVector(
            new Vector3(canvasLocalDelta.x, canvasLocalDelta.y)
        );
    }

    private static Rect Union(Rect first, Rect second) =>
        Rect.MinMaxRect(
            Mathf.Min(first.xMin, second.xMin),
            Mathf.Min(first.yMin, second.yMin),
            Mathf.Max(first.xMax, second.xMax),
            Mathf.Max(first.yMax, second.yMax)
        );

    private static Rect Inset(Rect rect, float inset)
    {
        var horizontalInset = Mathf.Min(inset, rect.width * 0.5f);
        var verticalInset = Mathf.Min(inset, rect.height * 0.5f);
        return Rect.MinMaxRect(
            rect.xMin + horizontalInset,
            rect.yMin + verticalInset,
            rect.xMax - horizontalInset,
            rect.yMax - verticalInset
        );
    }

    private static (string Label, string IconKey) ResolveEffect(
        CombatImpactKind kind,
        string nativeAttributeKey,
        CombatImpactEventSurface surface
    )
    {
        var iconKey = kind switch
        {
            CombatImpactKind.Destroy => "Destroy",
            CombatImpactKind.AttributeChange => AttributeIconKey(nativeAttributeKey),
            _ => nativeAttributeKey,
        };
        var display = NativeTagTypography.Resolve(
            kind == CombatImpactKind.Destroy ? "Destroy" : nativeAttributeKey
        );
        var label = kind switch
        {
            CombatImpactKind.Destroy => T("摧毁", "Destroy"),
            CombatImpactKind.AttributeChange => CombatImpactAttributeLabel.Resolve(
                nativeAttributeKey,
                surface,
                IsChinese()
            ),
            _ => display.Label,
        };
        return (label, iconKey);
    }

    private static string? CriticalMarker()
    {
        var marker =
            Data.TooltipTypography?.GetKeywordStringWithIconNoScale(
                "CritChance",
                string.Empty,
                useNumberFont: false
            ) ?? string.Empty;
        return string.IsNullOrWhiteSpace(marker) ? null : marker;
    }

    private static string AttributeIconKey(string key) =>
        key switch
        {
            "Health" or "HealthMax" or "HealAmount" => "HealAmount",
            "HealthRegen" => "RegenApplyAmount",
            "Rage" or "RageMax" => "RageApplyAmount",
            "Burn" => "BurnApplyAmount",
            "Poison" => "PoisonApplyAmount",
            "Shield" => "ShieldApplyAmount",
            "DamageCrit" => "CritChance",
            "CardModifyAttribute" or "PlayerModifyAttribute" => string.Empty,
            _ => key,
        };

    private static bool IsChinese() =>
        L.CurrentLanguageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static bool IsTypographyReadyForCurrentLocale()
    {
        if (!IsChinese())
            return true;

        return NativeGameTypography.PrepareOwnedText(
                NativeGameTypography.OwnedTextRole.Heading,
                out _
            ) == NativeGameTypography.Outcome.Ready
            && NativeGameTypography.PrepareOwnedText(NativeGameTypography.OwnedTextRole.Body, out _)
                == NativeGameTypography.Outcome.Ready;
    }

    private static string T(string chinese, string english) => IsChinese() ? chinese : english;

    private sealed class ImpactContentBlock
    {
        internal ImpactContentBlock(GameObject root, IReadOnlyList<GameObject> detailRows)
        {
            Root = root;
            DetailRows = detailRows;
        }

        internal GameObject Root { get; }
        internal IReadOnlyList<GameObject> DetailRows { get; }
        internal GameObject? LeadingDivider { get; set; }

        internal void Restore()
        {
            Root.SetActive(true);
            LeadingDivider?.SetActive(true);
            foreach (var row in DetailRows)
                row.SetActive(true);
        }
    }

    private enum PairSide
    {
        None,
        ImpactRight,
        ImpactLeft,
    }

    private sealed class NativePreviewOwner : INativeCardPreviewOwner
    {
        private readonly Dictionary<NativeCardPreviewSubject, RectTransform> _parents = new();

        internal NativePreviewOwner(int layer) => Layer = layer;

        public int Layer { get; }

        internal void Register(NativeCardPreviewSubject subject, RectTransform parent) =>
            _parents[subject] = parent;

        public Transform? ResolveParent(NativeCardPreviewSubject subject) =>
            _parents.TryGetValue(subject, out var parent) && parent != null ? parent : null;

        public void PrepareWhileInactive(NativeCardPreviewOwnerContext context)
        {
            var canvasGroup =
                context.Root.GetComponent<CanvasGroup>()
                ?? context.Root.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        public void OnAcquired(NativeCardPreviewOwnerContext context)
        {
            foreach (var graphic in context.Root.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            foreach (var collider in context.Root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        public void BeforeRelease(NativeCardPreviewOwnerContext context)
        {
            var canvasGroup = context.Root.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
                canvasGroup.alpha = 0f;
        }

        public void ReportFailure(NativeCardPreviewFailure failure)
        {
            var reason = PostCombatImpactLogEvents.ReasonCode.Bind(
                PostCombatImpactReasonCode.EntityPreviewUnavailable
            );
            if (failure.Exception == null)
                BppLog.WarnEvent(PostCombatImpactLogEvents.InteractionDegraded, reason);
            else
                BppLog.WarnEvent(
                    PostCombatImpactLogEvents.InteractionDegraded,
                    failure.Exception,
                    reason
                );
        }

        internal void Reveal(GameObject root)
        {
            if (root == null)
                return;
            var canvasGroup = root.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
                canvasGroup.alpha = 1f;
        }
    }

    private sealed class CanvasGroupGate
    {
        private readonly CanvasGroup _group;
        private readonly bool _ownedGroup;
        private readonly float _originalAlpha;
        private readonly bool _originalInteractable;
        private readonly bool _originalBlocksRaycasts;
        private readonly bool _originalIgnoreParentGroups;
        private readonly bool _forceNonInteractive;
        private bool _restored;

        private CanvasGroupGate(object controller, GameObject target, bool forceNonInteractive)
        {
            Controller = controller;
            _forceNonInteractive = forceNonInteractive;
            _group = target.GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = target.AddComponent<CanvasGroup>();
                _ownedGroup = true;
            }
            _originalAlpha = _group.alpha;
            _originalInteractable = _group.interactable;
            _originalBlocksRaycasts = _group.blocksRaycasts;
            _originalIgnoreParentGroups = _group.ignoreParentGroups;
            SetAlpha(0f);
        }

        internal object Controller { get; }

        internal float Alpha => _group == null ? 0f : _group.alpha;

        internal static CanvasGroupGate Create(
            object controller,
            GameObject target,
            bool forceNonInteractive = false
        ) => new(controller, target, forceNonInteractive);

        internal void SetAlpha(float alpha)
        {
            if (_group == null || _restored)
                return;

            _group.alpha = Mathf.Clamp01(alpha);
            var interactive = _group.alpha >= 1f - PlacementEpsilon;
            _group.interactable = !_forceNonInteractive && interactive && _originalInteractable;
            _group.blocksRaycasts = !_forceNonInteractive && interactive && _originalBlocksRaycasts;
        }

        internal void Restore()
        {
            if (_restored || _group == null)
                return;

            _restored = true;
            _group.alpha = _originalAlpha;
            _group.interactable = _originalInteractable;
            _group.blocksRaycasts = _originalBlocksRaycasts;
            _group.ignoreParentGroups = _originalIgnoreParentGroups;
            if (_ownedGroup)
                Object.Destroy(_group);
        }
    }

    private sealed class NativeAuxiliaryHostState
    {
        private readonly RectTransform? _auxParentRect;
        private readonly Vector2 _auxParentSizeDelta;
        private readonly Vector2 _auxParentAnchorMin;
        private readonly Vector2 _auxParentAnchorMax;
        private readonly Vector2 _auxParentPivot;
        private readonly Vector3 _auxParentAnchoredPosition;
        private readonly Vector2 _positioningSizeDelta;
        private readonly VerticalLayoutGroup? _layout;
        private readonly RectOffset? _padding;
        private readonly float _spacing;
        private readonly bool _headerWasActive;
        private readonly bool _bodyWasActive;
        private readonly bool _dividerWasActive;
        private readonly Sprite? _backgroundSprite;
        private readonly bool _backgroundImageWasEnabled;

        private NativeAuxiliaryHostState(AuxiliaryTooltipController controller)
        {
            Controller = controller;
            _auxParentRect = controller.auxParent?.transform as RectTransform;
            _auxParentSizeDelta = _auxParentRect?.sizeDelta ?? Vector2.zero;
            _auxParentAnchorMin = _auxParentRect?.anchorMin ?? Vector2.zero;
            _auxParentAnchorMax = _auxParentRect?.anchorMax ?? Vector2.zero;
            _auxParentPivot = _auxParentRect?.pivot ?? Vector2.zero;
            _auxParentAnchoredPosition = _auxParentRect?.anchoredPosition3D ?? Vector3.zero;
            _positioningSizeDelta = controller.PositioningRectTransform.sizeDelta;
            _layout = controller.auxParent?.GetComponent<VerticalLayoutGroup>();
            _padding =
                _layout == null
                    ? null
                    : new RectOffset(
                        _layout.padding.left,
                        _layout.padding.right,
                        _layout.padding.top,
                        _layout.padding.bottom
                    );
            _spacing = _layout?.spacing ?? 0f;
            _headerWasActive =
                controller.headerText != null && controller.headerText.gameObject.activeSelf;
            _bodyWasActive =
                controller.bodyText != null && controller.bodyText.gameObject.activeSelf;
            _dividerWasActive =
                controller.dividerParent != null && controller.dividerParent.activeSelf;
            _backgroundSprite = controller.backgroundImage?.sprite;
            _backgroundImageWasEnabled =
                controller.backgroundImage != null && controller.backgroundImage.enabled;
        }

        internal AuxiliaryTooltipController Controller { get; }

        internal static NativeAuxiliaryHostState Capture(AuxiliaryTooltipController controller) =>
            new(controller);

        internal void Restore(bool restoreContentVisibility)
        {
            if (Controller == null)
                return;

            Controller.PositioningRectTransform.sizeDelta = _positioningSizeDelta;
            if (_auxParentRect != null)
            {
                _auxParentRect.anchorMin = _auxParentAnchorMin;
                _auxParentRect.anchorMax = _auxParentAnchorMax;
                _auxParentRect.pivot = _auxParentPivot;
                _auxParentRect.anchoredPosition3D = _auxParentAnchoredPosition;
                _auxParentRect.sizeDelta = _auxParentSizeDelta;
            }
            if (_layout != null && _padding != null)
            {
                _layout.padding = new RectOffset(
                    _padding.left,
                    _padding.right,
                    _padding.top,
                    _padding.bottom
                );
                _layout.spacing = _spacing;
            }
            if (restoreContentVisibility)
            {
                if (Controller.headerText != null)
                    Controller.headerText.gameObject.SetActive(_headerWasActive);
                if (Controller.bodyText != null)
                    Controller.bodyText.gameObject.SetActive(_bodyWasActive);
                if (Controller.dividerParent != null)
                    Controller.dividerParent.SetActive(_dividerWasActive);
            }
            if (Controller.backgroundImage != null)
            {
                Controller.backgroundImage.sprite = _backgroundSprite;
                Controller.backgroundImage.enabled = _backgroundImageWasEnabled;
            }
        }
    }

    public void Dispose()
    {
        Hide();
        CleanupCustomContent();
    }
}
