# ADR-0008: Expose replay continue as a generic agent action

Status: Accepted

## Decision

When replay phase is `finishedAwaitingContinue`, publish a cardless `Continue` action in the `Flow` group. Dispatch it to the same `TryContinueReplay` facade used by the explicit replay-control route. The external decision agent treats it as an opaque flow advance and remains replay-agnostic.

## Why

After ADR-0007 removed automatic replay exit, the ordinary action loop had no legal way to advance after combat. Reusing `StartOrContinueRun` would also trigger unrelated run-start semantics.

## Guardrails

- Emit `Continue` only at `FinishedAwaitingContinue` ([context reader](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs#L558-L597)).
- Route it through `BazaarAgentGameBridge.CurrentRecorder.TryContinueReplay`; never add another exit path ([dispatcher](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs#L148-L157)).
- Keep `POST /v1/replay/continue` for recording tools; both entry points converge on the same facade.
- The additive action kind is part of schema `2.2.0` ([contract](../../src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs#L9-L29)); validator tests pin availability/staleness behavior ([tests](../../tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs#L487-L502)).
- Mid-playback skipping remains out of scope; phase races fail safely and retry on a later snapshot.
