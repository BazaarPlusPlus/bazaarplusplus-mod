# ADR-0007: Control external replay recording with primitive endpoints

Status: Accepted

## Decision

Expose replay recording through the existing loopback server as three primitives: raw `POST /v1/replay/record`, replay phase/battle id in `GET /v1/context`, and explicit `POST /v1/replay/continue`. The mod owns no batch-recording state machine; an external caller serializes the loop and polls phase/output.

## Why

A replay remains in `ReplayState` after playback. Video finalization depends on the eventual exit, so tick-driven auto-exit races external orchestration and can orphan a recording. Exit timing must remain explicit.

## Guardrails

- The pure core queues raw replay commands and drains them before the ordinary snapshot cadence ([routes](../../src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs#L175-L203), [controller](../../src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentRuntimeController.cs#L65-L75)).
- Accept recording only after payload/battle-id and recording-capability guards; `record` acknowledges start and the caller polls `replayPhase` plus the output file.
- `CombatReplayRuntime.TryContinueReplay` is the only programmatic `ReplayState` exit. It rejects while starting, playing, capturing recap post-roll, or already exiting ([runtime](../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs#L818-L860)).
- The host never calls `ReplayState.Exit()` directly; an architecture test pins the single-exit boundary ([test](../../tests/Architecture.Tests/CoreLayeringTests.cs#L904-L950)).
- Keep replay transport primitive and policy-free: one recording per battle; batching and concatenation stay external.
