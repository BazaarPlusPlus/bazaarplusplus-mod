# Monster Preview Unknown Monster Analysis (2026-03-14)

## Summary

The reported symptom is:

- after encountering a monster the local client has effectively not seen before, the Monster Preview stops showing content
- later previews for otherwise normal monsters can also become blank

Based on the current implementation, this does **not** look like the "unknown monster" path directly crashes the preview feature. The more likely behavior is:

1. the preview pipeline accepts a monster preview model
2. card creation later fails because one or more referenced card templates do not exist in local static data
3. the board is cleared before rebuild
4. an async rebuild race allows an empty or stale rebuild to overwrite later valid previews

So the most likely observed outcome is "preview shell opens but content is blank, and later normal monsters can also become blank", not a hard crash.

## What Happens Today

### 1. Encounter tracking is defensive when local DB data is missing

`EncounterTracker.BuildMonsterPreviews(...)` records a preview entry for combat encounters, but if `MonsterDatabase.TryGetByEncounterId(...)` misses, it stores `BoardCards` and `Skills` as `null` instead of throwing.

Relevant code:

- [EncounterTracker.cs](/C:/Users/cauyx/Desktop/codes/bazaarplannermod/Game/EncounterTracker.cs)

Implication:

- a monster absent from the local monster DB does not obviously crash at tracking time
- the cached preview entry is simply missing local board data

### 2. The lock-toggle flow refuses to open preview when no preview data can be built

`MonsterLockShowcaseRuntime.TryBuildPreview(...)` first tries the monster DB, then falls back to `ModState.EncounterMonsterPreviews`. If both paths produce no cards and no skills, it returns `false` and the preview should not open.

Relevant code:

- [MonsterLockShowcaseRuntime.cs](/C:/Users/cauyx/Desktop/codes/bazaarplannermod/Game/MonsterPreview/MonsterLockShowcaseRuntime.cs)
- [EncounterPreviewSpecConverter.cs](/C:/Users/cauyx/Desktop/codes/bazaarplannermod/Game/EncounterPreviewSpecConverter.cs)

Implication:

- a truly unknown monster with no local preview data should behave like "no preview available"
- this path alone does not explain "preview opens but everything is blank forever after"

### 3. Card creation can silently fail after preview acceptance

Once a `PreviewBoardModel` has already been accepted, individual cards are built much later by:

- `MonsterPreviewItemCardFactory.BuildCard(...)`
- `MonsterPreviewSkillCardFactory.BuildCard(...)`

Both factories try to resolve the referenced `TemplateId` from local static card data. If the template cannot be found, they return `null` instead of throwing.

Relevant code:

- [MonsterPreviewItemCardFactory.cs](/C:/Users/cauyx/Desktop/codes/bazaarplannermod/Game/MonsterPreview/GameObjectFactory/MonsterPreviewItemCardFactory.cs)
- [MonsterPreviewSkillCardFactory.cs](/C:/Users/cauyx/Desktop/codes/bazaarplannermod/Game/MonsterPreview/GameObjectFactory/MonsterPreviewSkillCardFactory.cs)

Implication:

- a monster may exist in the monster DB
- but its board or skills may reference templates not present in the client build or local static data
- in that case the preview opens, but card instantiation yields no visible cards

This is a much better match for the reported symptom than a direct crash.

### 4. The board is cleared before each rebuild

`MonsterPreviewBoard.RebuildAsync(...)` calls `Clear()` before recreating cards.

Relevant code:

- [MonsterPreviewBoard.cs](/C:/Users/cauyx/Desktop/codes/bazaarplannermod/Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs)

Implication:

- if a rebuild is triggered for a monster whose cards all fail to instantiate, the board ends up empty
- the preview surface still exists, but the contents are gone

### 5. Async rebuilds are currently not cancellable

`MonsterPreviewBoardRenderTarget.Render(...)` starts `RebuildAsync(...)` with `() => false` as the cancellation callback.

Relevant code:

- [MonsterPreviewBoardRenderTarget.cs](/C:/Users/cauyx/Desktop/codes/bazaarplannermod/Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs)

Implication:

- older rebuild tasks never expire
- an earlier empty rebuild can finish after a later valid preview request
- this can overwrite or clear content for later normal monsters

This is the strongest explanation for the "after that, even ordinary monsters show nothing" behavior.

## Most Likely Root Cause

The most likely sequence is:

1. a monster preview is considered available because the monster DB has an entry
2. that entry references one or more card or skill templates missing from the local client data
3. the preview board clears itself and rebuilds into an empty result
4. because rebuilds are not cancelled, this empty rebuild can race with later valid rebuilds
5. normal monsters shown afterward can inherit the blank outcome

## What Is Less Likely

These are less likely based on the current code:

- a direct null-reference crash caused solely by a monster missing from `MonsterDatabase`
- persistent corruption of `ModState.EncounterMonsterPreviews` from the missing-monster path alone
- `EncounterPreviewSpecConverter` causing the blank state, because it already handles `null` by returning empty lists

## Proposed Solution

The fix should happen in two layers.

### Solution A: Refuse to open a preview that cannot build at least one renderable card

Before calling `ShowRequest(...)`, validate that the candidate preview contains at least one card or skill that can be resolved against local static data.

Recommended behavior:

- if a monster preview model exists but every referenced `TemplateId` is missing from local static data, do not open the preview
- log which encounter and which template IDs failed resolution
- treat this case as "preview unavailable on this client version"

Implementation options:

- add a lightweight validation helper near `MonsterLockShowcaseRuntime.TryBuildPreview(...)`
- or add a validation pass in the factories/data source layer that filters out unrenderable entries before the preview request is shown

Expected result:

- unknown or partially incompatible monsters no longer open an empty shell preview
- the user sees "no preview" instead of a blank board

### Solution B: Make render target rebuilds cancellable

Add a render generation/version counter to `MonsterPreviewBoardRenderTarget`.

Recommended behavior:

1. increment a generation number on each `Render(...)`
2. capture the generation for the current rebuild
3. pass `isCancelled` that returns `true` when:
   - a newer generation exists
   - the board has been hidden
   - the render target has been disposed
4. also increment generation in `SetVisible(false)` so all older rebuilds expire immediately

Expected result:

- stale empty rebuilds can no longer overwrite later valid previews
- toggling between monsters becomes deterministic
- blank-state leakage from one problematic monster to later normal monsters is eliminated

## Recommended Order

The safest rollout order is:

1. implement Solution B first
2. implement Solution A second
3. add focused regression coverage for both cases

Reason:

- Solution B fixes the cross-preview contamination risk
- Solution A improves user-facing behavior for unsupported monsters

## Suggested Tests

### Test 1: Unsupported monster should not open blank preview

- create a preview model whose cards all reference template IDs absent from local static data
- assert that the preview request is rejected, or that no visible render is triggered

### Test 2: Empty rebuild must not overwrite later valid preview

- start rebuild A with invalid or empty content
- before A finishes, start rebuild B with valid content
- assert that the final board state matches B, not A

### Test 3: Hiding preview cancels prior rebuild

- start a rebuild
- call `SetVisible(false)`
- assert that the old rebuild does not repopulate the board after hide

## Recommended Logging Improvements

To confirm the issue in live logs, add structured logs for:

- encounter ID / encounter internal name
- count of preview item specs and skill specs
- count of specs filtered out as unrenderable
- template IDs that failed local static-data resolution
- rebuild generation number at render start and completion
- cancellation reason when a rebuild exits early

## Conclusion

The current evidence points to a compatibility-plus-race-condition problem, not a simple unknown-monster crash:

- missing local preview data usually degrades to "no preview"
- incompatible card template references can produce a blank preview surface
- uncancelled async rebuilds can then spread that blank result to later normal monsters

The recommended fix is to:

1. stop opening previews that contain no locally renderable content
2. add cancellation/generation control to async board rebuilds

That combination should directly address the user-reported "after one unseen monster, all preview content disappears" behavior.
