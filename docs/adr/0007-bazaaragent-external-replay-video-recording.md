# ADR-0007: Replay exit is explicit — primitive replay-control endpoints plus the agent `Continue` action

Status: Accepted; absorbs ADR-0008 (collapsed 2026-07-28)

## Decision

Expose replay recording through the existing loopback server as three primitives: raw `POST /v1/replay/record`, replay phase/battle id in `GET /v1/context`, and explicit `POST /v1/replay/continue`. The mod owns no batch-recording state machine; an external caller serializes the loop and polls phase/output.

For the ordinary action loop (absorbed from ADR-0008): when replay phase is `finishedAwaitingContinue`, publish a cardless `Continue` action in the `Flow` group. The external decision agent treats it as an opaque flow advance and remains replay-agnostic. Both entry points — the replay-control route and the agent action — converge on the same `CombatReplayRuntime.TryContinueReplay` facade.

## Why

A replay remains in `ReplayState` after playback. Video finalization depends on the eventual exit, so tick-driven auto-exit races external orchestration and can orphan a recording. Exit timing must remain explicit.

Once automatic replay exit was removed, the ordinary action loop had no legal way to advance after combat; reusing `StartOrContinueRun` would also trigger unrelated run-start semantics. Hence the dedicated `Continue` action.

## Guardrails

- The pure core queues raw replay commands and drains them before the ordinary snapshot cadence ([routes](../../src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs#L175-L203), [controller](../../src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentRuntimeController.cs#L65-L75)).
- Accept recording only after payload/battle-id and recording-capability guards; `record` acknowledges start and the caller polls `replayPhase` plus the output file.
- `CombatReplayRuntime.TryContinueReplay` is the only programmatic `ReplayState` exit. It rejects while starting, playing, capturing recap post-roll, or already exiting ([runtime](../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs#L763-L807)).
- The host never calls `ReplayState.Exit()` directly; an architecture test pins the single-exit boundary ([test](../../tests/Architecture.Tests/CoreLayeringTests.cs#L1226-L1272)).
- Emit `Continue` only at `FinishedAwaitingContinue` ([context reader](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs#L582-L595)); route it through `BazaarAgentGameBridge.CurrentRecorder.TryContinueReplay`; never add another exit path ([dispatcher](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs#L168-L177)).
- The additive `Continue` action kind is part of schema `2.2.0` ([contract](../../src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs#L9-L29)); validator tests pin availability/staleness behavior ([tests](../../tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs#L503-L522)).
- Keep replay transport primitive and policy-free: one recording per battle; batching and concatenation stay external. Mid-playback skipping remains out of scope; phase races fail safely and retry on a later snapshot.
