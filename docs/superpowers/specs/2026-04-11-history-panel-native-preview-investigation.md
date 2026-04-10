# History Panel Native Preview Investigation Notes

Date: 2026-04-11

## Context

This investigation tried to replace the History Panel preview region with the game's native monster board presentation path while keeping the preview embedded inside the History Panel UI.

The work was reverted in the same branch after repeated rendering and positioning failures. The History Panel preview has been restored to the previous `HistoryPanelPreviewRenderer` flow for this version.

## Original Goal

- keep the existing preview region inside History Panel
- keep showing only one side of the selected battle
- stop using the BazaarPlusPlus custom `PreviewBoardSurface` path for that region
- reuse the game's native monster board / tooltip rendering path instead

## Attempted Implementation Shape

The native-preview experiment introduced two new concepts:

- `HistoryPanelNativePreviewAdapter`
  - translated `HistoryBattlePreviewData` into a single-side model consumable by the native monster board path
- `HistoryPanelNativePreviewHost`
  - attempted to host a cloned native `MonsterBoardTooltip` inside the History Panel preview area

The History Panel orchestration was also changed to stop using `HistoryPanelPreviewRenderer`.

## Debugging Timeline

### 1. Direct native overlay host inside History Panel

First attempt:

- clone the native `MonsterBoardTooltip`
- place it into an overlay-style uGUI host
- align that host to the History Panel preview area

Observed behavior:

- board appeared outside the intended preview region
- board was often covered by the History Panel UI
- switching battles could leave cards partially visible, sometimes with only top badge elements remaining

Main conclusion:

- direct reuse of the native overlay path immediately ran into layering and coordinate-space problems between uGUI and UI Toolkit

### 2. Bounds and sorting adjustments

Second attempt focused on placement and draw order:

- stopped using the preview image element bounds directly for anchoring
- switched to the full preview container bounds
- raised sorting order
- normalized child canvases to follow the outer host ordering and camera

Observed behavior:

- placement still drifted
- the board could still render underneath or outside the visible preview region
- switching remained unstable

Main conclusion:

- the problem was not a single bad sort order; the larger issue was mismatch between UI Toolkit bounds and native overlay coordinate expectations

### 3. Off-screen render-to-texture host using native board content

Third attempt avoided UI layering by:

- keeping the native board clone
- rendering it with a dedicated off-screen camera into a `RenderTexture`
- showing that texture in the existing preview `Image`

This was intended to preserve native board visuals while removing direct overlay conflicts with UI Toolkit.

Observed behavior:

- the preview texture itself displayed correctly
- the render target could show a debug background color
- the native board content did not appear in the render target
- some intermediate iterations suggested the render texture chain was healthy while the board clone was not being captured

Main conclusion:

- the failure moved from History Panel overlay layering to the native board clone not being reliably visible to the off-screen camera

### 4. Repeated visibility and async-settle attempts

Several follow-up fixes targeted the possibility that the native cards were becoming hidden after async setup:

- force card preview components visible after render
- keep rendering at 30 FPS while the preview was active
- add a short settle window to continue refreshing after selection changes
- rebuild clone state on selection changes

Observed behavior:

- this did not restore the board content
- it reduced the likelihood that the issue was only "rendered too early once and then stopped"

Main conclusion:

- continuous refresh was necessary for future dynamic support anyway, but it did not solve the missing-content problem

### 5. Diagnostic render markers

To separate "texture not displayed" from "board not drawn", several diagnostics were added:

- solid magenta background in the render target
- debug label showing host state
- counts for previews, canvas groups, canvases, graphics, and renderers
- a simple colored marker intended to prove that ordinary hosted UI could be seen by the off-screen camera

Observed behavior from the diagnostic pass:

- the preview region displayed the render texture
- magenta background was visible
- debug text confirmed the host, clone root, and render texture were alive
- `previews`, `canvasGroups`, `graphics`, and `renderers` were non-zero
- the diagnostic marker still did not appear in the final texture in the world-space off-screen variant

Main conclusion:

- the off-screen render target path itself was active, but the hosted world-space UI was still not being captured as expected
- this pointed to camera-facing, canvas mode, or world-space geometry relationships rather than simple texture plumbing

### 6. Return to direct overlay with improved coordinate conversion

After the off-screen path failed, the experiment returned to direct overlay hosting, this time with stronger coordinate conversion:

- map the preview region bounds from UI Toolkit space into actual screen-space pixel coordinates
- keep the native board as a direct overlay object instead of feeding a render texture

Observed behavior:

- the board became visible again
- positioning was still wrong
- the board drifted downward and could end up effectively pinned to screen space rather than the exact preview region

Main conclusion:

- the latest blocking issue was still coordinate conversion between UI Toolkit panel coordinates and native uGUI overlay placement

## What Was Proven

These points are reasonably established by the investigation:

- cloning and refreshing the native board view is possible in principle
- History Panel selection changes can supply enough data for a one-side native preview model
- direct embedding of native overlay content into a UI Toolkit region is not plug-and-play
- `RenderTexture` display inside History Panel works
- the unsolved problems are mostly in native board hosting, coordinate conversion, and visibility/capture behavior

## Likely Root-Cause Areas

The unresolved issues now look concentrated in these areas:

1. UI Toolkit to uGUI coordinate conversion
   - `VisualElement.worldBound` was not sufficient by itself for reliable overlay placement
   - the History Panel panel root and actual game screen space likely need a stricter conversion path

2. Native board clone canvas hierarchy
   - cloned native tooltip content may contain nested canvases or assumptions that do not survive being reparented
   - child canvases may recreate state or resolve visibility differently from the host canvas

3. Native card preview lifecycle
   - card visuals appear to have async or delayed setup behavior
   - some states suggested internal subviews could hide or rebuild after initial host setup

4. Off-screen world-space capture assumptions
   - world-space UI under a cloned native board was not reliably captured even when host diagnostics looked healthy
   - camera direction, canvas render mode, or hidden nested-canvas behavior still need deeper inspection if this route is revisited

## Recommended Next Investigation Order

If the native-preview migration is resumed later, use this order rather than repeating the whole experiment blindly:

1. Keep History Panel on the old renderer while debugging the native path in isolation.
2. Build a tiny standalone harness that hosts only the cloned native `MonsterBoardTooltip` outside History Panel.
3. Verify the native clone under each mode separately:
   - direct overlay
   - screen-space camera
   - world-space with dedicated camera
4. Only after the clone is stable, solve History Panel anchoring:
   - compare UI Toolkit preview bounds
   - panel root bounds
   - actual screen size
   - resulting overlay rect
5. If direct embedding still fights UI Toolkit, prefer a proven off-screen path only after the standalone harness can render a marker and the board together.
6. Add temporary logging for:
   - panel root size
   - preview region bounds
   - final screen rect
   - clone root transform
   - nested canvas count and modes

## Why This Version Was Reverted

The experiment was reverted for this branch because:

- the preview region was not stable enough for release use
- native rendering still had unresolved placement and capture issues
- continuing inside the main branch would have left History Panel in a visibly broken state

The old `HistoryPanelPreviewRenderer` path remains the known-good behavior for now.
