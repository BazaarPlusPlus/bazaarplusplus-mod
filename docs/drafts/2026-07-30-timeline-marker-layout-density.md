# Timeline marker layout density

## Background

Timeline markers previously moved sideways to avoid collisions. That made their
horizontal position disagree with event time, so the layout was changed to keep
the exact x coordinate and distribute colliding markers vertically.

The current report still looks unstable: the same semantic icon appears at
several unrelated sizes, nearby timestamps form dense vertical chains, and some
markers become too small to read. The screenshot in this review shows the issue
across multiple item lanes, not one malformed event.

## Current problem

The visual contract must hold simultaneously:

- x is determined only by event time;
- collision handling may change y, never x;
- ordinary markers keep one readable visual size;
- multiple events are visibly separated instead of painted into one knot;
- dense cases remain deterministic and retain precise hit targets.

Code inspection confirmed two independent causes:

- a 52px adjacent-gap rule forms a transitive chain, then gives every marker in
  that chain a unique row and shrinks all of them according to the chain's total
  length;
- fallback glyphs ignore the resulting size cap, while native images and
  attribute glyphs honor it.

The result is both semantically wrong (chain length is not local concurrency)
and visually inconsistent (different renderer paths use different sizes).

## Candidate approaches

1. **Fixed-size row assignment** — keep exact x and assign overlapping marker
   intervals to a small set of vertical rows. This preserves time and gives all
   ordinary icons one size.
2. **Exact-frame stacks only** — stack only events with the same timestamp and
   allow different timestamps to overlap. This is temporally pure but can hide
   nearby events and make hit testing ambiguous.
3. **Overflow summary** — combine fixed-size rows with a final `+N` marker when
   all rows are occupied. This is readable but hides individual events from the
   primary view and needs a new detail interaction.

## Decision

Use fixed-row interval coloring inside each same-lane collision component:

- keep `markerX` equal to the time-derived `cluster.x`;
- sort deterministically, then place each marker in the first row whose previous
  marker no longer overlaps its visual footprint;
- include trailing critical/defeat decorations in that footprint, so their
  `!` / `×` indicators cannot overlap the next event;
- use the same footprint when ranking hover targets, so moving across a
  decoration cannot switch its tooltip to an adjacent marker;
- let non-overlapping markers reuse a row even when an intermediate marker links
  them into the same component;
- use one 14px marker size for the normal one-to-three-row case;
- preserve 14px for the rare four-row case, accepting about one pixel of
  vertical visual-box overlap rather than making a few icons look unrelated;
- only shrink when five or more rows are simultaneously required, using the
  available lane height rather than the component's total event count;
- apply the same final size to native images, attribute glyphs, and fallback
  glyphs.

This preserves every event without introducing an overflow summary and removes
event-tier size changes that read as random in the lane.

The current 782-event report validates the boundary: the old 52px rule created
target/source chains of 48/37 clusters, while true same-frame density peaks at
two/three. Using the real 14px marker diameter as the row-reuse distance limits
both modes to four rows. The hit radius remains larger for interaction, but no
longer drives static layout.

## Verification

- Pure cases for 1–6 same-time events and adjacent collision chains.
- A decorated marker immediately followed by an ordinary marker occupies a
  separate row; when they can share a row, the decoration remains owned by its
  original event during hit testing.
- Every marker through the measured four-row peak keeps the same rendered size;
  every marker keeps the exact time-derived x.
- Colliding intervals occupy distinct y rows; non-colliding markers may reuse a
  row.
- Hit testing still resolves each marker to its original event IDs.
- Chromium and WebKit behavior suites pass.
- The current review report is rebuilt and visually checked at the same time
  range as the supplied screenshot.
