# ADR-0003: Render native board previews in a screen-space overlay

Status: Accepted

## Decision

Render History and LiveBuild native card previews in a dedicated `ScreenSpaceOverlay` canvas positioned over their UI Toolkit containers. Do not render the uGUI card prefab through an offscreen camera into a `RenderTexture`.

## Why

The offscreen-camera prototype produced an empty texture under URP because the reused `CardPreviewBase` is uGUI, not scene geometry. The overlay keeps native rendering while letting UI Toolkit own layout.

## Guardrails

- `ItemBoardPreviewSurface` owns the overlay canvas ([surface](../../src/BazaarPlusPlus/GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L296-L337)). Views convert `worldBound` points to physical pixels with `scaledPixelsPerPoint` before updating the surface ([History view](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs#L192-L206), [LiveBuild view](../../src/BazaarPlusPlus/Game/LiveBuildPanel/Ui/LiveBuildPanelView.cs#L785-L796)).
- Reuse the shared native-card preview host; do not create a feature-local prefab/render pipeline.
- Do not re-propose offscreen uGUI-to-RT unless the render pipeline or Unity capability changes.
