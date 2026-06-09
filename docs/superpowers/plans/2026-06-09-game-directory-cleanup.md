# Game Directory Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clean up the remaining `Game/` directory structure after the outer `src/` layout refactor by removing dead compatibility code, fixing code-doc drift, concentrating UI chrome suppression, documenting `PvpBattles` as a shared module, and splitting oversized feature text files.

**Architecture:** Keep game-runtime adapters in `GameInterop/`, feature workflow/UI/state in `Game/`, and shared feature-owned evidence modules in `Game/` when they are consumed by several features. This plan adds one deeper module in `Game/OverlayPanels` for BPP UI chrome suppression and keeps `PvpBattles` out of `CombatReplay`, `HistoryPanel`, and `GameInterop`.

**Tech Stack:** C# 12 / netstandard2.1 main mod, xUnit architecture tests, existing exe-runner tests, BepInEx config/PlayerPrefs, repo docs under `docs/features` and `docs/superpowers/plans`.

---

## Scope

This plan covers five independently testable cleanup tracks:

1. Remove the legacy `RandomHeroSkinPool` PlayerPrefs migration path and set `BppVersion` to `4.1.9`.
2. Update `docs/features/collection-panel.md` so it matches the current top-level Collection filter implementation.
3. Add a `Game/OverlayPanels` UI chrome suppression module used by screenshots and replay video recording.
4. Document and test `Game/PvpBattles` as a shared battle-evidence module.
5. Split oversized feature text files inside their owning feature directories, without moving feature copy into `BazaarPlusPlus.Localization`.

No implementation should begin until the revised plan is confirmed. Before implementation, run the red-team review task below and revise this plan if the review finds a concrete risk.

## Evidence

- Main BepInEx config currently binds only current keys in `BppConfig.Initialize` (`src/BazaarPlusPlus/Core/Config/BppConfig.cs:33-90`).
- BazaarAgent runtime config switches are already forbidden by `BazaarAgent_host_has_no_runtime_config_switches` (`tests/Architecture.Tests/CoreLayeringTests.cs:667-694`).
- The remaining production old-key compatibility is `RandomHeroSkinPoolPlayerPrefs`: current key plus legacy key constants at `src/BazaarPlusPlus/Game/Lobby/RandomHeroSkinPool/RandomHeroSkinPoolPlayerPrefs.cs:12-13`, fallback read/write at `:21-37`, and legacy key builder at `:58-62`.
- `RandomHeroSkinPoolRuntime.ResolveState` reads selected ids and immediately saves the normalized state (`src/BazaarPlusPlus/Game/Lobby/RandomHeroSkinPool/RandomHeroSkinPoolRuntime.cs:58-63`).
- `Directory.Build.props` currently carries `<BppVersion>4.1.0</BppVersion>` (`Directory.Build.props:3`); this cleanup should set it to `4.1.9`.
- Collection docs still mention stale concepts: `IncludePackages` (`docs/features/collection-panel.md:29`), `CollectionMerchantKind` (`:34`), split selected source keys (`:73`), and `CollectionMerchantKind.cs` as a key file (`:84`).
- Current Collection code uses `SelectedSourceKey` and `PackagesOnly` (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:27-28`), tab profiles decide which controls exist (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabProfile.cs:44-61`), packages bypass source selection (`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:758-790`), and package cards are exclusive in the filter engine (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:42-49`).
- End-of-run screenshots directly import CollectionPanel, Settings, and CombatStatusBar only to suppress BPP UI chrome (`src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:7-15`, `:413-419`).
- Replay video recording directly imports CollectionPanel and Settings only to suppress BPP UI chrome, and intentionally keeps CombatStatusBar visible (`src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs:8-10`, `:750-761`).
- `UiSuppressionScope` is already the low-level lease combinator (`src/BazaarPlusPlus/Infrastructure/UiSuppressionScope.cs:17-57`); the missing depth is a BPP chrome-level module that knows the two capture modes.
- `PvpBattles` is already a shared battle-evidence module: CombatReplay capture constructs artifacts from `PvpBattleSequenceMatcher`, `PvpBattleSnapshotCollector`, and factories (`src/BazaarPlusPlus/Game/CombatReplay/CombatReplayCaptureService.cs:27-44`, `:116-138`); HistoryPanel projects `PvpBattleSnapshots` into item-board preview data (`src/BazaarPlusPlus/Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs:39-67`); RunLogging subscribes to `PvpBattleRecorded` and attaches battles to runs (`src/BazaarPlusPlus/Game/RunLogging/RunLoggingModule.cs:92-100`, `:185-217`).
- The localization assembly is only the resolution engine (`src/BazaarPlusPlus.Localization/L.cs:6-30`); feature copy currently lives in feature text files, with `HistoryPanelText.cs` at 918 lines, `LiveBuildPanelText.cs` at 282 lines, and `CollectionPanelText.cs` at 222 lines.

## Target File Structure

- Modify: `Directory.Build.props`
- Modify: `src/BazaarPlusPlus/Game/Lobby/RandomHeroSkinPool/RandomHeroSkinPoolPlayerPrefs.cs`
- Modify: `tests/Architecture.Tests/CoreLayeringTests.cs`
- Modify: `docs/features/collection-panel.md`
- Create: `src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppressionMode.cs`
- Create: `src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppression.cs`
- Modify: `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs`
- Modify: `src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs`
- Create: `docs/features/pvp-battles.md`
- Modify: `docs/features/ghost-battle-data-flow.md`
- Modify: `docs/features/combat-replay.md`
- Modify: `docs/features/run-logging-and-upload.md`
- Move/split: `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelText.cs` into `src/BazaarPlusPlus/Game/HistoryPanel/Text/HistoryPanelText.*.cs`
- Move/split: `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanelText.cs` into `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/LiveBuildPanelText.*.cs`
- Move: `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelText.cs` to `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs`
- Modify: test projects that explicitly link moved text files, including `tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj`

## Task 0: Review-Only Red Team

**Files:**
- Read: all files listed in "Target File Structure"
- Modify only after confirmation: this plan file if the review finds a concrete flaw

- [ ] **Step 1: Re-read the code evidence**

Run:

```bash
nl -ba src/BazaarPlusPlus/Game/Lobby/RandomHeroSkinPool/RandomHeroSkinPoolPlayerPrefs.cs | sed -n '1,90p'
nl -ba src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs | sed -n '1,25p;405,425p'
nl -ba src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs | sed -n '1,18p;744,765p'
nl -ba src/BazaarPlusPlus/Game/CombatReplay/CombatReplayCaptureService.cs | sed -n '1,70p;110,145p'
nl -ba src/BazaarPlusPlus/Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs | sed -n '1,75p'
nl -ba src/BazaarPlusPlus/Game/RunLogging/RunLoggingModule.cs | sed -n '1,35p;88,105p;180,222p'
```

Expected: the line evidence still matches the "Evidence" section.

- [ ] **Step 2: Write red-team findings**

Add a short "Red-team review" section near the end of this plan with concrete findings only. A valid finding must name one file and one failure mode, for example:

```markdown
## Red-team review

- `src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppression.cs`: importing `Game.CombatStatusBar.CombatStatusBar` without an alias is ambiguous with the namespace; use `CombatStatusBarFeature` alias in the implementation.
```

Expected: either the section says `No blocking findings.` or lists findings that can be addressed by adjusting the tasks below.

- [ ] **Step 3: Revise the plan before code**

If the review finds an issue, update the relevant task before implementing. If there are no blocking findings, leave implementation tasks unchanged.

## Task 1: Remove Legacy RandomHeroSkinPool PlayerPrefs Migration And Bump Version

**Files:**
- Modify: `src/BazaarPlusPlus/Game/Lobby/RandomHeroSkinPool/RandomHeroSkinPoolPlayerPrefs.cs`
- Modify: `Directory.Build.props`
- Modify: `tests/Architecture.Tests/CoreLayeringTests.cs`

- [ ] **Step 1: Add the architecture ratchet**

Add this test to `tests/Architecture.Tests/CoreLayeringTests.cs` near the other cleanup ratchets:

```csharp
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

    Assert.DoesNotContain("LegacyHeroSkinPoolPrefsKeyPrefix", source);
    Assert.DoesNotContain("BPP.RandomHeroSkinPool.Selected", source);
    Assert.DoesNotContain("BuildLegacyHeroSkinPrefsKey", source);
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter RandomHeroSkinPool_has_no_legacy_playerprefs_migration
```

Expected: FAIL because the current source still contains `LegacyHeroSkinPoolPrefsKeyPrefix` and `BPP.RandomHeroSkinPool.Selected`.

- [ ] **Step 3: Remove the legacy fallback**

Change `RandomHeroSkinPoolPlayerPrefs.cs` to this shape:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Lobby;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomCollectiblePool.Selected";
    private const string LogScope = "RandomHeroSkinPool";

    public static IReadOnlyCollection<string>? LoadSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        return RandomPoolPrefsHelpers.LoadIdCollection(
            BuildScopedPrefsKey(hero, collectionType),
            LogScope
        );
    }

    public static void SaveSelectedIds(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType,
        IEnumerable<string> ids
    )
    {
        RandomPoolPrefsHelpers.SaveIdCollection(BuildScopedPrefsKey(hero, collectionType), ids);
    }

    private static string BuildScopedPrefsKey(
        EHero hero,
        BazaarInventoryTypes.ECollectionType collectionType
    )
    {
        var scope = RandomPoolPrefsHelpers.ResolveAccountScopeForPrefs(LogScope);
        return $"{SelectedPoolPrefsKeyPrefix}.{Uri.EscapeDataString(collectionType.ToString())}.{Uri.EscapeDataString(hero.ToString())}.{scope}";
    }
}
```

Behavior change: old `BPP.RandomHeroSkinPool.Selected.*` values are no longer read or migrated. Current `BPP.RandomCollectiblePool.Selected.<collectionType>.<hero>.<scope>` values remain supported.

- [ ] **Step 4: Set the release version**

Change `Directory.Build.props` to:

```xml
<Project>
  <PropertyGroup>
    <BppVersion>4.1.9</BppVersion>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Run focused verification**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter RandomHeroSkinPool_has_no_legacy_playerprefs_migration
rg -n "LegacyHeroSkinPoolPrefsKeyPrefix|BPP\\.RandomHeroSkinPool\\.Selected|BuildLegacyHeroSkinPrefsKey" src tests docs -g '!decompiled/**'
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected:
- Architecture test PASS.
- `rg` returns no production/test/doc references to the deleted legacy key.
- Build PASS.

- [ ] **Step 6: Commit**

```bash
git add Directory.Build.props src/BazaarPlusPlus/Game/Lobby/RandomHeroSkinPool/RandomHeroSkinPoolPlayerPrefs.cs tests/Architecture.Tests/CoreLayeringTests.cs
git commit -m "refactor: remove random skin legacy prefs migration"
```

## Task 2: Update CollectionPanel Feature Documentation

**Files:**
- Modify: `docs/features/collection-panel.md`

- [ ] **Step 1: Replace stale filter terminology**

Update the filter-state bullets so they use current names and behavior:

```markdown
- **来源**（merchant / trainer）：单选，保存在 `CollectionFilterState.SelectedSourceKey`。当前 tab 的 `CollectionTabProfile.SourceKind` 决定它表示 merchant 还是 trainer；Item tab 显示 merchant 来源，Skill tab 显示 trainer 来源。
- **PackagesOnly**：Item tab 的互斥模式。开启后只显示 package cards，并临时禁用来源选择；关闭后默认排除 package cards。Skill tab 不显示该开关。
```

Remove the stale `CollectionMerchantKind` paragraph. If history is still useful, replace it with:

```markdown
历史 note：旧的 merchant-kind 过滤态已不再是当前面板入口。当前来源收窄走 `CollectionSourceCatalog` + offer-pool 路径；文档和实现都应以 `CollectionSourceKind`、`SelectedSourceKey`、`CollectionTabProfile` 为准。
```

- [ ] **Step 2: Replace stale source key schema text**

Change the source-key bullet so it says:

```markdown
- **sourceKey 派生**（非 JSON 字段）：`Build` 由 `(kind, name, availableHeroes)` slug 出 base key；同 base key 多条时追加 `sourceTemplateIds` 指纹去重 (`CollectionSourceCatalog.cs:180-215`)。这是过滤态 `SelectedSourceKey` 持有的稳定键；当前 tab 的 `CollectionTabProfile.SourceKind` 决定该 key 对应 merchant 还是 trainer。
```

- [ ] **Step 3: Update key-file lists**

Remove `CollectionMerchantKind.cs` from the key file list and add `CollectionTabProfile.cs`:

```markdown
- `Game/CollectionPanel/Data/CollectionCatalog.cs`、`CollectionCatalogBuildSession.cs`、`CollectionCardVm.cs`(+`.From.cs`)、`CollectionCardClassifier.cs`、`CollectionFilterState.cs`、`CollectionFilterEngine.cs`、`CollectionTabProfile.cs`、`CollectionHeroScope.cs`、`DayTierSchedule.cs`
```

- [ ] **Step 4: Verify docs no longer advertise stale concepts**

Run:

```bash
rg -n "IncludePackages|CollectionMerchantKind|SelectedMerchantSourceKey|SelectedTrainerSourceKey" docs/features/collection-panel.md
```

Expected: no matches.

- [ ] **Step 5: Commit**

```bash
git add docs/features/collection-panel.md
git commit -m "docs: refresh collection panel filter model"
```

## Task 3: Add BPP UI Chrome Suppression Module

**Files:**
- Create: `src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppressionMode.cs`
- Create: `src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppression.cs`
- Modify: `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs`
- Modify: `src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs`
- Modify: `tests/Architecture.Tests/CoreLayeringTests.cs`

- [ ] **Step 1: Add the architecture ratchet**

Add this test to `CoreLayeringTests.cs`:

```csharp
[Fact]
public void Capture_modules_use_ui_chrome_suppression_seam()
{
    var repoRoot = RepoRoot();
    var mainSource = MainSourceRoot(repoRoot);
    var screenshotSource = File.ReadAllText(
        Path.Combine(mainSource, "Game", "Screenshots", "EndOfRunScreenshotController.cs")
    );
    var recorderSource = File.ReadAllText(
        Path.Combine(mainSource, "Game", "CombatReplay", "Video", "CombatReplayVideoRecorder.cs")
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
        Assert.DoesNotContain("CollectionPanelDockButtonController.BeginScreenshotSuppression", source);
        Assert.DoesNotContain("BppSettingsDockController.BeginScreenshotSuppression", source);
        Assert.DoesNotContain("CombatStatusBarFeature.BeginScreenshotSuppression", source);
    }

    Assert.Contains("CollectionPanelDockButtonController.BeginScreenshotSuppression", chromeSuppressionSource);
    Assert.Contains("BppSettingsDockController.BeginScreenshotSuppression", chromeSuppressionSource);
    Assert.Contains("CombatStatusBarFeature.BeginScreenshotSuppression", chromeSuppressionSource);
}
```

- [ ] **Step 2: Run the test and verify it fails before the seam exists**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter Capture_modules_use_ui_chrome_suppression_seam
```

Expected: FAIL because `BppUiChromeSuppression.cs` does not exist yet.

- [ ] **Step 3: Add the mode enum**

Create `src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppressionMode.cs`:

```csharp
#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

internal enum BppUiChromeSuppressionMode
{
    Screenshot,
    ReplayRecording,
}
```

- [ ] **Step 4: Add the chrome suppression module**

Create `src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppression.cs`:

```csharp
#nullable enable

using System;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.OverlayPanels;

internal static class BppUiChromeSuppression
{
    public static IDisposable? Begin(BppUiChromeSuppressionMode mode)
    {
        return mode switch
        {
            BppUiChromeSuppressionMode.Screenshot => UiSuppressionScope.Begin(
                CollectionPanelDockButtonController.BeginScreenshotSuppression,
                BppSettingsDockController.BeginScreenshotSuppression,
                CombatStatusBarFeature.BeginScreenshotSuppression
            ),
            BppUiChromeSuppressionMode.ReplayRecording => UiSuppressionScope.Begin(
                CollectionPanelDockButtonController.BeginScreenshotSuppression,
                BppSettingsDockController.BeginScreenshotSuppression
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unknown BPP UI chrome suppression mode."
            ),
        };
    }
}
```

- [ ] **Step 5: Route screenshot suppression through the module**

In `EndOfRunScreenshotController.cs`:

```csharp
using BazaarPlusPlus.Game.OverlayPanels;
```

Remove these imports:

```csharp
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;
```

Replace `BeginUiSuppression()` with:

```csharp
private static IDisposable? BeginUiSuppression()
{
    return BppUiChromeSuppression.Begin(BppUiChromeSuppressionMode.Screenshot);
}
```

- [ ] **Step 6: Route replay video suppression through the module**

In `CombatReplayVideoRecorder.cs`:

```csharp
using BazaarPlusPlus.Game.OverlayPanels;
```

Remove these imports:

```csharp
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
```

Keep the existing behavior comment, then replace the direct `UiSuppressionScope.Begin(...)` call with:

```csharp
return BppUiChromeSuppression.Begin(BppUiChromeSuppressionMode.ReplayRecording);
```

Expected behavior remains unchanged: replay video recording suppresses CollectionPanel dock and settings dock, but keeps CombatStatusBar visible.

- [ ] **Step 7: Run focused verification**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter Capture_modules_use_ui_chrome_suppression_seam
dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj --filter UiSuppressionScopeTests
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppressionMode.cs src/BazaarPlusPlus/Game/OverlayPanels/BppUiChromeSuppression.cs src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs src/BazaarPlusPlus/Game/CombatReplay/Video/CombatReplayVideoRecorder.cs tests/Architecture.Tests/CoreLayeringTests.cs
git commit -m "refactor: centralize bpp ui chrome suppression"
```

## Task 4: Document And Guard PvpBattles As Shared Battle Evidence

**Files:**
- Create: `docs/features/pvp-battles.md`
- Modify: `docs/features/ghost-battle-data-flow.md`
- Modify: `docs/features/combat-replay.md`
- Modify: `docs/features/run-logging-and-upload.md`
- Modify: `tests/Architecture.Tests/CoreLayeringTests.cs`

- [ ] **Step 1: Add architecture guard**

Add this test to `CoreLayeringTests.cs`:

```csharp
[Fact]
public void PvpBattles_remains_a_shared_game_module()
{
    var repoRoot = RepoRoot();
    var mainSource = MainSourceRoot(repoRoot);
    var pvpBattlesDir = Path.Combine(mainSource, "Game", "PvpBattles");

    Assert.True(Directory.Exists(pvpBattlesDir), $"Could not locate '{pvpBattlesDir}'.");
    Assert.False(Directory.Exists(Path.Combine(mainSource, "Game", "CombatReplay", "PvpBattles")));
    Assert.False(Directory.Exists(Path.Combine(mainSource, "Game", "HistoryPanel", "PvpBattles")));
    Assert.False(Directory.Exists(Path.Combine(mainSource, "Game", "RunLogging", "PvpBattles")));

    var combatReplaySource = File.ReadAllText(
        Path.Combine(mainSource, "Game", "CombatReplay", "CombatReplayCaptureService.cs")
    );
    var historyProjectionSource = File.ReadAllText(
        Path.Combine(mainSource, "Game", "HistoryPanel", "Data", "HistoryBattlePreviewProjection.cs")
    );
    var runLoggingSource = File.ReadAllText(
        Path.Combine(mainSource, "Game", "RunLogging", "RunLoggingModule.cs")
    );

    Assert.Contains("using BazaarPlusPlus.Game.PvpBattles;", combatReplaySource);
    Assert.Contains("using BazaarPlusPlus.Game.PvpBattles;", historyProjectionSource);
    Assert.Contains("using BazaarPlusPlus.Game.PvpBattles;", runLoggingSource);
}
```

- [ ] **Step 2: Add the shared module doc**

Create `docs/features/pvp-battles.md`:

```markdown
# PvP Battles

`Game/PvpBattles` is the shared battle-evidence module for Bazaar++ PvP combat records.

It is not owned by `CombatReplay`, `HistoryPanel`, or `RunLogging`:

- `CombatReplay` uses it to recognize PvP combat windows, collect snapshots, and build replay artifacts.
- `HistoryPanel` uses its snapshot models to project historical player/opponent boards.
- `RunLogging` uses `PvpBattleRecorded` and persisted battle ids to attach combat evidence to a run.

Keep this module in `Game/PvpBattles` because it contains feature-owned battle semantics, storage models, and event payloads. Do not move it to `GameInterop/`: `GameInterop/` is for reusable adapters over game/Unity runtime surfaces, while `PvpBattles` is BPP-owned battle evidence consumed by multiple features.

## Key Files

- `Game/PvpBattles/PvpBattleSnapshotCollector.cs`
- `Game/PvpBattles/PvpBattleSequenceMatcher.cs`
- `Game/PvpBattles/PvpBattleManifestFactory.cs`
- `Game/PvpBattles/PvpReplayPayloadFactory.cs`
- `Game/PvpBattles/PvpBattleRecorded.cs`
- `Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs`

## Consumers

- `Game/CombatReplay/CombatReplayCaptureService.cs`
- `Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs`
- `Game/RunLogging/RunLoggingModule.cs`
```

- [ ] **Step 3: Link the shared module doc from existing docs**

In `docs/features/ghost-battle-data-flow.md`, add after the intro paragraph:

```markdown
The client-side battle models referenced below live in the shared [`PvP Battles`](pvp-battles.md) module.
```

In `docs/features/combat-replay.md`, add near the key files section:

```markdown
PvP battle capture and replay artifact models live in the shared [`PvP Battles`](pvp-battles.md) module rather than under `CombatReplay`.
```

In `docs/features/run-logging-and-upload.md`, add near the key files section:

```markdown
PvP battle manifests, snapshots, and persistence live in the shared [`PvP Battles`](pvp-battles.md) module; RunLogging attaches those battle ids to run logs but does not own the battle evidence model.
```

- [ ] **Step 4: Verify guard and docs**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter PvpBattles_remains_a_shared_game_module
rg -n "pvp-battles.md|PvP Battles" docs/features
```

Expected:
- Architecture test PASS.
- `rg` shows the new doc plus links from the three existing docs.

- [ ] **Step 5: Commit**

```bash
git add docs/features/pvp-battles.md docs/features/ghost-battle-data-flow.md docs/features/combat-replay.md docs/features/run-logging-and-upload.md tests/Architecture.Tests/CoreLayeringTests.cs
git commit -m "docs: define pvp battles as shared module"
```

## Task 5: Split Feature Text Files Inside Owning Features

**Files:**
- Move/split: `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelText.cs`
- Move/split: `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanelText.cs`
- Move: `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelText.cs`
- Modify: `tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj`
- Modify: any test csproj that explicitly links a moved text file

- [ ] **Step 1: Split HistoryPanelText by responsibility**

Create these files under `src/BazaarPlusPlus/Game/HistoryPanel/Text/` with namespace `BazaarPlusPlus.Game.HistoryPanel` and `internal static partial class HistoryPanelText`:

```text
HistoryPanelText.Chrome.cs        -> title, subtitle, tabs, close, generic action labels
HistoryPanelText.ServerHealth.cs  -> check-server/connectivity labels and status messages
HistoryPanelText.Runs.cs          -> run rows, run status labels, run formatting helpers
HistoryPanelText.Battles.cs       -> battle list/detail labels, replay labels, ghost labels
HistoryPanelText.FontSample.cs    -> font atlas sample cache and sample assembly
HistoryPanelText.Shared.cs        -> Resolve/FormatSimple helpers and shared local methods
```

Keep every public/internal method name unchanged, including `Title()`, `Subtitle()`, `Replay()`, `FontAtlasSample()`, and formatting helpers used by HistoryPanel views. The split changes file locality only.

- [ ] **Step 2: Split LiveBuildPanelText by responsibility**

Create these files under `src/BazaarPlusPlus/Game/LiveBuildPanel/Text/` with namespace `BazaarPlusPlus.Game.LiveBuildPanel` and `internal static partial class LiveBuildPanelText`:

```text
LiveBuildPanelText.Chrome.cs       -> title, subtitle, row labels, navigation labels
LiveBuildPanelText.Status.cs       -> no-run/no-candidate/no-recommendation/empty-row messages
LiveBuildPanelText.Refresh.cs      -> pull-build labels, success/failure details, corpus summary
LiveBuildPanelText.Evidence.cs     -> recommendation count and evidence strings
LiveBuildPanelText.FontSample.cs   -> FontAtlasSample()
```

Update `tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj` so the explicit compile link for `LiveBuildPanelText.cs` becomes links for all new `Text/LiveBuildPanelText.*.cs` files.

- [ ] **Step 3: Move CollectionPanelText under a Text folder**

Move `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelText.cs` to `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs`.

Keep the namespace as:

```csharp
namespace BazaarPlusPlus.Game.CollectionPanel;
```

No callers should change because the type name and namespace stay the same.

- [ ] **Step 4: Verify explicit test project links**

Run:

```bash
rg -n "HistoryPanelText\\.cs|LiveBuildPanelText\\.cs|CollectionPanelText\\.cs" tests src docs -g '*.csproj' -g '*.md'
```

Expected:
- No `.csproj` points at old text file paths.
- Docs either avoid file-level text paths or point at the new `Text/` paths.

- [ ] **Step 5: Run focused verification**

Run:

```bash
dotnet run --project tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/BazaarPlusPlus/Game/HistoryPanel src/BazaarPlusPlus/Game/LiveBuildPanel src/BazaarPlusPlus/Game/CollectionPanel tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj
git commit -m "refactor: split feature text modules"
```

## Task 6: Final Full Verification

**Files:**
- Read/verify only unless a previous task left a failing test

- [ ] **Step 1: Run architecture tests**

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
```

Expected: PASS.

- [ ] **Step 2: Run affected focused tests**

```bash
dotnet test tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj --filter UiSuppressionScopeTests
dotnet run --project tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj
dotnet run --project tests/HistoryPanelServerHealth.Tests/HistoryPanelServerHealth.Tests.csproj
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
```

Expected: all PASS.

- [ ] **Step 3: Build the mod**

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: PASS.

- [ ] **Step 4: Run stale-symbol scans**

```bash
rg -n "LegacyHeroSkinPoolPrefsKeyPrefix|BPP\\.RandomHeroSkinPool\\.Selected|BuildLegacyHeroSkinPrefsKey" src tests docs -g '!decompiled/**'
rg -n "IncludePackages|CollectionMerchantKind|SelectedMerchantSourceKey|SelectedTrainerSourceKey" docs/features/collection-panel.md
rg -n "CollectionPanelDockButtonController.BeginScreenshotSuppression|BppSettingsDockController.BeginScreenshotSuppression|CombatStatusBarFeature.BeginScreenshotSuppression" src/BazaarPlusPlus/Game/Screenshots src/BazaarPlusPlus/Game/CombatReplay/Video
```

Expected: all three commands return no matches.

- [ ] **Step 5: Review diff**

```bash
git diff --stat
git diff -- Directory.Build.props src/BazaarPlusPlus tests/Architecture.Tests docs/features
```

Expected:
- Version is exactly `4.1.9`.
- Old RandomHeroSkinPool legacy migration is gone.
- Capture modules call `BppUiChromeSuppression`.
- PvpBattles remains in `Game/PvpBattles`.
- Text files moved/split without namespace changes.
- Docs match the current code vocabulary.

- [ ] **Step 6: Resolve verification failures at the owning task**

If Task 6 fails, return to the task that introduced the failure, adjust that task's files, rerun that task's focused verification, and amend or recreate that task's scoped commit. Do not create a broad catch-all cleanup commit.

## Execution Notes

- Do not edit `decompiled/`.
- Do not remove `BppHotkeyService`'s `legacyCheck`; it is an input-runtime fallback, not old config compatibility (`src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:269-284`).
- Do not move `PvpBattles` into `GameInterop/`; it is BPP-owned battle evidence, not a reusable adapter over a game/Unity surface.
- Do not move feature copy into `BazaarPlusPlus.Localization`; that assembly should remain a resolution engine.
- Keep commits scoped to the task boundaries above.

## Red-team review

- No blocking findings.
- `Task 5 / Step 4`: the scan can report historical design/audit documents that intentionally cite old file paths. Treat `.csproj` links and living feature docs as actionable; do not rewrite archived history only to satisfy a broad path scan.

## Suggested Rule Additions

None. This plan applies existing project rules: code is the source of truth, old compatibility can be deleted for cleanliness, and shared runtime adapters belong in `GameInterop` while feature workflow/UI/state remain in `Game`.
