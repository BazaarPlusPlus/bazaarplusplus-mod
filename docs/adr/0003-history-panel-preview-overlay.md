# Render HistoryPanel previews via ScreenSpaceOverlay, not an offscreen RenderTexture

The HistoryPanel battle-board preview renders the game's native `CardPreviewBase` into an independent `ScreenSpaceOverlay` Canvas whose position is synced to the UI Toolkit preview container's `worldBound`. We deliberately do **not** render cards with an offscreen Camera into a `RenderTexture` blitted into a UI Toolkit `Image`.

## Context

An offscreen-RT migration was designed and implemented (the "clone fewer levels" approach: HistoryPanel owns its host Canvas + socket layout and reuses only the `CardPreviewBase` prefab, awaiting the real `SetUp` art-load task instead of the game's fragile reveal coroutine). The goal was a self-contained native render not coupled to the live game tooltip canvas.

It failed on a hard platform constraint: **under URP, an offscreen `Camera → RenderTexture` cannot render uGUI** (`CardPreviewBase` is uGUI), so the texture came back empty. The RT path was abandoned and reverted to a `ScreenSpaceOverlay` Canvas + `worldBound→clip` coordinate sync.

## Consequences

- `GameInterop/ItemBoardPreview/BppItemBoardPreview.cs` + `ItemBoardPreviewSurface.cs` own a `ScreenSpaceOverlay` Canvas; the overlay tracks the UI Toolkit container via `scaledPixelsPerPoint`, so it follows `PanelSettings.match` automatically (see [history-panel.md](../features/history-panel.md) §布局, §预览渲染).
- RT-era code was deleted: `NativeBoardWidth`/`NativeBoardHeight` constants survive in `GameInterop/ItemBoardPreview/ItemBoardSocketLayout.cs`; `HistoryPanelPreviewTextureGeometry` itself is gone, along with `ResolveTextureSize` / `ResolveBoardPlacement` and their tests, `Game/PreviewSurface/`, and `Game/MonsterPreview/Architecture/`.
- **Do not re-propose the offscreen-RT path** unless the project leaves URP or Unity gains offscreen uGUI rendering. The HistoryPanel preview stack is intentionally decoupled from the monster/CardSet preview path (see [monster-preview.md](../features/monster-preview.md)).

Related layout history: [docs/design/archive/2026-05-29-historypanel-fullscreen-responsive-design.md](../design/archive/2026-05-29-historypanel-fullscreen-responsive-design.md). (The detailed offscreen-RT migration spec was pruned; this ADR is the record of that decision.)
