# Encounter Tracker Preview Alignment Design

**Goal:** Align `EncounterTracker` with the current monster preview architecture by removing the legacy item/skill string cache path and keeping encounter state scoped to real encounter states.

## Current Problem

`EncounterTracker` still builds `RunInfo.MonsterPreview` from the removed `MonsterDatabase.TryGet(string encounterInternalName)` path. That means:

- encounter preview cache is partially empty
- `MonsterLockShowcaseRuntime` still carries a legacy fallback conversion
- debug UI shows stale legacy fields instead of the current structured monster data

The tracker also treats any non-combat `SelectionSet` as encounter choices, and run end does not proactively clear encounter cache state.

## Chosen Approach

Use the encounter template id as the only monster lookup key in `EncounterTracker`, and store structured preview data that matches the current monster database model.

Scope updates to these run states only:

- `ERunState.Encounter`
- `ERunState.Choice`
- `ERunState.Loot`
- `ERunState.Pedestal`

All other states clear encounter cache state immediately.

## Data Model Changes

Keep `RunInfo.MonsterPreview` as the lightweight cache model used by the tracker and debug panel, but replace the legacy fields:

- remove `Items`
- remove `Skills`

Add structured preview fields:

- `EncounterId`
- `EncounterShortId`
- `Title`
- `BoardCards`
- `Skills`

`BoardCards` and `Skills` use lightweight card-spec style entries so the runtime fallback can render directly without rebuilding from string ids.

## Runtime Behavior

### EncounterTracker

- Read cards from `SelectionSet`
- Ignore unsupported states
- For combat encounter cards, query `MonsterDatabase.TryGetByEncounterId(card.TemplateId, out monster)`
- Map the matched `MonsterInfo` into structured `RunInfo.MonsterPreview`
- Update either `AvailableEncounters` or `CurrentEncounterChoices`
- Clear stale state when leaving supported states

### MonsterLockShowcaseRuntime

- Keep monster DB as the primary source
- If DB lookup misses, use `ModState.EncounterMonsterPreviews`
- Convert cached structured preview cards directly to `PreviewCardSpec`
- Remove legacy string-id conversion helpers

### DebugPanel

- Continue matching previews by encounter template id / name
- Replace legacy `Items` display with structured board cards
- Replace legacy `Skills` display with structured skill entries

## Lifecycle Cleanup

Expose a tracker reset entry point and call it from run lifecycle handlers:

- `RunEnded`
- `RunInterrupted`

This prevents encounter cache state from surviving after a run ends.

## Testing Strategy

Use focused console-style tests in `tests/`:

1. verify supported encounter states are recognized and unsupported states are rejected
2. verify tracker reset clears all encounter cache fields
3. verify structured preview conversion produces usable `PreviewCardSpec` values without legacy string lists

## Non-Goals

- redesigning the primary monster preview data source
- changing the overlay presentation
- removing `EncounterMonsterPreviews` entirely in this pass
