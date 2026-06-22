# Expose replay continue as a first-class agent action so the controller stays replay-agnostic

When a battle ends the game sits in `ReplayState` at `finishedAwaitingContinue` and never advances on its own (ADR-0007). The only programmatic exit is `CombatReplayRuntime.TryContinueReplay`, today reachable only through the recording side-channel `POST /v1/replay/continue`. The external decision agent (the separate `bazaarplusplus-agent` repo) drives the run purely off `GET /v1/context`'s `availableActions` + `POST /v1/actions`; it deliberately knows nothing about replay. So after **every** combat it sees only `Wait / ExitState(canExit=false) / SellItem / MoveItem`, finds no way to advance, and stalls.

Decision: the context reader emits a generic **`Continue`** Flow action whenever the replay sits at `finishedAwaitingContinue`, and the dispatcher routes it to the same `TryContinueReplay` facade. The agent treats `Continue` as an opaque Flow action — replay stays entirely inside the mod.

## Context

- ADR-0007 deleted the tick auto-exit and made `CombatReplayRuntime.TryContinueReplay` the single programmatic `ReplayState` exit: it mirrors the native continue click and **rejects unless the replay is at `finishedAwaitingContinue`**. Exit timing belongs to the caller. An architecture test pins that no `.Exit()` call exists in the host assembly.
- `BazaarAgentGameContextReader.BuildActions` ([src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs)) emits no advance action for `Replay`; `ExitState` is listed but `canExit=false` and `ExitStateCommand` does not leave the replay.
- The agent's safe policy already classifies `Replay` as a flow state and looks for a `Flow`-group action there first (before `Sell`) — it has been waiting for a flow action the mod never sent. Reusing `StartOrContinueRun` is wrong: the agent resets its decision history on that kind, so every post-combat continue would wipe its build memory. A distinct kind avoids that.

## Decision

1. **New action kind `Continue`, group `Flow`.** Add `Continue` to `BazaarAgentActionKind` ([src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs](../../src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs)). Bump `BazaarAgentSchema.Version` `2.1.0 → 2.2.0` (additive — clients that don't know the kind simply never receive it).
2. **Context reader emits it only at `finishedAwaitingContinue`.** `BuildActions` takes the already-computed `replayPhase` and, when it equals `FinishedAwaitingContinue`, appends `{ ActionKind = Continue, Group = Flow, DisplayKey = "Continue" }` — no card, no target.
3. **Dispatcher routes `Continue` to the existing facade.** `BazaarAgentGameActionDispatcher.Dispatch` ([src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs)) gains `case Continue` → `BazaarAgentGameBridge.CurrentRecorder?.TryContinueReplay()`, mapping `Accepted` to success and any other outcome (or a null recorder) to a failure with the reason. This makes the dispatcher a **second caller** of `TryContinueReplay` — it introduces no new `ReplayState` exit, so ADR-0007's "no `.Exit()` in the host assembly" architecture test still holds.
4. **Validator unchanged.** `Continue` is not in `_cardBearingKinds` and is not `StartOrContinueRun`, so `BazaarAgentActionValidator` accepts it through the generic path: Rule 1 (known kind, satisfied by the enum), Rule 2 (present in `availableActions`), Rule 7 (`forTickId` match), Rule 8 (cooldown — effectively 0 after a battle). No validator code changes.
5. **Recording routes untouched.** `POST /v1/replay/record` and `POST /v1/replay/continue` stay for external recording scripts. Both the route and the new action converge on the same `TryContinueReplay`.
6. **The agent does not change.** `Replay` is already a flow state in the agent's policy, so it picks the `Flow` `Continue` first (never selling the freshly-won item); `Continue` is a new kind, so no history reset. The agent never learns the word "replay."

## Consequences

- **+** The controller is fully replay-agnostic and auto-advances every combat; no replay client lives in the agent.
- **+** Reuses the audited single-exit facade; no second exit path to keep in sync, no new architecture-test surface.
- **+** Symmetric with how `ReturnToMenu` already surfaces an out-of-`StateOps` advance as a `Flow` action — `Continue` is the same pattern for the replay gate.
- **−** The wire contract gains an action kind (schema `2.2.0`). The agent's "Replay is a flow state ⇒ a Flow action will be offered" assumption becomes load-bearing; it is pinned by an agent-side test (below).
- Mid-`playing` skipping is out of scope: `Continue` is emitted only at `finishedAwaitingContinue`. If the phase races between snapshot and dispatch, `TryContinueReplay` rejects and the agent retries on the next snapshot.

## Testing

- **Mod core (`tests/BazaarAgent.Tests/`, pure):**
  - Validator: a `Continue` present in `availableActions` validates `Ok`; absent → `StaleOrUnavailable` (409). (Also pins that the enum addition is recognized.)
- **Mod host (`BazaarPlusPlus.BazaarAgentHost`, game-coupled — not unit-testable):** the context-reader emit and the dispatcher route read live Unity statics (`AppState.CurrentState`, `BazaarAgentGameBridge.CurrentRecorder`) and are not reachable from the pure-core test project. They are verified by `dotnet build` (compile) plus the live checklist below — the same boundary every other Host adapter sits behind.
- **Agent (`bazaarplusplus-agent`):** one test that a `Replay`-state context carrying a `Flow` `Continue` action makes the safe policy choose `Continue` (not `SellItem`) — pinning the cross-repo assumption.
- **Live:** finish a battle → the replay's `finishedAwaitingContinue` auto-advances via the agent's `Continue` → the run proceeds to the next state.

## Related

- ADR-0007 (external replay video recording; established `TryContinueReplay` as the only programmatic exit).
- ADR-0005 (AutoBazaar isolated transport core), ADR-0006 (BazaarAgent as its own plugin).
- Agent-side transport: `bazaarplusplus-agent` `src/transport/modHttpClient.ts` (posts `/v1/actions`; no replay awareness).
