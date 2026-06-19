# Achievement Card Flicker And Tooltip Body Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Achievements cards render with stable native card materials (no strobe) and show both title and body text in the native tooltip.

**Confirmed premise (2026-06-19):** the achievement card **still flickers on real hardware after the already-merged F-1a safeguard** (premium-off + `ClearEnchantmentKeywords` on the bare material, commit `a532814c`). So the v1 keyword-only diagnosis was incomplete; this plan supersedes it with a grounded root cause and the clean clone-from-authored-material fix.

**Architecture:** Keep achievement definitions as BPP custom card descriptors. To give the art-replace postfix a *correct authored base material* to clone, synthesize the native template with a **donor `ArtKey`** (a real catalog item's key); the existing collection `LoadArt` prefix then loads that donor's authored `CardAssetDataSO`, clones its `cardMaterial` onto `instance._cardMaterial`, and refcounts the donor key on `marker.CurrentArtKey` exactly as it does for any native card. The achievement postfix then clones that authored base and swaps only `_BaseMap`, **reusing the same clone-from-base path the package-card feature already uses** (`CardArtReplacementFeature.TryGetPreviewMaterial` → `CustomCardArtMaterialCache`). The postfix does **not** touch `marker.CurrentArtKey` — the donor key stays prefix-owned and is released once on destroy. For the tooltip body, keep `Localization.Description` and also emit one passive `TTooltip`, because native item tooltips render passive tooltips, not `Description`.

**Tech Stack:** C# 12, netstandard2.1, Harmony patches, Unity `Material`/`Texture2D`, The Bazaar `TCardItem`/`TTooltip` DTOs, xUnit tests, in-game runtime verification.

---

## Already shipped (do not redo)

Merged in `a532814c` ("Fix achievement card flicker safeguards"):

- **F-1a** — bare custom material now disables premium + clears enchantment keywords: `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardMaterialCache.cs:70-72`. **Confirmed insufficient — still flickers.** This plan removes the bare path entirely.
- **F-2** — Achievements tab clears stale `Tiers` filter on entry: `CollectionFilterState.cs`.
- **F-3** — achievement catalog load wrapped in try/catch (`AchievementCardRegistrar`), so a bad catalog can't take down the whole plugin.
- **F-5** — custom-template build in `CollectionCardFactory.TryBind` is already try/catch-guarded.
- **F-6** — `tests/BppCustomCard.Tests` is leaf-include style (linked sources + `BazaarGameShared`), not a whole-plugin `ProjectReference`.

This plan implements **F-1b (clone authored material)** + the **tooltip body** fix, with the corrected design below.

---

## Root cause (grounded)

### Flicker

The native game produces a stable card material exactly one way: clone the **authored** `assetData.cardMaterial` and toggle *only* premium; for an unenchanted card it touches nothing else (`decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewItem.cs:59` and `:63-74`). The authored `.mat` is the source of truth for **every property default and the baked keyword set**, including `_ENCHANTMENTSTATUS_UNENCHANTED`, which **no script ever re-enables at runtime**.

The achievement path instead builds a **bare** `new Material(shader)` (`CollectionCardMaterialCache.cs:69`). A bare material starts with an **empty keyword set** and shader compile-time defaults — it never had the baked `_ENCHANTMENTSTATUS_UNENCHANTED` keyword. Worse, F-1a calls `ClearEnchantmentKeywords`, which at `decompiled/TheBazaarRuntime/TheBazaar.Utilities.Shaders/CardArtShaderVariables.cs:139` also `DisableKeyword(_ENCHANTMENTSTATUS_UNENCHANTED)`. So after F-1a the bare material runs an enchant-status variant the artist never shipped — the animated foil/shimmer overlay (`_Time`-driven, gated by that keyword) stays on → **persistent strobe**. F-1a didn't just fail to fix it; on a bare material it can be what *keeps* the animation alive.

Cloning `new Material(assetData.cardMaterial)` and swapping only `_BaseMap` reproduces the shipped, non-strobing material byte-for-byte except for the illustration — exactly what `CustomCardArtMaterialCache.cs:39-48` and the package-card path already do without flicker. **Do not** re-run `ClearEnchantmentKeywords` on the clone.

> The verdict from investigation is "likely, not certain": a strobe *could* instead be a per-frame rebind/`SetUp` loop, which no material change would fix. **Task 0 confirms the mechanism on-device before the rebuild** so this is not a third speculative patch.

### Tooltip body

Native tooltip rendering uses `GetCardDescription()` (which reads `Description`) only for `TCardEncounter`; every other card — including item cards — routes to `GetPassiveTooltipBlock()` (`decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipTypeHandler.cs:106-111`), which renders the `ETooltipType.Passive` entries in `Localization.Tooltips` (`decompiled/TheBazaarRuntime/TheBazaar.Tooltips/CardTooltipData.cs:569-629`). `GetLocalizedText()` falls back to `TLocalizableText.Text` when there's no localization key (`TooltipExtensions.cs:39-43`). A tier-less, unenchanted template filters cleanly to the full tooltip list (`CardTooltipData.cs:579-586`), so one passive tooltip renders. Achievement cards are `ECardType.Item` (`AchievementCardDescriptorMapper.cs:28-39`), so the body **must** be a passive tooltip; `Description` alone is never shown.

---

## Code Evidence

- Achievement descriptors are item cards: `src/BazaarPlusPlus/Game/Achievements/AchievementCardDescriptorMapper.cs:28-39`; size comes from `DisplaySize` at `:32`.
- Custom templates currently set `ArtKey = string.Empty` and write only `Localization.Description`: `src/BazaarPlusPlus/GameInterop/CustomCards/BppCustomCardTemplateFactory.cs:39-50`.
- Native authored-material clone + premium-only toggle: `decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewItem.cs:59` (clone) and `:63-74` (premium/enchant), `LoadArt` at `:80-101`.
- `ClearEnchantmentKeywords` also disables the unenchanted keyword: `decompiled/.../CardArtShaderVariables.cs:13` (`UnenchantedShaderKeyword`), `:139`.
- The collection `LoadArt` **prefix** already loads + refcounts art for any non-empty/valid `ArtKey`, and early-returns for empty/`Invalid`: `src/BazaarPlusPlus/Patches/CollectionPanel/CollectionItemLoadArtPatch.cs:50-104`. This prefix needs **no change** — a donor `ArtKey` flows through its normal valid-key path.
- The achievement **postfix** currently builds the bare material and does its own `marker.CurrentArtKey` dance: `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs:59-96`.
- Prior-art clone-from-base path (reuse this): `CardArtReplacementFeature.TryGetPreviewMaterial(Guid, Material, out Material?, out string?)` → `CustomCardArtMaterialCache.TryGetMaterial` (clone keyed by `templateId` + base material instance id): `src/BazaarPlusPlus/Game/CardArtReplacement/CardArtReplacementFeature.cs:97-118`, `src/BazaarPlusPlus/Game/CardArtReplacement/CustomCardArtMaterialCache.cs:22-64`.
- Bundled-art gate already used by the texture path: `BppCustomCardRegistry.HasBundledArt(Guid)` at `src/BazaarPlusPlus/GameInterop/CustomCards/BppCustomCardRegistry.cs:59`; texture lookup `PackageCardArtPatchGate.TryGetBppCustomCardTexture` at `src/BazaarPlusPlus/Patches/CardArtReplacement/PackageCardArtPatchGate.cs:53-64`.
- Destroy releases whatever `marker.CurrentArtKey` holds, from both caches, and nulls `_cardMaterial`: `src/BazaarPlusPlus/Patches/CollectionPanel/CollectionCardPreviewDestroyPatch.cs:30-37`.
- The codebase loads the full card map **off the main thread**: `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCatalog.cs:70` (`Task.Run(() => BppStaticDataAccess.LoadCardMap(...))`). `BppStaticDataAccess.LoadCardMap` returns `Dictionary<Guid, ITCard>?` and is safe off-thread: `src/BazaarPlusPlus/GameInterop/StaticCards/BppStaticDataAccess.cs:43-52`.
- `CollectionCardFactory.TryBind` checks the registry **before** any static-data / `ArtKey`-based classification, so an achievement is never classified by its donor key: `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs:60-99`. Achievement VMs hardcode `ArtKey = "bpp-custom"` independently: `BppCustomCardCollectionProjection.cs:28`.
- Classifier (used by the donor resolver to exclude bad/package donors): `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardClassifier.cs:19-59` (`CollectionCardEligibilityReason` lives in `CollectionCardClassification.cs`; `IsPackage` via `GameInterop/Cards/PackageIdentity.cs`).
- Existing custom card tests assert `ArtKey` empty + only verify `Description`: `tests/BppCustomCard.Tests/BppCustomCardTests.cs:110-129`.

---

## File Structure

- Modify `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardMaterialCache.cs`
  - Delete the bare-shader overload `GetOrCreate(string, Texture2D, Shader?)` (no caller remains after Task 3).
- Modify `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs`
  - Rewrite `TryApplyBppCustomCardMaterial` to clone `instance._cardMaterial` via `CardArtReplacementFeature.TryGetPreviewMaterial`, gated by `HasBundledArt`, with no `marker.CurrentArtKey` writes.
- Modify `src/BazaarPlusPlus/GameInterop/CustomCards/BppCustomCardTemplateFactory.cs`
  - Add `Build(descriptor, string? artKey)` overload; emit one `ETooltipType.Passive` tooltip alongside `Description`.
- Create `src/BazaarPlusPlus/Game/CollectionPanel/Grid/BppCustomCardMaterialDonorResolver.cs`
  - Off-main-thread, memoized donor `ArtKey` resolution by size (with any-size fallback), excluding package / non-catalog cards.
- Modify `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs`
  - Gate custom binding on `HasBundledArt`; require donor ready (else `NotReady`); thread the donor `ArtKey` into the template builder.
- Modify `tests/BppCustomCard.Tests/BppCustomCardTests.cs` and `tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj`
  - Tooltip + donor-key contract tests; link the resolver + classifier sources.

---

## Task 0: Confirm The Flicker Mechanism On-Device (temporary, main-path)

Per repo rule (no standalone probe scaffolding; add a temporary probe on the main path; the user builds + reloads to verify), prove the strobe is material-state before the rebuild.

**Files:**
- Temporarily modify: `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardMaterialCache.cs`
- Temporarily modify: `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs`

- [ ] **Step 1: Re-enable the unenchanted keyword on the bare material.** In the bare `GetOrCreate(string, Texture2D, Shader?)` overload, right after `ClearEnchantmentKeywords(ref material)`, add:

  ```csharp
  material.EnableKeyword(CardArtShaderVariables.UnenchantedShaderKeyword);
  ```

- [ ] **Step 2: Count postfix invocations on an idle card.** In `TryApplyBppCustomCardMaterial`, add a one-line `BppLog.Info` with a static counter so a single visible achievement card's per-second invocation rate is observable.

- [ ] **Step 3: Build + launch via Steam, open Achievements, observe.**

  ```bash
  ./run.sh build
  open "steam://run/1617400"
  ```

- [ ] **Step 4: Decide.**
  - **Strobe stops** with the keyword re-enabled, counter fires **once** → material-state confirmed (missing unenchanted variant). **Revert Step 1+2 and proceed to Task 1+** — ship the clean authored-material clone (handles this *and* any other property-default delta in one move).
  - **Strobe persists**, counter fires **once per frame** → it is a rebind/`SetUp` re-entry loop, **not** material state. **Stop this plan**; root-cause the re-realization in `CollectionGridVirtualizer` / the `LoadArt` postfix re-entry instead. The clone fix would not help.
  - **Strobe persists**, counter fires **once** → other authored property/texture defaults are missing; the full authored-material clone (Task 3) is required. Proceed.

- [ ] **Step 5: Revert all Task 0 edits** before implementing Task 1+ (the keyword line and the counter are diagnostics only).

---

## Task 1: Tooltip Body — Contract Test

**Files:**
- Modify: `tests/BppCustomCard.Tests/BppCustomCardTests.cs`

- [ ] **Step 1: Add the tooltip import** alongside the existing usings:

  ```csharp
  using BazaarGameShared.Domain.Tooltips;
  ```

- [ ] **Step 2: Extend `Template_factory_builds_native_item_template_for_tooltip_and_frame_setup`.** Keep the existing title/description asserts and add:

  ```csharp
  var passiveTooltip = Assert.Single(template.Localization.Tooltips);
  Assert.Equal(ETooltipType.Passive, passiveTooltip.TooltipType);
  Assert.Equal("Deal 9999+ damage in a single hit", passiveTooltip.Content.Text);
  ```

- [ ] **Step 3: Run and confirm RED.**

  ```bash
  dotnet test tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj --filter FullyQualifiedName~Template_factory_builds_native_item_template_for_tooltip_and_frame_setup
  ```

  Expected: fails because `Localization.Tooltips` is empty. If the filter can't apply, run the whole project; the failure is still on this test.

---

## Task 2: Emit Passive Tooltip Content

**Files:**
- Modify: `src/BazaarPlusPlus/GameInterop/CustomCards/BppCustomCardTemplateFactory.cs`

- [ ] **Step 1: Add imports.**

  ```csharp
  using System.Collections.Generic;
  using BazaarGameShared.Domain.Tooltips;
  ```

- [ ] **Step 2: Add a shared localization helper.** Replace the two inline `new TCardLocalization { ... }` blocks (one per `ApplySharedFields`) with a single call to:

  ```csharp
  private static TCardLocalization BuildLocalization(BppCustomCardDescriptor descriptor)
  {
      var title = BppCustomCardText.ResolveOrEnglish(descriptor.Title);
      var description = BppCustomCardText.ResolveOrEnglish(descriptor.Description);

      return new TCardLocalization
      {
          Title = new TLocalizableText { Text = title },
          Description = new TLocalizableText { Text = description },
          Tooltips = new List<TTooltip>
          {
              new()
              {
                  TooltipType = ETooltipType.Passive,
                  Content = new TLocalizableText { Text = description },
              },
          },
      };
  }
  ```

  In both `ApplySharedFields` overloads set `Localization = BuildLocalization(descriptor),`.

- [ ] **Step 3: Run and confirm GREEN.**

  ```bash
  dotnet test tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj --filter FullyQualifiedName~Template_factory_builds_native_item_template_for_tooltip_and_frame_setup
  ```

---

## Task 3: Donor Art Key On The Synthesized Template

**Files:**
- Modify: `tests/BppCustomCard.Tests/BppCustomCardTests.cs`
- Modify: `src/BazaarPlusPlus/GameInterop/CustomCards/BppCustomCardTemplateFactory.cs`

- [ ] **Step 1: Failing test for donor injection.** After the tooltip test add:

  ```csharp
  [Fact]
  public void Template_factory_uses_donor_art_key_when_provided()
  {
      var descriptor = Descriptor(new Guid("5351d91d-2b5c-5f44-8349-bbf334a9bbc5"));
      const string donorArtKey = "Addressables/CardArt/Donor.asset";

      var template = Assert.IsType<TCardItem>(
          BppCustomCardTemplateFactory.Build(descriptor, donorArtKey)
      );

      Assert.Equal(donorArtKey, template.ArtKey);
      var passiveTooltip = Assert.Single(template.Localization.Tooltips);
      Assert.Equal(ETooltipType.Passive, passiveTooltip.TooltipType);
  }
  ```

- [ ] **Step 2: Add the overload** (preserving the no-arg `Build`):

  ```csharp
  public static TCardBase Build(BppCustomCardDescriptor descriptor) => Build(descriptor, null);

  public static TCardBase Build(BppCustomCardDescriptor descriptor, string? artKey)
  {
      if (descriptor == null)
          throw new ArgumentNullException(nameof(descriptor));

      var resolvedArtKey = artKey ?? string.Empty;
      return descriptor.Type == ECardType.Skill
          ? ApplySharedFields(new TCardSkill { StartingTier = descriptor.StartingTier }, descriptor, resolvedArtKey)
          : ApplySharedFields(new TCardItem { Type = ECardType.Item, StartingTier = descriptor.StartingTier }, descriptor, resolvedArtKey);
  }
  ```

  Thread `string artKey` through both `ApplySharedFields` overloads and set `ArtKey = artKey,` (replacing `ArtKey = string.Empty`).

- [ ] **Step 3: Run tests — GREEN.**

  ```bash
  dotnet test tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj
  ```

---

## Task 4: Off-Main-Thread Donor Resolver

The resolver must **not** trigger a synchronous full-table card-map read on the bind path. It mirrors `CollectionCatalog.cs:70`: kick the scan onto a background `Task`, memoize per `(source, size)`, and return `null` until ready (the factory turns `null` into `NotReady`, which the virtualizer already retries — same pattern as static-data-not-ready).

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/Grid/BppCustomCardMaterialDonorResolver.cs`
- Modify: `tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj`

- [ ] **Step 1: Create the resolver.**

  ```csharp
  #nullable enable
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using BazaarGameShared.Domain.Cards;
  using BazaarGameShared.Domain.Cards.Item;
  using BazaarGameShared.Domain.Core.Types;
  using BazaarPlusPlus.Game.CollectionPanel.Data;
  using BazaarPlusPlus.GameInterop.StaticCards;

  namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

  // Resolves a deterministic donor ArtKey from native static data, by card size, so the
  // collection LoadArt prefix can materialize an *authored* base material for a synthetic
  // achievement card. The full-table card-map read runs on a worker thread (mirrors
  // CollectionCatalog.cs:70); Resolve returns null until the scan completes, which the
  // factory maps to NotReady (retried next frame).
  internal class BppCustomCardMaterialDonorResolver
  {
      private readonly object _gate = new();
      private object? _source;
      private readonly Dictionary<ECardSize, string?> _resolved = new();
      private readonly HashSet<ECardSize> _inFlight = new();

      public virtual string? Resolve(object? staticData, ECardSize size)
      {
          if (staticData == null)
              return null;

          lock (_gate)
          {
              if (!ReferenceEquals(_source, staticData))
              {
                  _source = staticData;
                  _resolved.Clear();
                  _inFlight.Clear();
              }
              if (_resolved.TryGetValue(size, out var done))
                  return done;
              if (!_inFlight.Add(size))
                  return null; // scan already running
          }

          var captured = staticData;
          _ = Task.Run(() =>
          {
              var key = ScanForDonor(captured, size);
              lock (_gate)
              {
                  if (ReferenceEquals(_source, captured))
                  {
                      _resolved[size] = key;
                      _inFlight.Remove(size);
                  }
              }
          });
          return null;
      }

      private static string? ScanForDonor(object source, ECardSize size)
      {
          var cardMap = BppStaticDataAccess.LoadCardMap(source);
          if (cardMap == null)
              return null;

          var items = cardMap
              .Values.OfType<TCardItem>()
              .Where(t =>
              {
                  var c = CollectionCardClassifier.Classify(t);
                  return c.IsCatalogCard && !c.IsPackage;
              })
              .ToList();

          string? Pick(IEnumerable<TCardItem> source2) =>
              source2.OrderBy(t => t.Id).Select(t => t.ArtKey).FirstOrDefault(k => !string.IsNullOrEmpty(k));

          // Prefer same size; fall back to any catalog item so a new size can never become
          // permanently unrenderable. The card-art material is keyed on ArtKey, not size.
          return Pick(items.Where(t => t.Size == size)) ?? Pick(items);
      }
  }
  ```

- [ ] **Step 2: Link sources into the test project.** Add near the existing collection-panel includes in `tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj`:

  ```xml
  <Compile Include="..\..\src\BazaarPlusPlus\Game\CollectionPanel\Data\CollectionCardClassifier.cs" Link="CollectionCardClassifier.cs" />
  <Compile Include="..\..\src\BazaarPlusPlus\Game\CollectionPanel\Data\CollectionCardClassification.cs" Link="CollectionCardClassification.cs" />
  <Compile Include="..\..\src\BazaarPlusPlus\Game\CollectionPanel\Grid\BppCustomCardMaterialDonorResolver.cs" Link="BppCustomCardMaterialDonorResolver.cs" />
  <Compile Include="..\..\src\BazaarPlusPlus\GameInterop\Cards\PackageIdentity.cs" Link="PackageIdentity.cs" />
  ```

- [ ] **Step 3: Compile** (catches namespace/using drift in the leaf-include csproj — historically the trap, see `project_test_harness_compile_include`).

  ```bash
  ./run.sh build
  dotnet test tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj
  ```

> The production resolver's threading is covered by in-game verification (Task 7). Unit tests inject a synchronous `TestDonorResolver` double (Task 5).

---

## Task 5: Bind Custom Cards With A Donor — Only When Art Is Bundled

**Files:**
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardFactory.cs`
- Modify: `tests/BppCustomCard.Tests/BppCustomCardTests.cs`

- [ ] **Step 1: Fields.** Replace the builder field type and add the resolver:

  ```csharp
  private readonly Func<BppCustomCardDescriptor, string?, TCardBase> _customTemplateBuilder;
  private readonly BppCustomCardMaterialDonorResolver _customMaterialDonorResolver;
  ```

- [ ] **Step 2: Constructors.** Public ctor passes `BppCustomCardTemplateFactory.Build` (resolves to the 2-arg overload) + `new BppCustomCardMaterialDonorResolver()`. Internal ctor gains:

  ```csharp
  Func<BppCustomCardDescriptor, string?, TCardBase>? customTemplateBuilder = null,
  BppCustomCardMaterialDonorResolver? customMaterialDonorResolver = null
  ```

  with `_customTemplateBuilder = customTemplateBuilder ?? BppCustomCardTemplateFactory.Build;` and `_customMaterialDonorResolver = customMaterialDonorResolver ?? new BppCustomCardMaterialDonorResolver();`.

- [ ] **Step 3: Custom branch in `TryBind`.** Replace the existing custom branch (lines 65-82) with:

  ```csharp
  if (BppCustomCardRegistry.Current?.TryGet(vm.Id, out var descriptor) == true)
  {
      // Achievements are display-only; without bundled art there is nothing to draw and a
      // donor key would leak the donor card's illustration. Require bundled art.
      if (BppCustomCardRegistry.Current?.HasBundledArt(vm.Id) != true)
      {
          BppLog.Warn("CollectionCardFactory", $"Custom card {vm.Id} ({vm.InternalName}) has no bundled art; skipping.");
          return CollectionCardBindResult.HardMiss();
      }

      var staticData = _staticDataProvider();
      if (staticData == null)
          return CollectionCardBindResult.NotReady();

      var donorArtKey = _customMaterialDonorResolver.Resolve(staticData, descriptor!.Size);
      if (string.IsNullOrEmpty(donorArtKey))
          return CollectionCardBindResult.NotReady(); // donor scan in flight; retried next frame

      TCardBase customTemplate;
      try
      {
          customTemplate = _customTemplateBuilder(descriptor, donorArtKey);
      }
      catch (Exception ex)
      {
          BppLog.Warn("CollectionCardFactory", $"Custom template build failed for id={vm.Id} ({vm.InternalName}): {ex.Message}");
          return CollectionCardBindResult.HardMiss();
      }

      return Bind(vm, customTemplate);
  }
  ```

  > A `null` donor returns `NotReady` (not `HardMiss`): the scan is asynchronous, so the card simply waits a frame. With the any-size fallback in the resolver, `null` after the scan completes means the card map is genuinely empty, which only happens before static data is ready.

- [ ] **Step 4: Test double + updated throw test.** Add a synchronous resolver double and update `Card_factory_hard_misses_when_custom_template_builder_throws` to supply ready static data, a non-throwing donor, and the 2-arg builder signature:

  ```csharp
  private sealed class TestDonorResolver : BppCustomCardMaterialDonorResolver
  {
      private readonly string? _artKey;
      public TestDonorResolver(string? artKey) => _artKey = artKey;
      public override string? Resolve(object? staticData, ECardSize size) => _artKey;
  }
  ```

  ```csharp
  staticDataProvider: () => new object(),
  templateResolver: (_, _) => throw new InvalidOperationException("not called"),
  customTemplateBuilder: (_, _) => throw new InvalidOperationException("bad custom"),
  customMaterialDonorResolver: new TestDonorResolver("Addressables/CardArt/Donor.asset")
  ```

  > The registry double in that test must report `HasBundledArt == true` for the descriptor (use the registry's real `Register` with a bundled-art descriptor, matching the existing test setup).

- [ ] **Step 5: Add a NotReady test** for the donor-not-yet-resolved case:

  ```csharp
  [Fact]
  public void Card_factory_reports_not_ready_until_donor_art_resolves()
  {
      var descriptor = Descriptor(Guid.NewGuid()); // must have bundled art
      var registry = new BppCustomCardRegistry(_ => false);
      registry.Register(descriptor);
      BppCustomCardRegistry.Current = registry;
      var vm = new CollectionCardVm
      {
          Id = descriptor.Id, Type = ECardType.Item, Size = ECardSize.Medium,
          StartingTier = ETier.Bronze, InternalName = "CustomCardWaitingForDonor",
      };
      var factory = new CollectionCardFactory(
          null!, null!,
          staticDataProvider: () => new object(),
          templateResolver: (_, _) => throw new InvalidOperationException("not called"),
          customTemplateBuilder: (_, _) => throw new InvalidOperationException("not called"),
          customMaterialDonorResolver: new TestDonorResolver(null)
      );

      var result = factory.TryBind(vm);

      Assert.Equal(CollectionCardBindStatus.NotReady, result.Status);
      Assert.Null(result.Binding);
  }
  ```

- [ ] **Step 6: Run tests — GREEN.**

  ```bash
  dotnet test tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj
  ```

---

## Task 6: Clone Via The Existing Package Path; Delete The Bare Path

This is the core fix. The achievement postfix now clones the **authored donor base material** (`instance._cardMaterial`, set by the unchanged `LoadArt` prefix) using the **same** clone-from-base cache the package feature uses. It writes **no** `marker.CurrentArtKey` — the donor key is prefix-owned and released once on destroy.

**Files:**
- Modify: `src/BazaarPlusPlus/Patches/CardArtReplacement/CardPreviewItemArtReplacePatch.cs`
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/Grid/CollectionCardMaterialCache.cs`

- [ ] **Step 1: Add a custom-card gate** in `PackageCardArtPatchGate` that mirrors the package preview-material gate but keys on bundled art:

  ```csharp
  public static bool TryGetBppCustomCardPreviewMaterial(
      TCardBase? template,
      Material? baseMaterial,
      out Material? material
  )
  {
      material = null;
      if (template == null || baseMaterial == null)
          return false;
      if (BppCustomCardRegistry.Current?.HasBundledArt(template.Id) != true)
          return false;

      var feature = CardArtReplacementFeature.Current;
      return feature != null
          && feature.TryGetPreviewMaterial(template.Id, baseMaterial, out material, out _)
          && material != null;
  }
  ```

- [ ] **Step 2: Rewrite `TryApplyBppCustomCardMaterial`.** Replace the entire bare-material + `marker` dance body (`CardPreviewItemArtReplacePatch.cs:59-96`) with:

  ```csharp
  private static bool TryApplyBppCustomCardMaterial(
      CardPreviewItem instance,
      CollectionPanelOwnedMarker marker
  )
  {
      // Base material is the authored donor material the LoadArt prefix already cloned onto
      // _cardMaterial (the donor ArtKey lives on the synthetic template). Clone it and swap
      // _BaseMap to the achievement art via the shared package clone cache. Do NOT touch
      // marker.CurrentArtKey: it holds the donor key, owned and released by the prefix /
      // destroy patch. Do NOT clear enchantment keywords here — that is exactly what made
      // the bare path strobe.
      if (instance._cardMaterial == null || instance._cardImage == null)
          return false;

      if (
          !PackageCardArtPatchGate.TryGetBppCustomCardPreviewMaterial(
              instance._cardData,
              instance._cardMaterial,
              out var material
          )
          || material == null
      )
          return false;

      instance._cardMaterial = material;
      instance._cardImage.material = material;
      return true;
  }
  ```

  `ReleaseCurrentArtKey` becomes unused in this file — remove it.

- [ ] **Step 3: Delete the bare-shader overload** `GetOrCreate(string artKey, Texture2D texture, Shader? shader)` from `CollectionCardMaterialCache.cs` (and remove the now-unused `using TheBazaar.Utilities.Shaders;` if the compiler flags it). Keep the `CardAssetDataSO` overload — the prefix still uses it to build the donor base.

- [ ] **Step 4: Verify no bare custom-card path remains.**

  ```bash
  rg -n "GetOrCreate\([^)]*Texture2D[^)]*Shader|_cardMaterialShader" src/BazaarPlusPlus/Game/CollectionPanel/Grid src/BazaarPlusPlus/Patches/CardArtReplacement
  ```

  Expected: no bare `GetOrCreate(string, Texture2D, Shader?)` definition or call; `_cardMaterialShader` no longer referenced by the achievement postfix.

---

## Task 7: Refcount Invariant + Build + In-Game Verification

- [ ] **Step 1: Document the donor-key invariant.** Add a short comment in `CardPreviewItemArtReplacePatch.cs` stating: *the achievement postfix never writes `marker.CurrentArtKey`; the marker holds the donor key, acquired once by `CollectionItemLoadArtPatch` and released once by `CollectionCardPreviewDestroyPatch`. The cloned achievement material is owned by `CustomCardArtMaterialCache` (feature-lifecycle disposed), not the collection LRU, so it can never be evicted while assigned to a live card.*

- [ ] **Step 2: Run the focused test project.**

  ```bash
  dotnet test tests/BppCustomCard.Tests/BppCustomCard.Tests.csproj
  ```

- [ ] **Step 3: Build the mod.**

  ```bash
  ./run.sh build
  ```

- [ ] **Step 4: Launch via Steam only.**

  ```bash
  open "steam://run/1617400"
  ```

- [ ] **Step 5: Validate Achievements in-game.** Open the collection panel → Achievements, and verify:
  - The card does **not** strobe/flicker while idle (the primary acceptance gate — see Task 0).
  - Art is visible, clipped correctly in the frame, with **no donor-art flash** during load.
  - Hover tooltip shows **both** the title and the body text.
  - No stray item chrome in the tooltip from the tier-less template — i.e. **no phantom sell price ("0g") / cooldown / ammo block**. If any appears, that is a follow-up (suppress the item-effect renderers for custom cards or supply minimal tier/attribute data); record it, don't block the flicker/tooltip fix.

- [ ] **Step 6: Check the BepInEx log.**

  ```bash
  tail -n 200 "$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log"
  ```

  Expected: no repeated `CollectionCardFactory` donor/skip warnings once the panel settles, no repeated `CardArtReplacement` postfix warnings, no `Addressables` errors for the donor key.

---

## Self-Review

- **Root cause is grounded, not asserted.** F-1a's `ClearEnchantmentKeywords` disables `_ENCHANTMENTSTATUS_UNENCHANTED` (`CardArtShaderVariables.cs:139`) on a bare material that never had the baked keyword, leaving the animated variant on. Task 0 confirms this on-device before the rebuild and has an explicit off-ramp if the strobe is a rebind loop instead.
- **Both red-team blockers are designed out.** The achievement postfix writes no `marker.CurrentArtKey`; the donor key has a single owner (prefix acquire → destroy release). The cloned material lives in `CustomCardArtMaterialCache` (no LRU), so it can't be evicted while live. No cross-namespace refcount dance.
- **Perf trap removed.** The donor full-table scan runs on a worker thread (mirrors `CollectionCatalog.cs:70`); the bind path is non-blocking and returns `NotReady` until the memoized result lands.
- **Donor edge cases handled.** Binding requires `HasBundledArt` (no art-less achievement renders the donor's illustration); the resolver falls back to any catalog item if no same-size donor exists, so no size is permanently unrenderable.
- **Prior art reused, not re-rolled.** Achievement and package cards now share one clone-from-base path (`CardArtReplacementFeature.TryGetPreviewMaterial`); the bespoke bare-shader overload is deleted.
- **ArtKey leak audited.** The donor key lives only on the bind-time synthetic template; no projection/classification/source-attribution/upload/storage path reads it (registry-first bind confirmed at `CollectionCardFactory.cs:60-99`; achievement VM ArtKey hardcoded `"bpp-custom"` at `BppCustomCardCollectionProjection.cs:28`).
- **Tooltip is grounded.** Items render passive tooltips (`CardTooltipTypeHandler.cs:106-111`); `GetLocalizedText` falls back to `.Text` (`TooltipExtensions.cs:39-43`); tier-less template filters cleanly (`CardTooltipData.cs:579-586`).
- **Type ordering is consistent.** `Build(descriptor, artKey)` exists before `CollectionCardFactory` calls it; the resolver exists before tests/factory reference it; the package gate method exists before the postfix calls it.
- **Known follow-up (non-blocking):** phantom item-tooltip chrome on a tier-less template (Task 7 Step 5) — verify in-game, file separately if present.
