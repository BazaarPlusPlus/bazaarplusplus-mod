---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master 2b4e1d80 + 9ae1999b (2026-06-30); Packages tab + package-only state + custom-art replacement subsystem fully removed, PackageIdentity + IsPackage Items-exclusion retained as planned.

# Remove Package Custom Art And Packages Tab Plan

Date: 2026-06-30

## Goal

Remove the package custom-card-art replacement feature and remove the Packages tab
from CollectionPanel.

The intended product behavior is:

- no Package Swap / package-art replacement setting
- no bundled package custom art in the mod assembly
- no startup installation of bundled custom art
- no Harmony patches that replace package card art on live cards, rewards, or
  CollectionPanel previews
- no Packages top-level tab in CollectionPanel
- package cards do not appear in the normal Items tab

This plan does not implement the deletion. It is the execution plan to confirm
before patching.

## Code Facts

Package custom art is currently a standalone feature registered at startup.

- `BppComposition` imports `BazaarPlusPlus.Game.CardArtReplacement` in
  `src/BazaarPlusPlus/BppComposition.cs:7`.
- `BppComposition` registers `new CardArtReplacementFeature(_paths)` in
  `src/BazaarPlusPlus/BppComposition.cs:95`.
- `BppComposition` registers `new PackageCardArtReplacementSettingsDockEntry()`
  in `src/BazaarPlusPlus/BppComposition.cs:109`.
- `BppConfig` exposes `EnablePackageCardArtReplacementConfig` in
  `src/BazaarPlusPlus/Core/Config/BppConfig.cs:33`.
- The config key is bound as `CardArtReplacement/EnablePackageArtReplacement` in
  `src/BazaarPlusPlus/Core/Config/BppConfig.cs:97-102`.
- `IBppConfig` exposes the same config entry in
  `src/BazaarPlusPlus/Core/Config/IBppConfig.cs:27`.
- `BppSettingsDockOrder` reserves `PackageCardArtReplacement = 4` in
  `src/BazaarPlusPlus/Game/Settings/BppSettingsDockOrder.cs:11`.
- `PackageCardArtReplacementSettingsDockEntry` builds the `PackageCardArtReplacement`
  settings row in
  `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementSettingsDockEntry.cs:7-19`.
- The settings row label is `"Package Swap"` / `"掉包快递"` in
  `src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementSettingsMenuLabel.cs:7-14`.

Package custom art has its own runtime directory and embedded assets.

- `IPathProvider` exposes `CustomCardArtDirectoryPath` in
  `src/BazaarPlusPlus.Storage/Paths/IPathProvider.cs:14`.
- `BepInExPathProvider` maps that directory to
  `BazaarPlusPlusV4/CustomCardArt` in
  `src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs:42-46`.
- The main project embeds `Resources\CustomCardArt\*.jpg` in
  `src/BazaarPlusPlus/BazaarPlusPlus.csproj:28-30`.
- `CardArtReplacementFeature.Start()` creates the directory and installs bundled
  art in
  `src/BazaarPlusPlus/Game/CardArtReplacement/CardArtReplacementFeature.cs:29-70`.
- `BundledCustomCardArtInstaller` discovers manifest resources under
  `BazaarPlusPlus.Resources.CustomCardArt.` in
  `src/BazaarPlusPlus/Game/CardArtReplacement/BundledCustomCardArtInstaller.cs:10-20`.

The art replacement is patch-driven, not only feature-driven.

- Plugin startup calls `_harmony.PatchAll()` in
  `src/BazaarPlusPlus/Plugin.cs:165-170`, so any remaining `[HarmonyPatch]`
  classes in the assembly will still be applied.
- `ItemVisualsArtReplacePatch` patches live card visuals through
  `ItemVisualsController.SetCardFrameMaterial` in
  `src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:27-52`.
- `RewardControllerArtReplacePatch` patches reward setup in
  `src/BazaarPlusPlus/Patches/CardArtReplacement/RewardControllerArtReplacePatch.cs:13-60`.
- `CardPreviewItemArtReplacePatch` patches CollectionPanel preview art in
  `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:12-53`.
- `PackageCardArtPatchGate` gates all replacement calls on the setting and the
  package identity check in
  `src/BazaarPlusPlus/Patches/CardArtReplacement/PackageCardArtPatchGate.cs:13-50`.
- `CardArtInjector` owns the runtime card identity tracking and material texture
  injection used by those patches in
  `src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs:17-130`.

CollectionPanel currently has a package-only tab mode.

- `CollectionTabKind` contains `Items`, `Packages`, and `Skills` in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabKind.cs:6-10`.
- `CollectionTabKind.CardType()` maps non-Skills tabs to item cards in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabKind.cs:15-16`.
- `CollectionTabKind.IsPackageOnly()` returns true only for `Packages` in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabKind.cs:18-19`.
- `CollectionTabProfile.For()` has a `CollectionTabKind.Packages` profile in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabProfile.cs:52-88`.
- `CollectionFilterState.PackagesOnly` maps to `ActiveTab == Packages` in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:44-58`.
- `CollectionFilterState.SelectPackagesOnly()` is a public state transition in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:97`.
- The CollectionPanel view model exposes `PackagesOnly` in
  `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:42`.
- The UI stores `_packageToggleButton` in
  `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:92`.
- The Packages button is created and inserted between Items and Skills in
  `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:78-95`.
- `CreatePackageTabButton()` sets the command to `SetActiveTab(CollectionTabKind.Packages)`
  in `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:387-397`.
- Package tab text is defined and exposed in
  `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs:72-76`
  and `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs:148-150`.

Package identity must remain for CollectionPanel filtering.

- `PackageIdentity.IsPackage()` returns true when hidden tags contain
  `EHiddenTag.Package` in
  `src/BazaarPlusPlus/GameInterop/Cards/PackageIdentity.cs:7-21`.
- `CollectionCardClassifier.Classify(TCardBase)` stores `IsPackage` on the card
  classification in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardClassifier.cs:19-27`.
- `CollectionCardClassifier.IsPackage()` delegates to `PackageIdentity` in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardClassifier.cs:58-59`.
- `CollectionCardVm` carries `IsPackage` in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.cs:50`.
- `CollectionCardVm.From()` copies the classification into the VM in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs:35-45`.
- `CollectionFilterEngine.Apply()` currently has a package-only branch in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:42-47`.
- The same filter excludes package cards from the normal path in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs:48-49`.
- `CollectionFacetAvailability.SnapshotFor()` skips packages while building
  facets in
  `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFacetAvailability.cs:51-55`.

Tests currently lock in both package-art replacement and package-only behavior.

- `CardArtReplacement.Tests` references the main project and game/Unity assemblies
  in `tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj:36-59`.
- `CardArtReplacementTests` covers custom-art catalog/cache/resources/installer,
  package identity through `CardArtInjector`, and package-art config policy in
  `tests/CardArtReplacement.Tests/CardArtReplacementTests.cs:25-206`.
- `SettingsDockRegistry.Tests` covers the Package Swap settings row in
  `tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs:98-140`.
- The same settings test asserts the package-art named order in
  `tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs:305-321`.
- `CollectionFilterEngine.Tests` covers package-only state transitions in
  `tests/CollectionFilterEngine.Tests/Program.cs:215-268`.
- `CollectionFilterEngine.Tests` already covers the default package exclusion in
  `tests/CollectionFilterEngine.Tests/Program.cs:309-324`.
- The same test project links `PackageIdentity.cs` directly in
  `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj:127-130`.
- Architecture tests currently assert the existence and layering of
  `GameInterop/CardArtReplacement` in
  `tests/Architecture.Tests/CoreLayeringTests.cs:507-543`.

## Confirmed Decisions

- Do not delete a user's existing `BazaarPlusPlusV4/CustomCardArt` directory on
  disk. The implementation should remove all code and bundled resources that use
  the directory, but it should not perform runtime filesystem cleanup.
- Do not delete `PackageIdentity`. The product decision is that removing the
  Packages tab means package cards disappear from CollectionPanel, not that they
  are mixed into the normal Items tab.
- Keep package identity keyed by `EHiddenTag.Package`, not by display name,
  internal name, or `ArtKey`.
- Do not edit files under `decompiled/`.
- Do not hand-edit curated docs such as `docs/MEMORY.md` or `docs/INDEX.md`.
  If implementation reveals durable knowledge, add a draft note and let
  consolidation sweep it.

## Target End State

- `src/BazaarPlusPlus/Game/CardArtReplacement/` no longer exists.
- `src/BazaarPlusPlus/Patches/CardArtReplacement/` no longer exists.
- `src/BazaarPlusPlus/GameInterop/CardArtReplacement/` no longer exists.
- `src/BazaarPlusPlus/Resources/CustomCardArt/` no longer exists or contains no
  files referenced by the project.
- `BazaarPlusPlus.csproj` no longer embeds `Resources\CustomCardArt\*.jpg`.
- `BppComposition` no longer registers `CardArtReplacementFeature`.
- The settings dock has no Package Swap entry.
- `BppConfig` and `IBppConfig` no longer expose
  `EnablePackageCardArtReplacementConfig`.
- `IPathProvider` and `BepInExPathProvider` no longer expose
  `CustomCardArtDirectoryPath`.
- CollectionPanel has exactly two top-level card tabs: Items and Skills.
- CollectionPanel has no `PackagesOnly` state and no `CollectionTabKind.Packages`.
- Package cards are still classified through `PackageIdentity`.
- Package cards are still excluded from normal Items results.
- `tests/CardArtReplacement.Tests/` no longer exists.
- Architecture tests no longer expect `GameInterop/CardArtReplacement` to exist.

Expected source search after implementation:

```bash
rg -n "CardArtReplacement|CustomCardArt|PackageCardArtReplacement|EnablePackageArtReplacement|PackagesOnly|CollectionTabKind\\.Packages|PackagesToggle|掉包快递" src tests
```

The command should return no hits. Broader terms such as `Package`,
`PackageReference`, and `EHiddenTag.Package` are expected to remain.

## Out Of Scope

- Do not delete or migrate user files under `BazaarPlusPlusV4/CustomCardArt`.
- Do not change server APIs or upload payloads.
- Do not change SQLite schemas.
- Do not change merchant/trainer source filtering.
- Do not make package cards visible in the normal Items tab.
- Do not add a replacement UI affordance for package cards.

## Commit Plan

Each commit should build. Where a commit removes production behavior covered by
tests, update or delete the corresponding tests in the same commit.

### Commit 1: Remove The Visible Packages Tab

Purpose: remove the user entry point first while leaving deeper package-only
state temporarily in place.

Changes:

- Remove `_packageToggleButton` from CollectionPanel view state.
- Remove package button creation from the primary controls row.
- Remove `CreatePackageTabButton()`.
- Remove `RefreshPackageToggle()`.
- Remove package-button refresh in `CollectionPanelView.Refresh()`.
- Remove package-button locale refresh in `RefreshChromeTexts()`.
- Remove package text from the font pre-rasterization string.
- Remove `CollectionPanelText.PackagesToggle()` and
  `CollectionPanelText.PackagesToggleTooltip()`.
- Remove the corresponding localized text fields.

Files:

- `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Filters.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs`

Verification:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected runtime behavior after this commit:

- Users can switch between Items and Skills only.
- Hidden `PackagesOnly` state still exists in data code but is unreachable from
  normal UI.

### Commit 2: Remove Package-Only CollectionPanel State

Purpose: remove the package-only tab model while preserving package exclusion.

Changes:

- Remove `CollectionTabKind.Packages`.
- Remove `CollectionTabKindExtensions.IsPackageOnly()`.
- Remove the Packages profile from `CollectionTabProfile.For()`.
- Remove `CollectionFilterState.PackagesOnly`.
- Remove `CollectionFilterState.SelectPackagesOnly()`.
- Ensure `ActiveType` continues to map only Items and Skills.
- Remove package-only checks from `CollectionQuery`.
- Remove package-only fields from `CollectionPanelViewModel`.
- Remove package-only handling from `CollectionPanel.RefreshView()`.
- In `CollectionFilterEngine.Apply()`, delete the `filter.PackagesOnly` branch
  but retain the normal-path exclusion of `card.IsPackage`.
- Keep `CollectionFacetAvailability` skipping package cards.
- Update `ICollectionPanelCommands` comments so they no longer mention
  packages-only.
- Update `CollectionFilterEngine.Tests` by deleting package-only transition and
  package-only result assertions.
- Keep or strengthen the test that says packages are excluded by default.
- Update classifier test wording from "exclusive package view" to "hidden from
  CollectionPanel" or equivalent.

Files:

- `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabKind.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabProfile.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterEngine.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionQuery.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Ui/ICollectionPanelCommands.cs`
- `tests/CollectionFilterEngine.Tests/Program.cs`

Verification:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected runtime behavior after this commit:

- CollectionPanel has no package-only state.
- Items still excludes cards classified as packages.
- Skills behavior is unchanged.

### Commit 3: Delete The Package Custom-Art Replacement Subsystem

Purpose: remove the feature, settings, patches, resources, paths, and tests as
one vertical slice so the codebase keeps building.

Changes:

- Remove `using BazaarPlusPlus.Game.CardArtReplacement` from `BppComposition`.
- Remove `new CardArtReplacementFeature(_paths)` registration.
- Remove `new PackageCardArtReplacementSettingsDockEntry()` registration.
- Remove `EnablePackageCardArtReplacementConfig` from `BppConfig`.
- Remove the `CardArtReplacement/EnablePackageArtReplacement` config bind.
- Remove `EnablePackageCardArtReplacementConfig` from `IBppConfig`.
- Remove `PackageCardArtReplacement` from `BppSettingsDockOrder`; renumber later
  order constants to keep the settings order dense, then update named-order
  tests.
- Delete `src/BazaarPlusPlus/Game/CardArtReplacement/`.
- Delete `src/BazaarPlusPlus/Patches/CardArtReplacement/`.
- Delete `src/BazaarPlusPlus/GameInterop/CardArtReplacement/`.
- Delete `src/BazaarPlusPlus/Resources/CustomCardArt/`.
- Remove the `Resources\CustomCardArt\*.jpg` embedded-resource item from
  `BazaarPlusPlus.csproj`.
- Remove `CustomCardArtDirectoryPath` from `IPathProvider`.
- Remove `CustomCardArtDirectoryPath` from `BepInExPathProvider`.
- Update all `IPathProvider` test fakes that currently implement
  `CustomCardArtDirectoryPath`.
- Delete `tests/CardArtReplacement.Tests/`.
- Remove `InternalsVisibleTo("CardArtReplacement.Tests")` from
  `src/BazaarPlusPlus/Properties/AssemblyAttributes.cs`.
- Remove Package Swap tests from `SettingsDockRegistry.Tests`.
- Remove the package-art named-order assertion from
  `SettingsDockRegistry.Tests`.
- Remove the architecture test that requires `GameInterop/CardArtReplacement`.
- Keep `src/BazaarPlusPlus/GameInterop/Cards/PackageIdentity.cs`.
- Keep `src/BazaarPlusPlus/Patches/CollectionPanel/CollectionItemLoadArtPatch.cs`;
  it is CollectionPanel preview cache plumbing, not package custom-art
  replacement.

Files:

- `src/BazaarPlusPlus/BppComposition.cs`
- `src/BazaarPlusPlus/Core/Config/BppConfig.cs`
- `src/BazaarPlusPlus/Core/Config/IBppConfig.cs`
- `src/BazaarPlusPlus/Core/Paths/BepInExPathProvider.cs`
- `src/BazaarPlusPlus.Storage/Paths/IPathProvider.cs`
- `src/BazaarPlusPlus/Game/Settings/BppSettingsDockOrder.cs`
- `src/BazaarPlusPlus/BazaarPlusPlus.csproj`
- `src/BazaarPlusPlus/Game/CardArtReplacement/*`
- `src/BazaarPlusPlus/Patches/CardArtReplacement/*`
- `src/BazaarPlusPlus/GameInterop/CardArtReplacement/*`
- `src/BazaarPlusPlus/Resources/CustomCardArt/*`
- `src/BazaarPlusPlus/Properties/AssemblyAttributes.cs`
- `tests/CardArtReplacement.Tests/*`
- `tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs`
- `tests/Architecture.Tests/CoreLayeringTests.cs`
- `tests/Storage.Tests/TempDirPathProviderTests.cs`
- `tests/RunLoggingSqliteStore.Tests/Program.cs`
- `tests/RunLoggingSqliteRecovery.Tests/Program.cs`

Verification:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
dotnet run --project tests/Storage.Tests/Storage.Tests.csproj
dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj
dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj
```

Expected runtime behavior after this commit:

- Startup no longer creates or reads `BazaarPlusPlusV4/CustomCardArt`.
- Startup does not delete any existing `BazaarPlusPlusV4/CustomCardArt`
  directory.
- No package-art Harmony patch exists for `PatchAll()` to install.
- Package cards use native game art everywhere.
- Settings dock has no Package Swap row.

### Commit 4: Update Current Documentation

Purpose: keep current docs aligned with the code deletion while leaving curated
memory/index files to consolidation.

Changes:

- Update `docs/ARCHITECTURE.md` CollectionPanel text so it no longer mentions
  packages-only mode.
- Update any current non-archived plan or draft that asserts package custom-art
  replacement is retained, if it would confuse implementation follow-up.
- Do not edit `docs/MEMORY.md`, `docs/INDEX.md`, or archived historical plans.
- Optionally append an implementation result section to this draft after the
  code changes land.

Files:

- `docs/ARCHITECTURE.md`
- possibly this file

Verification:

```bash
rg -n "packages-only|Package custom-art replacement|Package Swap|CustomCardArt|CardArtReplacement" docs/ARCHITECTURE.md docs/drafts --glob '!**/archive/**'
```

Expected result:

- Current docs no longer describe removed behavior as active.
- Historical or explicitly superseded draft references are either absent from
  current docs or clearly marked stale.

#### Implementation result

- Updated `docs/ARCHITECTURE.md` to describe the current CollectionPanel model
  as Items/Skills only, with package cards excluded from normal results by
  `CollectionFilterEngine.Apply()`.
- Updated `docs/ARCHITECTURE.md` runtime data wording so
  `BepInExPathProvider.Initialize()` no longer claims ownership of a custom
  card-art directory.
- Marked active, non-archived achievement/custom-card drafts that still discuss
  retained package art as superseded for implementation follow-up.
- Left `docs/MEMORY.md`, `docs/INDEX.md`, and archived historical plans
  untouched; stale curated-memory references remain for consolidation.

### Commit 5: Final Search And Test Cleanup

Purpose: catch residual code references, stale tests, and build-system drift.

Changes:

- Run the final source searches.
- Remove any accidental stale imports, linked test compile items, comments, or
  project references found by those searches.
- Run formatter if code was edited.

Verification:

```bash
rg -n "CardArtReplacement|CustomCardArt|PackageCardArtReplacement|EnablePackageArtReplacement|PackagesOnly|CollectionTabKind\\.Packages|PackagesToggle|掉包快递" src tests
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
./run.sh test
./run.sh format
git diff --check
```

Expected result:

- The `rg` command has no hits.
- Build passes.
- Full test suite passes.
- Formatting introduces no unrelated churn beyond CSharpier's normal output for
  touched files.

## Testing Decisions

- The core CollectionPanel behavior to preserve is external behavior: package
  cards do not appear in the visible Items result set.
- Do not test deleted package-only internals.
- Keep package classification tests in `CollectionFilterEngine.Tests`, because
  CollectionPanel still needs `PackageIdentity` to hide package cards.
- Delete `CardArtReplacement.Tests` entirely, because its subject disappears.
- Keep `SettingsDockRegistry.Tests`, but remove Package Swap assertions.
- Keep architecture tests for real remaining seams; delete only the
  CardArtReplacement-specific architecture test.
- Run `./run.sh test` after focused tests because this repo mixes xUnit projects
  and executable runner projects.

## Risk Notes

- Leaving any `[HarmonyPatch]` class under `Patches/CardArtReplacement` would
  keep behavior alive because `Plugin.ApplyHarmonyPatches()` calls
  `_harmony.PatchAll()`.
- Removing `PackageIdentity` would make it easy for packages to leak into Items;
  keep it and keep the `card.IsPackage` exclusion in `CollectionFilterEngine`.
- Removing `CustomCardArtDirectoryPath` is a public interface change within
  `BazaarPlusPlus.Storage`, so all `IPathProvider` implementers in tests must be
  updated in the same commit.
- Old user config files may still contain
  `CardArtReplacement/EnablePackageArtReplacement`; no code should read it after
  deletion, and no migration is needed.
- Old user `BazaarPlusPlusV4/CustomCardArt` files may remain on disk; the mod
  should ignore them after deletion.

## Final Verification

Run:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
./run.sh test
rg -n "CardArtReplacement|CustomCardArt|PackageCardArtReplacement|EnablePackageArtReplacement|PackagesOnly|CollectionTabKind\\.Packages|PackagesToggle|掉包快递" src tests
rg -n "PackageIdentity|EHiddenTag\\.Package|IsPackage" src/BazaarPlusPlus/Game/CollectionPanel src/BazaarPlusPlus/GameInterop/Cards tests/CollectionFilterEngine.Tests
```

Expected:

- Build passes.
- Focused tests pass.
- Full test suite passes.
- The deletion search has no source/test hits.
- The identity search still shows retained package classification and package
  exclusion coverage.
