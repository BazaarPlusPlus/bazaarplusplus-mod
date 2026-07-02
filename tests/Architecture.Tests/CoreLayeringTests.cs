#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

// Ratchet guard for the intended layering: types under Core/ are the pure abstraction floor and
// must not depend on the Game features, the GameInterop bridge, or the game DLLs. Because every
// layer compiles into one assembly, the C# compiler does not enforce this; this test does.
//
// A small allowlist records the deliberate, known exceptions. When a Core -> GameInterop leak is
// fixed, remove its entry; when a new dependency appears, this fails until it is justified (added
// here) or removed. Game.* and game-DLL (BazaarGameShared) references are never allowed.
public class CoreLayeringTests
{
    private static readonly HashSet<string> AllowedGameInteropFiles = new(StringComparer.Ordinal)
    {
        // The service aggregate re-exports GameInterop.IRunContext to features. Splitting Core into
        // its own assembly (or moving IRunContext's abstraction down) would let us drop these.
        "Core/Runtime/IBppServices.cs",
        "Core/Runtime/BppRuntimeServices.cs",
    };

    [Fact]
    public void Core_does_not_depend_on_Game_GameInterop_or_game_assemblies()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var coreDir = Path.Combine(mainSource, "Core");
        Assert.True(Directory.Exists(coreDir), $"Could not locate Core directory at '{coreDir}'.");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(coreDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(mainSource, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("using ", StringComparison.Ordinal))
                    continue;

                // The game DLL and sibling Game features are never permitted in Core.
                if (
                    line.StartsWith("using BazaarGameShared", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarPlusPlus.Game.", StringComparison.Ordinal)
                )
                {
                    violations.Add($"{relative}: {line}");
                    continue;
                }

                // GameInterop is permitted only for the explicitly allowlisted files.
                if (
                    line.StartsWith("using BazaarPlusPlus.GameInterop", StringComparison.Ordinal)
                    && !AllowedGameInteropFiles.Contains(relative)
                )
                {
                    violations.Add($"{relative}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Core must stay free of Game/GameInterop/game-DLL dependencies. Either remove the "
                + "import or, for a deliberate GameInterop dependency, add the file to "
                + "AllowedGameInteropFiles with justification. Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void CollectionPanel_does_not_depend_on_HistoryPanel_preview_internals()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var collectionPanelDir = Path.Combine(mainSource, "Game", "CollectionPanel");
        Assert.True(
            Directory.Exists(collectionPanelDir),
            $"Could not locate CollectionPanel directory at '{collectionPanelDir}'."
        );

        var violations = new List<string>();
        foreach (
            var file in Directory.EnumerateFiles(
                collectionPanelDir,
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(mainSource, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (
                    line.StartsWith(
                        "using BazaarPlusPlus.Game.HistoryPanel.Preview",
                        StringComparison.Ordinal
                    )
                )
                {
                    violations.Add($"{relative}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "CollectionPanel must use shared GameInterop card-preview adapters instead of "
                + "depending on HistoryPanel preview internals. Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    // Feature-scoped ratchet, not a repo-wide layering rule: Game/ may legitimately reference
    // TheBazaar.* elsewhere (Game/Tooltips does). This locks in the decision that CollectionPanel
    // consumes native tooltip typography through the GameInterop.TagTypography seam instead of
    // importing the game's tooltip namespaces directly. Known blind spots of the StartsWith scan:
    // fully-qualified inline references and alias usings are not detected.
    [Fact]
    public void CollectionPanel_does_not_import_native_tooltip_namespaces()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var collectionPanelDir = Path.Combine(mainSource, "Game", "CollectionPanel");
        Assert.True(
            Directory.Exists(collectionPanelDir),
            $"Could not locate CollectionPanel directory at '{collectionPanelDir}'."
        );

        var violations = new List<string>();
        foreach (
            var file in Directory.EnumerateFiles(
                collectionPanelDir,
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(mainSource, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (
                    line.StartsWith("using TheBazaar.UI.Tooltips", StringComparison.Ordinal)
                    || line.StartsWith("using TheBazaar.Tooltips", StringComparison.Ordinal)
                )
                {
                    violations.Add($"{relative}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "CollectionPanel must consume native tooltip typography through "
                + "GameInterop.TagTypography instead of importing the game's tooltip namespaces. "
                + "Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void CollectionPanel_opens_from_native_clone_button_or_tab_not_settings_dock()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var compositionSource = File.ReadAllText(Path.Combine(mainSource, "BppComposition.cs"));
        var configSource = File.ReadAllText(
            Path.Combine(mainSource, "Core", "Config", "BppConfig.cs")
        );
        var configInterfaceSource = File.ReadAllText(
            Path.Combine(mainSource, "Core", "Config", "IBppConfig.cs")
        );
        var collectionPanelSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "CollectionPanel", "CollectionPanel.cs")
        );
        const string oldCollectionPanelHotkeyPathIdentifier =
            @"(?<![A-Za-z0-9_])CollectionPanelHotkeyPathConfig(?![A-Za-z0-9_])";

        Assert.DoesNotContain("CollectionPanelSettingsDockEntry", compositionSource);
        Assert.DoesNotMatch(oldCollectionPanelHotkeyPathIdentifier, configSource);
        Assert.DoesNotMatch(oldCollectionPanelHotkeyPathIdentifier, configInterfaceSource);
        Assert.DoesNotContain("WasPressedThisFrame(togglePath", collectionPanelSource);
        Assert.Contains("BppHotkeyService.WasToggleHotkeyPressedThisFrame", collectionPanelSource);
        Assert.Contains("BppHotkeyActionId.ToggleCollectionPanel", collectionPanelSource);
    }

    [Fact]
    public void CollectionPanel_close_hides_native_card_layer_synchronously()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var collectionPanelSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "CollectionPanel", "CollectionPanel.cs")
        );

        var closeIndex = collectionPanelSource.IndexOf(
            "private void Close()",
            StringComparison.Ordinal
        );
        var hideIndex = collectionPanelSource.IndexOf(
            "HideNativeCardLayerImmediately();",
            closeIndex,
            StringComparison.Ordinal
        );
        var fadeIndex = collectionPanelSource.IndexOf(
            "_view?.SetVisible(false);",
            closeIndex,
            StringComparison.Ordinal
        );

        Assert.True(closeIndex >= 0, "CollectionPanel.Close() should exist.");
        Assert.True(
            hideIndex > closeIndex,
            "CollectionPanel.Close() must hide the native card overlay immediately instead of waiting for the UITK fade-out."
        );
        Assert.True(
            fadeIndex > hideIndex,
            "CollectionPanel.Close() should hide native cards before starting the remaining UITK fade-out."
        );
    }

    [Fact]
    public void CollectionGridVirtualizer_pending_returns_are_completed_after_generation_changes()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var virtualizerSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CollectionPanel",
                "Grid",
                "CollectionGridVirtualizer.cs"
            )
        );

        static int Find(string source, string value, int startIndex = 0) =>
            source.IndexOf(value, startIndex, StringComparison.Ordinal);

        var pendingBindsFieldIndex = Find(
            virtualizerSource,
            "private readonly Dictionary<int, PendingBind> _pendingBinds = new();"
        );
        var pendingBindTrackerFieldIndex = Find(
            virtualizerSource,
            "private readonly PendingBindTracker _pendingBindTracker = new();"
        );
        var hasPendingBindsIndex = Find(
            virtualizerSource,
            "public bool HasPendingBinds => _pendingBindTracker.HasPendingBinds;"
        );
        var whenPendingBindsSettledIndex = Find(
            virtualizerSource,
            "public Task WhenPendingBindsSettled => _pendingBindTracker.WhenSettled;"
        );
        var tryRealizeIndex = Find(virtualizerSource, "private void TryRealize(int index)");
        var pendingCreateIndex = Find(
            virtualizerSource,
            "var pending = new PendingBind(",
            tryRealizeIndex
        );
        var pendingRegisterIndex = Find(
            virtualizerSource,
            "_pendingBindTracker.Register()",
            pendingCreateIndex
        );
        var pendingTrackIndex = Find(
            virtualizerSource,
            "_pendingBinds[index] = pending;",
            pendingCreateIndex
        );
        var pendingStartIndex = Find(
            virtualizerSource,
            "_ = BindAndRealizeAsync(pending);",
            pendingTrackIndex
        );
        var bindAndRealizeIndex = Find(
            virtualizerSource,
            "private async Task BindAndRealizeAsync(PendingBind pending)"
        );
        var bindAsyncIndex = Find(
            virtualizerSource,
            "bindResult = await _factory.BindAsync(pending.Vm, pending.Token);",
            bindAndRealizeIndex
        );
        var staleOrCanceledIndex = Find(
            virtualizerSource,
            "var staleOrCanceled =",
            bindAsyncIndex
        );
        var canceledCheckIndex = Find(
            virtualizerSource,
            "pending.IsCanceled",
            staleOrCanceledIndex
        );
        var staleGenerationIndex = Find(
            virtualizerSource,
            "pending.Generation != _generation",
            staleOrCanceledIndex
        );
        var staleReturnIndex = Find(
            virtualizerSource,
            "_factory.Return(binding.Value.Card, binding.Value.Kind);",
            staleOrCanceledIndex
        );
        var removePendingIndex = Find(
            virtualizerSource,
            "_pendingBinds.Remove(pending.Index);",
            staleReturnIndex
        );
        var pendingDisposeIndex = Find(virtualizerSource, "pending.Dispose();", removePendingIndex);
        var pendingTrackerIndex = Find(
            virtualizerSource,
            "private sealed class PendingBindTracker"
        );
        var trackerHasPendingBindsIndex = Find(
            virtualizerSource,
            "public bool HasPendingBinds",
            pendingTrackerIndex
        );
        var trackerWhenSettledIndex = Find(
            virtualizerSource,
            "public Task WhenSettled",
            trackerHasPendingBindsIndex
        );
        var trackerRegisterIndex = Find(
            virtualizerSource,
            "public PendingBindOperation Register()",
            trackerWhenSettledIndex
        );

        Assert.True(
            pendingBindsFieldIndex >= 0,
            "CollectionGridVirtualizer should track in-flight async binds by visible index."
        );
        Assert.True(
            pendingBindTrackerFieldIndex > pendingBindsFieldIndex,
            "CollectionGridVirtualizer should keep a pending-bind tracker next to the pending bind map."
        );
        Assert.True(
            hasPendingBindsIndex > pendingBindTrackerFieldIndex,
            "CollectionGridVirtualizer should expose whether async binds are pending."
        );
        Assert.True(
            whenPendingBindsSettledIndex > hasPendingBindsIndex,
            "CollectionGridVirtualizer should expose a task that completes when pending async binds settle."
        );
        Assert.True(tryRealizeIndex >= 0, "CollectionGridVirtualizer.TryRealize should exist.");
        Assert.True(
            pendingCreateIndex > tryRealizeIndex,
            "TryRealize should create a PendingBind before starting async card realization."
        );
        Assert.True(
            pendingRegisterIndex > pendingCreateIndex,
            "TryRealize should register each pending bind with PendingBindTracker."
        );
        Assert.True(
            pendingTrackIndex > pendingCreateIndex,
            "TryRealize should add new pending binds to _pendingBinds before awaiting completion."
        );
        Assert.True(
            pendingStartIndex > pendingTrackIndex,
            "TryRealize should start BindAndRealizeAsync for the tracked pending bind."
        );
        Assert.True(
            bindAndRealizeIndex >= 0,
            "CollectionGridVirtualizer.BindAndRealizeAsync should exist."
        );
        Assert.True(
            bindAsyncIndex > bindAndRealizeIndex,
            "BindAndRealizeAsync should await CollectionCardFactory.BindAsync with the pending VM and cancellation token."
        );
        Assert.True(
            canceledCheckIndex > staleOrCanceledIndex,
            "BindAndRealizeAsync should treat canceled pending binds as stale completions."
        );
        Assert.True(
            staleGenerationIndex > staleOrCanceledIndex,
            "BindAndRealizeAsync should treat generation changes as stale completions."
        );
        Assert.True(
            staleReturnIndex > staleOrCanceledIndex,
            "Stale or canceled async bind completions should return created native cards to the factory."
        );
        Assert.True(
            removePendingIndex > staleReturnIndex,
            "BindAndRealizeAsync should remove completed pending binds from _pendingBinds."
        );
        Assert.True(
            pendingDisposeIndex > removePendingIndex,
            "BindAndRealizeAsync should dispose pending bind operations so PendingBindTracker can settle."
        );
        Assert.True(
            trackerHasPendingBindsIndex > pendingTrackerIndex,
            "PendingBindTracker should expose HasPendingBinds."
        );
        Assert.True(
            trackerWhenSettledIndex > trackerHasPendingBindsIndex,
            "PendingBindTracker should expose a settled task for tests and callers."
        );
        Assert.True(
            trackerRegisterIndex > trackerWhenSettledIndex,
            "PendingBindTracker should register operations that are completed when pending binds are disposed."
        );

        var pendingRecycleScratchIndex = Find(virtualizerSource, "_pendingRecycleScratch.Clear()");
        var pendingOutOfWindowIndex = Find(
            virtualizerSource,
            "if (pair.Key < firstIdx || pair.Key > lastIdx)",
            pendingRecycleScratchIndex
        );
        var cancelScrolledPendingIndex = Find(
            virtualizerSource,
            "CancelPendingBind(index);",
            pendingOutOfWindowIndex
        );
        var cancelPendingBindIndex = Find(
            virtualizerSource,
            "private void CancelPendingBind(int index)"
        );
        var removeScrolledPendingIndex = Find(
            virtualizerSource,
            "if (!_pendingBinds.Remove(index, out var pending))",
            cancelPendingBindIndex
        );
        var cancelScrolledOperationIndex = Find(
            virtualizerSource,
            "pending.Cancel();",
            removeScrolledPendingIndex
        );
        var cancelPendingBindsIndex = Find(virtualizerSource, "private void CancelPendingBinds()");
        var cancelAllIterationIndex = Find(
            virtualizerSource,
            "foreach (var pair in _pendingBinds)",
            cancelPendingBindsIndex
        );
        var cancelAllOperationIndex = Find(
            virtualizerSource,
            "pair.Value.Cancel();",
            cancelAllIterationIndex
        );
        var clearAllPendingIndex = Find(
            virtualizerSource,
            "_pendingBinds.Clear();",
            cancelAllOperationIndex
        );
        var bumpGenerationIndex = Find(virtualizerSource, "private void BumpGeneration()");
        var bumpGenerationCancelIndex = Find(
            virtualizerSource,
            "CancelPendingBinds();",
            bumpGenerationIndex
        );
        var disposeIndex = Find(virtualizerSource, "public void Dispose()");
        var disposeBumpGenerationIndex = Find(
            virtualizerSource,
            "BumpGeneration();",
            disposeIndex
        );

        Assert.True(
            pendingRecycleScratchIndex >= 0,
            "Tick should inspect pending binds that may have scrolled out of the visible window."
        );
        Assert.True(
            pendingOutOfWindowIndex > pendingRecycleScratchIndex,
            "Tick should identify pending binds outside the current visible index window."
        );
        Assert.True(
            cancelScrolledPendingIndex > pendingOutOfWindowIndex,
            "Pending binds that scroll out of the visible window should be canceled by index."
        );
        Assert.True(
            cancelPendingBindIndex >= 0,
            "CollectionGridVirtualizer.CancelPendingBind should exist."
        );
        Assert.True(
            removeScrolledPendingIndex > cancelPendingBindIndex,
            "CancelPendingBind should remove the pending bind from _pendingBinds."
        );
        Assert.True(
            cancelScrolledOperationIndex > removeScrolledPendingIndex,
            "CancelPendingBind should cancel the removed pending operation."
        );
        Assert.True(
            cancelPendingBindsIndex >= 0,
            "CollectionGridVirtualizer.CancelPendingBinds should exist."
        );
        Assert.True(
            cancelAllIterationIndex > cancelPendingBindsIndex,
            "CancelPendingBinds should visit every pending bind."
        );
        Assert.True(
            cancelAllOperationIndex > cancelAllIterationIndex,
            "CancelPendingBinds should cancel every pending operation."
        );
        Assert.True(
            clearAllPendingIndex > cancelAllOperationIndex,
            "CancelPendingBinds should clear the pending bind map after cancellation."
        );
        Assert.True(
            bumpGenerationCancelIndex > bumpGenerationIndex,
            "BumpGeneration should cancel pending binds on filter/tab generation changes."
        );
        Assert.True(
            disposeBumpGenerationIndex > disposeIndex,
            "Dispose should bump the generation so pending async binds are canceled."
        );

        var showWhenReadyIndex = virtualizerSource.IndexOf(
            "private async Task ShowWhenReady",
            StringComparison.Ordinal
        );
        var pendingReturnIndex = virtualizerSource.IndexOf(
            "if (cell.PendingReturn)",
            showWhenReadyIndex,
            StringComparison.Ordinal
        );
        var generationMismatchIndex = virtualizerSource.IndexOf(
            "generationSnapshot != _generation",
            showWhenReadyIndex,
            StringComparison.Ordinal
        );

        Assert.True(
            showWhenReadyIndex >= 0,
            "CollectionGridVirtualizer.ShowWhenReady should exist."
        );
        Assert.True(
            pendingReturnIndex > showWhenReadyIndex,
            "ShowWhenReady should complete pending returns after native SetUp finishes."
        );
        Assert.True(
            generationMismatchIndex > pendingReturnIndex,
            "Pending-return cells must be returned to the pool even when Dispose changed the generation."
        );
    }

    [Fact]
    public void ItemBoardPreview_uses_native_socket_proportions_for_slot_grid_defaults()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var socketLayoutSource = File.ReadAllText(
            Path.Combine(mainSource, "GameInterop", "ItemBoardPreview", "ItemBoardSocketLayout.cs")
        );
        var optionsSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "GameInterop",
                "ItemBoardPreview",
                "ItemBoardPreviewOptions.cs"
            )
        );

        Assert.Contains("public const float NativeSocketHeightPixels = 484f;", socketLayoutSource);
        Assert.Contains("public const float NativeSocketPitchPixels = 240f;", socketLayoutSource);
        Assert.Contains("public const float FrameHeightOverSocket = 1.03704f;", socketLayoutSource);
        Assert.Contains(
            "FallbackSocketHeightPixels = NativeSocketHeightPixels",
            socketLayoutSource
        );
        Assert.Contains("FallbackSocketWidthPixels = NativeSocketPitchPixels", socketLayoutSource);

        Assert.Contains("DefaultSlotGridMaxHeightRatio", optionsSource);
        Assert.Contains("ItemBoardSocketLayout.NativeSocketHeightPixels", optionsSource);
        Assert.Contains("ItemBoardSocketLayout.FrameHeightOverSocket", optionsSource);
        Assert.Contains("ItemBoardSocketLayout.NativeSocketPitchPixels", optionsSource);
        Assert.Contains("ItemBoardSocketLayout.NativeBoardHeight", optionsSource);
        Assert.Contains(
            "SlotGridMaxHeightRatio { get; init; } = DefaultSlotGridMaxHeightRatio",
            optionsSource
        );
        Assert.DoesNotContain("SlotGridMaxHeightRatio { get; init; } = 0.96f", optionsSource);
    }

    [Fact]
    public void CollectionGridVirtualizer_clamps_item_cards_by_body_width_not_frame_width()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var virtualizerSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CollectionPanel",
                "Grid",
                "CollectionGridVirtualizer.cs"
            )
        );
        var badgeSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CollectionPanel",
                "Grid",
                "CollectionSourceAttributionBadge.cs"
            )
        );

        static int Find(string source, string value, int startIndex = 0) =>
            source.IndexOf(value, startIndex, StringComparison.Ordinal);

        var applyCellScaleIndex = Find(virtualizerSource, "private void ApplyCellScale");
        var bodyWidthIndex = Find(
            applyCellScaleIndex >= 0 ? virtualizerSource : string.Empty,
            "var bodyW ="
        );
        var fitterIndex = Find(
            virtualizerSource,
            "rect.GetComponent<AspectRatioFitter>()",
            applyCellScaleIndex
        );
        var frameHeightIndex = Find(
            virtualizerSource,
            "ItemBoardSocketLayout.FrameHeightOverSocket",
            applyCellScaleIndex
        );
        var clampBodyWidthIndex = Find(
            virtualizerSource,
            "bodyW * scale > maxWidth",
            applyCellScaleIndex
        );
        var fallbackClampIndex = Find(virtualizerSource, "bodyW = natW;", applyCellScaleIndex);
        var oldClampIndex = Find(virtualizerSource, "natW * scale > maxWidth", applyCellScaleIndex);

        Assert.Contains("const float FallbackNativeCardHeight = 484f;", virtualizerSource);
        Assert.True(
            applyCellScaleIndex >= 0,
            "CollectionGridVirtualizer.ApplyCellScale should exist."
        );
        Assert.True(
            bodyWidthIndex > applyCellScaleIndex,
            "ApplyCellScale should derive the width clamp basis separately from measured frame width."
        );
        Assert.True(
            fitterIndex > applyCellScaleIndex && fitterIndex < bodyWidthIndex,
            "ApplyCellScale should use the root AspectRatioFitter for item-card body width."
        );
        Assert.True(
            frameHeightIndex > fitterIndex && frameHeightIndex < bodyWidthIndex,
            "Item body width should divide measured frame height by the native frame-over-socket factor."
        );
        Assert.True(
            clampBodyWidthIndex > bodyWidthIndex,
            "The collection width clamp should compare body width, not frame-subtree width."
        );
        Assert.True(
            fallbackClampIndex > bodyWidthIndex,
            "Cards without a usable AspectRatioFitter should fall back to the measured visual width."
        );
        Assert.True(
            oldClampIndex < 0,
            "The old frame-width clamp shrinks Large item cards because their frame art overhangs the body."
        );

        Assert.Contains("BadgeRootHeightScale", badgeSource);
        Assert.Contains("CollectionGridVirtualizer.FallbackNativeCardHeight / 200f", badgeSource);
    }

    [Fact]
    public void CollectionGridVirtualizer_measures_native_visual_bounds_for_card_layout()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var virtualizerSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CollectionPanel",
                "Grid",
                "CollectionGridVirtualizer.cs"
            )
        );

        static int Find(string source, string value, int startIndex = 0) =>
            source.IndexOf(value, startIndex, StringComparison.Ordinal);

        var applyCellScaleIndex = Find(virtualizerSource, "private void ApplyCellScale");
        var repositionIndex = Find(virtualizerSource, "private void Reposition", applyCellScaleIndex);
        var scaleBoundsIndex = Find(
            virtualizerSource,
            "var visualBounds = ResolveNativeVisualBounds(rect);",
            applyCellScaleIndex
        );
        var natWIndex = Find(virtualizerSource, "var natW = visualBounds.Width;", scaleBoundsIndex);
        var natHIndex = Find(virtualizerSource, "var natH = visualBounds.Height;", natWIndex);
        var legacyRootSizeIndex = Find(
            virtualizerSource,
            "var sizeDelta = rect.sizeDelta;",
            applyCellScaleIndex
        );
        var frameLookupIndex = Find(
            virtualizerSource,
            "FindDescendant(root, \"FrameContainer\")"
        );
        var rawImageBoundsIndex = Find(
            virtualizerSource,
            "TryMeasureRawImageBounds(root, out var imageBounds)",
            frameLookupIndex
        );
        var aspectFallbackCallIndex = Find(
            virtualizerSource,
            "TryResolveAspectRatioFallbackBounds(root, out var aspectBounds)",
            rawImageBoundsIndex
        );
        var aspectFallbackMethodIndex = Find(
            virtualizerSource,
            "GetComponent<AspectRatioFitter>()"
        );
        var fallbackHeightIndex = Find(
            virtualizerSource,
            "FallbackNativeCardHeight",
            aspectFallbackMethodIndex
        );
        var centerOffsetIndex = Find(
            virtualizerSource,
            "targetCenter.x - visualBounds.Center.x * rect.localScale.x",
            repositionIndex
        );

        Assert.True(applyCellScaleIndex >= 0, "CollectionGridVirtualizer.ApplyCellScale should exist.");
        Assert.True(
            scaleBoundsIndex > applyCellScaleIndex,
            "ApplyCellScale should measure native visual bounds instead of trusting the root RectTransform size."
        );
        Assert.True(
            natWIndex > scaleBoundsIndex && natHIndex > natWIndex,
            "ApplyCellScale should derive native width/height from the measured visual bounds."
        );
        Assert.True(
            legacyRootSizeIndex < 0 || legacyRootSizeIndex > repositionIndex,
            "ApplyCellScale must not use root rect.sizeDelta directly; native item roots can be zero-sized."
        );
        Assert.True(
            frameLookupIndex > repositionIndex,
            "Native visual bounds should prefer the CardPreviewBase FrameContainer subtree."
        );
        Assert.True(
            rawImageBoundsIndex > frameLookupIndex,
            "Native visual bounds should fall back to RawImage bounds when FrameContainer is not a RectTransform subtree."
        );
        Assert.True(
            aspectFallbackCallIndex > rawImageBoundsIndex
                && aspectFallbackMethodIndex >= 0
                && fallbackHeightIndex > aspectFallbackMethodIndex,
            "Native visual bounds should synthesize a stable item root size from AspectRatioFitter when native item RectTransforms report zero size."
        );
        Assert.True(
            centerOffsetIndex > repositionIndex,
            "Reposition should align the measured native visual center, not just the root pivot."
        );

        var showWhenReadyIndex = Find(virtualizerSource, "private async Task ShowWhenReady");
        var nativeShowIndex = Find(virtualizerSource, "NativeCardPreviewRuntime.Show(", showWhenReadyIndex);
        var showScaleIndex = Find(
            virtualizerSource,
            "ApplyCellScale(cell.Index, cell);",
            nativeShowIndex
        );
        var showRepositionIndex = Find(
            virtualizerSource,
            "Reposition(cell.Index, cell);",
            showScaleIndex
        );
        var fadeStartIndex = Find(virtualizerSource, "cell.FadeActive = true;", showRepositionIndex);

        Assert.True(
            showScaleIndex > nativeShowIndex,
            "ShowWhenReady should reapply scale after Show(true) reactivates native frame/image objects."
        );
        Assert.True(
            showRepositionIndex > showScaleIndex,
            "ShowWhenReady should reposition after the post-Show(true) scale pass."
        );
        Assert.True(
            fadeStartIndex > showRepositionIndex,
            "ShowWhenReady should finish native layout before handing the card to the fade-in path."
        );
    }

    [Fact]
    public void LiveBuildPanel_opens_from_caps_not_settings_dock()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var compositionSource = File.ReadAllText(Path.Combine(mainSource, "BppComposition.cs"));
        var liveBuildPanelSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "LiveBuildPanel", "LiveBuildPanel.cs")
        );

        Assert.DoesNotContain("LiveBuildPanelSettingsDockEntry", compositionSource);
        Assert.DoesNotContain("OpenFromDockEntry", liveBuildPanelSource);
        Assert.Contains("BppHotkeyService.WasToggleHotkeyPressedThisFrame", liveBuildPanelSource);
        Assert.Contains("BppHotkeyActionId.ToggleLiveBuildPanel", liveBuildPanelSource);
    }

    [Fact]
    public void HistoryPanel_and_LiveBuildPanel_do_not_depend_on_each_others_internals()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        Assert.False(
            Directory.Exists(Path.Combine(mainSource, "Game", "CardSetPreview")),
            "Game/CardSetPreview was replaced by LiveBuildPanel and must not be restored."
        );

        var rules = new[]
        {
            new PreviewBoundaryRule(
                Path.Combine(mainSource, "Game", "HistoryPanel"),
                "BazaarPlusPlus.Game.LiveBuildPanel",
                "HistoryPanel"
            ),
            new PreviewBoundaryRule(
                Path.Combine(mainSource, "Game", "LiveBuildPanel"),
                "BazaarPlusPlus.Game.HistoryPanel",
                "LiveBuildPanel"
            ),
        };

        var violations = new List<string>();
        foreach (var rule in rules)
        {
            Assert.True(
                Directory.Exists(rule.Directory),
                $"Could not locate {rule.FeatureName} directory at '{rule.Directory}'."
            );

            foreach (
                var file in Directory.EnumerateFiles(
                    rule.Directory,
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            {
                var relative = Path.GetRelativePath(mainSource, file).Replace('\\', '/');
                foreach (var rawLine in File.ReadLines(file))
                {
                    var line = rawLine.Trim();
                    if (
                        line.StartsWith(
                            $"using {rule.DisallowedNamespace}",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        violations.Add($"{relative}: {line}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "HistoryPanel and LiveBuildPanel must share runtime preview behavior through "
                + "GameInterop.ItemBoardPreview instead of importing each other's feature internals. Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Hero_badge_styling_is_centralized_in_GameInterop()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);

        var resolver = Path.Combine(mainSource, "GameInterop", "Heroes", "HeroVisual.cs");
        Assert.True(
            File.Exists(resolver),
            $"The shared hero badge resolver must live at '{resolver}'."
        );

        // No feature may re-hand-roll the hero name -> short-code/color mapping. It had already
        // drifted before centralization (HistoryPanel read Colors.Hero* tokens while RandomHeroPool
        // inlined raw RGB literals); everyone now consumes GameInterop.Heroes.HeroVisual instead.
        var resolverFull = Path.GetFullPath(resolver);
        var offenders = EnumerateSourceFiles(mainSource)
            .Where(file =>
                !string.Equals(Path.GetFullPath(file), resolverFull, StringComparison.Ordinal)
            )
            .Where(file =>
                File.ReadAllText(file).Contains("struct HeroBadgeStyle", StringComparison.Ordinal)
            )
            .Select(file => Path.GetRelativePath(mainSource, file).Replace('\\', '/'))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Hero badge styling must resolve through GameInterop.Heroes.HeroVisual, not a "
                + "per-feature HeroBadgeStyle copy. Offending files:\n"
                + string.Join("\n", offenders)
        );
    }

    [Fact]
    public void LiveBuildPanel_owns_build_recommendations()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var oldRecommendationsName = "Build" + "Recommendations";
        var oldRecommendationsNamespace = "BazaarPlusPlus.Game." + oldRecommendationsName;
        var oldRecommendationsDir = Path.Combine(mainSource, "Game", oldRecommendationsName);
        var liveRecommendationsDir = Path.Combine(
            mainSource,
            "Game",
            "LiveBuildPanel",
            "Recommendations"
        );

        Assert.False(
            Directory.Exists(oldRecommendationsDir),
            "BuildRecommendations is LiveBuildPanel-owned; do not restore the top-level Game recommendation directory."
        );
        Assert.True(
            Directory.Exists(liveRecommendationsDir),
            $"Could not locate LiveBuildPanel recommendations directory at '{liveRecommendationsDir}'."
        );

        var sourceFiles = Directory
            .EnumerateFiles(mainSource, "*.cs", SearchOption.AllDirectories)
            .ToList();
        var oldNamespaceHits = sourceFiles
            .Where(file =>
                File.ReadAllText(file)
                    .Contains(oldRecommendationsNamespace, StringComparison.Ordinal)
            )
            .Select(file => Path.GetRelativePath(mainSource, file).Replace('\\', '/'))
            .ToList();
        Assert.True(
            oldNamespaceHits.Count == 0,
            "Production code must not reference the old BuildRecommendations namespace:\n"
                + string.Join("\n", oldNamespaceHits)
        );

        var recommendationsRoot =
            Path.GetFullPath(liveRecommendationsDir) + Path.DirectorySeparatorChar;
        var liveBuildPanelFile = Path.GetFullPath(
            Path.Combine(mainSource, "Game", "LiveBuildPanel", "LiveBuildPanel.cs")
        );

        bool IsAllowedRecommendationConsumer(string file)
        {
            var fullPath = Path.GetFullPath(file);
            return fullPath.StartsWith(recommendationsRoot, StringComparison.Ordinal)
                || string.Equals(fullPath, liveBuildPanelFile, StringComparison.Ordinal);
        }

        var disallowedImports = sourceFiles
            .Where(file => !IsAllowedRecommendationConsumer(file))
            .SelectMany(file =>
                File.ReadLines(file)
                    .Select(
                        (line, index) =>
                            new
                            {
                                File = Path.GetRelativePath(mainSource, file).Replace('\\', '/'),
                                Line = index + 1,
                                Text = line.Trim(),
                            }
                    )
            )
            .Where(hit =>
                hit.Text.StartsWith(
                    "using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations",
                    StringComparison.Ordinal
                )
            )
            .Select(hit => $"{hit.File}:{hit.Line}: {hit.Text}")
            .ToList();

        Assert.True(
            disallowedImports.Count == 0,
            "Only LiveBuildPanel may import its recommendation internals. Offending imports:\n"
                + string.Join("\n", disallowedImports)
        );
    }

    [Fact]
    public void GameInterop_does_not_depend_on_Game_feature_namespaces()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var gameInteropDir = Path.Combine(mainSource, "GameInterop");
        Assert.True(
            Directory.Exists(gameInteropDir),
            $"Could not locate GameInterop directory at '{gameInteropDir}'."
        );

        var violations = new List<string>();
        foreach (
            var file in Directory.EnumerateFiles(
                gameInteropDir,
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(mainSource, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("using BazaarPlusPlus.Game.", StringComparison.Ordinal))
                    violations.Add($"{relative}: {line}");
                if (line.StartsWith("using BazaarPlusPlus.BazaarAgent", StringComparison.Ordinal))
                    violations.Add($"{relative}: {line}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "GameInterop must stay reusable and must not import Game feature namespaces. "
                + "Pass primitive ids/DTOs across the boundary instead. Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    private readonly record struct PreviewBoundaryRule(
        string Directory,
        string DisallowedNamespace,
        string FeatureName
    );

    [Fact]
    public void BazaarAgent_core_does_not_depend_on_host_or_game_runtime_namespaces()
    {
        var repoRoot = RepoRoot();
        var autoBazaarDir = ProjectRoot(repoRoot, "BazaarPlusPlus.BazaarAgent");
        Assert.True(
            Directory.Exists(autoBazaarDir),
            $"Could not locate BazaarAgent directory at '{autoBazaarDir}'."
        );

        var violations = new List<string>();
        foreach (
            var file in Directory.EnumerateFiles(autoBazaarDir, "*.cs", SearchOption.AllDirectories)
        )
        {
            var relative = Path.GetRelativePath(autoBazaarDir, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("using ", StringComparison.Ordinal))
                    continue;

                if (
                    line.StartsWith("using UnityEngine", StringComparison.Ordinal)
                    || line.StartsWith("using BepInEx", StringComparison.Ordinal)
                    || line.StartsWith("using HarmonyLib", StringComparison.Ordinal)
                    || line.StartsWith("using TheBazaar", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarGameClient", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarGameShared", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarPlusPlus.Game.", StringComparison.Ordinal)
                    || line.StartsWith("using BazaarPlusPlus.GameInterop", StringComparison.Ordinal)
                    || line.StartsWith(
                        "using BazaarPlusPlus.BazaarAgentHost",
                        StringComparison.Ordinal
                    )
                )
                {
                    violations.Add($"{relative}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "BazaarAgent core must stay independent from Unity, BepInEx, Harmony, game DLLs, "
                + "Game features, GameInterop, and the host. Put those dependencies in BazaarAgentHost. "
                + "Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    // External battle video recording depends on replays staying in the
    // finishedAwaitingContinue phase until an explicit POST /v1/replay/continue: the recording
    // only finalizes (moov atom) when ReplayState.Exit() runs, and the exit timing belongs to the
    // external orchestrator. The host must therefore never exit ReplayState from its tick — the
    // single allowed programmatic exit lives in CombatReplayRuntime.TryContinueReplay.
    [Fact]
    public void BazaarAgentHost_never_exits_replay_state_and_main_mod_exits_only_via_continue()
    {
        var repoRoot = RepoRoot();

        // Host side: no replay auto-advance, no ReplayState.Exit calls at all.
        var hostDir = ProjectRoot(repoRoot, "BazaarPlusPlus.BazaarAgentHost");
        var hostViolations = new List<string>();
        foreach (var file in EnumerateSourceFiles(hostDir))
        {
            var relative = Path.GetRelativePath(hostDir, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            if (text.Contains("TryAdvanceReplay", StringComparison.Ordinal))
                hostViolations.Add($"{relative}: TryAdvanceReplay");
            if (text.Contains(".Exit()", StringComparison.Ordinal))
                hostViolations.Add($"{relative}: .Exit() call");
        }
        Assert.True(
            hostViolations.Count == 0,
            "BazaarAgentHost must never exit ReplayState (snapshot ticks would race the external "
                + "POST /v1/replay/continue and orphan in-flight recordings). Offending code:\n"
                + string.Join("\n", hostViolations)
        );

        // Main mod side: replay.Exit() appears exactly once, inside CombatReplayRuntime
        // (TryContinueReplay). The Harmony exit patch intercepts Exit; it must not invoke it.
        var mainSource = MainSourceRoot(repoRoot);
        var exitCallers = new List<string>();
        foreach (var file in EnumerateSourceFiles(mainSource))
        {
            var relative = Path.GetRelativePath(mainSource, file).Replace('\\', '/');
            foreach (var rawLine in File.ReadLines(file))
            {
                if (rawLine.Contains("replay.Exit()", StringComparison.Ordinal))
                    exitCallers.Add(relative);
            }
        }
        Assert.Equal(
            new[] { "Game/CombatReplay/CombatReplayRuntime.cs" },
            exitCallers.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray()
        );
    }

    // End-of-run states expose no StateOps; the agent advances them with the ReturnToMenu action,
    // which must drive RunManager.LoadMainMenu() — the method the native end-of-run "return to menu"
    // button calls (EndOfRunScreenController.ReturnToMenuClicked). It must NEVER use the heavier
    // RunManager.ReturnToMainMenu() (deletes the session; that path belongs to the in-run pause menu).
    [Fact]
    public void BazaarAgentHost_advances_end_of_run_only_via_LoadMainMenu_in_the_dispatcher()
    {
        var repoRoot = RepoRoot();
        var hostDir = ProjectRoot(repoRoot, "BazaarPlusPlus.BazaarAgentHost");

        var loadMainMenuCallers = new List<string>();
        var wrongPathViolations = new List<string>();
        foreach (var file in EnumerateSourceFiles(hostDir))
        {
            var relative = Path.GetRelativePath(hostDir, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            if (text.Contains("LoadMainMenu(", StringComparison.Ordinal))
                loadMainMenuCallers.Add(relative);
            if (text.Contains("ReturnToMainMenu", StringComparison.Ordinal))
                wrongPathViolations.Add($"{relative}: ReturnToMainMenu (session-deleting path)");
        }

        Assert.Equal(
            new[] { "BazaarAgentGameActionDispatcher.cs" },
            loadMainMenuCallers.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray()
        );
        Assert.True(
            wrongPathViolations.Count == 0,
            "End-of-run advance must use RunManager.LoadMainMenu(), never ReturnToMainMenu(). "
                + "Offending code:\n"
                + string.Join("\n", wrongPathViolations)
        );
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            );

    [Fact]
    public void BazaarAgent_tests_reference_project_instead_of_source_linking_game_files()
    {
        var repoRoot = RepoRoot();
        var testProject = Path.Combine(
            repoRoot,
            "tests",
            "BazaarAgent.Tests",
            "BazaarAgent.Tests.csproj"
        );
        Assert.True(File.Exists(testProject), $"Could not locate test project at '{testProject}'.");

        var text = File.ReadAllText(testProject);
        Assert.Contains("BazaarPlusPlus.BazaarAgent.csproj", text);
        Assert.DoesNotContain("Game\\BazaarAgent", text);
        Assert.DoesNotContain("Game/BazaarAgent", text);
        Assert.DoesNotContain("<Compile Include=\"..\\..\\Game", text);
    }

    [Fact]
    public void Main_project_has_no_host_gating_and_scrubs_both_host_artifacts()
    {
        var repoRoot = RepoRoot();
        var mainProject = Path.Combine(MainSourceRoot(repoRoot), "BazaarPlusPlus.csproj");
        Assert.True(File.Exists(mainProject), $"Could not locate main project at '{mainProject}'.");

        var project = XDocument.Load(mainProject);
        var elements = project.Descendants().ToList();

        // After the inversion the main project carries no host gating at all.
        Assert.DoesNotContain(elements, e => e.Name.LocalName == "EnableBazaarAgentHost");
        Assert.DoesNotContain(
            elements,
            e =>
                e.Name.LocalName == "DefineConstants"
                && e.Value.Contains("BPP_BAZAARAGENT_HOST", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            elements,
            e =>
                e.Name.LocalName == "ProjectReference"
                && (
                    Attribute(e, "Include")
                        ?.Contains("BazaarPlusPlus.BazaarAgent.csproj", StringComparison.Ordinal)
                    ?? false
                )
        );
        Assert.DoesNotContain(
            elements,
            e =>
                e.Name.LocalName == "Compile"
                && (
                    Attribute(e, "Include")?.Contains("BazaarAgentHost", StringComparison.Ordinal)
                    ?? false
                )
        );

        // The agent core and host are their own projects under src/; the main project must not
        // reference either (it publishes the public GameInterop facade instead). The agent-core
        // ProjectReference absence is asserted above; here we also forbid a host ProjectReference.
        // No Compile Remove="BazaarAgent*/**" assertion is needed now that those source trees live
        // outside the main project's directory cone under src/.
        Assert.DoesNotContain(
            elements,
            e =>
                e.Name.LocalName == "ProjectReference"
                && (
                    Attribute(e, "Include")
                        ?.Contains(
                            "BazaarPlusPlus.BazaarAgentHost.csproj",
                            StringComparison.Ordinal
                        )
                    ?? false
                )
        );

        // Default Debug build unconditionally scrubs BOTH host dlls from the live plugins folder.
        foreach (
            var dll in new[]
            {
                "BazaarPlusPlus.BazaarAgent.dll",
                "BazaarPlusPlus.BazaarAgentHost.dll",
            }
        )
        {
            var deletes = elements
                .Where(e =>
                    e.Name.LocalName == "FilesToDelete"
                    && (Attribute(e, "Include")?.Contains(dll, StringComparison.Ordinal) ?? false)
                )
                .ToList();
            Assert.True(deletes.Count > 0, $"Debug FilesToDelete must scrub {dll}.");
            Assert.All(
                deletes,
                e =>
                    Assert.True(
                        Attribute(e, "Condition") is null,
                        $"The scrub of {dll} must be unconditional (no host flag gate)."
                    )
            );
        }

        // Release: both host dlls scrubbed from BOTH installer payload trees.
        foreach (
            var dll in new[]
            {
                "BazaarPlusPlus.BazaarAgent.dll",
                "BazaarPlusPlus.BazaarAgentHost.dll",
            }
        )
        {
            var installerDeletes = elements
                .Where(e =>
                    e.Name.LocalName == "InstallerFilesToDelete"
                    && (Attribute(e, "Include")?.Contains(dll, StringComparison.Ordinal) ?? false)
                )
                .Select(e => Attribute(e, "Include")!)
                .ToList();
            Assert.Contains(installerDeletes, p => p.Contains("/macos/", StringComparison.Ordinal));
            Assert.Contains(
                installerDeletes,
                p => p.Contains("/windows/", StringComparison.Ordinal)
            );
        }
    }

    [Fact]
    public void BazaarAgent_host_has_no_runtime_config_switches()
    {
        var repoRoot = RepoRoot();
        var optionsFile = Path.Combine(
            ProjectRoot(repoRoot, "BazaarPlusPlus.BazaarAgentHost"),
            "BazaarAgentBepInExOptions.cs"
        );
        var portsFile = Path.Combine(
            ProjectRoot(repoRoot, "BazaarPlusPlus.BazaarAgent"),
            "Contract",
            "BazaarAgentPorts.cs"
        );
        Assert.True(File.Exists(optionsFile), $"Could not locate options file at '{optionsFile}'.");
        Assert.True(File.Exists(portsFile), $"Could not locate ports file at '{portsFile}'.");

        var optionsText = File.ReadAllText(optionsFile);
        Assert.DoesNotContain("ConfigEntry<", optionsText);
        Assert.DoesNotContain(".Bind(", optionsText);
        Assert.DoesNotContain("BepInEx.Configuration", optionsText);
        Assert.DoesNotContain("Enabled", optionsText);
        Assert.DoesNotContain("HttpListenerPort", optionsText);

        var portsText = File.ReadAllText(portsFile);
        Assert.DoesNotContain("bool Enabled", portsText);
        Assert.DoesNotContain("int HttpListenerPort {", portsText);
        Assert.Contains("HttpListenerPort = 47900", portsText);
    }

    [Fact]
    public void RandomHeroSkinPool_has_no_legacy_playerprefs_migration()
    {
        var repoRoot = RepoRoot();
        var source = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(repoRoot),
                "Game",
                "Lobby",
                "RandomHeroSkinPool",
                "RandomHeroSkinPoolPlayerPrefs.cs"
            )
        );

        Assert.DoesNotContain(string.Concat("Legacy", "HeroSkinPool", "PrefsKeyPrefix"), source);
        Assert.DoesNotContain(string.Concat("BPP.Random", "HeroSkinPool", ".Selected"), source);
        Assert.DoesNotContain(string.Concat("BuildLegacy", "HeroSkin", "PrefsKey"), source);
    }

    [Fact]
    public void Capture_modules_use_ui_chrome_suppression_seam()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var screenshotSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "Screenshots", "EndOfRunScreenshotController.cs")
        );
        var recorderSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CombatReplay",
                "Video",
                "CombatReplayVideoRecorder.cs"
            )
        );
        var chromeSuppressionSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "OverlayPanels", "BppUiChromeSuppression.cs")
        );

        Assert.Contains("BppUiChromeSuppression.Begin", screenshotSource);
        Assert.Contains("BppUiChromeSuppressionMode.Screenshot", screenshotSource);
        Assert.Contains("BppUiChromeSuppression.Begin", recorderSource);
        Assert.Contains("BppUiChromeSuppressionMode.ReplayRecording", recorderSource);

        foreach (var source in new[] { screenshotSource, recorderSource })
        {
            Assert.DoesNotContain(
                "CollectionPanelDockButtonController.BeginScreenshotSuppression",
                source
            );
            Assert.DoesNotContain("BppSettingsDockController.BeginScreenshotSuppression", source);
            Assert.DoesNotContain("CombatStatusBarFeature.BeginScreenshotSuppression", source);
        }

        Assert.Contains(
            "CollectionPanelDockButtonController.BeginScreenshotSuppression",
            chromeSuppressionSource
        );
        Assert.Contains(
            "BppSettingsDockController.BeginScreenshotSuppression",
            chromeSuppressionSource
        );
        Assert.Contains(
            "CombatStatusBarFeature.BeginScreenshotSuppression",
            chromeSuppressionSource
        );
    }

    [Fact]
    public void PvpBattles_remains_a_shared_game_module()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var pvpBattlesDir = Path.Combine(mainSource, "Game", "PvpBattles");

        Assert.True(Directory.Exists(pvpBattlesDir), $"Could not locate '{pvpBattlesDir}'.");
        Assert.False(
            Directory.Exists(Path.Combine(mainSource, "Game", "CombatReplay", "PvpBattles"))
        );
        Assert.False(
            Directory.Exists(Path.Combine(mainSource, "Game", "HistoryPanel", "PvpBattles"))
        );
        Assert.False(
            Directory.Exists(Path.Combine(mainSource, "Game", "RunLogging", "PvpBattles"))
        );

        var combatReplaySource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "CombatReplay", "CombatReplayCaptureService.cs")
        );
        var historyProjectionSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "HistoryPanel",
                "Data",
                "HistoryBattlePreviewProjection.cs"
            )
        );
        var runLoggingSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "RunLogging", "RunLoggingModule.cs")
        );

        Assert.Contains("using BazaarPlusPlus.Game.PvpBattles;", combatReplaySource);
        Assert.Contains("using BazaarPlusPlus.Game.PvpBattles;", historyProjectionSource);
        Assert.Contains("using BazaarPlusPlus.Game.PvpBattles;", runLoggingSource);
    }

    [Fact]
    public void Run_script_exposes_only_the_canonical_bazaaragent_flag()
    {
        var repoRoot = RepoRoot();
        var runScript = Path.Combine(repoRoot, "run.sh");
        Assert.True(File.Exists(runScript), $"Could not locate run script at '{runScript}'.");

        var text = File.ReadAllText(runScript);
        Assert.Contains("--with-bazaaragent", text);
        Assert.DoesNotContain("--with-bazaaragent-host", text);
        Assert.DoesNotContain(
            "--bazaaragent",
            text.Replace("--with-bazaaragent", "", StringComparison.Ordinal)
        );
        Assert.DoesNotContain("--with-autobazaar-host", text);
    }

    [Fact]
    public void BazaarAgent_host_repackages_production_zip_after_copying_optional_artifacts()
    {
        var repoRoot = RepoRoot();
        var hostProject = Path.Combine(
            ProjectRoot(repoRoot, "BazaarPlusPlus.BazaarAgentHost"),
            "BazaarPlusPlus.BazaarAgentHost.csproj"
        );
        Assert.True(File.Exists(hostProject), $"Could not locate host project at '{hostProject}'.");

        var project = XDocument.Load(hostProject);
        var elements = project.Descendants().ToList();
        var target = Assert.Single(
            elements,
            e =>
                e.Name.LocalName == "Target" && Attribute(e, "Name") == "PackageHostInstallerSource"
        );
        var condition = Attribute(target, "Condition") ?? string.Empty;
        Assert.Equal("CopyHostToInstallerSource", Attribute(target, "AfterTargets"));
        Assert.Contains("$(BuildProductionPackage)", condition);
        Assert.Contains("true", condition);

        foreach (var platform in new[] { "macos", "windows" })
        {
            Assert.Contains(
                elements,
                e =>
                    e.Name.LocalName == "ZipDirectory"
                    && (
                        Attribute(e, "SourceDirectory")
                            ?.Contains($"/SourceForBuild/{platform}", StringComparison.Ordinal)
                        ?? false
                    )
                    && (
                        Attribute(e, "DestinationFile")
                            ?.Contains(
                                $"/BepInExSource/{platform}/BepInEx.zip",
                                StringComparison.Ordinal
                            )
                        ?? false
                    )
                    && Attribute(e, "Overwrite") == "true"
            );
        }
    }

    [Fact]
    public void BazaarPlusPlus_assembly_does_not_reference_the_agent_module()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var violations = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(mainSource, "*.cs", SearchOption.TopDirectoryOnly)
        )
            ScanForAgentImports(file, mainSource, violations);

        foreach (var dir in new[] { "Core", "Game", "GameInterop", "Patches", "Infrastructure" })
        {
            var full = Path.Combine(mainSource, dir);
            if (!Directory.Exists(full))
                continue;
            foreach (
                var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories)
            )
                ScanForAgentImports(file, mainSource, violations);
        }

        Assert.True(
            violations.Count == 0,
            "BazaarPlusPlus must not reference the BazaarAgent module (pure core or host). It "
                + "publishes the public GameInterop facade instead. Offending imports:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Production_assemblies_live_under_src_not_repo_root()
    {
        var repoRoot = RepoRoot();

        // Guard against regressing to the old flat layout: no production csproj may sit at the
        // repo root. Every assembly lives in its own src/<AssemblyName>/ directory.
        var strayRootCsprojs = Directory
            .EnumerateFiles(repoRoot, "BazaarPlusPlus*.csproj", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(
            strayRootCsprojs.Count == 0,
            "No BazaarPlusPlus*.csproj may live at the repo root; keep each assembly under "
                + "src/<AssemblyName>/. Offending root csprojs:\n"
                + string.Join("\n", strayRootCsprojs)
        );

        foreach (
            var assembly in new[]
            {
                "BazaarPlusPlus",
                "BazaarPlusPlus.ModApi",
                "BazaarPlusPlus.Storage",
                "BazaarPlusPlus.Localization",
                "BazaarPlusPlus.BazaarAgent",
                "BazaarPlusPlus.BazaarAgentHost",
            }
        )
        {
            var csproj = Path.Combine(ProjectRoot(repoRoot, assembly), assembly + ".csproj");
            Assert.True(
                File.Exists(csproj),
                $"Expected production project at '{csproj}'. Keep every assembly under src/<AssemblyName>/."
            );
        }
    }

    private static void ScanForAgentImports(string file, string baseDir, List<string> violations)
    {
        var relative = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
        foreach (var rawLine in File.ReadLines(file))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("using BazaarPlusPlus.BazaarAgent", StringComparison.Ordinal))
                violations.Add($"{relative}: {line}");
        }
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;

    // Production assemblies now live under <repo>/src/<AssemblyName>/. These helpers keep the
    // layering assertions anchored to the moved source trees while RepoRoot() stays the repo root.
    private static string MainSourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "src", "BazaarPlusPlus");

    private static string ProjectRoot(string repoRoot, string projectName) =>
        Path.Combine(repoRoot, "src", projectName);

    // The compile-time path of this source file anchors the repo root without loading any
    // game-coupled assembly at runtime: <repo>/tests/Architecture.Tests/CoreLayeringTests.cs.
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var testDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testDir, "..", ".."));
    }
}
