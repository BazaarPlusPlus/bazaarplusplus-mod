# Item Enchant Preview Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor item enchant preview into a feature-scoped module, eliminate live `ItemCard` mutation during preview rendering, and fix cache correctness while preserving current user-facing behavior.

**Architecture:** Replace the monolithic static builder with a small `Game/ItemEnchantPreview/` module composed of service, eligibility, formatting, cache, snapshot, and renderer classes. Keep the Harmony patch thin and route preview generation through a cloned or snapshot-backed render path so tooltip resolution does not mutate the live card instance.

**Tech Stack:** C#, .NET SDK, Harmony, Bazaar tooltip APIs, BepInEx

---

### Task 1: Create the feature module skeleton

**Files:**
- Create: `Game/ItemEnchantPreview/ItemEnchantPreviewService.cs`
- Create: `Game/ItemEnchantPreview/ItemEnchantPreviewEligibility.cs`
- Create: `Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs`
- Create: `Game/ItemEnchantPreview/ItemEnchantPreviewCache.cs`
- Create: `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewSnapshot.cs`
- Create: `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewSnapshotFactory.cs`
- Create: `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewRenderer.cs`
- Modify: `BazaarPlusPlus.csproj`

**Step 1: Write the failing test**

Add a small compile-focused test or temporary call site proving the new service API is expected by the patch layer. If no runnable test project exists, use the build as the first failing check.

**Step 2: Run test to verify it fails**

Run: `dotnet build`
Expected: FAIL until the new types and namespaces exist.

**Step 3: Write minimal implementation**

Create the new files with minimal class shells and method signatures:

- `ItemEnchantPreviewService.BuildPreviewSegments(...)`
- `ItemEnchantPreviewEligibility.IsEligible(...)`
- `ItemEnchantPreviewFormatting.GetEnchantmentLabel(...)`
- `ItemEnchantPreviewFormatting.GetEnchantmentColorHex(...)`
- `ItemEnchantPreviewFormatting.CreateSegment(...)`
- `ItemEnchantPreviewCache.TryGet(...)`
- `ItemEnchantPreviewCache.Save(...)`
- `ItemEnchantPreviewSnapshot`
- `ItemEnchantPreviewSnapshotFactory.Create(...)`
- `ItemEnchantPreviewRenderer.Render(...)`

**Step 4: Run test to verify it passes**

Run: `dotnet build`
Expected: PASS with the new module compiling.

**Step 5: Commit**

```bash
git add BazaarPlusPlus.csproj Game/ItemEnchantPreview
git commit -m "refactor: scaffold item enchant preview module"
```

### Task 2: Move eligibility and candidate selection into the service

**Files:**
- Modify: `Game/ItemEnchantPreview/ItemEnchantPreviewService.cs`
- Modify: `Game/ItemEnchantPreview/ItemEnchantPreviewEligibility.cs`
- Modify: `Game/ItemEnchantPreviewBuilder.cs`

**Step 1: Write the failing test**

Add coverage for these cases in the available test project, or isolate the logic into pure helper methods if the project currently lacks test infrastructure:

- null or non-item card returns no preview
- item outside hand/stash returns no preview
- combat state returns no preview
- duplicate available enchantments are deduped
- current enchant is excluded from preview output

**Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL on at least one eligibility or candidate-selection assertion, or be blocked by missing test scaffolding that must be added in the same task.

**Step 3: Write minimal implementation**

Implement eligibility and candidate selection in the new service. Keep the old builder forwarding to the new service temporarily so the patch call path stays stable during migration.

**Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS for the new eligibility and candidate-selection cases.

**Step 5: Commit**

```bash
git add Game/ItemEnchantPreview Game/ItemEnchantPreviewBuilder.cs
git commit -m "refactor: move item enchant preview gating into service"
```

### Task 3: Introduce snapshot creation for preview inputs

**Files:**
- Modify: `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewSnapshot.cs`
- Modify: `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewSnapshotFactory.cs`
- Modify: `Game/ItemEnchantPreview/ItemEnchantPreviewService.cs`

**Step 1: Write the failing test**

Add tests for snapshot creation:

- preview enchant is stored separately from current enchant
- preview attributes merge enchant template attributes over the item's current attributes
- unchanged attributes remain available in the snapshot

**Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL because snapshot creation has not yet encoded the merge rules.

**Step 3: Write minimal implementation**

Implement immutable snapshot creation from the live `ItemCard` and `TEnchantment`. Make the snapshot contain only render-relevant values and no mutable dictionaries shared with the card.

**Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS for snapshot merge behavior.

**Step 5: Commit**

```bash
git add Game/ItemEnchantPreview
git commit -m "refactor: add item enchant preview snapshots"
```

### Task 4: Replace live-card mutation with snapshot-backed rendering

**Files:**
- Modify: `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewRenderer.cs`
- Modify: `Game/ItemEnchantPreview/ItemEnchantPreviewService.cs`
- Modify: `Game/ItemEnchantPreviewBuilder.cs`

**Step 1: Write the failing test**

Add a regression test or focused harness proving rendering no longer mutates the source `ItemCard`:

- given an item with enchant and attributes, rendering a different preview enchant leaves the original `Enchantment` unchanged
- given an item with attributes, rendering leaves original attribute values untouched

**Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL because the old implementation still mutates the live card or the new renderer is not wired yet.

**Step 3: Write minimal implementation**

Implement rendering through a preview-only clone or other snapshot-backed adapter accepted by `TooltipBuilder`. Remove the mutation-and-restore pattern from the render path.

**Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS and no source-card mutation after preview rendering.

**Step 5: Commit**

```bash
git add Game/ItemEnchantPreview Game/ItemEnchantPreviewBuilder.cs
git commit -m "refactor: render item enchant previews without mutating cards"
```

### Task 5: Fix cache correctness and isolate cache policy

**Files:**
- Modify: `Game/ItemEnchantPreview/ItemEnchantPreviewCache.cs`
- Modify: `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewSnapshot.cs`
- Modify: `Game/ItemEnchantPreview/ItemEnchantPreviewService.cs`

**Step 1: Write the failing test**

Add cache-focused tests:

- identical snapshots hit the cache
- changing preview-relevant attributes misses the cache
- changing preview enchant misses the cache
- expired entries are not reused

**Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL because the cache key or expiration policy does not yet match the render inputs.

**Step 3: Write minimal implementation**

Move cache storage and key generation into `ItemEnchantPreviewCache`. Build the key from snapshot render inputs instead of only item instance metadata.

**Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS for cache hit, miss, and expiration behavior.

**Step 5: Commit**

```bash
git add Game/ItemEnchantPreview
git commit -m "fix: make item enchant preview cache input-aware"
```

### Task 6: Move formatting and localization fallbacks out of the builder

**Files:**
- Modify: `Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs`
- Modify: `Game/ItemEnchantPreview/Preview/ItemEnchantPreviewRenderer.cs`
- Modify: `Game/ItemEnchantPreview/ItemEnchantPreviewService.cs`

**Step 1: Write the failing test**

Add formatting tests:

- each segment includes enchant label and color
- blank rendered text produces no segment
- localization fallback returns raw enum or text when localized resolution fails

**Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: FAIL because formatting and fallback behavior are still embedded elsewhere.

**Step 3: Write minimal implementation**

Move label resolution, color mapping, and `TooltipSegment` creation into the formatting class. Keep renderer focused on text generation only.

**Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS for formatting and fallback cases.

**Step 5: Commit**

```bash
git add Game/ItemEnchantPreview
git commit -m "refactor: extract item enchant preview formatting"
```

### Task 7: Switch the patch to the new service and remove the old builder

**Files:**
- Modify: `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- Delete: `Game/ItemEnchantPreviewBuilder.cs`

**Step 1: Write the failing test**

Add a small integration-level check or build assertion verifying the patch now compiles against `ItemEnchantPreviewService` and the old builder is no longer referenced.

**Step 2: Run test to verify it fails**

Run: `dotnet build`
Expected: FAIL after removing the old builder reference until the patch is updated.

**Step 3: Write minimal implementation**

Update the patch to call `ItemEnchantPreviewService`. Remove the old builder file once all references are gone.

**Step 4: Run test to verify it passes**

Run: `dotnet build`
Expected: PASS with the patch using the new module directly.

**Step 5: Commit**

```bash
git add Patches/Tooltips/ItemEnchantPreviewPatch.cs Game/ItemEnchantPreview
git rm Game/ItemEnchantPreviewBuilder.cs
git commit -m "refactor: route item enchant preview through feature module"
```

### Task 8: Run regression verification

**Files:**
- Verify: `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- Verify: `Game/ItemEnchantPreview/`

**Step 1: Write the failing test**

No new test code in this step. Use build and the available automated suite as regression coverage.

**Step 2: Run test to verify it fails**

Not applicable unless the suite surfaces a regression.

**Step 3: Write minimal implementation**

Do not change code in this step unless verification finds a defect.

**Step 4: Run test to verify it passes**

Run: `dotnet build`
Expected: PASS

Run: `dotnet test`
Expected: PASS, or document if the repository still lacks a runnable test project for this feature.

Manual verification:

- Hover an item in hand with multiple enchant options and confirm preview lines render.
- Hover an item in stash and confirm preview lines render.
- Enter combat or inspect a non-eligible section and confirm no preview renders.
- Re-hover after changing enchant-relevant values and confirm text updates instead of staying stale.

**Step 5: Commit**

```bash
git add .
git commit -m "test: verify item enchant preview redesign"
```
