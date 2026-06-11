---
status: active
calibrated: 2026-06-11
---

# Package Card Art Live Game Replacement Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the `Package Swap` / `掉包快递` setting is enabled, package-card custom art applies in normal gameplay surfaces as well as CollectionPanel.

**Architecture:** Keep the existing custom-art catalog, bundled `.jpg` installer, SettingsDock toggle, and `EHiddenTag.Package` identity source. Fix live-game coverage by binding runtime card identity before native material setup and resolving package identity from static templates when runtime hidden tags are missing.

**Tech Stack:** C# 12, BepInEx 5.x, Harmony patches, Unity `Material` / `Texture2D`, The Bazaar publicized game assemblies, xUnit.

---

## Revision Log

- **2026-06-11 red-team calibration.** Original draft included a third task patching `RewardController.Setup(string, Card)`. Ruled out: the sole call site is `AssetLoader.ConstructInstantiateReward` (`decompiled/TheBazaarRuntime/AssetLoader.cs:354-378`, awaited at `:370`), reached only through the `ECardType.EncounterStep` arm of `AssetLoader.InstantiateCardAsync` (`decompiled/TheBazaarRuntime/AssetLoader.cs:277-278`). Package cards are `ECardType.Item` templates and always route through `ConstructAndInstantiateCard` → `ItemController` → `ItemVisualsController` (`decompiled/TheBazaarRuntime/AssetLoader.cs:273-274`), which Tasks 1-2 cover. A `RewardController` patch would be dead code. Also added: test-csproj game-DLL references (Task 1 Step 0), a guarded static-data fallback, and cite corrections.

## Background

The user-visible setting is already wired through SettingsDock. `BppComposition` registers `PackageCardArtReplacementSettingsDockEntry` (`src/BazaarPlusPlus/BppComposition.cs:83-95`), the row label is `Package Swap` / `掉包快递` (`src/BazaarPlusPlus/Game/CardArtReplacement/PackageCardArtReplacementSettingsMenuLabel.cs:7-13`), and both replacement patches already consult `PackageCardArtReplacementPolicy.IsEnabled(...)` before applying custom art (`src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:38-40`, `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:38-40`).

CollectionPanel works because it uses a CollectionPanel-owned `CardPreviewItem` path. CollectionPanel stamps native preview prefabs with `CollectionPanelOwnedMarker` (`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardPool.cs:76-84`), the card factory binds a static `TCardBase` template to the native preview (`src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs:45-69`), and `CardPreviewItemArtReplacePatch` only runs for marked CollectionPanel cards before checking the template's package identity (`src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:22-44`).

The live item-card path is different. `ItemVisualsArtReplacePatch` hooks `ItemVisualsController.SetCardFrameMaterial` and requires `CardArtInjector.TryResolveCard(...)` plus `CardArtInjector.IsPackageCard(...)` before loading the custom texture (`src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:28-61`). Today `IsPackageCard` checks only `Card.HiddenTags` (`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs:59`).

Runtime `Card.HiddenTags` is not guaranteed to contain static template hidden tags. Runtime `Card` starts with an empty `HiddenTags` set (`decompiled/BazaarGameClient/BazaarGameClient.Domain.Models.Cards/Card.cs:23-30`), `DTOUtils.CreateCard(...)` sets `InstanceId`, `TemplateId`, `Type`, and `Template` but does not copy `Template.HiddenTags` into `Card.HiddenTags` (`decompiled/TheBazaarRuntime/TheBazaar/DTOUtils.cs:57-70`), and game update code writes runtime hidden tags only when an update payload includes them (`decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs:148-151`, `decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs:238-240`). The snapshot update path additionally overwrites `Card.HiddenTags` unconditionally with whatever the server sends (`decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs:193`), so runtime tags can also be reset after being populated. Updates never reassign `Card.Template` or `Card.TemplateId`, so the template attached at creation cannot go stale relative to `TemplateId`.

## Purpose

- Make enabled package-card art replacement behave consistently across:
  - CollectionPanel package previews.
  - Live item visuals that flow through `ItemVisualsController` (board, shop, loot/choice screens, recap).
- Preserve `EHiddenTag.Package` as the only package identity source.
- Avoid package-name / art-key substring matching.
- Avoid changing the custom-art catalog format, embedded resource layout, or on-disk `CustomCardArt` directory behavior.
- Keep the patch narrowly scoped to package art replacement; do not redesign CollectionPanel filtering or SettingsDock behavior.

**Explicitly out of scope:** live (non-CollectionPanel) hover/tooltip previews rendered through `CardPreviewItem` are intentionally not replaced in this fix — that patch stays marker-gated to CollectionPanel (`src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:22-27`).

## Root Cause

### Cause 1: Live item identity is too narrow

`CardArtInjector.IsPackageCard(Card? card)` currently delegates directly to `PackageIdentity.IsPackage(card?.HiddenTags)` (`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs:59`). That works only when the runtime card update contains `EHiddenTag.Package`.

Static package identity already exists on templates. The CollectionPanel classifier uses template hidden tags through `CollectionCardClassifier.IsPackage(TCardBase template)` (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardClassifier.cs:58-59`), and tests explicitly define `EHiddenTag.Package` as the authoritative marker while rejecting package-like names without the tag (`tests/CollectionFilterEngine.Tests/Program.cs:1170-1189`).

### Cause 2: Runtime identity tracking can be late or unavailable

The current `ItemVisualsSetupCardArtIdentityPatch` tracks the card in a postfix of `ItemVisualsController.Setup(Card, BazaarCollectionLoadout)` (`src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:14-25`). `Setup(Card, ...)` is `async Task`; Harmony patches its kickoff stub, so the postfix runs at the first await suspension — but when the awaited asset/cardback tasks complete synchronously (cached), the whole body including `SetCardFrameMaterial(...)` runs inside the kickoff, before the postfix (`decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs:255-279`). The postfix timing is therefore racy.

Board cards often have an `ItemController` parent that can provide `CardData` (`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs:49-54`; `CardData` is inherited from `CardController`, `decompiled/TheBazaarRuntime/CardController.cs:183`), but not every `ItemVisualsController` consumer is under `ItemController`. Recap visuals call `visualsController.Setup(CardData)` from `RecapItemVisualController` (`decompiled/TheBazaarRuntime/TheBazaar/RecapItemVisualController.cs:92-105`), which binds to the same patched `Setup(Card, BazaarCollectionLoadout = null)` overload — so the weak-table identity binding must happen in a prefix, before native setup starts.

### Ruled out: RewardController is not a package surface

The original draft treated `RewardController.Setup(string artKey, Card card)` (`decompiled/TheBazaarRuntime/RewardController.cs:138-153`) as an uncovered normal-gameplay surface. Card instantiation dispatches on `ECardType` (`decompiled/TheBazaarRuntime/AssetLoader.cs:266-288`): `ECardType.Item` → `ConstructAndInstantiateCard` (the `ItemController`/`ItemVisualsController` path), and only `ECardType.EncounterStep` → `ConstructInstantiateReward` → `RewardController`. Package cards are `ECardType.Item`, so they never reach `RewardController`; loot/choice screens render them through the `ItemVisualsController` path covered by this fix. No `RewardController` patch ships. If in-game validation surfaces a package rendered with native art outside `ItemVisualsController`, root-cause that surface first rather than resurrecting the reward patch on spec.

## Solution

### Identity Resolution Policy

Centralize live package identity in `CardArtInjector.IsPackageCard(Card? card)`:

1. Return `true` when `card.HiddenTags` contains `EHiddenTag.Package`.
2. Return `true` when `card.Template.HiddenTags` contains `EHiddenTag.Package` (`Card.Template` is `ITCard?`).
3. Return `true` when ready static data resolves `card.TemplateId` to a template whose `HiddenTags` contain `EHiddenTag.Package`.
4. Return `false` otherwise.

The static-data fallback should use `BppStaticDataAccess.TryGetReadyManagerObject()` and `BppStaticDataAccess.GetCardTemplate(...)`, because this helper is already the repository seam for `Data.GetStatic()` version drift (`src/BazaarPlusPlus/GameInterop/StaticCards/BppStaticDataAccess.cs:11-35`). Use the non-blocking ready-manager accessor in the material patch path.

**Guard the static-data step with try/catch returning `false`.** `TryGetReadyManagerObject()` touches `TheBazaar.Data` (TheBazaarRuntime.dll); in the xunit test host that assembly is absent and the call throws at JIT of the callee, and at runtime a failure here must degrade to "not identifiable as package", not break the material patch.

### Live Item Path

Change `ItemVisualsSetupCardArtIdentityPatch` from postfix to prefix so `CardArtInjector.TrackCard(__instance, card)` runs before native `ItemVisualsController.Setup(...)` can call `SetCardFrameMaterial(...)`.

Keep `ItemVisualsArtReplacePatch` on `SetCardFrameMaterial`. After the identity resolver is fixed, this existing patch remains the correct material chokepoint because native setup creates the per-card material instance and assigns it to the illustration renderer before the postfix runs (`decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs:189-217`).

### What Not To Change

- Do not broaden `CardPreviewItemArtReplacePatch` by removing `CollectionPanelOwnedMarker`. That patch currently relies on CollectionPanel's material cache and ownership marker (`src/BazaarPlusPlus/Patches/CollectionPanel/CollectionItemLoadArtPatch.cs:12-35`, `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:22-27`). Normal gameplay coverage comes from the live item path.
- Do not add package-name or art-key substring detection. Tests already reject package-like names without `EHiddenTag.Package` (`tests/CollectionFilterEngine.Tests/Program.cs:1181-1189`).
- Do not change `PackageCardArtReplacementPolicy`, `BppConfig`, or SettingsDock defaults in this fix. The reported reproduction is with the setting already enabled.
- Do not edit files under `decompiled/`; they are evidence only.

## File Structure

- Modify `tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj`
  - Add `BazaarGameClient` and `BazaarGameShared` references (`$(ManagedPath)` HintPath pattern, mirroring `tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj:27-32`). `<Reference>` items do not flow transitively through the `ProjectReference`, and the new tests construct `Card` / `TCardItem` / `EHiddenTag` / `ECardType` directly.
- Modify `src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs`
  - Add template-backed package identity fallback with a guarded static-data step.
  - Keep `PackageIdentity` as the hidden-tag predicate.
- Modify `src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs`
  - Move identity tracking to a prefix.
  - Keep live item material replacement on `SetCardFrameMaterial`.
- Modify `tests/CardArtReplacement.Tests/CardArtReplacementTests.cs`
  - Add focused tests for runtime hidden-tag identity and template hidden-tag fallback.

## Implementation Tasks

### Task 1: Strengthen Runtime Package Identity

**Files:**
- Modify: `tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj`
- Modify: `tests/CardArtReplacement.Tests/CardArtReplacementTests.cs`
- Modify: `src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs`

- [ ] **Step 0: Add game assembly references to the test project**

In `CardArtReplacement.Tests.csproj`, add to the existing game-reference `ItemGroup`:

```xml
<Reference Include="BazaarGameClient">
  <HintPath>$(ManagedPath)/BazaarGameClient.dll</HintPath>
</Reference>
<Reference Include="BazaarGameShared">
  <HintPath>$(ManagedPath)/BazaarGameShared.dll</HintPath>
</Reference>
```

`$(ManagedPath)` is already resolved by this csproj. Copy-local defaults put both DLLs in the test bin, which is required for the xunit host to construct `Card` at runtime (this is the first xunit project in the repo to load game DLLs at runtime — existing game-DLL test projects are exe-runners; treat a `FileNotFoundException`/`TypeLoadException` for game assemblies as the expected first failure mode to debug, not an assertion failure).

- [ ] **Step 1: Add failing identity tests**

Add these tests to `CardArtReplacementTests`:

```csharp
[Fact]
public void Package_identity_accepts_runtime_hidden_tag()
{
    var card = new BazaarGameClient.Domain.Models.Cards.Card
    {
        TemplateId = Guid.NewGuid(),
        HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Package },
    };

    Assert.True(CardArtInjector.IsPackageCard(card));
}

[Fact]
public void Package_identity_accepts_template_hidden_tag_when_runtime_tags_are_empty()
{
    var templateId = Guid.NewGuid();
    var card = new BazaarGameClient.Domain.Models.Cards.Card
    {
        TemplateId = templateId,
        HiddenTags = new HashSet<EHiddenTag>(),
        Template = new TCardItem
        {
            Id = templateId,
            Type = ECardType.Item,
            ArtKey = "Assets/Cards/Package.png",
            InternalName = "Package Template",
            HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Package },
        },
    };

    Assert.True(CardArtInjector.IsPackageCard(card));
}

[Fact]
public void Package_identity_rejects_template_without_package_hidden_tag()
{
    var templateId = Guid.NewGuid();
    var card = new BazaarGameClient.Domain.Models.Cards.Card
    {
        TemplateId = templateId,
        HiddenTags = new HashSet<EHiddenTag>(),
        Template = new TCardItem
        {
            Id = templateId,
            Type = ECardType.Item,
            ArtKey = "Assets/Cards/Package.png",
            InternalName = "Package Name Without Hidden Tag",
            HiddenTags = new HashSet<EHiddenTag>(),
        },
    };

    Assert.False(CardArtInjector.IsPackageCard(card));
}
```

The reject test deliberately exercises the guarded static-data step: with no game runtime present, `IsPackageCard` must return `false` cleanly, not throw.

Add these `using` statements if they are not already present:

```csharp
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.CardArtReplacement;
```

- [ ] **Step 2: Run the focused test and verify the new fallback test fails**

Run:

```bash
dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj --filter Package_identity
```

Expected: `Package_identity_accepts_template_hidden_tag_when_runtime_tags_are_empty` fails before implementation.

- [ ] **Step 3: Implement template-backed identity**

In `CardArtInjector.cs`, add:

```csharp
using BazaarPlusPlus.GameInterop.StaticCards;
```

(`BazaarGameShared.Domain.Cards` is already imported.)

Replace `IsPackageCard` with:

```csharp
public static bool IsPackageCard(Card? card)
{
    if (card == null)
        return false;

    if (PackageIdentity.IsPackage(card.HiddenTags))
        return true;

    if (IsPackageTemplate(card.Template))
        return true;

    try
    {
        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        return IsPackageTemplate(BppStaticDataAccess.GetCardTemplate(staticData, card.TemplateId));
    }
    catch (Exception ex)
    {
        BppLog.Debug(LogCategory, $"Static package identity lookup failed: {ex.Message}");
        return false;
    }
}
```

Keep the existing public template overload:

```csharp
public static bool IsPackageTemplate(TCardBase? card) =>
    PackageIdentity.IsPackage(card?.HiddenTags);
```

Add this private overload below it (`Card.Template` is `ITCard?`; `GetCardTemplate` returns `TCardBase?` and binds to the public overload):

```csharp
private static bool IsPackageTemplate(ITCard? card) =>
    PackageIdentity.IsPackage(card?.HiddenTags);
```

- [ ] **Step 4: Run the focused tests and verify they pass**

Run:

```bash
dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj --filter Package_identity
```

Expected: all three `Package_identity_*` tests pass.

### Task 2: Bind ItemVisuals Identity Before Native Setup

**Files:**
- Modify: `src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs`

- [ ] **Step 1: Move tracking from postfix to prefix**

Replace the current identity patch method:

```csharp
[HarmonyPostfix]
private static void Postfix(ItemVisualsController __instance, Card card)
{
    CardArtInjector.TrackCard(__instance, card);
}
```

with:

```csharp
[HarmonyPrefix]
private static void Prefix(ItemVisualsController __instance, Card card)
{
    CardArtInjector.TrackCard(__instance, card);
}
```

This makes the weak-table binding available before native `Setup(Card, BazaarCollectionLoadout)` reaches the `SetCardFrameMaterial(...)` call, regardless of whether the intervening awaits complete synchronously.

- [ ] **Step 2: Build the main plugin**

Run:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
```

Expected: build succeeds with no Harmony signature errors.

### Task 3: Run Focused And Full Verification

**Files:**
- No source edits in this task unless a previous step fails.

- [ ] **Step 1: Run card-art tests**

Run:

```bash
dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Run the repository test entrypoint**

Run:

```bash
./run.sh test
```

Expected: all test projects pass. If an unrelated executable test fails, keep the exact failure string and do not mark the plan complete.

- [ ] **Step 3: Build the mod**

Run:

```bash
./run.sh build
```

Expected: Debug build succeeds and copies the plugin into the detected BepInEx plugins folder.

- [ ] **Step 4: Launch through Steam for runtime validation**

Run on macOS:

```bash
open "steam://run/1617400"
```

Expected: The Bazaar starts through Steam. Do not launch `TheBazaar.app` directly.

- [ ] **Step 5: Validate visible behavior with the setting enabled**

Use this checklist in one game session:

- `Package Swap` / `掉包快递` is enabled in BazaarPlusPlus settings.
- CollectionPanel package tab still shows custom package art.
- A normal live package item visual that uses `ItemVisualsController` (board, shop, loot/choice screen) shows custom package art.
- Non-package items keep native art.
- Turning the setting off prevents replacement on newly loaded item visuals.
- Live hover/tooltip preview of a package (CardPreviewItem path) is expected to keep native art — out of scope, not a failure.

- [ ] **Step 6: Check BepInEx log**

Read the runtime log at the game directory's `BepInEx/LogOutput.log`.

Expected:

- No repeated `CardArtReplacement` warning spam.
- Startup still reports the custom card art catalog count.

### Task 4: Review Diff

**Files:**
- No source edits in this task.

- [ ] **Step 1: Run markdown placeholder scan**

Run:

```bash
pattern=$(printf '%s' 'TO''DO|TB''D|fill'' in|implement'' later|appro''priate')
rg -n "$pattern" docs/plans/package-card-art-live-game-replacement-fix.md
```

Expected: no matches.

- [ ] **Step 2: Review the diff**

Run:

```bash
git diff -- docs/plans/package-card-art-live-game-replacement-fix.md docs/README.md src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj tests/CardArtReplacement.Tests/CardArtReplacementTests.cs
```

Expected:

- No edits under `decompiled/`.
- No package-name or art-key substring fallback.
- No SettingsDock or config default changes.
- No unrelated formatting churn.

## Risks And Mitigations

- **Risk: Runtime `card.Template` is null on some live surfaces.**
  Mitigation: fall back to ready static data by `TemplateId` through `BppStaticDataAccess`.

- **Risk: Static data is not ready at the exact material setup moment.**
  Mitigation: prefer `card.Template` first. The game already attaches `Template` in `DTOUtils.CreateCard(...)` (`decompiled/TheBazaarRuntime/TheBazaar/DTOUtils.cs:64-70`), and `ItemVisualsController.Setup(Card, ...)` itself reads static data before material setup (`decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs:255-260`).

- **Risk: The static-data fallback throws (assembly absent in test host, or game-side drift).**
  Mitigation: the static-data step is wrapped in try/catch returning `false`; failure degrades to "not a package".

- **Risk: Broadening `CardPreviewItem` replacement affects store / collection skin UI.**
  Mitigation: do not broaden the marker-gated `CardPreviewItem` patch in this fix.

- **Risk: A live material shader ignores `mainTexture`.**
  Mitigation: `CardArtInjector.Apply(Material, Texture2D)` first tries the game's `_BaseMap` shader property (`CardArtShaderVariables.EncounterBaseMap`, `decompiled/TheBazaarRuntime/TheBazaar.Utilities.Shaders/CardArtShaderVariables.cs:47`) and then sets `material.mainTexture` (`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs:76-89`).

## Acceptance Criteria

- With `Package Swap` / `掉包快递` enabled, CollectionPanel package art remains replaced.
- With the same setting enabled, package cards in normal gameplay item visuals (board, shop, loot/choice screens, recap) are replaced.
- Non-package cards are not replaced, even when their name or art key contains package-like text.
- Live hover/tooltip previews through `CardPreviewItem` outside CollectionPanel intentionally keep native art (out of scope).
- The implementation uses only `EHiddenTag.Package` through `PackageIdentity`.
- `dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj` passes.
- `./run.sh test` passes or any unrelated failure is reported with exact output.
- `./run.sh build` succeeds.
- Runtime validation launches the game through Steam and checks `BepInEx/LogOutput.log`.
