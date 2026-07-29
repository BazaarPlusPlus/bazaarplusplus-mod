# Timeline marker vertical distribution

## Background

Timeline markers use the horizontal axis as a data axis: a marker's X coordinate is
the projected combat time. Dense same-lane markers therefore must not be moved
sideways for collision avoidance.

The first vertical-only revision removed synthetic X offsets and enumerated Y slots
for groups of 1–5 markers. It still detected a dense group with a 20px window
measured from the first marker.

## Current problem

The real report around Primal Core at 12.8s shows several adjacent markers whose
visual/hit regions form one crowded run, but whose centers are more than 20px apart.
They are split into separate one-marker groups, so every marker falls back to the
lane center. The result is a horizontal row of icons that looks squeezed together
despite the vertical slot implementation.

The first grouping fix exposed a second cause: Haste, Slow, Freeze, and other status
range start markers were explicitly excluded from marker layout. Ordinary events
could move vertically while the status markers stayed on the centerline, recreating
the same collision in mixed runs.

These are grouping-participation failures, not slot-coordinate failures.

## Candidate approaches

1. Increase the first-marker window only.
   - Simple, but a chain can still split where two adjacent markers collide while
     the last marker is outside the first marker's window.
2. Move markers in X.
   - Rejected because it falsifies event time.
3. Group by adjacent visual collision, then distribute the complete run in Y.
   - Preserves every event's X.
   - Handles the real `12.8s → 13.x` run because each neighboring pair is visually
     close.
   - Requires a bounded fallback for long runs.

## Decision

Use adjacent-gap grouping with a threshold derived from the marker interaction
diameter plus breathing room. Keep every marker's `markerX = cluster.x`.

Status range start markers participate in the same collision run. Only their marker
anchor moves vertically; the interval band retains its original lane geometry.

For each resulting run:

- one marker remains centered;
- two markers use the upper and lower usable lane edges rather than center-adjacent
  slots;
- three to five markers use explicit evenly distributed Y slots spanning the lane;
- six or more markers distribute over the same vertical span with a bounded minimum
  marker size.

No animation is added. Layout remains deterministic and instantaneous.

## Verification

- Pure tests enumerate 1–5 and 6+ marker runs.
- Every marker X must equal its original projected X.
- Y positions must be distinct and span the expected usable lane height.
- A mixed transitive fixture (`x = 100, 148, 196`) must become one three-marker
  vertical run
  even though the first-to-last distance exceeds the adjacent threshold.
- The mixed fixture includes status range start markers on both ends so they cannot
  regress to the lane center while the ordinary event moves.
- Exact marker centers must resolve to their own events in the hit index.
- Chromium and WebKit behavior tests hover/click every marker in a real-density
  fixture.
- Localhost smoke reloads the current report and checks the Primal Core run no
  longer sits on one horizontal centerline.
