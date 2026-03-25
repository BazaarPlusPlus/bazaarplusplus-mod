# Run History Decompiled Analysis

## Question

Based on the decompiled game code, can we recover a full per-day, per-hour history for the current run, including:

- what the player chose at each step
- what encounter content appeared at each step

Or do we need to record that ourselves while the run is in progress?

## Short Answer

From the currently inspected decompiled runtime and message types:

- the game exposes the current run day and hour
- the game exposes the current encounter id and current selection set
- the game exposes a shallow list of previous run states
- but it does **not** expose a complete built-in history containing per-hour encounter contents and final player choices

So if the goal is to reconstruct the full run afterward, you need to record it yourself during gameplay by hooking runtime messages or state transitions.

## Status

This note predates the current `Game/RunLogging/` implementation. Keep it as background for why
BazaarPlusPlus records live run history itself; the capture strategy below is no longer pending
work.

## Decompiled Types Checked

Primary files inspected:

- `decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/Run.cs`
- `decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/RunState.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdateRun.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdateRunState.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdatePreviousRunState.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages/RunStateSnapshotDTO.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/GameSimEventStateTransitioned.cs`
- `decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs`
- `decompiled/TheBazaarRuntime/TheBazaar/GameSimHandler.cs`

## What Exists

### 1. Current run progress exists

`Run` stores current run-level progress:

- `Day`
- `Hour`
- `Victories`
- `Losses`
- `Player`
- `Opponent`

Relevant file:

- `decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/Run.cs`

`SimUpdateRun` also carries:

- `Day`
- `Hour`
- `Victories`
- `Defeats`
- `HasVisitedFates`
- `CurrentHourXP`
- `DataVersion`

Relevant file:

- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdateRun.cs`

This is enough to know the current day/hour, but not enough to reconstruct a detailed log.

### 2. Current encounter context exists

`RunState` and `SimUpdateRunState` carry current state information:

- `StateName`
- `CurrentEncounterId`
- `SelectionSet`
- `RerollCost`
- `RerollsRemaining`
- `SelectionContextRules`

Relevant files:

- `decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/RunState.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdateRunState.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages/RunStateSnapshotDTO.cs`

This means we can inspect what is currently on screen:

- which encounter is active
- which selectable card/entity ids are currently offered

But this is still only the current snapshot.

### 3. A shallow previous-state list exists

`SimUpdateRunState` includes:

- `PreviousRunStates`

Each entry is `SimUpdatePreviousRunState`, which contains only:

- `State`
- `SequenceNumber`
- `Day`
- `Hour`

Relevant files:

- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdateRunState.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/SimUpdatePreviousRunState.cs`

This is the strongest hint that the game tracks some minimal state progression metadata.

## What Does Not Exist

### 1. No detailed encounter history object

In the inspected types, there is no built-in history structure like:

- encounter content by hour
- full choice list by hour
- selected option by hour
- encounter resolution result log

I did not find a run-history object carrying:

- historical `SelectionSet` snapshots
- resolved player picks
- encounter text/content payloads for previous steps

### 2. No detailed history stored in runtime `RunState`

`RunState` itself only stores the current state fields:

- current encounter id
- current selection set
- current reroll data
- current rules

Relevant file:

- `decompiled/BazaarGameClient/BazaarGameClient.Domain.Models/RunState.cs`

It does not contain `PreviousRunStates`, and it does not contain any detailed archive of previous selections.

### 3. `PreviousRunStates` is not hydrated into runtime state

`DataExtensions.Update(this RunState state, SimUpdateRunState snapshot)` copies:

- `RerollCost`
- `RerollsRemaining`
- `SelectionSet`
- `StateName`
- `CurrentEncounterId`
- `SelectionContextRules`

It does **not** copy `PreviousRunStates`.

Relevant file:

- `decompiled/TheBazaarRuntime/TheBazaar/DataExtensions.cs`

So even the shallow previous-state list only exists on the raw message, not on the stabilized runtime state object used elsewhere.

## State Transition Events Are Also Shallow

The relevant state events are:

- `GameSimEventStateTransitioned`
- `GameSimEventStateSuspended`
- `GameSimEventStateResumed`

They contain only:

- destination state
- optional `CurrentEncounterId`

Relevant files:

- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/GameSimEventStateTransitioned.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/GameSimEventStateSuspended.cs`
- `decompiled/BazaarGameShared/BazaarGameShared.Infra.Messages.GameSimEvents/GameSimEventStateResumed.cs`

These events help detect when the state changed, but they still do not provide a full historical encounter record.

## What This Means In Practice

### Can we know the current day/hour?

Yes.

### Can we know the current encounter and current options?

Yes.

You can use:

- `Run.Day`
- `Run.Hour`
- `RunState.CurrentEncounterId`
- `RunState.SelectionSet`

or read the same values directly from raw `GameSim` messages.

### Can we reconstruct the full run afterward without recording it live?

Not from the inspected built-in data structures.

At most, the built-in data gives you:

- the current run position
- the current encounter/options
- a shallow previous-state breadcrumb list with day/hour/state only

That is not enough to answer:

- what exact options were shown two hours ago
- which option the player clicked then
- what the exact encounter contents were for each prior hour

## Conclusion

The decompiled code suggests:

1. the game maintains current run state well enough for overlays and live inspection
2. the raw message stream exposes current encounter/selection context
3. the raw message stream also exposes a shallow `PreviousRunStates` list
4. but there is no built-in detailed per-hour encounter-and-choice history suitable for full after-the-fact reconstruction

So the practical answer is:

- if you want a complete per-day, per-hour run log with concrete choices and encounter contents, you must record it yourself while the run is happening

## Capture Strategy That Informed The Current Implementation

The run-logging implementation that landed later follows these same broad hook points:

1. `NetMessageGameSim.Data.Run`
2. `NetMessageGameSim.Data.CurrentState`
3. `GameSimEventStateTransitioned`
4. card/selection-related `GameSimEvent*` payloads

At each relevant transition, record:

- day
- hour
- state
- current encounter id
- selection set contents
- resolved entity/card details for those selection ids
- the player action taken, if it can be inferred from subsequent events

That would produce the history the built-in runtime does not preserve for us.
