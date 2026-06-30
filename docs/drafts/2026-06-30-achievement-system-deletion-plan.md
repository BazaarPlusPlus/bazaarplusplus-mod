# Achievement System Deletion Plan

Date: 2026-06-30

## Goal

Remove the current BazaarPlusPlus achievement system completely from the mod:

- no Achievements tab in CollectionPanel
- no embedded achievement catalog
- no synthetic achievement card registration
- no achievement-only custom-card rendering path
- no achievement-specific tests or bundled achievement art

This plan does not implement the deletion. It is the execution plan to confirm before patching.

## Code Facts

Current achievement behavior is local UI/catalog/rendering, not a server-backed progress system.

- Plugin startup creates a BPP custom-card registry and registers achievements via `AchievementCardRegistrar.Register(BppCustomCardRegistry.Current)` in `src/BazaarPlusPlus/BppComposition.cs:97-98`.
- Startup clears the same registry on dispose in `src/BazaarPlusPlus/BppComposition.cs:168-174`.
- The main plugin embeds the achievement catalog explicitly at `src/BazaarPlusPlus/BazaarPlusPlus.csproj:22-26`.
- The current catalog contains one achievement, `storm_traveler`, with template id `ff35bbaa-3545-5fef-b469-79a4f4592326` in `src/BazaarPlusPlus/Data/AchievementCards/achievement-cards.json:5-6`.
- `Game/Achievements` owns catalog parsing, definition shape, descriptor mapping, and registration in `src/BazaarPlusPlus/Game/Achievements/AchievementCardCatalog.cs:15-31`, `src/BazaarPlusPlus/Game/Achievements/AchievementCardDefinition.cs:8-21`, `src/BazaarPlusPlus/Game/Achievements/AchievementCardDescriptorMapper.cs:23-39`, and `src/BazaarPlusPlus/Game/Achievements/AchievementCardRegistrar.cs:11-47`.
- CollectionPanel has an explicit `Achievements` tab value in `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabKind.cs:6-12`.
- CollectionPanel gives that tab a profile that hides most filters and keeps tier filtering in `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabProfile.cs:52-99`.
- Switching to Achievements clears stale filters in `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:81-97`.
- CollectionPanel applies a special Achievements branch that builds VMs from `BppCustomCardRegistry.Current` instead of normal card catalog data in `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:786-804`.
- The Achievements tab button is built in `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs:78-103`.
- The view stores, refreshes, and localizes an achievement button in `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:90-92`, `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:333-335`, and `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs:480-487`.
- Achievement tab text is defined in `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs:26-33` and exposed at `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs:109-113`.
- The synthetic custom-card foundation is used by achievements through `BppCustomCardCollectionProjection.BuildVms` in `src/BazaarPlusPlus/Game/CollectionPanel/Data/BppCustomCardCollectionProjection.cs:8-35`.
- `CollectionCardFactory` has a registry-first custom-card branch before native static-data lookup in `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs:65-117`.
- The custom-card donor-art resolver exists only to give synthetic achievement cards an authored donor material, per its own comment in `src/BazaarPlusPlus/Game/CollectionPanel/Grid/BppCustomCardMaterialDonorResolver.cs:14-19`.
- The preview art postfix has a BPP custom-card material branch in `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:36-40` and `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:61-87`.
- The package-art gate has a custom-card-only material method in `src/BazaarPlusPlus/Patches/CardArtReplacement/PackageCardArtPatchGate.cs:53-69`.
- Tests directly compile achievement and custom-card source files into `BppCustomCard.Tests` in `tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj:38-169`.
- `CollectionFilterEngine.Tests` asserts achievement tab filter behavior in `tests/CollectionFilterEngine.Tests/Program.cs:270-358`.
- `CardArtReplacement.Tests` currently expects 121 bundled custom-art resources and explicitly checks the achievement jpg in `tests/CardArtReplacement.Tests/CardArtReplacementTests.cs:80-104`.

## Keep Explicitly

Do not delete the package card-art replacement system.

- The wildcard `Resources\CustomCardArt\*.jpg` is embedded at `src/BazaarPlusPlus/BazaarPlusPlus.csproj:29-31`; most files are package-card art, not achievement art.
- `CardArtReplacementFeature` installs bundled custom art and builds texture/material caches in `src/BazaarPlusPlus/Game/CardArtReplacement/CardArtReplacementFeature.cs:29-70`.
- Runtime/package replacement still uses `PackageCardArtPatchGate.TryGetReplacementTexture` and `TryGetReplacementPreviewMaterial` in `src/BazaarPlusPlus/Patches/CardArtReplacement/PackageCardArtPatchGate.cs:14-51`.
- `CollectionItemLoadArtPatch` remains needed for collection-panel preview material caching in `src/BazaarPlusPlus/Patches/CollectionPanel/CollectionItemLoadArtPatch.cs:18-35`.

The only bundled art file identified as achievement-owned by the current catalog is:

- `src/BazaarPlusPlus/Resources/CustomCardArt/ff35bbaa-3545-5fef-b469-79a4f4592326.jpg`

## Target End State

- `rg "Achievement|Achievements|achievement|achievements|成就" src/BazaarPlusPlus tests` should return no implementation/test hits except historical docs or deliberately retained archived references.
- CollectionPanel has exactly three top-level modes: Items, Packages, Skills.
- CollectionPanel no longer constructs or renders BPP custom-card VMs.
- `BppCustomCardRegistry.Current` no longer exists.
- `GameInterop/CustomCards` no longer exists unless a non-achievement caller is introduced before deletion. Current code search shows only achievement/custom-card test and CollectionPanel rendering callers.
- Package card-art replacement still builds, installs bundled package art, and applies to package cards.
- `./run.sh test` should not discover `tests/BppCustomCard.Tests` after that project is deleted.

## Commit Plan

Each commit should build. Where a commit removes production behavior covered by tests, update or delete the corresponding tests in the same commit.

### Commit 1: Remove the visible Achievements tab

Purpose: remove the user entry point while leaving the deeper synthetic-card plumbing temporarily intact.

Changes:

- Remove `_achievementTabButton` from CollectionPanel view state.
- Remove the button creation from the primary controls row.
- Remove achievement tab refresh and locale-refresh calls.
- Remove `CollectionPanelText.AchievementsTab()` and its `LocalizedTextSet`.
- Remove achievement text from the font pre-rasterization sample.

Files:

- `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Ui/CollectionPanelView.Tree.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Text/CollectionPanelText.cs`

Verification:

- `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`

Expected runtime behavior after this commit:

- Users cannot switch to Achievements through the UI.
- Hidden achievement code still exists but is unreachable from normal CollectionPanel interaction.

### Commit 2: Remove Achievements from the CollectionPanel model

Purpose: restore CollectionPanel to a three-mode model and remove the achievement filtering path.

Changes:

- Remove `CollectionTabKind.Achievements`.
- Remove the Achievements branch from `CollectionTabProfile.For`.
- Remove the Achievements special-case clearing in `CollectionFilterState.SelectTab`.
- Remove the Achievements guard in `CollectionFilterState.ToggleSource`.
- Remove the Achievements branch in `CollectionPanel.ApplyFilters`.
- Remove `BppCustomCardCollectionProjection` after its production caller is gone.
- Update `CollectionFilterEngine.Tests` by deleting assertions that exercise Achievements tab behavior.

Files:

- `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabKind.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionTabProfile.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Data/BppCustomCardCollectionProjection.cs`
- `tests/CollectionFilterEngine.Tests/Program.cs`

Verification:

- `dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
- `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`

Expected runtime behavior after this commit:

- CollectionPanel has no Achievements state, profile, source filtering behavior, or visible-set branch.

### Commit 3: Remove achievement catalog and startup registration

Purpose: delete the actual achievement domain module and embedded catalog.

Changes:

- Remove `using BazaarPlusPlus.Game.Achievements` from composition.
- Remove `AchievementCardRegistrar.Register(...)`.
- Remove `BppCustomCardRegistry.Current = new BppCustomCardRegistry()` if no non-achievement custom-card caller remains at this point.
- Remove `BppCustomCardRegistry.Current = null` from dispose if the registry is deleted in the same commit.
- Delete `src/BazaarPlusPlus/Game/Achievements/`.
- Delete `src/BazaarPlusPlus/Data/AchievementCards/achievement-cards.json`.
- Remove the explicit embedded resource for `Data\AchievementCards\achievement-cards.json`.
- Delete the achievement-owned bundled art file `src/BazaarPlusPlus/Resources/CustomCardArt/ff35bbaa-3545-5fef-b469-79a4f4592326.jpg`.

Files:

- `src/BazaarPlusPlus/BppComposition.cs`
- `src/BazaarPlusPlus/BazaarPlusPlus.csproj`
- `src/BazaarPlusPlus/Game/Achievements/*`
- `src/BazaarPlusPlus/Data/AchievementCards/achievement-cards.json`
- `src/BazaarPlusPlus/Resources/CustomCardArt/ff35bbaa-3545-5fef-b469-79a4f4592326.jpg`

Verification:

- `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`

Expected runtime behavior after this commit:

- Plugin startup no longer loads or registers any achievement catalog.

### Commit 4: Remove the custom-card rendering foundation

Purpose: remove infrastructure that only existed to render synthetic achievement cards.

Changes:

- Delete `GameInterop/CustomCards`.
- Remove `BppCustomCardDescriptor` and custom-template builder dependencies from `CollectionCardFactory`.
- Remove the registry-first custom-card branch from `CollectionCardFactory.TryBind`; the factory should go straight to native static-data lookup.
- Delete `BppCustomCardMaterialDonorResolver`.
- Remove custom-card material application from `CardPreviewItemArtReplacePatch`, leaving package preview replacement intact.
- Remove `PackageCardArtPatchGate.TryGetBppCustomCardPreviewMaterial`.
- Remove custom-card-related `InternalsVisibleTo` entries if no remaining test assembly needs them.

Files:

- `src/BazaarPlusPlus/GameInterop/CustomCards/*`
- `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/Grid/BppCustomCardMaterialDonorResolver.cs`
- `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs`
- `src/BazaarPlusPlus/Patches/CardArtReplacement/PackageCardArtPatchGate.cs`
- `src/BazaarPlusPlus/Properties/AssemblyAttributes.cs`
- `src/BazaarPlusPlus.Localization/Properties/AssemblyInfo.cs`

Important guardrail:

- Keep package preview replacement: `TryGetReplacementPreviewMaterial` must still run for package templates.
- Keep `CollectionPanelOwnedMarker` use in `CardPreviewItemArtReplacePatch`; the postfix should still be scoped to collection-panel previews.

Verification:

- `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`
- `dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj`

Expected runtime behavior after this commit:

- Unknown synthetic GUIDs are no longer supported in CollectionPanel. That is intended because achievements are gone.
- Package card-art preview replacement still works.

### Commit 5: Delete achievement/custom-card tests and fix package-art resource assertions

Purpose: remove tests for deleted behavior and keep tests for retained behavior accurate.

Changes:

- Delete `tests/BppCustomCard.Tests/`.
- Update any test discovery assumptions only if needed. `run.sh` discovers projects dynamically with `find tests -mindepth 2 -maxdepth 2 -name '*.csproj'` in `run.sh:121-132`, so deleting the project directory is enough.
- Update `CardArtReplacement.Tests` resource count from 121 to 120 if the only removed resource is the achievement jpg.
- Remove the explicit assertion for `BazaarPlusPlus.Resources.CustomCardArt.ff35bbaa-3545-5fef-b469-79a4f4592326.jpg`.
- Confirm Chinese locale behavior remains covered by `SettingsDockRegistry.Tests`; it already checks config migration/status/cycling in `tests/SettingsDockRegistry.Tests/SettingsDockRegistryTests.cs:175-216`.

Files:

- `tests/BppCustomCard.Tests/*`
- `tests/CardArtReplacement.Tests/CardArtReplacementTests.cs`
- possibly `src/BazaarPlusPlus/Properties/AssemblyAttributes.cs`
- possibly `src/BazaarPlusPlus.Localization/Properties/AssemblyInfo.cs`

Verification:

- `dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj`
- `./run.sh test`

Expected runtime behavior after this commit:

- Full test suite no longer compiles or runs achievement/custom-card tests.
- Package art tests still validate retained bundled art behavior.

### Commit 6: Documentation cleanup draft

Purpose: record that achievement deletion supersedes the older achievement plans without editing curated docs directly.

Changes:

- Add a short completion note under `docs/drafts/` after implementation, or append an implementation result section to this plan.
- Do not hand-edit `docs/MEMORY.md`, `docs/INDEX.md`, or files under `docs/plans/` during the implementation. Project rules say those are curated by consolidation runs.
- In the final wrap-up, call out that current active-plan references to achievements are stale and should be swept by a consolidation run.

Files:

- `docs/drafts/2026-06-30-achievement-system-deletion-plan.md`

Verification:

- Documentation-only.

## Final Verification

Run:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
./run.sh test
rg -n "Achievement|Achievements|achievement|achievements|成就|BppCustomCard" src/BazaarPlusPlus tests --glob '!bin/**' --glob '!obj/**'
```

Expected:

- Build passes.
- Full test suite passes.
- The final `rg` has no production/test hits, or only intentionally retained terms unrelated to the deleted system. Any remaining hit must be justified in the implementation summary.

Runtime smoke test:

1. Build and deploy the mod.
2. Launch The Bazaar through Steam.
3. Open CollectionPanel.
4. Confirm top-level controls expose only Items, Packages, and Skills.
5. Confirm package card-art replacement still works in CollectionPanel and live/reward package surfaces.
6. Check `BepInEx/LogOutput.log` for absence of achievement catalog load warnings and absence of custom-card template/material warnings.

## Out Of Scope

- Server-side achievement APIs, analyzers, or storage schemas. Current source search found no achievement implementation in `BazaarPlusPlus.ModApi`, `BazaarPlusPlus.Storage`, `BazaarPlusPlus.Localization`, `BazaarPlusPlus.BazaarAgent`, or `BazaarPlusPlus.BazaarAgentHost`.
- Renaming `CustomCardArt*` classes to package-specific names. They are still used by package art replacement. Renaming them would be a separate cleanup with broader blast radius.
- Editing archived docs under `docs/archive/`.
- Editing curated docs (`docs/MEMORY.md`, `docs/INDEX.md`, `docs/plans/`) outside the consolidation workflow.

## Main Risks

- Accidentally deleting all `Resources/CustomCardArt/*.jpg` would break package card art. Only delete the achievement catalog's one jpg.
- Removing `CollectionPanelOwnedMarker` scoping from `CardPreviewItemArtReplacePatch` could widen package preview replacement outside the collection panel.
- Deleting `BppCustomCard.Tests` also removes two Chinese-locale assertions. Keep equivalent coverage in `SettingsDockRegistry.Tests`, which already exercises the current config/status behavior.
- Leaving `BppCustomCardRegistry.Current` references behind would create dead startup state and misleading custom-card logs even after the UI is gone.

## Suggested Implementation Order

Implement commits 1 through 5 in one branch, running the targeted verification after each commit. Run the full test suite only after commit 5, because early commits deliberately remove behavior that multiple tests currently exercise.

After all code deletion is complete, review the diff specifically for:

- stale `using BazaarPlusPlus.Game.Achievements`
- stale `using BazaarPlusPlus.GameInterop.CustomCards`
- stale `Achievements` enum/text branches
- stale `BppCustomCard` test or `InternalsVisibleTo` references
- accidental removal of package art replacement files

## Implementation Result

Implemented on branch `codex/achievement-system-deletion`.

Code commits:

- `5ab415a5` removed the visible CollectionPanel Achievements tab entry point.
- `4904d30f` removed the Achievements tab/model/filtering path from CollectionPanel.
- `b2a66a77` removed achievement catalog startup registration, deleted the catalog/art resource, and cleaned stale achievement-only test references that would otherwise break the repo test sweep.
- `0de611e6` removed the custom-card rendering foundation and deleted `tests/BppCustomCard.Tests` in the same commit, because the production behavior it covered was removed and `./run.sh test` discovers test projects dynamically.

Review adjustments from the original commit plan:

- `CardArtReplacement.Tests` was updated in the catalog/art deletion commit, not a later test-only commit, because deleting the achievement jpg immediately changed the retained package-art resource count from 121 to 120.
- `tests/BppCustomCard.Tests` was deleted with the custom-card infrastructure removal, not as a separate follow-up commit, because the project linked deleted source files and would have broken `./run.sh test`.

Curated docs were not edited. Older active-plan references to achievements should be swept by a future consolidation run.
