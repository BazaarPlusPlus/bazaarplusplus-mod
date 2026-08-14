# ADR-0003: Replay exit is explicit through the agent `Continue` action

Status: Accepted

## Decision

The V1 replay-control endpoints were removed. When replay phase is `finishedAwaitingContinue`, V3 publishes a cardless `Continue` action in the `Flow` group. The external decision agent treats it as an opaque flow advance and remains replay-agnostic.

## Why

A replay remains in `ReplayState` after playback. Video finalization depends on the eventual exit, so tick-driven auto-exit races external orchestration and can orphan a recording. Exit timing must remain explicit.

Once automatic replay exit was removed, the ordinary action loop had no legal way to advance after combat; reusing `StartOrContinueRun` would also trigger unrelated run-start semantics. Hence the dedicated `Continue` action.

## Guardrails

- `CombatReplayRuntime.TryContinueReplay` is the only programmatic `ReplayState` exit. It rejects while starting, playing, capturing recap post-roll, or already exiting ([runtime](../../src/BazaarPlusPlus/Game/CombatReplay/CombatReplayRuntime.cs#L763-L807)).
- The host never calls `ReplayState.Exit()` directly; an architecture test pins the single-exit boundary ([test](../../tests/Architecture.Tests/CoreLayeringTests.cs#L1226-L1272)).
- Emit `Continue` only at `FinishedAwaitingContinue` ([context reader](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs#L582-L595)); route it through `BazaarAgentGameBridge.CurrentRecorder.TryContinueReplay`; never add another exit path ([dispatcher](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs#L168-L177)).
- The additive `Continue` action kind is part of schema `2.2.0` ([contract](../../src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs#L9-L29)); validator tests pin availability/staleness behavior ([tests](../../tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs#L503-L522)).
- Keep replay transport primitive and policy-free: one recording per battle; batching and concatenation stay external. Mid-playback skipping remains out of scope; phase races fail safely and retry on a later snapshot.

## Ghost payload perspective contract

The manifest inside a ghost replay payload always uses the recorder perspective: the challenger occupies the `Player` side. `GhostBattlePayload.PerspectiveVersion` identifies the stored convention: `0` is the legacy local perspective and is migrated automatically when loaded; `1` is the recorder perspective. External delivery must send ghost payloads with `PerspectiveVersion = 1`.
