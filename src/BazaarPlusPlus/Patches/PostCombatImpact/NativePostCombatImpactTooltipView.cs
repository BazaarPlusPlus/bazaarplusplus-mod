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
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.UI.Tooltips;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Patches.PostCombatImpact;

internal sealed class NativePostCombatImpactTooltipView : IPostCombatImpactTooltipView
{
    private const float EntityPreviewHeight = 52f;
    private const float SkillPreviewTrailingPadding = 4f;
    private const float TooltipPreferredWidth = 660f;
    private const float TooltipReadableWidth = 360f;
    private const float TooltipGap = 18f;
    private const float CanvasMargin = 16f;
    private const int NativeBottomPaddingReduction = 4;
    private const float PlacementEpsilon = 0.5f;
    private const float VisibilityFadeDuration = 0.1f;
    private const float PanelTitleFontScale = 0.84f;
    private const float ModeLabelFontScale = 0.66f;
    private const float HeaderHintFontScale = 0.6f;
    private const float SummaryFontScale = 0.75f;
    private const float TriggerSummaryFontScale = 0.64f;
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
    private static readonly NativePairedTooltipOptions PairedOptions = new(
        preferredContentWidth: TooltipPreferredWidth,
        readableContentWidth: TooltipReadableWidth,
        gap: TooltipGap,
        canvasMargin: CanvasMargin,
        fadeDuration: VisibilityFadeDuration,
        nativeBottomPaddingReduction: NativeBottomPaddingReduction
    );

    private readonly INativeCardPreviewHost _previewHost;
    private readonly NativePairedTooltipSession _session;
    private readonly List<LayoutElement> _metricColumns = [];
    private readonly List<INativeCardPreviewSession> _previewSessions = [];
    private readonly List<ImpactContentBlock> _causedBlocks = [];
    private readonly List<ImpactContentBlock> _receivedBlocks = [];
    private GameObject? _contentRoot;
    private GameObject? _causedRoot;
    private GameObject? _receivedRoot;
    private TMP_Text? _causedMoreText;
    private TMP_Text? _receivedMoreText;
    private NativePreviewOwner? _previewOwner;
    private INativeCardPreviewScope? _previewScope;
    private CancellationTokenSource? _previewCancellation;
    private bool _overflowDegradedLogged;
    private bool _widthDegradedLogged;
    private bool _topAlignmentDegradedLogged;
    private bool _receivedPerspectiveAvailable;
    private int _pendingPreviewCount;
    private CombatImpactPerspective _activePerspective = CombatImpactPerspective.Caused;

    internal NativePostCombatImpactTooltipView(
        INativeCardPreviewHost previewHost,
        NativePairedTooltipHost tooltipHost
    )
    {
        _previewHost = previewHost ?? throw new ArgumentNullException(nameof(previewHost));
        if (tooltipHost == null)
            throw new ArgumentNullException(nameof(tooltipHost));
        // The session's generation is bumped before this callback runs, so an async preview
        // continuation that races cleanup can no longer pass its own identity check.
        _session = tooltipHost.Acquire(this, ReleaseOwnerResources);
    }

    public string Header => T("本场影响", "Combat Impact");

    public bool IsReadyToReveal => _pendingPreviewCount == 0;

    public bool IsContentActive => _session.IsContentActive;

    public bool CanSwitchPerspective => IsContentActive && _receivedPerspectiveAvailable;

    public void PrepareNativePrimary(CardTooltipController primary) =>
        _session.PreparePrimary(primary);

    public void CancelPreparedNativePrimary(CardTooltipController primary) =>
        _session.CancelPreparedPrimary(primary);

    public void PrepareNativeAuxiliary(AuxiliaryTooltipController auxiliary) =>
        _session.PrepareAuxiliary(auxiliary);

    public void CancelPreparedNativeAuxiliary(AuxiliaryTooltipController auxiliary) =>
        _session.CancelPreparedAuxiliary(auxiliary);

    public bool Show(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        string entityName,
        bool isSkill,
        CombatImpactSource? source,
        CombatImpactReceived? received,
        CombatImpactPerspective perspective
    )
    {
        if (!IsTypographyReadyForCurrentLocale())
            return false;

        // The host owns taking over the native pair; it self-cleans and returns false (never
        // throws) so the caller keeps reporting AuxiliaryTooltipContentUnavailable rather than
        // TooltipRenderException.
        if (!_session.TryOpen(auxiliary, primary, PairedOptions))
            return false;

        var generation = _session.Generation;
        _receivedPerspectiveAvailable = received != null;
        _activePerspective = _receivedPerspectiveAvailable
            ? perspective
            : CombatImpactPerspective.Caused;

        var root = CreateVertical("BppPostCombatImpactContent", auxiliary.auxParent.transform, 8f);
        _contentRoot = root.gameObject;
        var rootLayout = root.GetComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(0, 0, 14, 0);
        AddLayout(
            root.gameObject,
            preferredHeight: -1f,
            preferredWidth: TooltipPreferredWidth,
            minWidth: TooltipPreferredWidth
        );
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, TooltipPreferredWidth);
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
            isSkill,
            source,
            _receivedPerspectiveAvailable,
            generation
        );
        BuildReceived(
            auxiliary.headerText,
            auxiliary.bodyText,
            receivedRoot,
            isSkill,
            received,
            _receivedPerspectiveAvailable,
            generation
        );
        ApplyPerspectiveVisibility(_activePerspective);
        return _session.AttachContent(_contentRoot, ApplyMetricColumnWidth);
    }

    /// <summary>
    /// Retunes the metric columns whenever the host resizes the panel.
    /// </summary>
    /// <remarks>
    /// The column ratio and its clamps are Combat Impact design parameters, so they stay here
    /// instead of becoming defaults inside the shared host. Only a float crosses the boundary.
    /// </remarks>
    private void ApplyMetricColumnWidth(float contentWidth)
    {
        var metricWidth = Mathf.Clamp(
            contentWidth * 0.3f,
            MetricColumnMinWidth,
            MetricColumnPreferredWidth
        );
        foreach (var metricColumn in _metricColumns)
            metricColumn.minWidth = metricWidth;
    }

    public bool SetPerspective(CombatImpactPerspective perspective, Transform anchor)
    {
        if (perspective == CombatImpactPerspective.Received && !_receivedPerspectiveAvailable)
            return false;

        if (_contentRoot == null || _causedRoot == null || _receivedRoot == null)
            return false;

        var previousPerspective = _activePerspective;
        // The host only masks and rebuilds; choosing the perspective, deciding the swap failed,
        // and rolling it back are Combat Impact decisions and stay here.
        using (_session.BeginMaskedLayout())
        {
            _activePerspective = perspective;
            ApplyPerspectiveVisibility(perspective);
            _session.RebuildLayout();
            var result = _session.Position(anchor, ResolveContentBudget());
            if (result.Positioned)
            {
                LogPlacementDegradations(result);
                return true;
            }

            _activePerspective = previousPerspective;
            ApplyPerspectiveVisibility(previousPerspective);
            _session.RebuildLayout();
            // The rollback pass must reach the same reason-code logic as the forward pass:
            // LogPlacementDegradationOnce clears its latch whenever a dimension stops being
            // degraded, so discarding this result changes how many records later passes emit.
            LogPlacementDegradations(_session.Position(anchor, ResolveContentBudget()));
            return false;
        }
    }

    public bool Position(
        AuxiliaryTooltipController auxiliary,
        CardTooltipController primary,
        Transform anchor
    )
    {
        if (!_session.OwnsAuxiliary(auxiliary) || !_session.OwnsPrimary(primary))
            return false;

        var result = _session.Position(anchor, ResolveContentBudget());
        LogPlacementDegradations(result);
        return result.Positioned;
    }

    /// <summary>
    /// Turns a placement outcome into this feature's reason codes.
    /// </summary>
    /// <remarks>
    /// Kept here rather than in the host: the once-only latches and their reset-on-recovery
    /// behavior decide how many records reach the log, and that is Combat Impact's contract.
    /// A pass that never positioned logs nothing and leaves the latches untouched.
    /// </remarks>
    private void LogPlacementDegradations(PlacementResult result)
    {
        if (!result.Positioned)
            return;

        LogPlacementDegradationOnce(
            result.Overflowed,
            ref _overflowDegradedLogged,
            PostCombatImpactReasonCode.PairPlacementOverflowed
        );
        LogPlacementDegradationOnce(
            result.WidthBelowReadable,
            ref _widthDegradedLogged,
            PostCombatImpactReasonCode.PairPlacementTooNarrow
        );
        LogPlacementDegradationOnce(
            result.TopAlignmentAdjusted,
            ref _topAlignmentDegradedLogged,
            PostCombatImpactReasonCode.PairTopAlignmentAdjusted
        );
    }

    public void Reveal() => _session.Reveal();

    public void Hide() => _session.Hide();

    public bool OnNativeTooltipChanging(CardTooltipController controller)
    {
        if (!_session.OwnsPrimary(controller))
            return false;

        _session.Hide();
        return true;
    }

    public bool OnNativeAuxiliaryTooltipShowing(AuxiliaryTooltipController controller)
    {
        if (!_session.OwnsAuxiliary(controller))
        {
            // Someone else is about to show the native auxiliary tooltip this session had only
            // prepared. Handing the snapshot back here is what keeps the native layout's padding
            // and anchors from staying permanently rewritten.
            _session.ReleasePrepared(controller);
            return false;
        }

        _session.Release(restoreNativeContent: true);
        return true;
    }

    public bool OnNativeAuxiliaryTooltipHiding(AuxiliaryTooltipController controller)
    {
        if (!_session.OwnsAuxiliary(controller))
            return false;

        // Restore the reusable host geometry for native fade-out, but keep its original content
        // inactive until the next real native show. Reactivating the header here lets a later
        // native fade/tween expose a detached title at this host's old paired position.
        //
        // This is also the recovery path for a fade coroutine that never completes: it is hosted
        // on the native controller, so deactivating that MonoBehaviour kills it silently and no
        // generation check can rescue a callback that never fires.
        _session.ForceSettle();
        return true;
    }

    /// <summary>
    /// Releases this feature's own resources during <see cref="NativePairedTooltipSession.Release"/>.
    /// </summary>
    /// <remarks>
    /// Invoked by the host after it has stopped the fade and bumped the generation, but before it
    /// destroys UI or restores native state. Relying on that ordering is what keeps a cancelled
    /// preview continuation from re-attaching to a presentation that is being torn down.
    /// </remarks>
    private void ReleaseOwnerResources()
    {
        DisposeNativePreviews();
        _contentRoot = null;
        _causedRoot = null;
        _receivedRoot = null;
        _causedMoreText = null;
        _receivedMoreText = null;
        _previewOwner = null;
        _metricColumns.Clear();
        _causedBlocks.Clear();
        _receivedBlocks.Clear();
        _pendingPreviewCount = 0;
        _overflowDegradedLogged = false;
        _widthDegradedLogged = false;
        _topAlignmentDegradedLogged = false;
        _receivedPerspectiveAvailable = false;
        _activePerspective = CombatImpactPerspective.Caused;
    }

    private IPairedContentBudget ResolveContentBudget() =>
        _activePerspective == CombatImpactPerspective.Caused
            ? new ImpactContentBudget(_causedBlocks, _causedMoreText)
            : new ImpactContentBudget(_receivedBlocks, _receivedMoreText);

    private static void ShowMoreRow(TMP_Text moreText, int hiddenCount)
    {
        moreText.text = T($"另有 {hiddenCount} 项", $"+{hiddenCount} more");
        moreText.transform.parent.gameObject.SetActive(true);
    }

    /// <summary>
    /// Combat Impact's trim order, driven by the host while it fits the panel to the canvas.
    /// </summary>
    /// <remarks>
    /// Reproduces the original nested loop exactly: walk the blocks from last to first, dropping
    /// each block's detail rows from last to first, then the block itself (with its leading
    /// divider). The host decides <i>whether</i> to keep trimming; this type only decides
    /// <i>what</i> goes next, so no measurement crosses back over the boundary.
    /// </remarks>
    private sealed class ImpactContentBudget : IPairedContentBudget
    {
        private readonly List<ImpactContentBlock> _blocks;
        private readonly TMP_Text? _moreText;
        private int _blockIndex = -1;
        private int _rowIndex = -1;
        private int _hiddenCount;
        private bool _started;

        internal ImpactContentBudget(List<ImpactContentBlock> blocks, TMP_Text? moreText)
        {
            _blocks = blocks;
            _moreText = moreText;
        }

        public void RestoreAll()
        {
            if (_moreText == null)
                return;

            foreach (var block in _blocks)
                block.Restore();
            _moreText.transform.parent.gameObject.SetActive(false);
            _blockIndex = _blocks.Count - 1;
            _rowIndex = _blockIndex >= 0 ? _blocks[_blockIndex].DetailRows.Count - 1 : -1;
            _hiddenCount = 0;
            _started = true;
        }

        public bool TryShrinkOneStep()
        {
            if (_moreText == null || !_started || _blockIndex < 0)
                return false;

            var block = _blocks[_blockIndex];
            if (_rowIndex >= 0)
            {
                block.DetailRows[_rowIndex].SetActive(false);
                _rowIndex--;
                ShowMoreRow(_moreText, ++_hiddenCount);
                return true;
            }

            block.Root.SetActive(false);
            block.LeadingDivider?.SetActive(false);
            _blockIndex--;
            _rowIndex = _blockIndex >= 0 ? _blocks[_blockIndex].DetailRows.Count - 1 : -1;
            ShowMoreRow(_moreText, ++_hiddenCount);
            return true;
        }
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
        bool isSkill,
        CombatImpactSource? source,
        bool canSwitchPerspective,
        int generation
    )
    {
        BuildPanelHeader(
            headingTemplate,
            bodyTemplate,
            root,
            CombatImpactPerspective.Caused,
            isSkill,
            canSwitchPerspective
        );
        BuildImpactSummary(
            bodyTemplate,
            root,
            source == null
                ? string.Empty
                : CombatImpactMetricFormatter.CausedSummary(source, IsChinese())
        );
        AddDivider(root);

        if (source == null)
        {
            BuildEmptyState(
                bodyTemplate,
                root,
                T("本场未造成效果", "No effects caused in this combat")
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
        bool isSkill,
        CombatImpactReceived? received,
        bool canSwitchPerspective,
        int generation
    )
    {
        BuildPanelHeader(
            headingTemplate,
            bodyTemplate,
            root,
            CombatImpactPerspective.Received,
            isSkill,
            canSwitchPerspective
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
        CombatImpactPerspective perspective,
        bool isSkill,
        bool canSwitchPerspective
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
                ? isSkill
                    ? T("此技能造成", "CAUSED BY THIS SKILL")
                    : T("此卡造成", "CAUSED BY THIS CARD")
                : isSkill
                    ? T("此技能受到", "RECEIVED BY THIS SKILL")
                    : T("此卡受到", "RECEIVED BY THIS CARD"),
            ModeLabelFontScale,
            flexibleWidth: 1f
        );
        mode.alignment = TextAlignmentOptions.MidlineLeft;
        mode.color =
            perspective == CombatImpactPerspective.Caused ? CausedAccentColor : ReceivedAccentColor;

        if (!canSwitchPerspective)
            return;

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

    private void BuildImpactSummary(TMP_Text bodyTemplate, RectTransform parent, string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return;

        var row = CreateVertical("ImpactSummary", parent, 2f);
        AddLayout(row.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
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
        var triggerSources = CombatImpactMetricFormatter.TriggerSourceValues(group);
        var hasTriggerSummary = !string.IsNullOrWhiteSpace(triggerSources);
        var groupRoot = CreateVertical("ImpactCausedGroup", parent, hasTriggerSummary ? 6f : 4f);
        AddLayout(groupRoot.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        var headingRoot = groupRoot;
        if (hasTriggerSummary)
        {
            headingRoot = CreateVertical("ImpactGroupHeading", groupRoot, 0f);
            AddLayout(headingRoot.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        }
        BuildGroupHeader(
            textTemplate,
            headingRoot,
            group.Kind,
            group.NativeAttributeKey,
            group.Surface,
            group.AuthoritativeMetric is { Basis: CombatImpactAuthoritativeBasis.TotalAmount } total
                ? total.Value
                : group.ObservedValue,
            group.HasMixedValueDirections,
            CombatImpactMetricFormatter.Group(group, IsChinese(), CriticalMarker()),
            preferredHeight: hasTriggerSummary ? 44f : 48f
        );
        if (hasTriggerSummary)
        {
            BuildTriggerSummary(
                textTemplate,
                headingRoot,
                CombatImpactMetricFormatter.TriggerSourceLabel(IsChinese()),
                triggerSources,
                addLabelSpacing: !IsChinese()
            );
        }
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
            group.ObservedValue,
            group.HasMixedValueDirections,
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
        int? changeValue,
        bool hasMixedValueDirections,
        string metricText,
        float preferredHeight = 48f
    )
    {
        var header = CreateHorizontal(
            "ImpactGroupHeader",
            parent,
            10f,
            preferredHeight: preferredHeight
        );
        var (label, iconKey) = ResolveEffect(
            kind,
            nativeAttributeKey,
            surface,
            changeValue,
            hasMixedValueDirections
        );
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

    private static void BuildTriggerSummary(
        TMP_Text textTemplate,
        RectTransform parent,
        string label,
        string sources,
        bool addLabelSpacing
    )
    {
        var row = CreateVertical("ImpactTriggerSummary", parent, 0f);
        AddLayout(row.gameObject, preferredHeight: -1f, flexibleWidth: 1f);
        var labelColor = ColorUtility.ToHtmlStringRGB(CausedAccentColor);
        var labelSpacing = addLabelSpacing ? " " : string.Empty;
        var summary = $"<color=#{labelColor}>{label}</color>{labelSpacing}{sources}";
        var trigger = CloneText(
            textTemplate,
            row,
            summary,
            TriggerSummaryFontScale,
            flexibleWidth: 1f
        );
        trigger.alignment = TextAlignmentOptions.Left;
        trigger.color = DisclosureColor;
        trigger.alpha = 0.82f;
        trigger.textWrappingMode = TextWrappingModes.Normal;
        trigger.overflowMode = TextOverflowModes.Overflow;
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
        var trailingPadding = string.Equals(
            entity.TypeLabel,
            "Skill",
            StringComparison.OrdinalIgnoreCase
        )
            ? SkillPreviewTrailingPadding
            : 0f;
        _ = LoadNativePreview(
            _previewScope,
            _previewOwner,
            subject,
            rect,
            trailingPadding,
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
        float trailingPadding,
        int generation,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var outcome = await scope.AcquireAsync(subject, cancellationToken);
            var session = outcome.Session;
            if (
                generation != _session.Generation
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
                ) || !FitPreviewColumnToVisibleWidth(slot, visibleWidth, trailingPadding)
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
            if (generation == _session.Generation)
                _pendingPreviewCount = Mathf.Max(0, _pendingPreviewCount - 1);
        }
    }

    private async Task LoadHero(Image image, RectTransform slot, EHero hero, int generation)
    {
        try
        {
            var outcome = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
            if (generation == _session.Generation && image != null && outcome?.Sprite != null)
            {
                image.sprite = outcome.Sprite;
                image.enabled = true;
            }
            else if (generation == _session.Generation)
            {
                HidePreviewSlot(slot);
            }
        }
        catch (Exception ex)
        {
            if (generation == _session.Generation)
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
            if (generation == _session.Generation)
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

    private static bool FitPreviewColumnToVisibleWidth(
        RectTransform slot,
        float visibleWidth,
        float trailingPadding
    )
    {
        if (
            slot == null
            || slot.parent is not RectTransform column
            || column.name != "ImpactEntityPreviewColumn"
            || visibleWidth <= 0f
        )
            return false;

        var columnWidth = visibleWidth + Mathf.Max(0f, trailingPadding);
        column.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, columnWidth);
        if (column.TryGetComponent<LayoutElement>(out var layout))
        {
            layout.minWidth = columnWidth;
            layout.preferredWidth = columnWidth;
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
        float minWidth = -1f
    )
    {
        var text = Object.Instantiate(template, parent, worldPositionStays: false);
        text.name = "BppPostCombatImpactText";
        text.gameObject.SetActive(true);
        text.fontSize = Mathf.Max(1f, template.fontSize * fontScale);
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = text.fontSize;
        text.enableAutoSizing = false;
        text.color = new Color32(248, 238, 213, 255);
        text.alpha = 1f;
        text.margin = Vector4.zero;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.richText =
            content.Contains("<sprite", StringComparison.Ordinal)
            || content.Contains("<color", StringComparison.Ordinal);
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

    private static (string Label, string IconKey) ResolveEffect(
        CombatImpactKind kind,
        string nativeAttributeKey,
        CombatImpactEventSurface surface,
        int? changeValue,
        bool hasMixedValueDirections
    )
    {
        var (baseAttributeKey, variant) = SplitAttributeKey(nativeAttributeKey);
        var iconKey = kind switch
        {
            CombatImpactKind.Destroy => "Destroy",
            CombatImpactKind.AttributeChange => AttributeIconKey(baseAttributeKey, variant),
            _ => nativeAttributeKey,
        };
        var label = kind switch
        {
            CombatImpactKind.Destroy => T("摧毁", "Destroy"),
            CombatImpactKind.AttributeChange => CombatImpactAttributeLabel.Resolve(
                baseAttributeKey,
                surface,
                changeValue,
                IsChinese(),
                hasMixedValueDirections
            ),
            _ => NativeTagTypography.Resolve(nativeAttributeKey).Label,
        };
        if (
            kind == CombatImpactKind.AttributeChange
            && baseAttributeKey == "EnchantTargets"
            && !string.IsNullOrWhiteSpace(variant)
        )
        {
            var enchantment = NativeTagTypography.Resolve(variant).Label;
            label = T($"{enchantment}{label}", $"{enchantment} {label}");
        }
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

    private static (string BaseKey, string? Variant) SplitAttributeKey(string key)
    {
        var separator = key.IndexOf(':');
        return separator < 0 ? (key, null) : (key[..separator], key[(separator + 1)..]);
    }

    private static string AttributeIconKey(string key, string? variant) =>
        key switch
        {
            "EnchantTargets" when !string.IsNullOrWhiteSpace(variant) => variant,
            "Health" or "HealthMax" or "HealAmount" => "HealAmount",
            "HealthRegen" or "RegenRemoveAmount" => "RegenApplyAmount",
            "Rage" or "RageMax" => "RageApplyAmount",
            "Tempo" => "TempoApplyAmount",
            "Burn" or "BurnRemoveAmount" => "BurnApplyAmount",
            "Poison" or "PoisonRemoveAmount" => "PoisonApplyAmount",
            "RageRemoveAmount" => "RageApplyAmount",
            "Shield" or "ShieldRemoveAmount" => "ShieldApplyAmount",
            "EnchantRemoveTargets" => "EnchantTargets",
            "DamageCrit" => "CritChance",
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

    public void Dispose()
    {
        Hide();
        _session.Release(restoreNativeContent: true);
    }
}
