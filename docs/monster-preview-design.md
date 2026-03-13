# Monster Preview

## Goal

This document records the current Monster Preview runtime path and the file boundaries that still matter for maintenance.
It intentionally omits superseded design iterations and one-off implementation plans.

## Runtime Entry

The feature is mounted from [Plugin.cs](../Plugin.cs):

- `MonsterPreviewController`
- `MonsterLockShowcaseRuntime`
- `MonsterPreviewDebugController` in debug builds only

The production trigger path is:

```text
CardTooltipController.LockTooltipToggle()
  -> ShowcaseTooltipPatches
  -> MonsterLockShowcaseRuntime
  -> MonsterPreviewController
  -> MonsterPreviewOverlayCoordinator
  -> PreviewBoardSession
  -> MonsterPreviewBoardRenderTarget
  -> MonsterPreviewBoard
```

Key files:

- [Patches/Showcase/ShowcaseTooltipPatches.cs](../Patches/Showcase/ShowcaseTooltipPatches.cs)
- [Game/MonsterPreview/MonsterLockShowcaseRuntime.cs](../Game/MonsterPreview/MonsterLockShowcaseRuntime.cs)
- [Game/MonsterPreview/MonsterPreviewController.cs](../Game/MonsterPreview/MonsterPreviewController.cs)
- [Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs](../Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs)
- [Game/MonsterPreview/Architecture/PreviewBoardSession.cs](../Game/MonsterPreview/Architecture/PreviewBoardSession.cs)
- [Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs](../Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs)
- [Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs](../Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs)

## Responsibilities

### `MonsterLockShowcaseRuntime`

- decides whether the current lock toggle should open BazaarPlusPlus preview
- builds preview data from `MonsterDatabase` or cached encounter state
- creates a `PreviewBoardRequest`
- hides the preview on repeated toggle

### `MonsterPreviewController`

- owns the Unity `MonoBehaviour` lifecycle
- creates the render target and coordinator in `Awake()`
- forwards show/hide/update commands
- ticks the coordinator from `LateUpdate()`

### `MonsterPreviewOverlayCoordinator`

- stores the active external request
- assembles internal requests for debug usage when needed
- forwards the resolved request to `PreviewBoardSession`

### `PreviewBoardSession`

- resolves model, pose, and presentation from the request
- computes signatures for render invalidation
- decides when a new render is required
- keeps target visibility in sync with the active request

### `MonsterPreviewBoardRenderTarget`

- ensures the `MonsterPreviewBoard` exists
- applies visibility and pose
- forwards render models to the board rebuild path

### `MonsterPreviewBoard`

- owns the world-space board root
- rebuilds item and skill card objects
- applies layout, markers, and cleanup for the rendered board

## Core Models

Primary architecture types:

- [Game/MonsterPreview/Architecture/PreviewBoardRequest.cs](../Game/MonsterPreview/Architecture/PreviewBoardRequest.cs)
- [Game/MonsterPreview/Architecture/PreviewBoardModel.cs](../Game/MonsterPreview/Architecture/PreviewBoardModel.cs)
- [Game/MonsterPreview/Architecture/PreviewCardSpec.cs](../Game/MonsterPreview/Architecture/PreviewCardSpec.cs)
- [Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs](../Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs)
- [Game/MonsterPreview/Architecture/BoardPose.cs](../Game/MonsterPreview/Architecture/BoardPose.cs)
- [Game/MonsterPreview/Architecture/InMemoryPreviewDataSource.cs](../Game/MonsterPreview/Architecture/InMemoryPreviewDataSource.cs)
- [Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs](../Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs)
- [Game/MonsterPreview/Anchor/FixedAnchorStrategy.cs](../Game/MonsterPreview/Anchor/FixedAnchorStrategy.cs)

## Debug Scope

Debug-only entry points live under:

- [Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs](../Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs)
- [Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs](../Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs)

They are mounted only when `ModState.IsDebug` is true in [Plugin.cs](../Plugin.cs).

## Related References

- [docs/monster-preview-investigation-2026-03-13.md](./monster-preview-investigation-2026-03-13.md)
- [docs/2026-03-13-skillcard-artkey-investigation.md](./2026-03-13-skillcard-artkey-investigation.md)
