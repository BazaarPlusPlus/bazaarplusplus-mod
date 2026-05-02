# Remove cards.json Dependency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove BazaarPlusPlus mod code that directly locates, parses, caches, or warms `cards.json` while preserving preview behavior through the game's static data API.

**Architecture:** Keep `PreviewCardSpecFilter` as the small card-spec normalization and filtering unit, but stop its public path from depending on `LocalCardTemplateCatalog`. Move history preview filtering to `HistoryPanelRepository.Preview`, where the code already has a `Data.GetStatic().GetCardById(Guid)` reflection helper. Then delete the now-unused `cards.json` path service, cache, template catalog, attribute catalog, and warmup hooks.

**Tech Stack:** C# 12, .NET SDK projects, BepInEx mod runtime, The Bazaar `Data.GetStatic()` static game data, existing console-style test projects.

---

## Files To Modify

- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/HistoryPanel/HistoryPanelRepository.Preview.cs`
  - Use the game's static card data as the renderability filter for history preview cards.
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/MonsterPreview/Architecture/PreviewCardSpecFilter.cs`
  - Remove `FilterLocallyRenderable`; keep the injected `Filter` method.
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/MonsterPreview/MonsterPreviewWarmupController.cs`
  - Warm only game static data.
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Plugin.cs`
  - Remove `CardsJsonCache.Install`.
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Core/Paths/IPathService.cs`
  - Remove `CardsJsonPath`.
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Core/Paths/BppPathService.cs`
  - Remove `CardsJsonPath` initialization.
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Core/Paths/CardJsonPathResolver.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Infrastructure/CardsJson/CardsJsonCache.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Infrastructure/CardsJson/LocalCardTemplateCatalog.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Infrastructure/CardsJson/CardAttributeCatalog.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/MonsterPreview/DataSources/MonsterPreviewAttributeResolver.cs`
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`
  - Remove the test-only `TestLocalCardTemplateCatalog.cs` compile include.
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/MonsterPreviewResilience.Tests/TestLocalCardTemplateCatalog.cs`

## Task 1: Repoint History Preview Filtering To Game Static Data

**Files:**
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/HistoryPanel/HistoryPanelRepository.Preview.cs`
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/MonsterPreview/Architecture/PreviewCardSpecFilter.cs`
- Test: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/MonsterPreviewResilience.Tests/Program.cs`

- [ ] **Step 1: Confirm the current filter test passes before changing behavior**

Run:

```bash
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
```

Expected:

```text
MonsterPreviewResilience checks passed.
```

- [ ] **Step 2: Update history preview to pass a static-data template resolver**

In `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/HistoryPanel/HistoryPanelRepository.Preview.cs`, replace the body of `BuildPreviewBoard` with this version:

```csharp
    private static PreviewBoardModel BuildPreviewBoard(
        PvpBattleCardSetCapture itemCapture,
        PvpBattleCardSetCapture skillCapture
    )
    {
        var itemSnapshots = itemCapture?.Items;
        var socketEffectsBySocket = BuildSocketEffectMap(itemSnapshots);
        var staticData = TryGetStaticGameData();
        var model = new PreviewBoardModel
        {
            ItemCards = PreviewCardSpecFilter.Filter(
                BuildPreviewCardSpecs(itemSnapshots, isSkill: false, socketEffectsBySocket),
                templateId => HasStaticCardTemplate(staticData, templateId)
            ),
            SkillCards = PreviewCardSpecFilter.Filter(
                BuildPreviewCardSpecs(skillCapture?.Items, isSkill: true, null),
                templateId => HasStaticCardTemplate(staticData, templateId)
            ),
            Metadata = new Dictionary<string, string>(),
        };
        model.Signature = PreviewBoardSignature.Build(model);
        return model;
    }
```

- [ ] **Step 3: Add static-data failure handling helpers**

In the same file, immediately above the existing `GetStaticGameData()` method, add these helper methods:

```csharp
    private static bool HasStaticCardTemplate(object? staticData, Guid templateId)
    {
        return templateId != Guid.Empty && GetTemplate(staticData, templateId) != null;
    }

    private static object? TryGetStaticGameData()
    {
        try
        {
            return GetStaticGameData();
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "HistoryPanelRepository",
                "Failed to load static game data for battle preview filtering",
                ex
            );
            return null;
        }
    }
```

This preserves the current "drop unrenderable preview cards" behavior without reading `cards.json`. If static game data cannot load, the preview cards are filtered out rather than passing unknown templates into the render surface.

- [ ] **Step 4: Remove the cards.json-backed public filter**

In `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/MonsterPreview/Architecture/PreviewCardSpecFilter.cs`, remove this method:

```csharp
    public static List<PreviewCardSpec> FilterLocallyRenderable(IEnumerable<PreviewCardSpec> specs)
    {
        return Filter(specs, LocalCardTemplateCatalog.Contains);
    }
```

Leave the existing `internal static List<PreviewCardSpec> Filter(...)` method unchanged.

- [ ] **Step 5: Run the focused resilience test**

Run:

```bash
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
```

Expected:

```text
MonsterPreviewResilience checks passed.
```

- [ ] **Step 6: Commit the filter reroute**

Run:

```bash
git add Game/HistoryPanel/HistoryPanelRepository.Preview.cs Game/MonsterPreview/Architecture/PreviewCardSpecFilter.cs
git commit -m "Remove cards json preview filtering dependency"
```

## Task 2: Remove cards.json Runtime Infrastructure

**Files:**
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Plugin.cs`
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/MonsterPreview/MonsterPreviewWarmupController.cs`
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Core/Paths/IPathService.cs`
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Core/Paths/BppPathService.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Core/Paths/CardJsonPathResolver.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Infrastructure/CardsJson/CardsJsonCache.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Infrastructure/CardsJson/LocalCardTemplateCatalog.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Infrastructure/CardsJson/CardAttributeCatalog.cs`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/MonsterPreview/DataSources/MonsterPreviewAttributeResolver.cs`

- [ ] **Step 1: Remove startup installation of the cards.json cache**

In `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Plugin.cs`, change `InstallStaticUtilities` from:

```csharp
    private static void InstallStaticUtilities(IBppServices services)
    {
        CardsJsonCache.Install(services.Paths);
        LegendaryPositionDisplayFormatter.Install(services.Config);
        BppChineseLocalization.Install(services.Config);
        BppSettingsDockCatalog.Install(services.Config);
        BppHotkeyService.Install(services.Config);
        RunLoggingGameDataReader.Install(services.RunContext);
    }
```

to:

```csharp
    private static void InstallStaticUtilities(IBppServices services)
    {
        LegendaryPositionDisplayFormatter.Install(services.Config);
        BppChineseLocalization.Install(services.Config);
        BppSettingsDockCatalog.Install(services.Config);
        BppHotkeyService.Install(services.Config);
        RunLoggingGameDataReader.Install(services.RunContext);
    }
```

- [ ] **Step 2: Remove cards.json warmup**

In `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Game/MonsterPreview/MonsterPreviewWarmupController.cs`, change `Start` from:

```csharp
        var catalogReady = LocalCardTemplateCatalog.Warm();
        var attributesReady = CardAttributeCatalog.Warm();
        var staticDataReady = await WarmStaticDataAsync();

        BppLog.Info(
            "MonsterPreviewWarmupController",
            $"Warmup finished catalogReady={catalogReady} attributesReady={attributesReady} staticDataReady={staticDataReady}"
        );
```

to:

```csharp
        var staticDataReady = await WarmStaticDataAsync();

        BppLog.Info(
            "MonsterPreviewWarmupController",
            $"Warmup finished staticDataReady={staticDataReady}"
        );
```

- [ ] **Step 3: Remove `CardsJsonPath` from the path service interface**

In `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Core/Paths/IPathService.cs`, change the interface from:

```csharp
internal interface IPathService
{
    string? CardsJsonPath { get; }

    string? RunLogDatabasePath { get; }

    string? CombatReplayDirectoryPath { get; }

    string? ScreenshotsDirectoryPath { get; }

    string? IdentityDirectoryPath { get; }
}
```

to:

```csharp
internal interface IPathService
{
    string? RunLogDatabasePath { get; }

    string? CombatReplayDirectoryPath { get; }

    string? ScreenshotsDirectoryPath { get; }

    string? IdentityDirectoryPath { get; }
}
```

- [ ] **Step 4: Remove `CardsJsonPath` from the concrete path service**

In `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/Core/Paths/BppPathService.cs`, remove this property:

```csharp
    public string? CardsJsonPath { get; private set; }
```

Then remove this line from `Initialize()`:

```csharp
        CardsJsonPath = CardJsonPathResolver.GetCardsJsonPath();
```

After the change, the start of the file should look like this:

```csharp
#nullable enable

namespace BazaarPlusPlus.Core.Paths;

internal sealed class BppPathService : IPathService
{
    public string? RunLogDatabasePath { get; private set; }

    public string? CombatReplayDirectoryPath { get; private set; }

    public string? ScreenshotsDirectoryPath { get; private set; }

    public string? IdentityDirectoryPath { get; private set; }

    public void Initialize()
    {
        RunLogDatabasePath = System.IO.Path.Combine(
            BepInEx.Paths.GameRootPath,
            "BazaarPlusPlus",
            BppPathConstants.RunLogDatabaseFileName
        );
```

- [ ] **Step 5: Delete the obsolete files**

Run:

```bash
rm Core/Paths/CardJsonPathResolver.cs \
  Infrastructure/CardsJson/CardsJsonCache.cs \
  Infrastructure/CardsJson/LocalCardTemplateCatalog.cs \
  Infrastructure/CardsJson/CardAttributeCatalog.cs \
  Game/MonsterPreview/DataSources/MonsterPreviewAttributeResolver.cs
```

These deletes are scoped to files that become unreachable after Task 1 and the warmup cleanup.

- [ ] **Step 6: Verify no production references remain**

Run:

```bash
rg -n "cards\\.json|CardsJson|CardJsonPathResolver|CardsJsonPath|LocalCardTemplateCatalog|CardAttributeCatalog|MonsterPreviewAttributeResolver" --glob '!**/obj/**' --glob '!**/bin/**' --glob '!decompiled/**'
```

Expected output at this point will still include the test shim and test project reference:

```text
tests/MonsterPreviewResilience.Tests/TestLocalCardTemplateCatalog.cs:5:internal static class LocalCardTemplateCatalog
tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj:27:    <Compile Include="TestLocalCardTemplateCatalog.cs" />
```

No production files should appear.

- [ ] **Step 7: Commit the infrastructure removal**

Run:

```bash
git add Plugin.cs Game/MonsterPreview/MonsterPreviewWarmupController.cs Core/Paths/IPathService.cs Core/Paths/BppPathService.cs
git add -u Core/Paths/CardJsonPathResolver.cs Infrastructure/CardsJson/CardsJsonCache.cs Infrastructure/CardsJson/LocalCardTemplateCatalog.cs Infrastructure/CardsJson/CardAttributeCatalog.cs Game/MonsterPreview/DataSources/MonsterPreviewAttributeResolver.cs
git commit -m "Remove cards json runtime infrastructure"
```

## Task 3: Clean Up Test Shim And Verify Search Is Clean

**Files:**
- Modify: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`
- Delete: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/MonsterPreviewResilience.Tests/TestLocalCardTemplateCatalog.cs`
- Test: `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/MonsterPreviewResilience.Tests/Program.cs`

- [ ] **Step 1: Remove the test shim compile include**

In `/Users/yxinyu/codes/bpp_codes/bazaarplusplus-mod/tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`, remove this line:

```xml
    <Compile Include="TestLocalCardTemplateCatalog.cs" />
```

The `ItemGroup` should keep these compile includes:

```xml
  <ItemGroup>
    <Compile
      Include="../../Game/PreviewSurface/Models/PreviewCardSpec.cs"
      Link="PreviewCardSpec.cs"
    />
    <Compile
      Include="../../Game/MonsterPreview/Architecture/PreviewCardSpecFilter.cs"
      Link="PreviewCardSpecFilter.cs"
    />
    <Compile
      Include="../../Game/MonsterPreview/CardSetBuildRecommendationMode.cs"
      Link="CardSetBuildRecommendationMode.cs"
    />
    <Compile
      Include="../../Game/MonsterPreview/CardSetBuildRecommendationModeFlow.cs"
      Link="CardSetBuildRecommendationModeFlow.cs"
    />
    <Compile Include="Program.cs" />
  </ItemGroup>
```

- [ ] **Step 2: Delete the unused test shim**

Run:

```bash
rm tests/MonsterPreviewResilience.Tests/TestLocalCardTemplateCatalog.cs
```

- [ ] **Step 3: Run the focused resilience test**

Run:

```bash
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
```

Expected:

```text
MonsterPreviewResilience checks passed.
```

- [ ] **Step 4: Verify the cards.json dependency search is clean**

Run:

```bash
rg -n "cards\\.json|CardsJson|CardJsonPathResolver|CardsJsonPath|LocalCardTemplateCatalog|CardAttributeCatalog|MonsterPreviewAttributeResolver" --glob '!**/obj/**' --glob '!**/bin/**' --glob '!decompiled/**'
```

Expected: no output.

- [ ] **Step 5: Commit the test cleanup**

Run:

```bash
git add tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
git add -u tests/MonsterPreviewResilience.Tests/TestLocalCardTemplateCatalog.cs
git commit -m "Clean up cards json test shim"
```

## Task 4: Compile The Mod And Do Final Verification

**Files:**
- Verify only.

- [ ] **Step 1: Build the main mod project**

Run this first, letting the repo's game assembly auto-detection work:

```bash
dotnet build BazaarPlusPlus.csproj -p:Configuration=Debug
```

Expected: build succeeds.

If auto-detection cannot find the local game assemblies on macOS, rerun with the common Steam paths from the project file:

```bash
dotnet build BazaarPlusPlus.csproj -p:Configuration=Debug -p:ManagedPath="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed" -p:GamePath="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar"
```

Expected: build succeeds. If the user's local install is not in that Steam directory, pass the actual `ManagedPath` and `GamePath` explicitly rather than editing project references.

- [ ] **Step 2: Run the focused resilience test one final time**

Run:

```bash
dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
```

Expected:

```text
MonsterPreviewResilience checks passed.
```

- [ ] **Step 3: Run the final dependency scan**

Run:

```bash
rg -n "cards\\.json|CardsJson|CardJsonPathResolver|CardsJsonPath|LocalCardTemplateCatalog|CardAttributeCatalog|MonsterPreviewAttributeResolver" --glob '!**/obj/**' --glob '!**/bin/**' --glob '!decompiled/**'
```

Expected: no output.

- [ ] **Step 4: Inspect the diff**

Run:

```bash
git diff --stat
git diff -- Game/HistoryPanel/HistoryPanelRepository.Preview.cs Game/MonsterPreview/Architecture/PreviewCardSpecFilter.cs Game/MonsterPreview/MonsterPreviewWarmupController.cs Plugin.cs Core/Paths/IPathService.cs Core/Paths/BppPathService.cs tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj
```

Expected:

- History preview still filters preview specs through `PreviewCardSpecFilter.Filter`.
- The filter predicate resolves templates through `GetTemplate(staticData, templateId)`.
- Startup no longer installs or warms `CardsJsonCache`, `LocalCardTemplateCatalog`, or `CardAttributeCatalog`.
- Path services no longer mention `CardsJsonPath`.
- The resilience test project no longer compiles `TestLocalCardTemplateCatalog.cs`.

- [ ] **Step 5: Commit final verification adjustments if any were needed**

If Task 4 required code changes, commit them:

```bash
git add <changed-files>
git commit -m "Finalize cards json dependency removal"
```

If Task 4 did not require code changes, do not create an empty commit.

## Manual Game Smoke Test

- [ ] **Step 1: Launch the game with the Debug build installed**

Use the existing Debug build copy flow from `BazaarPlusPlus.csproj`; do not change packaging behavior.

- [ ] **Step 2: Open history panel battle preview**

Expected:

- Battles with current local card templates still show item and skill preview cards.
- Battles containing stale or unknown template IDs omit those cards instead of crashing.
- The BepInEx log does not contain `CardsJsonCache`, `cards.json`, `LocalCardTemplateCatalog`, or `CardAttributeCatalog` entries.

- [ ] **Step 3: Open card-set preview and final-build recommendation preview**

Expected:

- Selected-set preview still renders selected item cards.
- Final-build recommendation preview still renders known recommended item cards.
- Unknown recommendation card IDs are handled by the existing render path without mod-level `cards.json` lookup.

## Self-Review

**Spec coverage:** The plan removes all direct production references to `cards.json`, `CardsJsonCache`, `CardJsonPathResolver`, `CardsJsonPath`, `LocalCardTemplateCatalog`, `CardAttributeCatalog`, and `MonsterPreviewAttributeResolver`. It preserves history preview filtering by using the same static game data source the render surfaces already use.

**Placeholder scan:** No task contains unresolved placeholder language or unspecified test work.

**Type consistency:** The plan keeps existing types (`PreviewCardSpecFilter`, `PreviewBoardModel`, `PvpBattleCardSetCapture`, `BppLog`, `Data.GetStatic`, `GetTemplate`) and does not introduce new public APIs.
