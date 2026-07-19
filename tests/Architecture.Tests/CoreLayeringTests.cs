#nullable enable
using System.Diagnostics;
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
        var overlayHostSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "OverlayPanels", "OverlayPanelHost.cs")
        );
        const string oldCollectionPanelHotkeyPathIdentifier =
            @"(?<![A-Za-z0-9_])CollectionPanelHotkeyPathConfig(?![A-Za-z0-9_])";

        Assert.DoesNotContain("CollectionPanelSettingsDockEntry", compositionSource);
        Assert.DoesNotMatch(oldCollectionPanelHotkeyPathIdentifier, configSource);
        Assert.DoesNotMatch(oldCollectionPanelHotkeyPathIdentifier, configInterfaceSource);
        Assert.DoesNotContain("WasPressedThisFrame(togglePath", collectionPanelSource);
        // The toggle hotkey is registered by the panel but polled centrally by the overlay host.
        Assert.Contains("BppHotkeyActionId.ToggleCollectionPanel", collectionPanelSource);
        Assert.Contains("BppHotkeyService.WasToggleHotkeyPressedThisFrame", overlayHostSource);
    }

    [Fact]
    public void Overlay_panel_host_mounts_before_every_main_overlay_panel()
    {
        var repoRoot = RepoRoot();
        var compositionSource = File.ReadAllText(
            Path.Combine(MainSourceRoot(repoRoot), "BppComposition.cs")
        );

        var hostIndex = compositionSource.IndexOf(
            "_mountables.Register(overlayPanelHostMount);",
            StringComparison.Ordinal
        );
        var collectionIndex = compositionSource.IndexOf(
            "new CollectionPanelMount(",
            StringComparison.Ordinal
        );
        var historyIndex = compositionSource.IndexOf(
            "new HistoryPanelMount(",
            StringComparison.Ordinal
        );
        var liveBuildIndex = compositionSource.IndexOf(
            "new LiveBuildPanelMount(",
            StringComparison.Ordinal
        );

        Assert.True(hostIndex >= 0, "OverlayPanelHostMount registration should exist.");
        Assert.True(
            collectionIndex >= 0 && historyIndex >= 0 && liveBuildIndex >= 0,
            "Every Main Overlay Panel mount registration should exist."
        );
        Assert.True(
            hostIndex < collectionIndex && hostIndex < historyIndex && hostIndex < liveBuildIndex,
            "OverlayPanelHostMount must register before every Main Overlay Panel mount."
        );
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
    public void CollectionPanel_close_button_routes_through_overlay_host()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var collectionPanelSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "CollectionPanel", "CollectionPanel.cs")
        );

        var commandsIndex = collectionPanelSource.IndexOf(
            "private sealed class PanelCommands",
            StringComparison.Ordinal
        );
        var closeCommandIndex = collectionPanelSource.IndexOf(
            "public void Close()",
            commandsIndex,
            StringComparison.Ordinal
        );
        var nextCommandIndex = collectionPanelSource.IndexOf(
            "public void SetActiveTab",
            closeCommandIndex,
            StringComparison.Ordinal
        );

        Assert.True(commandsIndex >= 0, "CollectionPanel.PanelCommands should exist.");
        Assert.True(closeCommandIndex > commandsIndex, "PanelCommands.Close() should exist.");
        Assert.True(
            nextCommandIndex > closeCommandIndex,
            "PanelCommands.Close() should appear before SetActiveTab()."
        );

        var closeCommandSource = collectionPanelSource.Substring(
            closeCommandIndex,
            nextCommandIndex - closeCommandIndex
        );

        Assert.DoesNotContain("panel.Close()", closeCommandSource);
        Assert.Contains("RequestClose", closeCommandSource);
    }

    [Fact]
    public void CollectionGridVirtualizer_retains_shown_cards_when_filters_reorder_the_grid()
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

        var setVisibleStart = virtualizerSource.IndexOf(
            "public void SetVisible(",
            StringComparison.Ordinal
        );
        var setViewportStart = virtualizerSource.IndexOf(
            "public void SetViewport(",
            setVisibleStart,
            StringComparison.Ordinal
        );
        Assert.True(setVisibleStart >= 0 && setViewportStart > setVisibleStart);

        var setVisibleSource = virtualizerSource.Substring(
            setVisibleStart,
            setViewportStart - setVisibleStart
        );
        Assert.Contains("RetainShownCells", setVisibleSource);
        Assert.DoesNotContain("RecycleAll();", setVisibleSource);
        Assert.Contains("if (!cell.IsShown)", virtualizerSource);
        Assert.Contains("ReferenceEquals(cell.Vm, nextVisible[newIndex])", virtualizerSource);
        Assert.Contains("cell.Index = newIndex;", virtualizerSource);
        Assert.Contains("cell.HoverRelay?.Bind(cell.Session);", virtualizerSource);
        Assert.Contains("Reposition(newIndex, cell);", virtualizerSource);
    }

    [Fact]
    public void CollectionPanel_tier_tooltips_are_scoped_to_collection_preview_cards()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var patchSource = File.ReadAllText(
            Path.Combine(mainSource, "Patches", "CollectionPanel", "CollectionTierTooltipPatch.cs")
        );
        var previewSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Patches",
                "CollectionPanel",
                "CollectionTierTooltipPreview.cs"
            )
        );
        var ownerSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CollectionPanel",
                "Grid",
                "CollectionNativeCardPreviewOwner.cs"
            )
        );
        var destroyPatchSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Patches",
                "CollectionPanel",
                "CollectionCardPreviewDestroyPatch.cs"
            )
        );

        Assert.Contains("nameof(CardTooltipData.GetActiveAbilityTooltipBlock)", patchSource);
        Assert.Contains("nameof(CardTooltipData.GetPassiveTooltipBlock)", patchSource);
        Assert.Contains("nameof(CooldownRenderer.RenderFromTooltip)", patchSource);
        Assert.Contains("CollectionTierTooltipRegistry.Contains", previewSource);
        Assert.Contains("CollectionTierTooltipRegistry.Register", ownerSource);
        Assert.Contains("CollectionTierTooltipRegistry.Unregister", ownerSource);
        Assert.Contains("PreviewOwner?.OnNativeDestroyed", destroyPatchSource);
        Assert.Contains("CardExtensions.BuildAttributeDictionaryForTier", previewSource);
        Assert.Contains("CollectionTierTooltipTextMerger.Merge", previewSource);
        Assert.Contains("TryGetTierAttributeValues", previewSource);
    }

    [Fact]
    public void CollectionPanel_IME_tracking_uses_the_new_input_system()
    {
        var source = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "CollectionPanel",
                "CollectionPanel.cs"
            )
        );

        Assert.Contains("onIMECompositionChange", source);
        Assert.DoesNotContain("UnityEngine.Input.compositionString", source);
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

        var applyCellScaleIndex = virtualizerSource.IndexOf(
            "private void ApplyCellScale",
            StringComparison.Ordinal
        );
        Assert.True(
            applyCellScaleIndex >= 0,
            "CollectionGridVirtualizer.ApplyCellScale should exist."
        );
        var oldClampIndex = virtualizerSource.IndexOf(
            "natW * scale > maxWidth",
            applyCellScaleIndex,
            StringComparison.Ordinal
        );
        Assert.True(
            oldClampIndex < 0,
            "The old frame-width clamp shrinks Large item cards because their frame art overhangs the body."
        );

        Assert.Contains("BadgeRootHeightScale", badgeSource);
        Assert.Contains("CollectionGridVirtualizer.FallbackNativeCardHeight / 200f", badgeSource);
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
        var overlayHostSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "OverlayPanels", "OverlayPanelHost.cs")
        );

        Assert.DoesNotContain("LiveBuildPanelSettingsDockEntry", compositionSource);
        Assert.DoesNotContain("OpenFromDockEntry", liveBuildPanelSource);
        // The toggle hotkey is registered by the panel but polled centrally by the overlay host.
        Assert.Contains("BppHotkeyActionId.ToggleLiveBuildPanel", liveBuildPanelSource);
        Assert.Contains("BppHotkeyService.WasToggleHotkeyPressedThisFrame", overlayHostSource);
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
    public void LiveBuildPanel_owns_build_recommendation_implementation()
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
        var liveBuildPanelMountFile = Path.GetFullPath(
            Path.Combine(mainSource, "Game", "LiveBuildPanel", "LiveBuildPanelMount.cs")
        );
        var compositionFile = Path.GetFullPath(Path.Combine(mainSource, "BppComposition.cs"));

        bool IsAllowedRecommendationConsumer(string file)
        {
            var fullPath = Path.GetFullPath(file);
            return fullPath.StartsWith(recommendationsRoot, StringComparison.Ordinal)
                || string.Equals(fullPath, liveBuildPanelFile, StringComparison.Ordinal)
                || string.Equals(fullPath, liveBuildPanelMountFile, StringComparison.Ordinal)
                || string.Equals(fullPath, compositionFile, StringComparison.Ordinal);
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
            "Recommendation internals may only be used by the owning panel, its mount adapter, "
                + "and the composition root that owns their plugin-lifetime catalog. Offending imports:\n"
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

    [Fact]
    public void VoiceSubtitles_game_layer_does_not_reference_fmod_or_vo_player()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var voiceSubtitlesDir = Path.Combine(mainSource, "Game", "VoiceSubtitles");
        Assert.True(
            Directory.Exists(voiceSubtitlesDir),
            $"Could not locate VoiceSubtitles directory at '{voiceSubtitlesDir}'."
        );

        var forbiddenTokens = new[]
        {
            "using FMOD",
            "using FMODUnity",
            "VOPlayer",
            "EventInstance",
            "EVENT_CALLBACK",
            "PLAYBACK_STATE",
            "SoundManager",
            "EventReference",
            "CardAudio.AudioHookType",
        };
        var violations = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(
                voiceSubtitlesDir,
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(mainSource, file).Replace('\\', '/');
            var lineNumber = 0;
            foreach (var rawLine in File.ReadLines(file))
            {
                lineNumber++;
                foreach (var token in forbiddenTokens)
                {
                    if (rawLine.Contains(token, StringComparison.Ordinal))
                        violations.Add($"{relative}:{lineNumber}: {token}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "VoiceSubtitles Game code must stay FMOD/VOPlayer-free; keep native VO coupling in "
                + "GameInterop/VoiceSubtitles or Patches/VoiceSubtitles. Offending references:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void VoiceSubtitles_playvo_transpiler_uses_constant_guard_for_stopped_mask()
    {
        var repoRoot = RepoRoot();
        var patchPath = Path.Combine(
            MainSourceRoot(repoRoot),
            "Patches",
            "VoiceSubtitles",
            "VOPlayerPatches.cs"
        );
        Assert.True(File.Exists(patchPath), $"Could not locate VOPlayer patch at '{patchPath}'.");

        var source = File.ReadAllText(patchPath);
        var guardIndex = source.IndexOf("LoadsConstant", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0, "VOPlayer.PlayVO transpiler must use LoadsConstant.");
        var beforeGuard = source[..guardIndex];

        Assert.Contains("private const int StoppedCallbackMask = 0x20;", source);
        Assert.Contains("private const int ExpectedPatchCount = 1;", source);
        Assert.Contains("codes[i - 1].LoadsConstant(StoppedCallbackMask)", source);
        Assert.Contains(
            "VoicePatchLogEvents.CallbackPatchDegradedActualCount.Bind(actualCount)",
            source
        );
        Assert.Contains(
            "VoicePatchLogEvents.CallbackPatchDegradedExpectedCount.Bind(ExpectedPatchCount)",
            source
        );
        Assert.DoesNotContain("expected=1", source);
        Assert.DoesNotContain("opcode == OpCodes.Ldc_I4", source);
        Assert.DoesNotContain("OpCodes.Ldc_I4,", beforeGuard);
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

    [Fact]
    public void BazaarAgent_core_project_dependencies_remain_System_and_Newtonsoft_only()
    {
        var root = ProjectRoot(RepoRoot(), "BazaarPlusPlus.BazaarAgent");
        var projectPath = Path.Combine(root, "BazaarPlusPlus.BazaarAgent.csproj");
        var project = XDocument.Load(projectPath);
        var elements = project.Descendants().ToList();
        var packages = elements
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => Attribute(element, "Include"))
            .Where(name => name != null)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "NETStandard.Library", "Newtonsoft.Json" }, packages);
        Assert.DoesNotContain(elements, element => element.Name.LocalName == "ProjectReference");
        Assert.DoesNotContain(elements, element => element.Name.LocalName == "Reference");
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
    public void Bpp_ui_surfaces_use_only_the_games_native_font_assets()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var adapterSource = File.ReadAllText(
            Path.Combine(mainSource, "GameInterop", "Fonts", "NativeGameTypography.cs")
        );
        var pluginSource = File.ReadAllText(Path.Combine(mainSource, "Plugin.cs"));
        var productionSource = string.Join(
            "\n",
            Directory
                .EnumerateFiles(mainSource, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
        );
        var projectSource = File.ReadAllText(Path.Combine(mainSource, "BazaarPlusPlus.csproj"));

        Assert.Contains("NotoFontFallbackRuntime._configuration", adapterSource);
        Assert.Contains("NotoFontFallbackRuntime._loadedSerifPrimary", adapterSource);
        Assert.Contains("NotoSansFallbacksOrdered", adapterSource);
        Assert.Contains("NotoSerifFallbacksOrdered", adapterSource);
        Assert.Contains("sourceFontFile", adapterSource);
        Assert.Contains("ScriptableObject.CreateInstance<PanelTextSettings>()", adapterSource);
        Assert.Contains("textSettings.defaultFontAsset = null", adapterSource);
        Assert.Contains(
            "textSettings.fallbackFontAssets = new List<TextCoreFontAsset>()",
            adapterSource
        );
        Assert.Contains("EmojiSupportField!.SetValue(textSettings, false)", adapterSource);
        Assert.Contains(
            "SetEmptyCollection(textSettings, EmojiFallbackTextAssetsField!)",
            adapterSource
        );
        Assert.Contains(
            "SetEmptyCollection(textSettings, OsFallbackFontAssetsField!)",
            adapterSource
        );
        Assert.Contains("if (!IsIsolated(textSettings))", adapterSource);
        Assert.Contains("NativeGameTypography.InitializeForCurrentThread()", pluginSource);
        Assert.Contains("NativeGameTypography.Reset()", pluginSource);
        Assert.Contains("internal static Outcome PrepareOwnedText", adapterSource);
        Assert.Contains("internal static Outcome EnsureNativeTextCoverage", adapterSource);
        Assert.Contains("internal static Outcome TryAttachPanel", adapterSource);
        Assert.Contains("internal sealed class PanelScope", adapterSource);
        Assert.Contains("internal Outcome Apply(VisualElement element", adapterSource);
        Assert.Contains("element.style.unityFont = _bodyFont", adapterSource);
        Assert.Contains(
            "element.style.unityFontDefinition = FontDefinition.FromFont(_bodyFont)",
            adapterSource
        );
        Assert.Contains("private static bool TryGetSansFontAsset", adapterSource);
        Assert.Contains("private static bool TryGetSerifFontAsset", adapterSource);
        Assert.Contains("private static bool TryGetSansSourceFont", adapterSource);
        Assert.DoesNotContain("TryGetSerifSourceFont", adapterSource);
        Assert.DoesNotContain("BppUiFont", productionSource);
        Assert.DoesNotContain("BppTmpFont", productionSource);
        Assert.DoesNotContain("UseUiFont", productionSource);
        Assert.DoesNotContain("GetBuiltinResource<Font>", productionSource);
        Assert.DoesNotContain("CreateDynamicFontFromOSFont", productionSource);
        Assert.DoesNotContain("TMP_FontAsset.CreateFontAsset", productionSource);
        Assert.DoesNotContain("FontEngine.LoadFontFace", productionSource);
        Assert.DoesNotContain("new Font(", productionSource);
        Assert.DoesNotContain("LXGWWenKai", projectSource);
        var fontResourceDirectory = Path.Combine(mainSource, "Resources", "Fonts");
        var fontResources = Directory.Exists(fontResourceDirectory)
            ? Directory.EnumerateFiles(fontResourceDirectory).Select(Path.GetFileName).ToArray()
            : Array.Empty<string?>();
        Assert.DoesNotContain(
            fontResources,
            file => file?.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) == true
        );
        Assert.DoesNotContain(
            fontResources,
            file => file?.Contains("LXGWWenKai", StringComparison.OrdinalIgnoreCase) == true
        );
    }

    [Fact]
    public void Bpp_owned_ugui_text_uses_the_game_primary_with_the_complete_cjk_chain()
    {
        var mainSource = MainSourceRoot(RepoRoot());
        var adapterSource = File.ReadAllText(
            Path.Combine(mainSource, "GameInterop", "Fonts", "NativeGameTypography.cs")
        );
        var surfaces = new[]
        {
            Path.Combine("Game", "CombatStatusBar", "CombatStatusBar.Canvas.cs"),
            Path.Combine("Game", "CollectionPanel", "Grid", "CollectionSourceAttributionBadge.cs"),
            Path.Combine("Game", "VoiceSubtitles", "VoiceLineDisplay.cs"),
            Path.Combine("GameInterop", "Fonts", "NativeGameTitleOverlay.cs"),
        };
        var legacyTextPatterns = new[]
        {
            "AddComponent<Text>",
            "GetComponent<Text>",
            "GetComponents<Text>",
            "GetComponentInChildren<Text>",
            "GetComponentsInChildren<Text>",
            "GetComponentInParent<Text>",
            "GetComponentsInParent<Text>",
            "TryGetComponent<Text>",
            "typeof(Text)",
        };

        Assert.Contains("private static bool TryGetSansFontAsset", adapterSource);
        Assert.Contains("NotoFontFallbackRuntime._loadedSansPrimary", adapterSource);
        Assert.Contains("private static bool TryGetSerifFontAsset", adapterSource);
        Assert.Contains("NotoFontFallbackRuntime._loadedSerifPrimary", adapterSource);
        Assert.Contains("clone.fallbackFontAssetTable", adapterSource);
        foreach (var relativePath in surfaces)
        {
            var source = File.ReadAllText(Path.Combine(mainSource, relativePath));
            Assert.Contains("TextMeshProUGUI", source);
            Assert.Contains("NativeGameTypography.PrepareOwnedText", source);
            Assert.Contains(".Apply(", source);
            Assert.DoesNotContain("TryGetSansFontAsset", source);
            Assert.DoesNotContain("TryGetSansDynamicFontAsset", source);
            foreach (var legacyTextPattern in legacyTextPatterns)
                Assert.DoesNotContain(legacyTextPattern, source);
        }
    }

    [Fact]
    public void Font_strategy_does_not_escape_the_native_game_typography_layer()
    {
        var mainSource = MainSourceRoot(RepoRoot());
        var typographyDirectory = Path.Combine(mainSource, "GameInterop", "Fonts");
        var callerSource = string.Join(
            "\n",
            Directory
                .EnumerateFiles(mainSource, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.StartsWith(typographyDirectory, StringComparison.Ordinal))
                .Select(File.ReadAllText)
        );

        Assert.DoesNotContain("NativeGameFonts.", callerSource);
        Assert.DoesNotContain("TryGetSansFontAsset", callerSource);
        Assert.DoesNotContain("TryGetSansSourceFont", callerSource);
        Assert.DoesNotContain("TryGetSerifSourceFont", callerSource);
        Assert.DoesNotContain("TryConfigurePanel", callerSource);
        Assert.DoesNotContain("ReleasePanelTextSettings", callerSource);
        Assert.DoesNotContain("FontDefinition.FromFont", callerSource);
    }

    [Fact]
    public void Chinese_voice_subtitle_does_not_synthetically_bold_the_game_cjk_font()
    {
        var source = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "VoiceSubtitles",
                "VoiceLineDisplay.cs"
            )
        );

        Assert.Contains("chineseUi.fontStyle = FontStyles.Normal", source);
        Assert.Contains("label.fontStyle = FontStyles.Normal", source);
        Assert.DoesNotContain("FontStyles.Bold", source);
    }

    [Fact]
    public void Supporter_attribution_pins_one_native_font_definition_for_all_names()
    {
        var source = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "Supporters",
                "Ui",
                "BPPSupporterAttributionRow.cs"
            )
        );

        Assert.Contains("NativeGameTypography.PanelScope typography", source);
        Assert.Contains("typography.CheckExternalText", source);
        Assert.Contains("typography.Apply(label)", source);
        Assert.Contains("typography.Apply(button)", source);
        Assert.DoesNotContain("Font uiFont", source);
        Assert.DoesNotContain("FontDefinition.FromFont", source);

        var supporterNameStart = source.IndexOf(
            "private static Label CreateSupporterName",
            StringComparison.Ordinal
        );
        var supporterNameEnd = source.IndexOf(
            "private static Color ResolveTierText",
            supporterNameStart,
            StringComparison.Ordinal
        );
        Assert.True(supporterNameStart >= 0 && supporterNameEnd > supporterNameStart);
        var supporterNameMethod = source[supporterNameStart..supporterNameEnd];
        Assert.Contains("FontStyle.Normal", supporterNameMethod);
        Assert.DoesNotContain("FontStyle.Bold", supporterNameMethod);
    }

    [Fact]
    public void Collection_quality_chips_use_content_basis_and_never_wrap()
    {
        var source = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "CollectionPanel",
                "Ui",
                "CollectionPanelView.Filters.cs"
            )
        );
        var createChipButton = MethodSource(
            source,
            "private Button CreateChipButton",
            "private static Button CreateTagFacetChipButton"
        );
        var createButton = MethodSource(
            source,
            "private static Button CreateButton",
            "private static void StyleButton"
        );

        Assert.DoesNotContain("chip.style.flexBasis = 0f", createChipButton);
        Assert.DoesNotContain("chip.style.minWidth = 0f", createChipButton);
        Assert.Contains("button.style.whiteSpace = WhiteSpace.NoWrap", createButton);
    }

    [Fact]
    public void Collection_title_uses_native_game_heading_typography()
    {
        var mainSource = MainSourceRoot(RepoRoot());
        var viewSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "CollectionPanel", "Ui", "CollectionPanelView.cs")
        );
        var treeSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "CollectionPanel", "Ui", "CollectionPanelView.Tree.cs")
        );
        var adapterSource = File.ReadAllText(
            Path.Combine(mainSource, "GameInterop", "Fonts", "NativeGameTypography.cs")
        );
        var colorsSource = File.ReadAllText(
            Path.Combine(mainSource, "Infrastructure", "UiTokens", "Colors.cs")
        );
        var ensureCreated = MethodSource(
            viewSource,
            "public void EnsureCreated",
            "public void SetVisible"
        );
        var titleStyle = MethodSource(treeSource, "_title = CreateLabel", "titleRow.Add(_title);");

        Assert.Contains("NativeGameTypography.TryAttachPanel", ensureCreated);
        Assert.Contains("NativeGameTitleOverlay.TryCreate", ensureCreated);
        Assert.Contains(
            "NativeGameTypography.OwnedTextRole.Heading",
            File.ReadAllText(
                Path.Combine(mainSource, "GameInterop", "Fonts", "NativeGameTitleOverlay.cs")
            )
        );
        Assert.Contains("NotoFontFallbackRuntime._loadedSerifPrimary", adapterSource);
        Assert.Contains("_titleOverlay.Attach(_title!)", ensureCreated);
        Assert.Contains("_titleOverlay?.SetText(model.Title)", viewSource);
        Assert.Contains("FontStyle.Normal", titleStyle);
        Assert.Contains("Colors.GameTitleText", titleStyle);
        Assert.DoesNotContain("PanelFontRole.Heading", titleStyle);
        Assert.Contains("GameTitleText => Rgba(1f, 0.8352941f, 0.6745098f, 1f)", colorsSource);
    }

    [Fact]
    public void LiveBuild_title_uses_native_game_heading_typography()
    {
        var source = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "LiveBuildPanel",
                "Ui",
                "LiveBuildPanelView.cs"
            )
        );
        var ensureCreated = MethodSource(
            source,
            "public void EnsureCreated",
            "public void SetVisible"
        );
        var titleStyle = MethodSource(source, "_title = CreateLabel", "titleRow.Add(_title);");

        Assert.Contains("NativeGameTypography.TryAttachPanel", ensureCreated);
        Assert.Contains("NativeGameTitleOverlay.TryCreate", ensureCreated);
        Assert.Contains("_titleOverlay.Attach(_title!)", ensureCreated);
        Assert.Contains("_titleOverlay?.SetText(_title.text)", source);
        Assert.Contains("Sizes.FontTitle", titleStyle);
        Assert.Contains("FontStyle.Normal", titleStyle);
        Assert.Contains("Colors.GameTitleText", titleStyle);
        Assert.DoesNotContain("PanelFontRole.Heading", titleStyle);
    }

    [Fact]
    public void Bilingual_item_names_use_the_games_native_chinese_serif_fallback()
    {
        var mainSource = MainSourceRoot(RepoRoot());
        var patchSource = File.ReadAllText(
            Path.Combine(mainSource, "Patches", "Tooltips", "BilingualItemNamePatch.cs")
        );
        var eligibilitySource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "BilingualItemNames",
                "BilingualNameCardEligibility.cs"
            )
        );
        var providerSource = File.ReadAllText(
            Path.Combine(mainSource, "GameInterop", "Fonts", "NativeGameTypography.cs")
        );

        Assert.Contains("NativeGameTypography.EnsureNativeTextCoverage", patchSource);
        Assert.DoesNotContain("NativeGameFonts.TryInstallFallback", patchSource);
        Assert.Contains("BilingualNameCardEligibility.IsSupported", patchSource);
        Assert.Contains("ECardType.Item", eligibilitySource);
        Assert.Contains("ECardType.Skill", eligibilitySource);
        Assert.Contains("ECardType.EncounterStep", eligibilitySource);
        Assert.Contains("ECardType.EventEncounter", eligibilitySource);
        Assert.DoesNotContain("ECardType.SocketEffect", eligibilitySource);
        Assert.Contains("NotoFontFallbackRuntime", providerSource);
        Assert.Contains("NotoSerifFallbacksOrdered", providerSource);
        Assert.Contains("Object.Instantiate(primary)", providerSource);
        Assert.Contains("text.font = clone", providerSource);
        Assert.Contains("binding.Text != null", providerSource);
        Assert.Contains("Object.DestroyImmediate(binding.Clone)", providerSource);
        Assert.Contains("AsyncOperationHandle<TMP_FontAsset> handle = default", providerSource);
        Assert.Contains("Handles.Add(handle);\n                handle = default;", providerSource);
        Assert.Contains("TryRelease(handle)", providerSource);
        Assert.Contains("Addressables.Release(handle)", providerSource);
        Assert.Contains("finally", providerSource);
        Assert.DoesNotContain("CreateDynamicFontFromOSFont", providerSource);
        Assert.DoesNotContain("PingFang", providerSource);
        Assert.DoesNotContain("Microsoft YaHei", providerSource);
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
    public void Random_pools_reuse_native_cards_without_legacy_or_per_card_ui()
    {
        var mainSource = MainSourceRoot(RepoRoot());
        var lobbyRoot = Path.Combine(mainSource, "Game", "Lobby");
        var heroRoot = Path.Combine(lobbyRoot, "RandomHeroPool");
        var collectibleRoot = Path.Combine(lobbyRoot, "RandomHeroSkinPool");

        Assert.False(File.Exists(Path.Combine(lobbyRoot, "LobbyPanelLayout.cs")));
        Assert.False(File.Exists(Path.Combine(heroRoot, "RandomHeroPoolPanelController.cs")));
        Assert.False(
            File.Exists(Path.Combine(collectibleRoot, "RandomHeroSkinPoolPanelController.cs"))
        );

        var heroController = File.ReadAllText(
            Path.Combine(heroRoot, "RandomHeroPoolNativeController.cs")
        );
        var collectibleController = File.ReadAllText(
            Path.Combine(collectibleRoot, "RandomHeroSkinPoolNativeController.cs")
        );
        var patchSource = string.Join(
            "\n",
            File.ReadAllText(
                Path.Combine(mainSource, "Patches", "Lobby", "RandomHeroPoolPatches.cs")
            ),
            File.ReadAllText(
                Path.Combine(mainSource, "Patches", "Lobby", "RandomHeroSkinPoolPatches.cs")
            )
        );
        var replacementSource = string.Join(
            "\n",
            heroController,
            collectibleController,
            patchSource
        );

        Assert.DoesNotContain("BPP_RandomHeroPoolPanel", replacementSource);
        Assert.DoesNotContain("BPP_RandomCollectiblePoolPanel", replacementSource);
        Assert.DoesNotContain("new GameObject", replacementSource);
        Assert.DoesNotContain("UnityEngine.UI.Button", replacementSource);
        Assert.DoesNotContain("TextMeshPro", replacementSource);
        Assert.DoesNotContain("Outline", replacementSource);
        Assert.DoesNotContain("item.gameObject.AddComponent", replacementSource);
        Assert.Contains(
            "view.gameObject.AddComponent<RandomHeroPoolNativeController>()",
            heroController
        );
        Assert.Contains(
            "view.gameObject.AddComponent<RandomHeroSkinPoolNativeController>()",
            collectibleController
        );
    }

    [Fact]
    public void Random_pool_preference_keys_remain_account_scoped_and_stable()
    {
        var lobbyRoot = Path.Combine(MainSourceRoot(RepoRoot()), "Game", "Lobby");
        var heroPrefs = File.ReadAllText(
            Path.Combine(lobbyRoot, "RandomHeroPool", "RandomHeroPoolPlayerPrefs.cs")
        );
        var collectiblePrefs = File.ReadAllText(
            Path.Combine(lobbyRoot, "RandomHeroSkinPool", "RandomHeroSkinPoolPlayerPrefs.cs")
        );

        Assert.Contains("BPP.RandomHeroPool.Selected", heroPrefs);
        Assert.Contains("BPP.RandomCollectiblePool.Selected", collectiblePrefs);
        Assert.Contains("ResolveAccountScopeForPrefs", heroPrefs);
        Assert.Contains("ResolveAccountScopeForPrefs", collectiblePrefs);
        Assert.Contains("collectionType.ToString()", collectiblePrefs);
        Assert.Contains("hero.ToString()", collectiblePrefs);
    }

    [Fact]
    public void Capture_modules_use_ui_chrome_suppression_seam()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var screenshotSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "Screenshots", "EndOfRunCaptureDriver.cs")
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
        Assert.DoesNotContain("BppSettingsDockController", chromeSuppressionSource);
        Assert.Contains(
            "CombatStatusBarFeature.BeginScreenshotSuppression",
            chromeSuppressionSource
        );
    }

    [Fact]
    public void Native_settings_and_collection_dock_have_separate_layout_owners()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var nativeSettingsSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "Settings", "BppNativeSettingsSectionController.cs")
        );
        var collectionControllerSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CollectionPanel",
                "CollectionPanelDockButtonController.cs"
            )
        );
        var settingsPatchSource = File.ReadAllText(
            Path.Combine(mainSource, "Patches", "Settings", "BppSettingsDockPatch.cs")
        );
        var screenLayoutSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "Settings", "BppDockButtonScreenLayout.cs")
        );

        Assert.Contains("ScrollSpyEntry", nativeSettingsSource);
        Assert.Contains("BPP_SettingsSection", nativeSettingsSource);
        Assert.False(
            File.Exists(
                Path.Combine(mainSource, "Game", "Settings", "BppSettingsDockController.cs")
            )
        );
        Assert.Contains("TryResolveAndApplyCollection", collectionControllerSource);
        Assert.Contains("GetActiveScene().name", collectionControllerSource);
        Assert.DoesNotContain("GetActiveScene().handle", collectionControllerSource);
        Assert.DoesNotContain("BppSettingsDockController", settingsPatchSource);
        Assert.DoesNotContain("CalculateDockButtonLocalPosition", collectionControllerSource);
        Assert.DoesNotContain("siblingStepCount: 2", settingsPatchSource);
        Assert.DoesNotContain("WithRightDockStackedPlacement", settingsPatchSource);
        Assert.Contains("IsActiveBelowOwner", screenLayoutSource);
        Assert.Contains("current == owner.transform", screenLayoutSource);
        Assert.DoesNotContain("GetComponentInParent<Button>()", screenLayoutSource);
    }

    [Fact]
    public void Current_replay_recording_uses_the_lower_right_settings_dock()
    {
        var repoRoot = RepoRoot();
        var mainSource = MainSourceRoot(repoRoot);
        var controllerSource = File.ReadAllText(
            Path.Combine(
                mainSource,
                "Game",
                "CombatReplay",
                "CurrentReplayRecordingButtonController.cs"
            )
        );
        var patchSource = File.ReadAllText(
            Path.Combine(mainSource, "Patches", "Combat", "CurrentReplayRecordingButtonPatch.cs")
        );
        var runtimeSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "CombatReplay", "CombatReplayRuntime.cs")
        );
        var projectSource = File.ReadAllText(Path.Combine(mainSource, "BazaarPlusPlus.csproj"));

        Assert.Contains("typeof(FightMenuDialog)", patchSource);
        Assert.Contains("\"SettingButton\"", patchSource);
        Assert.Contains("CollectionPanelDockButtonController", controllerSource);
        Assert.Contains("BppDockButtonScreenLayout", controllerSource);
        Assert.Contains("BppDockButtonSpriteProvider.Get(spriteId)", controllerSource);
        Assert.Contains("BppDockButtonVisuals.ApplyIcon(_icon, sprite)", controllerSource);
        Assert.Contains("BppDockButtonVisualState.Capture(", controllerSource);
        Assert.Contains("BppDockButtonVisuals.Apply(", controllerSource);
        Assert.Contains("fallbackFrame.color = new Color(1f, 1f, 1f, 0f)", controllerSource);
        Assert.Contains("_icon != null && _icon.sprite != null", controllerSource);
        Assert.Contains("tooltip.PositionOverUI(_cloneRect)", controllerSource);
        Assert.Contains("tooltip._coroutine != null", controllerSource);
        Assert.Contains("tooltip.KeepTooltipWithinBounds()", controllerSource);
        Assert.Contains("GetWorldCorners(_buttonWorldCorners)", controllerSource);
        Assert.Contains("tooltip._contentForWorldBounds ?? tooltipRect", controllerSource);
        Assert.Contains("buttonTop - tooltipBottom + gap", controllerSource);
        Assert.Contains("while (_tooltipHovered)", controllerSource);
        Assert.DoesNotContain("const int maxFrames", controllerSource);
        Assert.DoesNotContain("_cloneRect.TransformVector(", controllerSource);
        Assert.DoesNotContain("_cloneRect.position + Vector3.up", controllerSource);
        Assert.Contains("CurrentReplayRecordingUiLogState", controllerSource);
        Assert.Contains("BindNativeActions(", controllerSource);
        Assert.Contains("nativeRecapButton.onClick.Invoke", controllerSource);
        Assert.Contains("nativeRecapBackButton.onClick.Invoke", controllerSource);
        Assert.Contains("_button.interactable = nativeActionsBound", controllerSource);
        Assert.Contains("\"RecapButton\"", patchSource);
        Assert.Contains("\"BackButton\"", patchSource);
        Assert.Contains("StartCurrentReplayAfterRecapClosed", runtimeSource);
        Assert.Contains(
            "boardManager.IsRecapViewOpen || boardManager.StorageMoving",
            runtimeSource
        );
        Assert.Contains("invokeNativeRecapBack();", runtimeSource);
        Assert.Contains(
            "!boardManager.IsRecapViewOpen && !boardManager.StorageMoving",
            runtimeSource
        );
        Assert.Contains("invokeNativeRecap();", runtimeSource);
        Assert.Contains("Singleton<BoardManager>.Instance?.IsRecapViewOpen != true", runtimeSource);
        Assert.Contains("\"native-recap-not-started\"", runtimeSource);
        Assert.Contains("\"native-replay-invoke-failed\"", runtimeSource);
        Assert.Contains("\"native-recap-close-timeout\"", runtimeSource);
        Assert.Contains("CancelArmedCurrentReplay(recordingId, endReason)", runtimeSource);
        Assert.Contains("CurrentReplayRecapPostRollSeconds = 3f", runtimeSource);
        Assert.Contains(
            "new WaitForSecondsRealtime(CurrentReplayRecapPostRollSeconds)",
            runtimeSource
        );
        Assert.Contains("\"native-replay-recap-post-roll-ended\"", runtimeSource);
        Assert.Contains("Replay recording is still capturing the recap.", runtimeSource);
        Assert.DoesNotContain("PublishEnded(\"native-replay-ended\"", runtimeSource);
        Assert.True(
            runtimeSource.IndexOf("invokeNativeRecap();", StringComparison.Ordinal)
                < runtimeSource.IndexOf(
                    "new WaitForSecondsRealtime(CurrentReplayRecapPostRollSeconds)",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            "settingsButton.gameObject.AddComponent<CurrentReplayRecordingButtonController>()",
            controllerSource
        );
        Assert.DoesNotContain("RecapReplayButtonContainer", patchSource);
        Assert.DoesNotContain("_nativeRect.anchoredPosition", controllerSource);
        Assert.DoesNotContain("TextMeshPro", controllerSource);
        Assert.DoesNotContain("NativeGameTypography", controllerSource);
        Assert.DoesNotContain("targetGraphic = _glyph", controllerSource);
        Assert.DoesNotContain("_button.targetGraphic = frame", controllerSource);
        Assert.DoesNotContain("nativeIcon.enabled = false", controllerSource);
        Assert.DoesNotContain("nativeIcon.gameObject.SetActive(false)", controllerSource);
        Assert.DoesNotContain(
            "clone.AddComponent<CurrentReplayRecordingButtonController>()",
            controllerSource
        );
        foreach (
            var iconName in new[]
            {
                "replay-export-icon.png",
                "replay-recording-icon.png",
                "replay-view-icon.png",
                "replay-retry-icon.png",
            }
        )
        {
            Assert.True(
                File.Exists(Path.Combine(mainSource, "Resources", "DockButtons", iconName)),
                $"Missing dock icon '{iconName}'."
            );
            Assert.Contains($"BazaarPlusPlus.Resources.DockButtons.{iconName}", projectSource);
        }
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
    public void Run_logging_is_a_feature_owned_event_intake_not_a_mounted_controller()
    {
        var mainSource = MainSourceRoot(RepoRoot());
        var moduleSource = File.ReadAllText(
            Path.Combine(mainSource, "Game", "RunLogging", "RunLoggingModule.cs")
        );
        var compositionSource = File.ReadAllText(Path.Combine(mainSource, "BppComposition.cs"));
        var pluginSource = File.ReadAllText(Path.Combine(mainSource, "Plugin.cs"));

        Assert.Contains("sealed class RunLoggingModule : IBppFeature", moduleSource);
        Assert.False(
            File.Exists(Path.Combine(mainSource, "Game", "RunLogging", "RunLoggingController.cs"))
        );
        Assert.False(
            File.Exists(Path.Combine(mainSource, "Game", "RunLogging", "RunLogCaptureService.cs"))
        );
        Assert.DoesNotContain("RunLoggingControllerCore", moduleSource);
        Assert.DoesNotContain("RunLogPvpBattleInput", moduleSource);
        Assert.DoesNotContain("ComponentMount<RunLoggingController>", compositionSource);
        Assert.Contains("_featureRegistry.Register(_runLoggingModule);", compositionSource);
        Assert.Contains("_featureRegistry.Stop();", compositionSource);
        Assert.Contains("() => CreateStore(services)", moduleSource);

        var combatReplayRegistration = compositionSource.IndexOf(
            "_featureRegistry.Register(_combatReplayModule);",
            StringComparison.Ordinal
        );
        var runLoggingRegistration = compositionSource.IndexOf(
            "_featureRegistry.Register(_runLoggingModule);",
            StringComparison.Ordinal
        );
        Assert.True(
            combatReplayRegistration >= 0 && runLoggingRegistration > combatReplayRegistration,
            "Reverse feature Stop must settle RunLogging before stopping CombatReplay."
        );

        var unmount = pluginSource.IndexOf(
            "PluginTeardownStep.UnmountComponents",
            StringComparison.Ordinal
        );
        var disposeComposition = pluginSource.IndexOf(
            "PluginTeardownStep.DisposeComposition",
            StringComparison.Ordinal
        );
        var destroyReplay = pluginSource.IndexOf(
            "PluginTeardownStep.DestroyCombatReplayRuntime",
            StringComparison.Ordinal
        );
        Assert.True(
            unmount >= 0 && disposeComposition > unmount && destroyReplay > disposeComposition,
            "Teardown must unmount, stop features, then destroy CombatReplayRuntime."
        );
    }

    [Fact]
    public void Event_preview_owns_queries_plans_evaluation_and_presentation()
    {
        var mainSource = MainSourceRoot(RepoRoot());
        var eventPreviewRoot = Path.Combine(mainSource, "Game", "EventPreview");
        var collectionRoot = Path.Combine(mainSource, "Game", "CollectionPanel");
        var tooltipPatches = Path.Combine(mainSource, "Patches", "Tooltips");
        var moduleSource = File.ReadAllText(
            Path.Combine(eventPreviewRoot, "EncounterPreviewModule.cs")
        );
        var eventLocalizationSource = File.ReadAllText(
            Path.Combine(eventPreviewRoot, "EventPreviewLocalization.cs")
        );
        var collectionLocalizationSource = File.ReadAllText(
            Path.Combine(collectionRoot, "Data", "CollectionLocalizationResolver.cs")
        );
        var effectReaderSource = File.ReadAllText(
            Path.Combine(mainSource, "GameInterop", "Cards", "CardEffectValueReader.cs")
        );

        Assert.Contains("interface IEncounterPreviewModule", moduleSource);
        Assert.Contains(
            "EncounterPreviewResult ResolveEvent(EventPreviewQuery query)",
            moduleSource
        );
        Assert.Contains(
            "EncounterStepPreviewResult ResolveStep(EncounterStepPreviewQuery query)",
            moduleSource
        );
        Assert.Contains(
            "LevelUpPreviewResult ResolveLevelUp(LevelUpPreviewQuery query)",
            moduleSource
        );
        Assert.Contains("class CardEffectValueReader", effectReaderSource);
        Assert.DoesNotContain("GetProperty(\"Abilities\")", eventLocalizationSource);
        Assert.DoesNotContain("GetProperty(\"Abilities\")", collectionLocalizationSource);

        foreach (var file in Directory.EnumerateFiles(eventPreviewRoot, "*.cs"))
            Assert.DoesNotContain("Game.CollectionPanel", File.ReadAllText(file));
        foreach (
            var file in new[]
            {
                Path.Combine(tooltipPatches, "EncounterEventTooltipPatch.cs"),
                Path.Combine(tooltipPatches, "HeroLevelRewardsTooltipPatch.cs"),
            }
        )
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("Game.CollectionPanel", source);
            Assert.DoesNotContain("EventPreviewPlanRuntime", source);
            Assert.DoesNotContain("TryBuildInventory", source);
            Assert.DoesNotContain("EncounterTierRuntime", source);
        }

        Assert.Empty(Directory.EnumerateFiles(collectionRoot, "CollectionEncounter*.cs"));
        Assert.False(
            File.Exists(Path.Combine(collectionRoot, "CollectionMerchantTierResolver.cs"))
        );
        Assert.False(
            File.Exists(
                Path.Combine(
                    collectionRoot,
                    "Data",
                    "CollectionLocalizationResolver.PreviewPlans.cs"
                )
            )
        );
        foreach (
            var file in Directory.EnumerateFiles(
                collectionRoot,
                "*.cs",
                SearchOption.AllDirectories
            )
        )
            Assert.DoesNotContain("Game.EventPreview", File.ReadAllText(file));
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
    public void Run_script_exposes_publish_and_fetch_data_without_all()
    {
        var repoRoot = RepoRoot();
        var runScript = Path.Combine(repoRoot, "run.sh");
        Assert.True(File.Exists(runScript), $"Could not locate run script at '{runScript}'.");

        var text = File.ReadAllText(runScript);
        Assert.Contains("publish)", text);
        Assert.Contains("fetch-data)", text);
        Assert.DoesNotContain("    all)", text);
        Assert.DoesNotContain("$0 all", text);
    }

    [Fact]
    public void Run_script_disables_ipv6_by_default_but_allows_an_explicit_override()
    {
        var repoRoot = RepoRoot();
        var runScript = Path.Combine(repoRoot, "run.sh");
        Assert.True(File.Exists(runScript), $"Could not locate run script at '{runScript}'.");

        var text = File.ReadAllText(runScript);
        Assert.Contains(
            "export DOTNET_SYSTEM_NET_DISABLEIPV6=\"${DOTNET_SYSTEM_NET_DISABLEIPV6:-1}\"",
            text
        );
    }

    [Fact]
    public void Installer_side_effects_require_an_explicit_production_package()
    {
        var repoRoot = RepoRoot();
        var projectsAndTargets = new Dictionary<string, string[]>
        {
            [Path.Combine(MainSourceRoot(repoRoot), "BazaarPlusPlus.csproj")] =
            [
                "CopyToInstallerSource",
                "PackageInstallerSource",
            ],
            [
                Path.Combine(
                    ProjectRoot(repoRoot, "BazaarPlusPlus.BazaarAgentHost"),
                    "BazaarPlusPlus.BazaarAgentHost.csproj"
                )
            ] = ["CopyHostToInstallerSource", "PackageHostInstallerSource"],
        };

        foreach (var (projectPath, targetNames) in projectsAndTargets)
        {
            var project = XDocument.Load(projectPath);
            foreach (var targetName in targetNames)
            {
                var target = Assert.Single(
                    project.Descendants(),
                    element =>
                        element.Name.LocalName == "Target"
                        && Attribute(element, "Name") == targetName
                );
                var condition = Attribute(target, "Condition") ?? string.Empty;
                Assert.Contains("$(BuildProductionPackage)", condition);
                Assert.Contains("true", condition);
            }
        }
    }

    [Fact]
    public void Remote_embedded_data_pipeline_declares_the_two_stable_resources()
    {
        var repoRoot = RepoRoot();
        var targetsPath = Path.Combine(MainSourceRoot(repoRoot), "RemoteEmbeddedData.targets");
        Assert.True(File.Exists(targetsPath), $"Could not locate targets file at '{targetsPath}'.");

        var targets = XDocument.Load(targetsPath);
        var resources = targets
            .Descendants()
            .Where(element => element.Name.LocalName == "RemoteEmbeddedData")
            .ToDictionary(
                element => Attribute(element, "Include") ?? string.Empty,
                element =>
                    element
                        .Elements()
                        .ToDictionary(child => child.Name.LocalName, child => child.Value)
            );

        Assert.Equal(2, resources.Count);
        Assert.Equal(
            "BazaarPlusPlus.Data.VoiceSubtitles.voice-lines.json",
            resources["voice-lines.json"]["LogicalName"]
        );
        Assert.Equal("102400", resources["voice-lines.json"]["MinBytes"]);
        Assert.Equal(
            "BazaarPlusPlus.Data.BuildRecommendations.tenwin_builds.json",
            resources["tenwin_builds.json"]["LogicalName"]
        );
        Assert.Equal("51200", resources["tenwin_builds.json"]["MinBytes"]);
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
    public void Structured_logging_core_has_no_runtime_framework_dependencies()
    {
        var repoRoot = RepoRoot();
        var coreDirectory = Path.Combine(
            MainSourceRoot(repoRoot),
            "Infrastructure",
            "Logging",
            "Core"
        );
        Assert.True(
            Directory.Exists(coreDirectory),
            $"Could not locate structured logging core at '{coreDirectory}'."
        );

        var forbiddenTokens = new[]
        {
            "BepInEx",
            "UnityEngine",
            "HarmonyLib",
            "TheBazaar",
            "BazaarGameClient",
            "BazaarGameShared",
        };
        var violations = new List<string>();
        foreach (var file in EnumerateSourceFiles(coreDirectory))
        {
            var relative = Path.GetRelativePath(coreDirectory, file).Replace('\\', '/');
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                foreach (var token in forbiddenTokens)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                        violations.Add($"{relative}:{lineNumber}: {token}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Structured logging core must remain System-only; runtime adapters belong outside "
                + "Infrastructure/Logging/Core. Offending references:\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Plugin_installs_logger_before_first_Awake_emission()
    {
        var pluginSource = File.ReadAllText(Path.Combine(MainSourceRoot(RepoRoot()), "Plugin.cs"));
        var awakeStart = pluginSource.IndexOf(
            "protected virtual void Awake()",
            StringComparison.Ordinal
        );
        var awakeEnd = pluginSource.IndexOf(
            "protected virtual void OnDestroy()",
            awakeStart,
            StringComparison.Ordinal
        );
        var awakeSource = pluginSource.Substring(awakeStart, awakeEnd - awakeStart);
        var install = awakeSource.IndexOf("BppLog.Install(", StringComparison.Ordinal);
        var firstEmission = new[]
        {
            "BppLog.DebugEvent(",
            "BppLog.InfoEvent(",
            "BppLog.WarnEvent(",
            "BppLog.ErrorEvent(",
        }
            .Select(token => awakeSource.IndexOf(token, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .Min();

        Assert.True(install >= 0, "Plugin.Awake must install the logger.");
        Assert.True(
            install < firstEmission,
            "Plugin.Awake must install the logger before its first operational emission."
        );
    }

    [Fact]
    public void Plugin_flushes_logs_after_feature_teardown_on_all_paths()
    {
        var pluginSource = File.ReadAllText(Path.Combine(MainSourceRoot(RepoRoot()), "Plugin.cs"));
        var teardownStart = pluginSource.IndexOf(
            "private void Teardown()",
            StringComparison.Ordinal
        );
        var teardownEnd = pluginSource.IndexOf(
            "private void RunTeardownSteps(PluginTeardownAccumulator failures)",
            teardownStart,
            StringComparison.Ordinal
        );
        var teardownSource = pluginSource.Substring(teardownStart, teardownEnd - teardownStart);

        Assert.True(
            teardownSource.IndexOf("RunTeardownSteps(failures);", StringComparison.Ordinal)
                < teardownSource.IndexOf("BppLog.Flush", StringComparison.Ordinal),
            "Registered teardown hooks must run before pending storm summaries are flushed."
        );
        Assert.Contains(
            "protected virtual void OnDestroy()\n    {\n        Teardown();",
            pluginSource
        );
        Assert.Contains(
            "private void CleanupFailedInitialization()\n    {\n        Teardown();",
            pluginSource
        );
    }

    [Fact]
    public void BppLog_structured_Debug_facade_keeps_compile_time_guards_and_lazy_fields()
    {
        var source = File.ReadAllText(
            Path.Combine(MainSourceRoot(RepoRoot()), "Infrastructure", "BppLog.cs")
        );
        var structuredSignature = source.IndexOf(
            "public static void DebugEvent(\n        BppLogEventDefinition definition",
            StringComparison.Ordinal
        );

        Assert.True(structuredSignature >= 0);
        Assert.Contains(
            "[Conditional(\"DEBUG\")]",
            source.Substring(Math.Max(0, structuredSignature - 80), 80)
        );
        Assert.Contains("Func<BppLogFieldValue[]> valuesFactory", source);
        Assert.DoesNotContain("public static void Debug(string component", source);
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

    [Fact]
    public void ManagedPath_discovery_is_shared_by_all_projects()
    {
        var repoRoot = RepoRoot();
        var directoryProps = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"));
        var managedPathProps = Path.Combine(repoRoot, "build", "ManagedPath.props");

        Assert.Contains("build/ManagedPath.props", directoryProps);
        Assert.True(
            File.Exists(managedPathProps),
            $"Expected shared discovery at '{managedPathProps}'."
        );

        var discoverySource = File.ReadAllText(managedPathProps);
        Assert.Contains("<WinSteamManagedC>", discoverySource);
        Assert.Contains("<WinSteamManagedD>", discoverySource);
        Assert.Contains("<WinSteamManagedE>", discoverySource);
        Assert.Contains("<MacSteamManagedDefault>", discoverySource);
        Assert.Contains("<ManagedPath>", discoverySource);

        var projectOverrides = Directory
            .EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path =>
                File.ReadAllText(path).Contains("<ManagedPath>", StringComparison.Ordinal)
            )
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .ToList();

        Assert.True(
            projectOverrides.Count == 0,
            "ManagedPath discovery belongs in build/ManagedPath.props. Project-local definitions:\n"
                + string.Join("\n", projectOverrides)
        );
    }

    [Fact]
    public void ManagedPath_discovery_evaluates_candidates_and_preserves_explicit_override()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bpp-managed-path-{Guid.NewGuid():N}");
        var explicitManaged = Path.Combine(tempRoot, "explicit", "Managed");
        var project = Path.Combine(
            RepoRoot(),
            "tests",
            "VoiceSubtitles.Tests",
            "VoiceSubtitles.Tests.csproj"
        );

        try
        {
            foreach (
                var (managedProperty, gameProperty) in new[]
                {
                    ("WinSteamManagedC", "WinGamePathC"),
                    ("WinSteamManagedD", "WinGamePathD"),
                    ("WinSteamManagedE", "WinGamePathE"),
                    ("MacSteamManagedDefault", "MacGamePathDefault"),
                }
            )
            {
                var candidateGame = Path.Combine(tempRoot, managedProperty);
                var candidateManaged = Path.Combine(candidateGame, "Managed");
                Directory.CreateDirectory(candidateManaged);
                File.WriteAllBytes(Path.Combine(candidateManaged, "Assembly-CSharp.dll"), []);

                var candidates = new Dictionary<string, string>
                {
                    ["WinSteamManagedC"] = Path.Combine(tempRoot, "missing-c"),
                    ["WinSteamManagedD"] = Path.Combine(tempRoot, "missing-d"),
                    ["WinSteamManagedE"] = Path.Combine(tempRoot, "missing-e"),
                    ["MacSteamManagedDefault"] = Path.Combine(tempRoot, "missing-mac"),
                    [managedProperty] = candidateManaged,
                    [gameProperty] = candidateGame,
                };
                var properties = candidates.Select(pair => $"{pair.Key}={pair.Value}").ToArray();

                Assert.Equal(
                    candidateManaged,
                    EvaluateMsBuildProperty(project, "ManagedPath", properties)
                );
                Assert.Equal(
                    candidateGame,
                    EvaluateMsBuildProperty(project, "GamePath", properties)
                );
            }

            Assert.Equal(
                explicitManaged,
                EvaluateMsBuildProperty(project, "ManagedPath", $"ManagedPath={explicitManaged}")
            );
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Structured_tooltip_sections_disable_native_paragraph_spacing()
    {
        var tooltipPatches = Path.Combine(MainSourceRoot(RepoRoot()), "Patches", "Tooltips");
        foreach (
            var patchName in new[]
            {
                "EncounterEventTooltipPatch.cs",
                "HeroLevelRewardsTooltipPatch.cs",
            }
        )
        {
            var source = File.ReadAllText(Path.Combine(tooltipPatches, patchName));
            Assert.DoesNotContain("UseUiFont", source);
            Assert.Contains("ParagraphSpacing = 0f", source);

            if (patchName == "EncounterEventTooltipPatch.cs")
            {
                Assert.Contains("SourceBottomPaddingScale = 0.6f", source);
                Assert.Contains("NativeSectionBottomPaddingScale = 1.2f", source);
            }
            else
            {
                Assert.Contains("SectionTopPaddingScale = 1f", source);
            }
        }
    }

    [Fact]
    public void Quest_tooltip_implementations_share_one_gate_and_keep_separate_cleanup_paths()
    {
        var tooltipPatches = Path.Combine(MainSourceRoot(RepoRoot()), "Patches", "Tooltips");
        var questRewardSource = File.ReadAllText(
            Path.Combine(tooltipPatches, "QuestRewardPreviewTooltipPatch.cs")
        );
        var aggregateSource = File.ReadAllText(
            Path.Combine(tooltipPatches, "AggregateItemMissingTypesTooltipPatch.cs")
        );
        var questSettingsSource = File.ReadAllText(
            Path.Combine(
                MainSourceRoot(RepoRoot()),
                "Game",
                "QuestPreview",
                "QuestPreviewSettingsDockEntry.cs"
            )
        );
        var compositionSource = File.ReadAllText(
            Path.Combine(MainSourceRoot(RepoRoot()), "BppComposition.cs")
        );

        Assert.Contains("QuestPreviewGate.IsEnabled()", questRewardSource);
        Assert.Contains("QuestPreviewGate.IsEnabled()", aggregateSource);
        Assert.Contains("descriptionText.text = presentation.NativeText", questRewardSource);
        Assert.Contains("RestoreNativeTextLayoutCore(descriptionText)", questRewardSource);
        Assert.Contains("BppTooltipSections.HideAll(SectionKey)", aggregateSource);
        Assert.DoesNotContain("BazaarPlusPlus.Patches", questSettingsSource);
        Assert.Contains(
            "QuestRewardPreviewTooltipPatch.ClearPooledPresentation()",
            compositionSource
        );
        Assert.Contains(
            "AggregateItemMissingTypesTooltipPatch.ClearPooledPresentation()",
            compositionSource
        );
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

    private static string MethodSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method marker '{startMarker}'.");

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find method boundary '{endMarker}'.");
        return source.Substring(start, end - start);
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;

    private static string EvaluateMsBuildProperty(
        string project,
        string propertyName,
        params string[] properties
    )
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add($"-getProperty:{propertyName}");
        foreach (var property in properties)
            startInfo.ArgumentList.Add($"-p:{property}");

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"MSBuild property evaluation failed ({process.ExitCode}):\n{standardError.Result}"
        );
        return standardOutput.Result.Trim();
    }

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
