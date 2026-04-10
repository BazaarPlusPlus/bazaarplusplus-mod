# History Panel Native Board Preview Design

Date: 2026-04-10

## Goal

Replace the History Panel battle preview rendering path with the game's native monster board view while keeping the existing preview region inside the History Panel UI.

The new design keeps the current product behavior that the History Panel shows a single-side board preview in its dedicated preview region, but it stops using BazaarPlusPlus's custom `PreviewBoardSurface` render-to-texture pipeline for that area.

## Non-Goals

- Reintroducing two-sided board preview inside History Panel
- Changing History Panel battle selection rules
- Reworking MonsterPreview hover or lock-toggle behavior
- Replacing the game's native tooltip system globally
- Adding new preview configuration switches

## Current State

History Panel currently renders preview content through a dedicated off-screen pipeline:

- `HistoryPanel` selects preview data and starts a render coroutine
- `HistoryPanelPreviewRenderer` creates two `PreviewBoardSurface` instances, a camera, lights, and a `RenderTexture`
- the UI Toolkit preview region displays that texture through an `Image`

That path uses the BazaarPlusPlus custom preview surface stack. It works, but it is not the intended long-term direction because MonsterPreview already has a native-game preview path and the custom board-creation path is planned for removal.

The game-native side already exists elsewhere in the mod:

- native monster tooltip augmentation
- native board-only formatting
- `ItemBoardOverlay`, which clones and hosts the game's native `MonsterBoardTooltip`

Those pieces prove that the game-native board can be cloned, hosted, shown, hidden, and refreshed outside the default tooltip flow.

## Desired Outcome

History Panel preview should:

- remain inside the existing preview region
- continue showing only one side of the battle preview
- use the game's native board view instead of the custom preview-surface board
- manage preview object lifetime safely across panel close, scene change, selection changes, and object invalidation

After the migration is validated, History Panel should no longer depend on `HistoryPanelPreviewRenderer`.

## Architecture

### 1. HistoryPanel stays the orchestration layer

`HistoryPanel` continues to own:

- selected battle / run state
- preview visibility decisions
- preview status text
- scene-change and panel-close cleanup timing

It no longer owns board rendering details.

### 2. Add a native preview host for History Panel

Add `HistoryPanelNativePreviewHost` as the rendering host for the preview region.

Responsibilities:

- lazily create a clone of the game's native `MonsterBoardTooltip` view
- attach that clone under a History Panel-owned preview host container
- render a single-side board into the native view
- hide, invalidate, and dispose the native view safely
- detect when cached Unity objects are no longer valid and rebuild when required

The host is explicitly lifecycle-aware and must not assume that a created object remains valid forever.

### 3. Add a preview data adapter

Add `HistoryPanelNativePreviewAdapter` to translate `HistoryBattlePreviewData` into the card/input model required by the native board view.

Responsibilities:

- accept the already-selected single-side preview data from `HistoryPanel`
- convert item cards into the native board's expected card input type
- preserve the current one-side-only behavior
- supply optional metadata when available
- return an explicit empty result when the current preview cannot be rendered natively

This adapter keeps History Panel data-shaping separate from Unity view hosting.

### 4. Replace the preview region internals, not the region itself

The History Panel preview region remains part of `HistoryPanelUiToolkitView`, but it stops being a texture presentation surface.

The region should instead expose a stable host anchor for the native preview host.

The preview region still owns:

- outer frame styling
- empty / loading / error status text
- debug text if still needed during migration

The rendered content inside that region becomes a native board clone managed by the host.

## Rendering Flow

1. User changes selected battle or run.
2. `HistoryPanel.BuildPreviewRequest()` continues deciding which single-side preview should be shown.
3. `HistoryPanelNativePreviewAdapter` translates that side's `HistoryBattlePreviewData` into native board input.
4. `HistoryPanelNativePreviewHost.EnsureCreated()` guarantees a valid native board clone is attached to the preview host region.
5. `HistoryPanelNativePreviewHost.Render(...)` refreshes the native board contents.
6. If no renderable native preview is available, the host hides the cloned board and History Panel shows the existing status text instead.

## Lifecycle Model

The host must support four distinct lifecycle operations.

### EnsureCreated

- create lazily on first use
- locate or derive a valid source `MonsterBoardTooltip` template
- clone the native view under the History Panel preview host container
- cache only weak runtime assumptions and always validate Unity object liveness before reuse

### Render

- reuse an existing live native board instance when possible
- clear previous board contents before applying the next selection
- update only the currently selected single-side board
- keep the host active only when content is renderable

### Hide

- hide native preview content without destroying the entire host immediately
- clear transient display state tied to the current selected battle
- leave the object reusable for the next render when the scene and host remain valid

### Invalidate / Dispose

`Invalidate` is used when the structure still exists conceptually but cached Unity references are no longer trustworthy.

Examples:

- scene changed
- preview host container was rebuilt
- cloned native view was destroyed externally
- source native tooltip/template is no longer valid

`Dispose` is used when the History Panel itself is being torn down or the preview host must fully release all objects.

Examples:

- `OnDestroy`
- permanent panel shutdown
- scene teardown path where reuse is not safe

## Failure and Invalidity Rules

The host must refuse to render and fall back to status text when:

- no native source view can be located
- the preview host container is unavailable
- the selected preview side has no renderable item cards
- native card conversion fails for the selected side
- a previously cached Unity object has become invalid and recreation also fails

In these cases, History Panel should not leave stale rendered content visible.

## UI Changes

`HistoryPanelUiToolkitView` should be updated so the preview region can expose a stable host surface for the native board clone.

Expected UI changes:

- keep the existing preview frame
- remove the `Image` dependency for preview rendering
- keep status text rendering for loading, empty, and failure states
- allow native content to be layered into the preview region

The user-visible layout should remain substantially the same.

## Migration Plan

### Phase 1: Introduce native host path

- add host container support to the preview region
- implement `HistoryPanelNativePreviewHost`
- implement `HistoryPanelNativePreviewAdapter`
- wire History Panel selection refresh to the native host

### Phase 2: Detach History Panel from render-to-texture preview

- stop updating preview texture from `HistoryPanelPreviewRenderer`
- remove History Panel's dependency on the custom preview renderer path
- keep existing status behavior intact

### Phase 3: Remove obsolete History Panel custom preview path

- delete `HistoryPanelPreviewRenderer`
- remove unused UI texture plumbing
- remove any History Panel-only code paths that exist only for the custom preview surface renderer

This phase does not automatically delete all custom preview-surface code across the repo. It only removes the History Panel dependency on that stack. Wider cleanup can follow once other consumers are migrated or removed.

## Testing and Verification

Verification should stay proportional and focused on the changed area.

Required verification:

- targeted build of the main mod project in Debug
- any targeted tests that still cover History Panel or related adapter logic
- manual in-game smoke validation of:
  - opening History Panel
  - selecting different battles
  - showing a single-side native board in the preview region
  - empty-state fallback when no renderable preview is available
  - closing the panel without leaving native preview remnants
  - switching scenes without stale references or orphaned objects

If the repository does not provide a meaningful automated seam for the native Unity view host, manual verification is acceptable.

## Risks

### Native view sourcing risk

The native board host depends on obtaining a valid source `MonsterBoardTooltip` view for cloning. If that source is absent in some scenes or timing windows, the host must fail cleanly and show status text instead of stale content.

### Mixed UI system risk

History Panel uses UI Toolkit while the native board view comes from the game's existing UI hierarchy. Bridging those systems can create positioning and lifetime edge cases. The design reduces risk by introducing a dedicated host instead of pushing that responsibility into `HistoryPanel` directly.

### Scene lifetime risk

Cached Unity references can become invalid during scene changes or tooltip system rebuilds. This is why `Invalidate` is a first-class operation rather than an incidental implementation detail.

## Open Questions Resolved

### Should History Panel use the same hover/lock interaction shell as MonsterPreview?

No. History Panel only reuses the native board view capability. It does not participate in hover, lock-toggle, or global click-to-close behavior.

### Should History Panel continue showing only one side?

Yes. The migration intentionally preserves the current one-side preview behavior.

### Should History Panel depend on the `UseNativeMonsterPreview` config switch?

No. History Panel preview should always use the native board path after this migration because that is the chosen long-term architecture.
