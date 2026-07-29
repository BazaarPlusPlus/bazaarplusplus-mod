# Timeline marker hover hit consistency

## Background

Timeline events are drawn on Canvas and separately indexed as pointer hit
regions. Dense events can form a collision chain whose markers keep their
original time X while only their Y positions change. Hover opens the complete
FrameInspector for the resolved visual marker; clicking pins the same detail.

The Viewer has previously fixed several related failures:

- hit regions using pre-layout instead of final marker coordinates;
- adjacent markers visually colliding while remaining separate targets;
- hover state changing while the pointer stays within one marker;
- a summary tooltip and the pinned inspector resolving different event sets.

## Current problem

In report `edec33acd04444c89966c08693bf1509.html`, the opponent hero Shield marker
at frame 61 / 3.05s can show different details while the pointer moves from the
left edge to the right edge of what appears to be one icon.

The screenshots show one highlighted Shield marker with a nearby critical
badge. One detail state includes Shield `61`, source Duct Tape, and trigger
source Fang. The expected invariant is stricter than matching the visible
headline: every point inside one rendered marker must resolve the same marker
ID, event member IDs, relation tree, and critical decoration.

## Root cause

The real report reproduces a deterministic hidden split:

- X `654–662` and `678–684` resolve frame 61: Shield `61`, Duct Tape,
  trigger source Fang.
- X `664–676` resolves frame 62: critical Shield `466`, The Core.

`buildVisualClusters` grouped semantically identical clusters whose X
coordinates were within 30px, even when they belonged to different frames. It
painted the merged result as one marker. `TimelineView` then called
`timelineClusterAtCombatMs`, which selected a different member according to the
pointer-derived time. The critical badge was only decoration; it did not own a
separate hit region.

The later marker-layout pass already stacks adjacent visual markers vertically
while preserving each event's true X coordinate. Cross-frame visual merging is
therefore both redundant and semantically lossy.

## Decision

- Keep the frame-level clusters produced by `buildClusters` independent.
- Let `layoutTimelineMarkers` distribute adjacent markers vertically.
- Reuse each independent cluster for paint, hit testing, hover, click, and
  inspector details.
- Continue grouping same-frame records in `buildClusters`; those records are
  intentionally one event presentation and already share a stable identity.
- A critical `!` remains decoration of its effect marker.

## Verification

- Sweep pointer X across the rendered Shield markers and record the resolved
  frame, amount, source, and trigger source.
- Add a pure hit-test regression for adjacent-frame markers across the complete
  painted icon width.
- Add Chromium and WebKit behavior coverage that sweeps both marker edges and
  asserts one stable full inspector per marker.
- Re-run Viewer typecheck, pure tests, and full behavior tests.
- Refresh the localhost production report and repeat the real pointer sweep.
