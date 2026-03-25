# Monster Preview

## Scope

This document records the current Monster Preview runtime path and the file boundaries that still
matter for maintenance. Older one-off investigations and implementation plans are intentionally
omitted.

## Runtime Entry

The feature is attached from [Plugin.cs](../Plugin.cs):

- `MonsterPreviewController`
- `MonsterPreviewWarmupController`
- `MonsterLockShowcaseRuntime`
- `MonsterPreviewDebugController` in debug builds only

Production trigger path:

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

## Key Files

- [Plugin.cs](../Plugin.cs)
- [Patches/Showcase/ShowcaseTooltipPatches.cs](../Patches/Showcase/ShowcaseTooltipPatches.cs)
- [Game/MonsterPreview/MonsterLockShowcaseRuntime.cs](../Game/MonsterPreview/MonsterLockShowcaseRuntime.cs)
- [Game/MonsterPreview/MonsterPreviewController.cs](../Game/MonsterPreview/MonsterPreviewController.cs)
- [Game/MonsterPreview/MonsterPreviewWarmupController.cs](../Game/MonsterPreview/MonsterPreviewWarmupController.cs)
- [Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs](../Game/MonsterPreview/Architecture/MonsterPreviewOverlayCoordinator.cs)
- [Game/MonsterPreview/Architecture/PreviewBoardSession.cs](../Game/MonsterPreview/Architecture/PreviewBoardSession.cs)
- [Game/MonsterPreview/Architecture/PreviewBoardRequest.cs](../Game/MonsterPreview/Architecture/PreviewBoardRequest.cs)
- [Game/MonsterPreview/Architecture/PreviewBoardModel.cs](../Game/MonsterPreview/Architecture/PreviewBoardModel.cs)
- [Game/MonsterPreview/Architecture/PreviewCardSpec.cs](../Game/MonsterPreview/Architecture/PreviewCardSpec.cs)
- [Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs](../Game/MonsterPreview/Architecture/PreviewBoardPresentation.cs)
- [Game/MonsterPreview/Architecture/BoardPose.cs](../Game/MonsterPreview/Architecture/BoardPose.cs)
- [Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs](../Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoardRenderTarget.cs)
- [Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs](../Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs)
- [Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs](../Game/MonsterPreview/DataSources/MonsterDatabasePreviewDataSource.cs)
- [Game/EncounterTracker.cs](../Game/EncounterTracker.cs)
- [Game/EncounterPreviewSpecConverter.cs](../Game/EncounterPreviewSpecConverter.cs)

## Responsibilities

### `MonsterLockShowcaseRuntime`

- decides whether the current lock toggle should be intercepted
- builds a `PreviewBoardModel` from `MonsterDatabase` or encounter-tracker cache
- creates the external `PreviewBoardRequest`
- handles repeated-toggle hide behavior and close-on-next-click behavior

### `MonsterPreviewController`

- owns the Unity `MonoBehaviour` lifecycle
- creates the render target and overlay coordinator
- forwards show / hide requests
- drives coordinator updates from `LateUpdate()`

### `MonsterPreviewOverlayCoordinator`

- stores the active request
- resolves the final request used for rendering
- forwards work to `PreviewBoardSession`

### `PreviewBoardSession`

- compares request signatures
- decides whether a rerender is required
- resolves pose, presentation, and model into a render-ready result

### `MonsterPreviewBoardRenderTarget` and `MonsterPreviewBoard`

- ensure the world-space board root exists
- apply pose and visibility
- rebuild item and skill cards and their markers

## Data Sources

The runtime currently has two production data sources:

- `MonsterDatabasePreviewDataSource` for static encounter data from `MonsterDatabase`
- `EncounterTracker` + `EncounterPreviewSpecConverter` for cached live encounter boards when
  static database coverage is missing

`MonsterLockShowcaseRuntime.TryBuildPreview(...)` prefers the database path first and falls back
to encounter-tracker cache.

## Debug Scope

Debug-only entry points live under:

- [Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs](../Game/MonsterPreview/Debug/MonsterPreviewDebugController.cs)
- [Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs](../Game/MonsterPreview/Debug/MonsterPreviewDebugTuner.cs)

They are mounted only when `BppBuild.IsDebug` is true in [Plugin.cs](../Plugin.cs).
