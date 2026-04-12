# Preview Surface Implementation Summary

Date: 2026-04-08
Commit: `57a4412`
Related spec: `docs/superpowers/specs/2026-04-08-preview-surface-design.md`
Related plan: `docs/superpowers/plans/2026-04-08-preview-surface.md`

## Scope

This change extracts the preview rendering stack into a neutral `Game/PreviewSurface` area and rewires `MonsterPreview` and `HistoryPanel` to consume it through render-target boundaries instead of directly owning card-object creation details.

The first version intentionally keeps the current `GameObject` lifecycle model. It does not introduce a handle abstraction.

## What Changed

### 1. Neutral preview models and interfaces

Added neutral preview models and surface interfaces under `Game/PreviewSurface`:

- `Models/PreviewBoardModel.cs`
- `Models/PreviewCardSpec.cs`
- `Models/PreviewBoardPresentation.cs`
- `Board/IPreviewBoardSurface.cs`
- `Cards/IPreviewCardSurface.cs`

These types replace the old monster-specific placement of the same concepts and give both `MonsterPreview` and `HistoryPanel` a shared surface contract.

### 2. Card-object creation moved behind preview card surfaces

Added:

- `Cards/PreviewItemCardSurface.cs`
- `Cards/PreviewSkillCardSurface.cs`

These classes now own the concrete item/skill preview object creation details, including:

- static data lookup
- preview card model construction
- asset-loader card instantiation
- controller refresh/setup
- pooling or destroy behavior
- showcase marker attachment

`MonsterPreview` no longer directly references these object-creation details.

### 3. Board rendering moved into preview board surface + host

Added:

- `Board/PreviewBoardSurface.cs`
- `Board/PreviewBoardRenderTarget.cs`
- `Board/PreviewBoardRenderTargetFactory.cs`

`PreviewBoardSurface` remains responsible for board visuals, slot layout, card placement, skill placement, and clear/rebuild behavior.

`PreviewBoardRenderTarget` is the host layer that applies render requests to a shared surface. `RenderAsync` now uses `CancellationToken` with cancel-and-replace semantics.

### 4. Cancel-and-replace made safe for shared surfaces

The initial token-based host still allowed a race where a cancelled older render could clear a newer replacement render.

The final implementation serializes all surface work inside `PreviewBoardRenderTarget`:

- render requests are queued
- visibility changes are queued
- cancelling an active render only invalidates that render
- old work no longer runs concurrently against the same surface

This preserves cancel-and-replace semantics without allowing late `Clear()` calls from cancelled work to wipe newer content.

### 5. Runtime consumers rewired to the new boundary

At the time of this refactor, both the monster showcase flow and `HistoryPanel` consumed the shared render-target boundary.

Today, the monster self-render showcase path has been removed, while `HistoryPanelPreviewRenderer` still uses `PreviewBoardSurface` plus `PreviewBoardRenderTarget`. The shared host/cancellation model remains the active architecture for `HistoryPanel`.

### 6. Later cleanup

The old monster-specific showcase runtime, controller, projector, and request/session glue were deleted in a later cleanup once the runtime had fully converged on native monster preview plus shared `PreviewSurface` consumers.

## Verification

The change was verified with targeted checks proportional to the work:

- `dotnet run --project tests/PreviewSurfaceHost.Tests/PreviewSurfaceHost.Tests.csproj`
- `dotnet run --project tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj`
- `dotnet build BazaarPlusPlus.csproj -c Debug`
- `git diff --check`
- `rg -n "new (PreviewItemCardSurface|PreviewSkillCardSurface|PreviewBoardSurface|ItemController|SkillController)" Game/MonsterPreview`

Observed results:

- preview-surface host tests passed
- monster-preview resilience tests passed
- main project Debug build passed with `0` warnings and `0` errors
- no whitespace errors in diff
- no direct card-object creation references remained under `Game/MonsterPreview`

## Tests Added or Updated

### `tests/PreviewSurfaceHost.Tests`

Added a focused host-level test project for the new render target behavior. It covers:

- newer render requests cancelling older ones
- hide cancelling in-flight work and clearing the surface
- cancelled older renders not clearing a replacement render later

### `tests/MonsterPreviewResilience.Tests`

Removed the obsolete `PreviewRenderGenerationGate` test coverage because that mechanism is no longer on the runtime path after the move to `CancellationToken`-based render control.

The remaining tests still validate meaningful non-rendering behavior around preview-card filtering.

## Constraints Preserved

- No files under `decompiled/` were modified.
- The first version keeps the existing `GameObject` lifecycle model.
- No handle abstraction was introduced.
- The existing build-and-copy behavior in `BazaarPlusPlus.csproj` was preserved.
- Validation stayed targeted instead of falling back to whole-repo coverage work.

## Residual Risks

- `PreviewBoardSurface` still uses full `Clear + Rebuild` rendering rather than incremental diffing. This is an intentional first-version tradeoff.
- Automated verification covers host-level sequencing and cancellation semantics, but not real in-game Unity interaction timing.
- A game-side smoke check is still useful for hover, show/hide, and `HistoryPanel` capture timing under actual runtime conditions.
