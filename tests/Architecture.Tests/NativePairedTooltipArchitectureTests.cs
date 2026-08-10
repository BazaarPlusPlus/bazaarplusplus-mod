#nullable enable
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Guards the seam between the shared paired-tooltip host and the features that consume it.
/// </summary>
public sealed class NativePairedTooltipArchitectureTests
{
    /// <summary>
    /// Ratchet. Before the extraction these types lived inside
    /// <c>Patches/PostCombatImpact/NativePostCombatImpactTooltipView.cs</c>, so this test failed;
    /// it passes only because the native paired-tooltip plumbing now has exactly one home.
    /// </summary>
    [Fact]
    public void Native_paired_tooltip_plumbing_lives_only_in_the_shared_host()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var moduleRoot = Path.Combine(sourceRoot, "GameInterop", "Tooltips");
        var forbidden = new[]
        {
            "CanvasGroupGate",
            "NativeAuxiliaryHostState",
            "PairSide",
            "TryCreateNativeBackground",
            "PrepareNativePresentation",
        };
        var violations = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            if (file.StartsWith(moduleRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            var source = File.ReadAllText(file);
            foreach (var token in forbidden.Where(source.Contains))
                violations.Add($"{Path.GetRelativePath(sourceRoot, file)}: {token}");
        }

        Assert.True(
            violations.Count == 0,
            "Native paired-tooltip host plumbing must stay in GameInterop/Tooltips instead of "
                + "being re-implemented inside a feature:\n"
                + string.Join("\n", violations)
        );
    }

    /// <summary>
    /// Guardrail, not a ratchet: this already held before the extraction, so passing proves nothing
    /// about the migration. It exists to stop feature vocabulary from leaking into the shared host
    /// later.
    /// </summary>
    [Fact]
    public void Shared_tooltip_adapters_do_not_reference_features_or_patches()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var moduleRoot = Path.Combine(sourceRoot, "GameInterop", "Tooltips");
        var violations = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            foreach (var line in File.ReadAllLines(file))
            {
                if (
                    line.StartsWith("using BazaarPlusPlus.Game.", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarPlusPlus.Patches", StringComparison.Ordinal)
                )
                    violations.Add($"{relative}: {line.Trim()}");
            }

            var source = File.ReadAllText(file);
            foreach (
                var token in new[] { "PostCombatImpact", "CombatImpact" }.Where(source.Contains)
            )
                violations.Add($"{relative}: mentions {token}");
        }

        Assert.True(
            violations.Count == 0,
            "GameInterop/Tooltips must stay feature-agnostic — no Game/Patches imports and no "
                + "feature vocabulary:\n"
                + string.Join("\n", violations)
        );
    }

    /// <summary>
    /// The Combat Impact view must consume the shared session rather than owning the native pair
    /// itself.
    /// </summary>
    [Fact]
    public void Combat_impact_tooltip_view_consumes_the_shared_session()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var view = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Patches",
                "PostCombatImpact",
                "NativePostCombatImpactTooltipView.cs"
            )
        );

        Assert.Contains(
            "using BazaarPlusPlus.GameInterop.Tooltips;",
            view,
            StringComparison.Ordinal
        );
        Assert.Contains("NativePairedTooltipSession", view, StringComparison.Ordinal);
        Assert.Contains("IPairedContentBudget", view, StringComparison.Ordinal);
        Assert.DoesNotContain("_renderGeneration", view, StringComparison.Ordinal);
        Assert.DoesNotContain("StartVisibilityFade", view, StringComparison.Ordinal);
        Assert.DoesNotContain("CleanupCustomContent", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Combat_impact_lock_keeps_owned_native_tooltips_raycast_transparent()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var controller = File.ReadAllText(
            Path.Combine(sourceRoot, "Game", "PostCombatImpact", "PostCombatImpactController.cs")
        );
        var patch = File.ReadAllText(
            Path.Combine(sourceRoot, "Patches", "PostCombatImpact", "PostCombatImpactRecapPatch.cs")
        );

        var lockCall = controller.IndexOf("primary.SetLockedFlag(true);", StringComparison.Ordinal);
        var initialSuppression = controller.IndexOf(
            "SuppressTooltipRaycasts(primary);",
            lockCall,
            StringComparison.Ordinal
        );
        Assert.True(lockCall >= 0 && initialSuppression > lockCall);

        Assert.Contains("OwnsNativeTooltip(controller)", controller, StringComparison.Ordinal);
        Assert.Contains(
            "canvasGroup.blocksRaycasts = false;",
            controller,
            StringComparison.Ordinal
        );
        Assert.Contains("canvasGroup.interactable = false;", controller, StringComparison.Ordinal);
        Assert.Contains(
            "[HarmonyPatch(typeof(BaseTooltipController), \"ToggleInteractabilityOnCanvas\")]",
            patch,
            StringComparison.Ordinal
        );
        Assert.Contains("[HarmonyPostfix]", patch, StringComparison.Ordinal);
        Assert.Contains("OnNativeTooltipInteractabilityChanged(", patch, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelling_a_prepared_auxiliary_keeps_it_concealed_until_native_teardown()
    {
        var host = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "GameInterop",
                "Tooltips",
                "NativePairedTooltipHost.cs"
            )
        );
        var methodStart = host.IndexOf(
            "internal void CancelPreparedAuxiliary",
            StringComparison.Ordinal
        );
        var methodEnd = host.IndexOf(
            "internal bool ReleaseForNativeShow",
            methodStart,
            StringComparison.Ordinal
        );

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = host[methodStart..methodEnd];
        Assert.Contains("ConcealNativeAuxiliary(auxiliary);", method, StringComparison.Ordinal);
        Assert.Contains(
            "RestorePreparedNativeHost(restoreContentVisibility: false);",
            method,
            StringComparison.Ordinal
        );
        // The auxParent gate is the only concealment the native fade cannot rewrite; the stale
        // cancel path must hand it back via reuse/ReleasePrepared/despawn, never restore it here.
        Assert.DoesNotContain("RestorePreparedAuxiliaryGate", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("ConcealNativeAuxiliary(auxiliary);", StringComparison.Ordinal)
                < method.IndexOf(
                    "RestorePreparedNativeHost(restoreContentVisibility: false);",
                    StringComparison.Ordinal
                )
        );
    }

    /// <summary>
    /// In the Tooltip_Aux_P prefab, auxParent (Tooltip_Aux_Content) owns the complete visual tree
    /// (Background, TitleText, BodyText, Divider), while the controller root is a world-space
    /// Transform ABOVE the prefab's nested Canvas — a CanvasGroup there does not propagate into
    /// the canvas. The prepare gate must therefore target auxParent, never the controller root,
    /// and a matching still-held gate must be reused closed instead of restore-then-recreate
    /// (Destroy is deferred to frame end, so restoring first renders the shell for one frame).
    /// </summary>
    [Fact]
    public void Preparing_an_auxiliary_gates_auxparent_and_reuses_a_held_gate()
    {
        var host = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "GameInterop",
                "Tooltips",
                "NativePairedTooltipHost.cs"
            )
        );
        var methodStart = host.IndexOf("internal void PrepareAuxiliary", StringComparison.Ordinal);
        var methodEnd = host.IndexOf(
            "internal void CancelPreparedAuxiliary",
            methodStart,
            StringComparison.Ordinal
        );

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = host[methodStart..methodEnd];
        Assert.Contains("auxiliary.auxParent.gameObject", method, StringComparison.Ordinal);
        Assert.DoesNotContain("auxiliary.gameObject,", method, StringComparison.Ordinal);
        var reuseCheck = method.IndexOf(
            "ReferenceEquals(_preparedAuxiliaryGate.Controller, auxiliary)",
            StringComparison.Ordinal
        );
        var reuseClose = method.IndexOf(
            "_preparedAuxiliaryGate.SetAlpha(0f);",
            StringComparison.Ordinal
        );
        var recreate = method.IndexOf("RestorePreparedAuxiliaryGate();", StringComparison.Ordinal);
        Assert.True(
            reuseCheck >= 0 && reuseClose > reuseCheck && recreate > reuseClose,
            "a matching held gate must be reused closed before the restore-then-recreate fallback"
        );
    }

    [Fact]
    public void A_new_native_show_reactivates_pooled_text_nodes_before_assigning_content()
    {
        var host = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "GameInterop",
                "Tooltips",
                "NativePairedTooltipHost.cs"
            )
        );
        var releaseStart = host.IndexOf(
            "internal bool ReleaseForNativeShow",
            StringComparison.Ordinal
        );
        var releaseEnd = host.IndexOf(
            "// ── Open / content",
            releaseStart,
            StringComparison.Ordinal
        );
        Assert.True(releaseStart >= 0 && releaseEnd > releaseStart);
        var release = host[releaseStart..releaseEnd];
        Assert.Contains("RestorePreparedNativeHostForNativeShow(controller);", release);

        var restoreStart = host.IndexOf(
            "internal void RestoreForNativeShow()",
            StringComparison.Ordinal
        );
        Assert.True(restoreStart >= 0);
        var restore = host[restoreStart..];
        Assert.Contains("Restore(restoreContentVisibility: false);", restore);
        Assert.Contains("Controller.headerText.gameObject.SetActive(true);", restore);
        Assert.Contains("Controller.bodyText.gameObject.SetActive(true);", restore);
        Assert.DoesNotContain("Controller.dividerParent.SetActive", restore);
    }

    [Fact]
    public void Native_auxiliary_anomaly_storms_are_partitioned_by_category_and_phase()
    {
        var events = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "PostCombatImpact",
                "PostCombatImpactLogEvents.cs"
            )
        );

        Assert.Contains(
            "new BppLogStormPolicy([AnomalyCategory, AnomalyPhase])",
            events,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Visible_empty_native_auxiliary_is_concealed_below_the_native_fade()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var controller = File.ReadAllText(
            Path.Combine(sourceRoot, "Game", "PostCombatImpact", "PostCombatImpactController.cs")
        );
        var host = File.ReadAllText(
            Path.Combine(sourceRoot, "GameInterop", "Tooltips", "NativePairedTooltipHost.cs")
        );

        var auditStart = controller.IndexOf(
            "private void AuditVisibleNativeAuxiliaryTooltip()",
            StringComparison.Ordinal
        );
        var auditEnd = controller.IndexOf(
            "private static NativeAuxiliaryTooltipState CaptureNativeAuxiliaryState",
            auditStart,
            StringComparison.Ordinal
        );
        Assert.True(auditStart >= 0 && auditEnd > auditStart);
        var audit = controller[auditStart..auditEnd];
        Assert.Contains("TryConcealVisibleEmptyNativeAuxiliary(controller)", audit);
        Assert.Contains("!CaptureNativeAuxiliaryState(controller).FrameVisible", audit);

        var recoveryStart = host.IndexOf(
            "internal bool TryConcealVisibleEmptyNativeAuxiliary",
            StringComparison.Ordinal
        );
        var recoveryEnd = host.IndexOf(
            "// ── Open / content",
            recoveryStart,
            StringComparison.Ordinal
        );
        Assert.True(recoveryStart >= 0 && recoveryEnd > recoveryStart);
        var recovery = host[recoveryStart..recoveryEnd];
        Assert.Contains("_preparedAuxiliaryGate.SetAlpha(0f);", recovery);
        Assert.Contains("auxiliary.auxParent.gameObject", recovery);
        Assert.DoesNotContain("tooltipCanvasGroup.alpha = 0f", recovery);
        var restoreGate = recovery.IndexOf(
            "RestorePreparedAuxiliaryGate();",
            StringComparison.Ordinal
        );
        var createGate = recovery.IndexOf(
            "_preparedAuxiliaryGate = CanvasGroupGate.Create(",
            StringComparison.Ordinal
        );
        Assert.True(restoreGate >= 0 && createGate > restoreGate);
        Assert.DoesNotContain(
            "RestorePreparedAuxiliaryGate();",
            recovery[(createGate + 1)..],
            StringComparison.Ordinal
        );
    }

    /// <summary>
    /// AddComponent on a controller whose Destroy is already pending hands back a dead reference
    /// while every other liveness check still passes that frame. Gate creation must fail soft and
    /// TryOpen must report an unusable shape instead of throwing into the feature's catch-all.
    /// </summary>
    [Fact]
    public void Gate_creation_fails_soft_and_open_reports_a_dying_controller_as_unusable()
    {
        var host = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "GameInterop",
                "Tooltips",
                "NativePairedTooltipHost.cs"
            )
        );

        var createStart = host.IndexOf(
            "internal static CanvasGroupGate? Create(",
            StringComparison.Ordinal
        );
        Assert.True(createStart >= 0, "gate creation must be able to report failure as null");

        // Owned gates must die immediately on restore: a deferred Destroy leaves the corpse
        // attached until frame end, a same-frame re-prepare adopts it via GetComponent, and the
        // gate silently disappears with the corpse — the ungated native fade then flashes the
        // tooltip shell.
        Assert.Contains("Object.DestroyImmediate(_group);", host, StringComparison.Ordinal);
        Assert.DoesNotContain("Object.Destroy(_group);", host, StringComparison.Ordinal);
        var addComponent = host.IndexOf(
            "target.AddComponent<CanvasGroup>();",
            createStart,
            StringComparison.Ordinal
        );
        var failSoft = host.IndexOf("return null;", addComponent, StringComparison.Ordinal);
        Assert.True(addComponent > createStart && failSoft > addComponent);

        var openStart = host.IndexOf("internal bool TryOpen(", StringComparison.Ordinal);
        var openEnd = host.IndexOf(
            "internal bool AttachContent(",
            openStart,
            StringComparison.Ordinal
        );
        Assert.True(openStart >= 0 && openEnd > openStart);
        var open = host[openStart..openEnd];
        var nullGateCheck = open.IndexOf(
            "if (_preparedAuxiliaryGate == null)",
            StringComparison.Ordinal
        );
        Assert.True(nullGateCheck >= 0, "TryOpen must check the auxiliary gate for dead targets");
        var conceal = open.IndexOf(
            "ConcealNativeAuxiliary(auxiliary);",
            nullGateCheck,
            StringComparison.Ordinal
        );
        var rollback = open.IndexOf(
            "Release(restoreNativeContent: false);",
            nullGateCheck,
            StringComparison.Ordinal
        );
        var reportUnusable = open.IndexOf("return false;", nullGateCheck, StringComparison.Ordinal);
        Assert.True(
            conceal > nullGateCheck && rollback > conceal && reportUnusable > rollback,
            "a failed open must conceal the native auxiliary before Release restores the gates"
        );

        // Every rollback branch in TryOpen (gate death, missing background, background clone)
        // must conceal immediately before Release: Release restores the gates to their visible
        // originals, which would otherwise expose the header-only native shell.
        var concealThenRelease =
            "ConcealNativeAuxiliary(auxiliary);\n            Release(restoreNativeContent: false);";
        var pairCount = 0;
        for (
            var index = open.IndexOf(concealThenRelease, StringComparison.Ordinal);
            index >= 0;
            index = open.IndexOf(concealThenRelease, index + 1, StringComparison.Ordinal)
        )
            pairCount++;
        Assert.True(
            pairCount >= 3,
            "each TryOpen rollback must conceal the native auxiliary before Release"
        );
    }

    /// <summary>
    /// A transient open failure (dying native controller) must re-request the auxiliary tooltip
    /// while the hover is still valid; suppressing until pointer exit is reserved for the retry
    /// budget running out.
    /// </summary>
    [Fact]
    public void Transient_show_failure_retries_before_suppressing_the_hover()
    {
        var controller = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "PostCombatImpact",
                "PostCombatImpactController.cs"
            )
        );

        var notShown = controller.IndexOf("if (!shown)", StringComparison.Ordinal);
        Assert.True(notShown >= 0);
        var retry = controller.IndexOf(
            "TryConsumeTransientShowRetry(revision)",
            notShown,
            StringComparison.Ordinal
        );
        var requeue = controller.IndexOf("StartPendingShow();", notShown, StringComparison.Ordinal);
        var suppress = controller.IndexOf(
            "PostCombatImpactReasonCode.AuxiliaryTooltipContentUnavailable",
            notShown,
            StringComparison.Ordinal
        );
        Assert.True(retry > notShown, "the not-shown path must consult the transient retry budget");
        Assert.True(requeue > retry, "a granted retry must requeue the pending show");
        Assert.True(
            suppress > requeue,
            "suppression must remain the fallback after the retry budget"
        );
    }

    /// <summary>
    /// Group titles use the generic text clone, whose safe default is Ellipsis. These controlled
    /// product labels must override that default so vertical pressure cannot replace them with an
    /// ellipsis in CJK locales.
    /// </summary>
    [Fact]
    public void Combat_impact_group_titles_never_use_ellipsis()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var view = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Patches",
                "PostCombatImpact",
                "NativePostCombatImpactTooltipView.cs"
            )
        );

        Assert.Contains(
            "labelText.overflowMode = TextOverflowModes.Overflow;",
            view,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Combat_impact_builds_only_the_visible_perspective_until_shift_requests_the_other()
    {
        var sourceRoot = MainSourceRoot(RepoRoot());
        var view = File.ReadAllText(
            Path.Combine(
                sourceRoot,
                "Patches",
                "PostCombatImpact",
                "NativePostCombatImpactTooltipView.cs"
            )
        );

        var showStart = view.IndexOf("public bool Show(", StringComparison.Ordinal);
        var showEnd = view.IndexOf(
            "public bool SetPerspective(",
            showStart,
            StringComparison.Ordinal
        );
        Assert.True(showStart >= 0 && showEnd > showStart);
        var show = view[showStart..showEnd];
        Assert.Contains(
            "EnsurePerspectiveBuilt(_activePerspective)",
            show,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("BuildCaused(", show, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildReceived(", show, StringComparison.Ordinal);

        var switchStart = showEnd;
        var switchEnd = view.IndexOf(
            "public bool Position(",
            switchStart,
            StringComparison.Ordinal
        );
        Assert.True(switchEnd > switchStart);
        var perspectiveSwitch = view[switchStart..switchEnd];
        var lazyBuild = perspectiveSwitch.IndexOf(
            "EnsurePerspectiveBuilt(perspective)",
            StringComparison.Ordinal
        );
        var reveal = perspectiveSwitch.IndexOf(
            "ApplyPerspectiveVisibility(perspective)",
            StringComparison.Ordinal
        );
        Assert.True(
            lazyBuild >= 0 && reveal > lazyBuild,
            "the requested perspective must be built inside the masked Shift transition"
        );

        var ensureStart = view.IndexOf(
            "private bool EnsurePerspectiveBuilt(",
            StringComparison.Ordinal
        );
        var ensureEnd = view.IndexOf(
            "private void BuildCaused(",
            ensureStart,
            StringComparison.Ordinal
        );
        Assert.True(ensureStart >= 0 && ensureEnd > ensureStart);
        var ensure = view[ensureStart..ensureEnd];
        Assert.Contains("if (_causedPerspectiveBuilt)", ensure, StringComparison.Ordinal);
        Assert.Contains("if (_receivedPerspectiveBuilt)", ensure, StringComparison.Ordinal);
        Assert.Contains("BuildCaused(", ensure, StringComparison.Ordinal);
        Assert.Contains("BuildReceived(", ensure, StringComparison.Ordinal);
    }

    [Fact]
    public void Attaching_paired_content_commits_only_the_final_width_layout()
    {
        var host = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "GameInterop",
                "Tooltips",
                "NativePairedTooltipHost.cs"
            )
        );
        var attachStart = host.IndexOf("internal bool AttachContent(", StringComparison.Ordinal);
        var attachEnd = host.IndexOf(
            "internal PlacementResult Position(",
            attachStart,
            StringComparison.Ordinal
        );
        Assert.True(attachStart >= 0 && attachEnd > attachStart);
        var attach = host[attachStart..attachEnd];

        var applyWidth = attach.IndexOf("ApplyContentWidth(auxiliary", StringComparison.Ordinal);
        var rebuild = attach.IndexOf("ForceRebuildLayout(auxiliary);", StringComparison.Ordinal);
        Assert.True(
            applyWidth >= 0 && rebuild > applyWidth,
            "width-dependent columns must be updated before the bounded layout commit"
        );
        Assert.Equal(
            rebuild,
            attach.LastIndexOf("ForceRebuildLayout(auxiliary);", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Combat_impact_geometry_settling_reuses_capture_buffers()
    {
        var controller = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "PostCombatImpact",
                "PostCombatImpactController.cs"
            )
        );
        var captureStart = controller.IndexOf(
            "private static bool TryCaptureGeometry(",
            StringComparison.Ordinal
        );
        var captureEnd = controller.IndexOf(
            "internal void OnNativeTooltipChanging(",
            captureStart,
            StringComparison.Ordinal
        );
        Assert.True(captureStart >= 0 && captureEnd > captureStart);
        var capture = controller[captureStart..captureEnd];

        Assert.Contains("sample.CopyWorldCorners", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("new Vector3[", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("new GeometrySample", capture, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pre-existing native card-tooltip content refresher is a different concern and must not
    /// be folded into the paired host.
    /// </summary>
    [Fact]
    public void Card_tooltip_content_refresher_stays_a_separate_adapter()
    {
        var moduleRoot = Path.Combine(MainSourceRoot(RepoRoot()), "GameInterop", "Tooltips");
        var refresher = Path.Combine(moduleRoot, "NativeCardTooltipContentRefresher.cs");

        Assert.True(
            File.Exists(refresher),
            "NativeCardTooltipContentRefresher.cs must remain its own adapter."
        );
        Assert.DoesNotContain(
            "NativePairedTooltip",
            File.ReadAllText(refresher),
            StringComparison.Ordinal
        );
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (
                File.Exists(Path.Combine(current.FullName, "CLAUDE.md"))
                && Directory.Exists(Path.Combine(current.FullName, "src"))
            )
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string MainSourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "BazaarPlusPlus");
}
