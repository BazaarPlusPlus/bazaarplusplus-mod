# Offline Replay Bootstrap Design

## Goal

Allow a saved combat replay to start directly from the lobby without calling `RunManager.StartRun()` or depending on the normal live run lifecycle.

## Problem

The current saved replay flow can deserialize a saved `CombatSequenceMessages` bundle and inject it into the native replay pipeline, but the lobby bootstrap path still uses `RunManager.StartRun()` to bring up gameplay state. That makes the replay bootstrap path too close to a real run start and creates an unacceptable risk of server-side side effects.

## Scope

In scope:

- start a saved combat replay from the lobby
- keep using the native `ReplayState` pipeline
- prepare only the minimum gameplay environment required for replay
- verify the path using code-path review and local runtime logs

Out of scope:

- full run bootstrap
- run timeline replay
- network capture or packet-level proof
- migration for the renamed run logging database

## Constraints

- Do not call `RunManager.StartRun()` in the saved replay bootstrap path.
- Do not trigger `Events.RunStarted`.
- Do not require `Data.HasActiveRun == true` as a readiness signal.
- Keep saved replay playback restricted to the lobby with no active run.

## Current Replay Flow

The replay runtime already has the core saved replay pieces:

- capture raw `GameSim -> CombatSim -> GameSim` triplets
- persist them as JSON with MessagePack payloads
- load them back into `CombatSequenceMessages`
- inject `LastCombatSequence`
- feed the opening `GameSim` through `GameSimHandler`
- enter `ReplayState`

The weak point is only the lobby bootstrap path.

## Proposed Architecture

Split the lobby replay start into four explicit phases:

1. Scene preparation
2. Replay dependency resolution
3. Saved sequence injection
4. Exit and rollback handling

Each phase must have clear logs and a narrow failure boundary.

## Phase 1: Scene Preparation

Add `EnsureReplayBootstrapReadyAsync()` to prepare a gameplay-capable scene environment without starting a real run.

Responsibilities:

- load `SceneID.GameScene`
- load `SceneID.GameplayLoading` only if required by gameplay services
- wait for `BoardManager` and `GameServiceManager` to exist and initialize
- set the active scene correctly
- unload temporary loading scenes once ready

Readiness is defined only by replay-critical objects being present, not by run lifecycle state.

## Phase 2: Replay Dependency Resolution

Add `ResolveReplayDependencies()` to centralize reflection-based access to native replay dependencies.

Dependencies to resolve:

- `SocketBehavior.GetInstance()`
- `NetMessageProcessor`
- `AppState._gameSimHandler`
- writable `LastCombatSequence`
- invokable `CombatSequenceCreated`

These should be wrapped in a small internal context object so the runtime does not keep repeating reflection lookups during replay start.

## Phase 3: Saved Sequence Injection

Add `TryInjectSavedReplayAsync(sequence, replayId)` to execute the native replay start in the strict existing order:

1. set `LastCombatSequence`
2. call `GameSimHandler.Handle(spawnMessage)`
3. trigger `CombatSequenceCreated`
4. `AppState.TryPushState<ReplayState>()`

The existing ordering is important because the opening `GameSim` must populate runtime state before `ReplayState` begins consuming the combat sequence.

## Phase 4: Exit and Rollback Handling

If the replay was bootstrapped from the lobby:

- exiting `ReplayState` should return to the main menu
- bootstrap flags should be cleared

If bootstrap fails at any point:

- call `AppState.Reset()`
- call `Data.ResetRunData()`
- unload temporary scenes if still loaded
- return to `SceneID.HeroSelectScene`

Failure cleanup must not depend on any run lifecycle event.

## Internal Structure

Recommended runtime structure in `Game/CombatReplay/CombatReplayRuntime.cs`:

- `EnsureReplayBootstrapReadyAsync()`
- `ResolveReplayDependencies()`
- `TryInjectSavedReplayAsync(...)`
- `RollbackReplayBootstrapAsync()`
- `ReplayBootstrapContext`

`ReplayBootstrapContext` should contain the reflected objects and delegates needed for replay injection so the call graph stays explicit and debuggable.

## Verification Strategy

This feature will be accepted with code-path review plus local logs, not packet capture.

Required code-path guarantees:

- no `RunManager.StartRun()` call in the saved replay bootstrap path
- no `Events.RunStarted.Trigger()` call in the saved replay bootstrap path

Required runtime logs:

- bootstrap start with current scene and app state
- scene readiness completion
- dependency resolution completion
- replay sequence injection completion
- `ReplayState` entry success
- rollback path on failure
- return-to-menu path after replay exit

## Test Strategy

Extend `tests/CombatReplayRecording.Tests/Program.cs` with source assertions that:

- `CombatReplayRuntime.cs` does not contain `StartRun()`
- `CombatReplayRuntime.cs` does not contain `Events.RunStarted.Trigger()`
- `EnsureReplayBootstrapReadyAsync` exists
- `ResolveReplayDependencies` exists
- `RollbackReplayBootstrapAsync` exists
- `HandleSpawnMessageAsync(...)` still appears before `TriggerCombatSequenceCreated(...)`

Verification commands:

- `dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`
- `dotnet build BazaarPlusPlus.csproj`

## Risks

The main risk is that `ReplayState` may implicitly depend on objects that are currently created only during a normal run start. If that happens, the correct response is to extend the minimum bootstrap chain, not to restore `StartRun()`.

## Success Criteria

This design is successful when:

- saved replays can be started from the lobby
- the bootstrap path no longer calls real run-start entrypoints
- replay startup failures are diagnosable from logs
- replay exit cleanly returns the player to the main menu
