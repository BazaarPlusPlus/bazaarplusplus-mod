# Combat Log Panel Design

## Goal

Add a runtime-only combat log panel that follows the native combat playback frame-by-frame, shows readable combat events as the fight progresses, and supports two different future-row styles for first play versus explicit replay playback of the same fight data.

## Problem

The current mod can observe combat playback progress and raw `CombatSim` data, but it does not expose a readable event timeline during battle. The native runtime emits useful combat events, but those events are already processed, partially transformed, and do not carry stable frame indices for a log UI. For a combat log panel that can follow playback precisely under speed changes, pauses, and final-blow slowdown, the source of truth must stay aligned with the original `CombatSim.Data.Frames` timeline.

## Scope

In scope:

- build a runtime-only combat log for the current combat
- derive log rows from raw `CombatSim` frame data
- advance the panel using combat `frameIndex`, not wall-clock time
- attach the log UI beside the existing `DebugPanel`
- provide an independent show/hide toggle for the log panel
- support two future-row behaviors:
  - first play: future rows are hidden
  - explicit replay playback: future rows are visible but dimmed

Out of scope:

- file persistence
- replay export or import
- multi-combat history
- search and filtering
- rich formatting or localization polish
- full support for every `ICombatSimEvent` subtype in the first pass

## Constraints

- The panel must remain correct under combat speed changes.
- The panel must remain correct while combat is paused.
- The panel must remain correct during the game's final-blow slowdown path.
- The panel must not derive playback position from elapsed real time.
- The first version should reuse the existing debug runtime context rather than create a standalone top-level window flow.

## Current State

The relevant runtime pieces already exist:

- `CombatSimHandler.Simulate(...)` consumes `CombatSim.Data.Frames` in order and drives combat playback.
- `CombatStatusBar` already tracks processed combat frames using a postfix on `FinalBlowSlowDownController.Process(...)`.
- `DebugPanel` already provides a debug-only runtime UI surface.

This means the mod already has:

- a stable source timeline: `CombatSim.Data.Frames`
- a stable playback progress signal: processed combat frame count
- a convenient runtime UI home: the debug panel area

## Proposed Architecture

Split the feature into four runtime responsibilities:

1. `CombatLogRuntime`
2. `CombatLogFormatter`
3. `CombatLogPanel`
4. `CombatLogPlaybackState`

Each layer should have one job and avoid mixing raw timeline construction with UI concerns.

## Runtime Data Flow

The runtime flow should be:

1. `Events.CombatSimReceived` provides the current `CombatSim` payload.
2. `CombatLogRuntime` converts the payload into an in-memory combat log timeline.
3. `CombatLogFormatter` derives display rows from that timeline.
4. `CombatLogPanel` renders rows according to the current processed frame.
5. `CombatLogPanel` refreshes row visibility and highlight state as playback advances.

No file writes occur anywhere in this flow.

## Data Model

Keep two in-memory layers:

### 1. Raw-ish timeline layer

This layer preserves frame structure and is the source of truth for playback alignment.

Recommended shape:

- `CombatLogTimeline`
- `CombatLogMeta`
- `CombatLogFrame`
- `CombatLogEventEntry`
- `CombatLogSideUpdate`
- `CombatLogCardUpdateEntry`

Important fields:

- `FrameIndex`
- `FramesLeft`
- `LogicalTime`
- `Events`
- `Player`
- `Opponent`
- `CardUpdates`

This layer should remain close to `CombatSim` semantics and avoid premature text formatting.

### 2. Display-row layer

This layer is panel-facing and flattened.

Recommended shape:

- `CombatLogRow`
- `CombatLogRowCategory`
- `CombatLogRowVisualState`

Each row should include:

- `FrameIndex`
- `LogicalTime`
- `Category`
- `Text`
- optional `ExecutionContextId`
- optional `SourceId`
- optional `TargetId`

This layer exists only to render the panel cleanly.

## Playback Model

The panel must follow combat playback by frame count, not by elapsed seconds.

The active playback progress should come from the existing processed-frame state rather than from a new timer. That keeps the log synchronized with:

- speed overrides
- pause/unpause behavior
- final-blow slowdown
- native replay playback

Recommended rule:

- `ProcessedFrameCount = ProcessedCombatFrames`
- `LastProcessedFrameIndex = ProcessedCombatFrames - 1`

The panel does not own time. It only maps processed-frame progress to row states.

Important semantic rule:

- `ProcessedCombatFrames` is a count, not a frame index
- the "current" row in the panel means "the most recently processed frame"
- if `ProcessedCombatFrames == 0`, no frame has been played yet and there is no current row

## Future-Row Behavior

The panel supports two playback passes:

- `FirstPlay`
- `Replay`

For a given row:

- if `ProcessedCombatFrames == 0`, every row is future
- `row.FrameIndex < LastProcessedFrameIndex`: row is already played
- `row.FrameIndex == LastProcessedFrameIndex`: row is current
- `row.FrameIndex > LastProcessedFrameIndex && pass == FirstPlay`: row is hidden
- `row.FrameIndex > LastProcessedFrameIndex && pass == Replay`: row is visible and dimmed

This gives the UI the desired "lyrics-like" reveal on first watch while still allowing full-timeline preview during explicit replay playback.

## Replay Detection

Replay styling should not be inferred from a heuristic timeline signature.

The panel should switch to `Replay` mode only when replay playback is known explicitly from runtime state, for example through the existing combat replay controller or replay runtime.

Rules:

- live combat playback starts as `FirstPlay`
- saved replay playback starts as `Replay`
- a newly received live combat timeline must never be classified as replay based only on content similarity

If the panel later needs to distinguish "replaying the same fight data" more precisely, that should be based on explicit replay-source metadata, not content-shape guessing.

## UI Placement

The combat log panel should live beside the existing `DebugPanel`, not inside one of its current sections.

Design intent:

- `DebugPanel` remains the main anchor and lifecycle owner
- `CombatLogPanel` gets its own visible region and independent toggle
- the panel can later be promoted into a more general-purpose tool without rewriting its internals

Implementation note:

- the first version should render the combat log in a second IMGUI window or explicit side-by-side area
- it should not be implemented as another `DebugPanelSection`

This layout keeps the first version cheap while preserving a clean separation of concerns.

## First-Version Event Coverage

The first pass should support only the rows most useful for validating synchronization and readability:

- `EffectExecuted`
- player health adjustments
- player attribute updates
- card attribute updates
- `CombatantDied`
- monster gold / xp reward events

Delayed support:

- `EffectTriggered`
- aura executed events
- transform and transform-revert events
- quest events
- enchant events

These can be added later once the panel interaction and timing model are validated.

## Formatting Strategy

Formatting should be deterministic and structure-first.

Guidelines:

- prefer one concise row per meaningful action or state change
- preserve `frameIndex` exactly
- resolve names when available, but keep stable ids as fallback
- never require runtime object lookup to succeed for the row to exist

Fallback behavior should produce readable placeholders such as card instance ids rather than dropping the row.

## Error Handling

The runtime must tolerate incomplete or unfamiliar data:

- unknown event subtype -> emit an `unknown` row or skip with debug logging
- missing source/target name -> use stable fallback identifiers
- empty frame -> valid, no special handling needed
- repeated combat load -> replace current timeline cleanly
- replay/live mode changes -> update playback pass from explicit runtime source

The UI should fail soft. A bad row should not break the whole panel.

## Test Strategy

The first version should be verified with focused source and state tests rather than full UI automation.

Key checks:

- `CombatSim` maps to timeline frames with stable frame indices
- playback row-state mapping is correct for `FirstPlay` and `Replay`
- `ProcessedCombatFrames == 0` yields no current row
- `ProcessedCombatFrames == 1` highlights frame `0`
- future rows are hidden on first play
- future rows are dimmed on replay
- the panel reads processed frame progress rather than elapsed time
- the combat log uses a distinct side panel rather than a debug-panel section

## Risks

The biggest practical risks are:

- over-formatting too early and losing raw timeline structure
- coupling row playback to a timer instead of processed frame state
- trying to support every combat event type in the first pass

The right response is to keep the first version narrow and preserve raw frame information in memory.

## Success Criteria

This design is successful when:

- a current combat can be rendered as readable runtime log rows
- row highlighting follows the last processed frame without off-by-one drift
- speed changes, pause, and final-blow slowdown do not desync the panel
- future rows are hidden on first play
- future rows are dimmed during explicit replay playback
- the panel can be independently toggled while living beside the debug panel
