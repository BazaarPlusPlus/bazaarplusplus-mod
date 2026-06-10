---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Live Build Recommendations Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the ten-win build recommendation backend from top-level `Game/BuildRecommendations` into `Game/LiveBuildPanel/Recommendations` because current production code uses it only as LiveBuildPanel-owned recommendation data/model/refresh backend.

**Architecture:** `LiveBuildPanel` owns the UX, candidate state, manual refresh action, and recommendation consumption. The recommendation backend remains separate from panel UI files, but it becomes a child module under `Game/LiveBuildPanel/Recommendations` instead of a sibling `Game/` feature. Shared runtime adapters stay in `GameInterop/`; this move does not change `GameInterop.ItemBoardPreview` or `GameInterop.LiveCards`.

**Tech Stack:** C# 12 / netstandard2.1 main mod, xUnit architecture tests, existing exe-runner recommendation tests, repo docs under `docs/superpowers/specs` and `docs/design`.

---

## Scope

This plan covers one ownership cleanup:

1. Add an architecture ratchet that forbids restoring top-level `Game/BuildRecommendations`.
2. Move production recommendation files into `Game/LiveBuildPanel/Recommendations` and rename their namespace.
3. Rename the stale `CardSetBuildRecommendationTier.Tests` exe-runner project to `LiveBuildRecommendations.Tests` and update reflection type strings.
4. Update current design/spec docs that still describe `BuildRecommendations` as a sibling/shared module.
5. Verify source, tests, and living docs no longer reference the old namespace/path.

No implementation should begin until this plan is confirmed. This is a mechanical ownership refactor; it must not change the recommendation algorithm, analyzer JSON schema, cache path, remote URL, button behavior, or UI text.

## Evidence

- Production usage is already LiveBuildPanel-only: `LiveBuildPanel.cs` imports `BazaarPlusPlus.Game.BuildRecommendations` at `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:10`.
- `LiveBuildPanel` owns repository/service instances and recommendation state: `_recommendations`, `_refreshService`, and `_matches` are fields at `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:34-41`.
- Manual refresh is already a LiveBuildPanel action: `TryRefreshFinalBuilds()` is the view callback path at `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:217-238`, and `RefreshFinalBuildsAsync(...)` awaits `_refreshService.RefreshAsync(...)` at `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:241-247`.
- Recommendation lookup is already driven from LiveBuildPanel candidate state: `_recommendations.FindRecommendations(...)` is called at `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:319-329`.
- No production `HistoryPanel` references remain: `rg -n "BuildRecommendations|BuildRecommendation|TryRefreshFinalBuilds|RefreshFinalBuilds" src/BazaarPlusPlus/Game/HistoryPanel` returns no matches.
- The recommendation backend itself is data/refresh/model code, not UI: `BuildRecommendationRepository` says it loads the analyzer-v4 ten-win corpus, handles cache/remote refresh, and answers local recommendation queries at `src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendationRepository.cs:19-24`.
- `TenWinBuildCorpus` is pure parsing/recall/scoring, but it is still private to final-build recommendations and not a cross-feature contract: its summary notes zero game/Unity references and delegates board projection to the repository at `src/BazaarPlusPlus/Game/BuildRecommendations/TenWinBuildCorpus.cs:21-23`.
- Architecture tests still contain the now-stale phrase "LiveBuildPanel plus BuildRecommendations" at `tests/Architecture.Tests/CoreLayeringTests.cs:291-293`.
- The exe-runner test project still locates types by the old namespace: `tests/CardSetBuildRecommendationTier.Tests/Program.cs:523-527`, `:654-656`, `:750-755`, and `:805-807`.
- Current docs still preserve old sibling-module wording: `docs/superpowers/specs/2026-06-05-live-build-panel-design.md:9,70,131,302,369,378,419,438` and `docs/design/2026-06-07-live-build-refresh-and-history-health-layout.md:23-24,36,101-120,215,281`.

## Target File Structure

- Move directory: `src/BazaarPlusPlus/Game/BuildRecommendations/`
- Create directory: `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/`
- Move: `src/BazaarPlusPlus/Game/BuildRecommendations/BuildLiveState.cs` to `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildLiveState.cs`
- Move: `src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendation.cs` to `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendation.cs`
- Move: `src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendationRefreshService.cs` to `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRefreshService.cs`
- Move: `src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendationRepository.cs` to `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs`
- Move: `src/BazaarPlusPlus/Game/BuildRecommendations/TenWinBuildCorpus.cs` to `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/TenWinBuildCorpus.cs`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`
- Modify: `tests/Architecture.Tests/CoreLayeringTests.cs`
- Rename directory: `tests/CardSetBuildRecommendationTier.Tests/` to `tests/LiveBuildRecommendations.Tests/`
- Rename project: `tests/LiveBuildRecommendations.Tests/CardSetBuildRecommendationTier.Tests.csproj` to `tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj`
- Modify: `tests/LiveBuildRecommendations.Tests/Program.cs`
- Modify: `docs/superpowers/specs/2026-06-05-live-build-panel-design.md`
- Modify: `docs/design/2026-06-07-live-build-refresh-and-history-health-layout.md`

## Task 0: Review-Only Red Team

**Files:**
- Read: all files listed in "Target File Structure"
- Modify only after confirmation: this plan file if review finds a concrete flaw

- [x] **Step 1: Re-check production consumers**

Run:

```bash
rg -n "BazaarPlusPlus\\.Game\\.BuildRecommendations|BuildRecommendationRepository|BuildRecommendationRefreshService|BuildRecommendation\\b|BuildLiveState\\b|TenWinBuildCorpus\\b" src/BazaarPlusPlus tests -g '*.cs' -g '*.csproj'
```

Expected:
- Production matches outside `src/BazaarPlusPlus/Game/BuildRecommendations/` appear only in `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`.
- Test matches appear in the recommendation exe-runner project only.

- [x] **Step 2: Re-check HistoryPanel**

Run:

```bash
rg -n "BuildRecommendations|BuildRecommendation|TryRefreshFinalBuilds|RefreshFinalBuilds" src/BazaarPlusPlus/Game/HistoryPanel
```

Expected: no matches.

- [x] **Step 3: Write red-team findings**

Add a short `## Red-team review` section near the end of this plan. Use one of these exact outcomes:

```markdown
## Red-team review

- No blocking findings.
```

or, for concrete issues:

```markdown
## Red-team review

- `path/to/file`: specific failure mode and exact mitigation.
```

- [x] **Step 4: Confirm before implementation**

If the review finds a blocker, revise the relevant tasks before implementing. If there are no blocking findings, leave implementation tasks unchanged and proceed only after confirmation.

## Task 1: Add Architecture Ratchet For LiveBuildPanel Ownership

**Files:**
- Modify: `tests/Architecture.Tests/CoreLayeringTests.cs`

- [x] **Step 1: Update the stale CardSetPreview replacement message**

In `HistoryPanel_and_LiveBuildPanel_do_not_depend_on_each_others_internals()`, replace:

```csharp
"Game/CardSetPreview was replaced by LiveBuildPanel plus BuildRecommendations and must not be restored."
```

with:

```csharp
"Game/CardSetPreview was replaced by LiveBuildPanel and must not be restored."
```

- [x] **Step 2: Add the ownership ratchet**

Add this test near `HistoryPanel_and_LiveBuildPanel_do_not_depend_on_each_others_internals()`:

```csharp
[Fact]
public void LiveBuildPanel_owns_build_recommendations()
{
    var repoRoot = RepoRoot();
    var mainSource = MainSourceRoot(repoRoot);
    var oldRecommendationsDir = Path.Combine(mainSource, "Game", "BuildRecommendations");
    var liveRecommendationsDir = Path.Combine(
        mainSource,
        "Game",
        "LiveBuildPanel",
        "Recommendations"
    );

    Assert.False(
        Directory.Exists(oldRecommendationsDir),
        "BuildRecommendations is LiveBuildPanel-owned; do not restore top-level Game/BuildRecommendations."
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
                .Contains("BazaarPlusPlus.Game.BuildRecommendations", StringComparison.Ordinal)
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
                .Select((line, index) => new
                {
                    File = Path.GetRelativePath(mainSource, file).Replace('\\', '/'),
                    Line = index + 1,
                    Text = line.Trim(),
                })
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
```

- [x] **Step 3: Run the ratchet and verify it fails before the move**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter LiveBuildPanel_owns_build_recommendations
```

Expected: FAIL because `src/BazaarPlusPlus/Game/BuildRecommendations` still exists and `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations` does not exist yet.

- [x] **Step 4: Commit**

```bash
git add tests/Architecture.Tests/CoreLayeringTests.cs
git commit -m "test: ratchet live build recommendations ownership"
```

## Task 2: Move Production Recommendation Module Under LiveBuildPanel

**Files:**
- Move: `src/BazaarPlusPlus/Game/BuildRecommendations/*`
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`

- [x] **Step 1: Move files**

Run:

```bash
mkdir -p src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations
git mv src/BazaarPlusPlus/Game/BuildRecommendations/BuildLiveState.cs src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildLiveState.cs
git mv src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendation.cs src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendation.cs
git mv src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendationRefreshService.cs src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRefreshService.cs
git mv src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendationRepository.cs src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs
git mv src/BazaarPlusPlus/Game/BuildRecommendations/TenWinBuildCorpus.cs src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/TenWinBuildCorpus.cs
rmdir src/BazaarPlusPlus/Game/BuildRecommendations
```

Expected: the old directory is gone and the five files exist under `Game/LiveBuildPanel/Recommendations/`.

- [x] **Step 2: Rename production namespace**

In each moved file, change:

```csharp
namespace BazaarPlusPlus.Game.BuildRecommendations;
```

to:

```csharp
namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
```

- [x] **Step 3: Update LiveBuildPanel import**

In `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`, replace:

```csharp
using BazaarPlusPlus.Game.BuildRecommendations;
```

with:

```csharp
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
```

- [x] **Step 4: Run focused source scans**

Run:

```bash
rg -n "BazaarPlusPlus\\.Game\\.BuildRecommendations|Game/BuildRecommendations" src/BazaarPlusPlus tests/Architecture.Tests -g '*.cs'
```

Expected: no matches.

Run:

```bash
rg -n "BazaarPlusPlus\\.Game\\.LiveBuildPanel\\.Recommendations" src/BazaarPlusPlus/Game -g '*.cs'
```

Expected:
- Matches in files under `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/`.
- One import in `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`.
- No matches under `src/BazaarPlusPlus/Game/HistoryPanel/`.

- [x] **Step 5: Run production verification**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter LiveBuildPanel_owns_build_recommendations
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations tests/Architecture.Tests/CoreLayeringTests.cs
git add -u src/BazaarPlusPlus/Game/BuildRecommendations
git commit -m "refactor: move build recommendations under live build panel"
```

## Task 3: Rename And Update Recommendation Tests

**Files:**
- Rename: `tests/CardSetBuildRecommendationTier.Tests/` to `tests/LiveBuildRecommendations.Tests/`
- Rename: `tests/LiveBuildRecommendations.Tests/CardSetBuildRecommendationTier.Tests.csproj` to `tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj`
- Modify: `tests/LiveBuildRecommendations.Tests/Program.cs`
- Modify: `src/BazaarPlusPlus.Localization/Properties/AssemblyInfo.cs`

- [x] **Step 1: Rename test directory and project**

Run:

```bash
git mv tests/CardSetBuildRecommendationTier.Tests tests/LiveBuildRecommendations.Tests
git mv tests/LiveBuildRecommendations.Tests/CardSetBuildRecommendationTier.Tests.csproj tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj
test -z "$(git ls-files tests/CardSetBuildRecommendationTier.Tests)" && rm -rf tests/CardSetBuildRecommendationTier.Tests
```

Expected:
- `tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj` exists.
- `tests/CardSetBuildRecommendationTier.Tests` has no tracked files left.

- [x] **Step 2: Update test comments and output name**

In `tests/LiveBuildRecommendations.Tests/Program.cs`, replace the file header comment:

```csharp
// Behavior tests for the analyzer-v4 ten-win build corpus consumed by the mod.
```

with:

```csharp
// Behavior tests for the analyzer-v4 ten-win build corpus consumed by LiveBuildPanel.
```

Replace:

```csharp
Console.WriteLine("TenWin build recommendation checks passed.");
```

with:

```csharp
Console.WriteLine("LiveBuild recommendation checks passed.");
```

- [x] **Step 3: Update reflection type strings**

Replace every old namespace string in `tests/LiveBuildRecommendations.Tests/Program.cs`:

```csharp
BazaarPlusPlus.Game.BuildRecommendations.
```

with:

```csharp
BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.
```

Concrete strings that must change:

```csharp
"BazaarPlusPlus.Game.BuildRecommendations.BuildRatingTier"
"BazaarPlusPlus.Game.BuildRecommendations.BuildRecommendationRefreshService"
"BazaarPlusPlus.Game.BuildRecommendations.TenWinBuildCorpus"
"BazaarPlusPlus.Game.BuildRecommendations.BuildRecommendationRepository"
"BazaarPlusPlus.Game.BuildRecommendations.BuildLiveState"
```

to:

```csharp
"BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRatingTier"
"BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRecommendationRefreshService"
"BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.TenWinBuildCorpus"
"BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildRecommendationRepository"
"BazaarPlusPlus.Game.LiveBuildPanel.Recommendations.BuildLiveState"
```

- [x] **Step 4: Run focused test**

Run:

```bash
dotnet run --project tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj
```

Expected:

```text
LiveBuild recommendation checks passed.
```

- [x] **Step 5: Run discovery sanity check**

Run:

```bash
find tests -mindepth 2 -maxdepth 2 -name '*.csproj' | sort | rg "CardSetBuildRecommendationTier|LiveBuildRecommendations"
```

Expected: only `tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj` is listed.

- [x] **Step 6: Commit**

```bash
git add tests/LiveBuildRecommendations.Tests
git add -u tests/CardSetBuildRecommendationTier.Tests
git commit -m "test: rename live build recommendation tests"
```

## Task 4: Update Current Docs That Still Describe A Shared BuildRecommendations Module

**Files:**
- Modify: `docs/superpowers/specs/2026-06-05-live-build-panel-design.md`
- Modify: `docs/design/2026-06-07-live-build-refresh-and-history-health-layout.md`

- [x] **Step 1: Update the LiveBuildPanel target document implementation status**

In `docs/superpowers/specs/2026-06-05-live-build-panel-design.md`, replace:

```markdown
Game/LiveBuildPanel/、GameInterop/ItemBoardPreview/BppItemBoard*、Game/BuildRecommendations/、Game/OverlayPanels/BppOverlayPanelMutex
```

with:

```markdown
Game/LiveBuildPanel/（含 Recommendations/）、GameInterop/ItemBoardPreview/BppItemBoard*、Game/OverlayPanels/BppOverlayPanelMutex
```

- [x] **Step 2: Update technical acceptance wording**

Replace:

```markdown
final-build 数据逻辑迁到中性模块，例如 `Game/BuildRecommendations/`；`Game/CardSetPreview/` 不作为 fallback 保留。
```

with:

```markdown
final-build 数据逻辑迁到 `Game/LiveBuildPanel/Recommendations/`；`Game/CardSetPreview/` 不作为 fallback 保留，且不再保留顶层旧 recommendation 目录。
```

- [x] **Step 3: Update file structure block**

Replace the sibling block:

```text
Game/BuildRecommendations/
  BuildRecommendationRepository.cs
  BuildRecommendation.cs
  BuildRecommendationSource.cs
```

with this child block under `Game/LiveBuildPanel/`:

```text
  Recommendations/
    BuildRecommendationRepository.cs
    BuildRecommendation.cs
    BuildRecommendationRefreshService.cs
    BuildLiveState.cs
    TenWinBuildCorpus.cs
```

- [x] **Step 4: Update ownership paragraphs**

Replace:

```markdown
`BuildRecommendationRepository` 只负责 final-build 数据、cache/remote refresh 和候选匹配。它可以返回 `BuildRecommendation`，其中包含一个 `BppItemBoard(Id=FinalBuild, Type=Reference)`，从而删除旧 `ItemBoardItemSpec`。
```

with:

```markdown
`LiveBuildPanel/Recommendations/BuildRecommendationRepository` 只负责 final-build 数据、cache/remote refresh 和候选匹配。它可以返回 `BuildRecommendation`，其中包含一个 `BppItemBoard(Id=FinalBuild, Type=Reference)`，从而删除旧 `ItemBoardItemSpec`。该模块是 LiveBuildPanel 的私有 recommendation backend，不是跨 feature shared module。
```

Replace:

```markdown
`BppItemBoard` 放在 `GameInterop/ItemBoardPreview/`，因为它是 native item-board preview surface 的输入 contract，且现在有三个消费者：LiveBuildPanel、HistoryPanel、final-build recommendation rendering。
```

with:

```markdown
`BppItemBoard` 放在 `GameInterop/ItemBoardPreview/`，因为它是 native item-board preview surface 的输入 contract，且现在有两个 feature consumers：LiveBuildPanel 和 HistoryPanel。final-build recommendation rendering is an internal LiveBuildPanel consumer through `LiveBuildPanel/Recommendations/`.
```

- [x] **Step 5: Update stale migration/test references**

In `docs/superpowers/specs/2026-06-05-live-build-panel-design.md`:

Replace:

```markdown
final-build recommendation 数据和匹配逻辑迁到 `Game/BuildRecommendations/`。
```

with:

```markdown
final-build recommendation 数据和匹配逻辑迁到 `Game/LiveBuildPanel/Recommendations/`。
```

Replace:

```markdown
`CardSetBuildDataRepository.cs`, `CardSetBuildRecommendation.cs`, `ItemBoardItemSpec.cs` 迁到 `Game/BuildRecommendations/`
```

with:

```markdown
`CardSetBuildDataRepository.cs`, `CardSetBuildRecommendation.cs`, `ItemBoardItemSpec.cs` 迁到 `Game/LiveBuildPanel/Recommendations/`
```

Replace:

```markdown
`CardSetBuildRecommendationTier.Tests` 不能漏。
```

with:

```markdown
`LiveBuildRecommendations.Tests` 不能漏。
```

Replace:

```markdown
`BuildRecommendations.Tests`
```

with:

```markdown
`LiveBuildRecommendations.Tests`
```

Replace:

```markdown
迁移 final-build repository 到 `Game/BuildRecommendations/`，更新 `HistoryPanelDataService` 和 `CardSetBuildRecommendationTier.Tests`。
```

with:

```markdown
迁移 final-build repository 到 `Game/LiveBuildPanel/Recommendations/`，确认 `HistoryPanelDataService` 不再消费它，并更新 `LiveBuildRecommendations.Tests`。
```

- [x] **Step 6: Update the 2026-06-07 refresh-layout design**

In `docs/design/2026-06-07-live-build-refresh-and-history-health-layout.md`, update the code-fact paths:

```markdown
`src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendationRepository.cs`
```

to:

```markdown
`src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRepository.cs`
```

Replace the goal:

```markdown
保持十胜阵容刷新逻辑在 `Game/BuildRecommendations` 共享层，避免 LiveBuildPanel 依赖 HistoryPanel。
```

with:

```markdown
保持十胜阵容刷新逻辑在 `Game/LiveBuildPanel/Recommendations`，避免 LiveBuildPanel 依赖 HistoryPanel，也避免误把 recommendation backend 表达成跨 feature shared module。
```

Replace the section title:

```markdown
### 1. 在 BuildRecommendations 增加共享刷新服务
```

with:

```markdown
### 1. 在 LiveBuildPanel/Recommendations 保留刷新服务
```

Replace the test command:

```bash
dotnet run --project tests/CardSetBuildRecommendationTier.Tests/CardSetBuildRecommendationTier.Tests.csproj
```

with:

```bash
dotnet run --project tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj
```

Replace the key-file entry:

```markdown
- `src/BazaarPlusPlus/Game/BuildRecommendations/BuildRecommendationRefreshService.cs`（新增）
```

with:

```markdown
- `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/BuildRecommendationRefreshService.cs`
```

- [x] **Step 7: Run docs scan**

Run:

```bash
rg -n "Game/BuildRecommendations|BazaarPlusPlus\\.Game\\.BuildRecommendations|CardSetBuildRecommendationTier|(^|[^A-Za-z])BuildRecommendations\\.Tests" docs/superpowers/specs docs/design -g '*.md' -g '!docs/design/archive/**' -g '!docs/superpowers/plans/archive/**'
```

Expected:
- No matches in current `docs/superpowers/specs` or current `docs/design` files.
- Archived docs may still mention historical names; do not rewrite archive files for this ownership cleanup.

- [x] **Step 8: Commit**

```bash
git add docs/superpowers/specs/2026-06-05-live-build-panel-design.md docs/design/2026-06-07-live-build-refresh-and-history-health-layout.md
git commit -m "docs: mark live build recommendations as panel-owned"
```

## Task 5: Final Verification

**Files:**
- Read/verify only unless a previous task left a failure

- [x] **Step 1: Run source and test stale-name scans**

Run:

```bash
rg -n "BazaarPlusPlus\\.Game\\.BuildRecommendations|Game/BuildRecommendations|CardSetBuildRecommendationTier" src/BazaarPlusPlus tests -g '*.cs' -g '*.csproj'
```

Expected: no matches.

Run:

```bash
rg -n "BazaarPlusPlus\\.Game\\.LiveBuildPanel\\.Recommendations" src/BazaarPlusPlus/Game/HistoryPanel src/BazaarPlusPlus/Game/RunLogging src/BazaarPlusPlus/Game/CombatReplay
```

Expected: no matches.

- [x] **Step 2: Run docs stale-name scan**

Run:

```bash
rg -n "Game/BuildRecommendations|BazaarPlusPlus\\.Game\\.BuildRecommendations|CardSetBuildRecommendationTier|(^|[^A-Za-z])BuildRecommendations\\.Tests" docs/superpowers/specs docs/design -g '*.md' -g '!docs/design/archive/**' -g '!docs/superpowers/plans/archive/**'
```

Expected: no matches.

- [x] **Step 3: Run focused tests**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter LiveBuildPanel_owns_build_recommendations
dotnet run --project tests/LiveBuildRecommendations.Tests/LiveBuildRecommendations.Tests.csproj
dotnet run --project tests/LiveBuildPanel.Tests/LiveBuildPanel.Tests.csproj
```

Expected: all PASS.

- [x] **Step 4: Run broad safety checks**

Run:

```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: PASS.

- [x] **Step 5: Review diff**

Run:

```bash
git diff --stat
git diff -- src/BazaarPlusPlus/Game/LiveBuildPanel tests/Architecture.Tests tests/LiveBuildRecommendations.Tests docs/superpowers/specs/2026-06-05-live-build-panel-design.md docs/design/2026-06-07-live-build-refresh-and-history-health-layout.md
git diff --check
```

Expected:
- `src/BazaarPlusPlus/Game/BuildRecommendations/` is deleted.
- `src/BazaarPlusPlus/Game/LiveBuildPanel/Recommendations/` contains the five moved recommendation files.
- All moved files use `namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;`.
- `LiveBuildPanel.cs` imports `BazaarPlusPlus.Game.LiveBuildPanel.Recommendations`.
- `tests/LiveBuildRecommendations.Tests` reflection strings use the new namespace.
- Current docs describe recommendation backend as LiveBuildPanel-owned.
- `git diff --check` emits no whitespace errors.

## Execution Notes

- Do not move recommendation code into `GameInterop/`; it is not a reusable adapter over game/Unity runtime surfaces.
- Do not change the analyzer-v4 `tenwin_builds.json` wire schema, cache filename, remote URL, cache duration, or matching/ranking semantics.
- Do not reintroduce any `HistoryPanel` dependency on recommendation refresh. `HistoryPanel` should remain free of `BuildRecommendation*` and `LiveBuildPanel.Recommendations` imports.
- Do not preserve namespace compatibility shims. This module is internal and unpublished as an API surface.
- Keep archived docs unchanged unless a verification command explicitly includes them.

## Red-team review

- No blocking findings.

## Self-Review

- Spec coverage: the plan covers production move, namespace update, test rename/reflection update, architecture ratchet, current docs cleanup, scans, tests, and build verification.
- Placeholder scan: no placeholder tasks remain; all paths, commands, and expected results are concrete.
- Type consistency: all planned production types use `BazaarPlusPlus.Game.LiveBuildPanel.Recommendations`; the test project is consistently named `LiveBuildRecommendations.Tests`.

## Suggested Rule Additions

None. This plan applies existing rules: code is the source of truth, feature-owned workflow/state stays in `Game/`, and internals can take a breaking namespace move instead of compatibility shims.
