---
status: implemented
archived: 2026-07-11
calibrated: 2026-07-11
superseded-by: code
---

> Status: IMPLEMENTED. All checkboxes completed and merged with PR#17 (collection search + tier tooltips). Companion design doc: [../design/2026-07-10-collection-search-conventional-matching-ime.md](../design/2026-07-10-collection-search-conventional-matching-ime.md).

# Collection Conventional Search And IME Plan

- [x] Add regression tests: Latin queries require contiguous text; exact partial words still
  match; Chinese queries remain normalized; debounce is paused during IME composition.
- [x] Run `CollectionFilterEngine.Tests` and confirm the new assertions fail for the current
  ordered-subsequence matcher and composition-unaware refresh gate.
- [x] Remove Latin ordered-subsequence fallback from `CollectionCardSearch`; retain exact
  normalized matching, CJK compact matching, and multi-term AND semantics.
- [x] Extend `CollectionSearchRefreshGate.Advance` with composition state and pause its countdown
  while composition is active.
- [x] Track composition through `Keyboard.onIMECompositionChange` and pass that state from
  `CollectionPanel.Tick` into the gate.
- [x] Run CollectionFilterEngine tests, Architecture tests, `git diff --check`, and the main build.
- [x] Review the scoped diff and update the external project memory with the final behavior.

## Initialisms And Filter Composition

- [x] Add failing tests for `mbb` -> `Molten Ball Blaster`, arbitrary non-initial letters, and
  text search composed with source/hero/day/quality/size/tag/keyword filters.
- [x] Add exact multi-word initialism matching for display names, internal names, and art keys.
- [x] Remove the search-only `CollectionQuery` path so text flows through the normal filter and
  source resolution pipeline.
- [x] Verify search, layout, architecture tests, diff hygiene, and the main build.
- [x] Add a pre-PR architecture guard against legacy `UnityEngine.Input.compositionString` and
  unregister Collection tooltip instances from the defensive card-destroy path.
