# ADR-0001: Query current encounter state; do not record a timeline

Status: Accepted

## Decision

Expose current encounter state through the pull-based `IEncounterStateProbe`: cheap encounter ids, choice/pedestal classification, and a separate targeting-legality read. Do not turn this seam into an event-sourced run timeline.

## Why

A timeline needs fragile attribution for rerolls, interrupts, PVP combat, item transforms, and recovery gaps. The original proposal had no shipping consumer; current UI and agent consumers need only “what is true now.” A future choice timeline is tracked separately in [#33](https://github.com/cauyxy/bazaarplusplus-mod/issues/33) and must revisit this decision instead of overloading the live probe.

## Guardrails

- Keep reads split by cost and main-thread only; the implementation caches each result per frame ([interface](../../src/BazaarPlusPlus/Core/GameState/IEncounterStateProbe.cs#L5-L19), [implementation](../../src/BazaarPlusPlus/GameInterop/Encounter/EncounterStateProbe.cs#L27-L112)).
- Consumers may share pure identity resolvers, but must not infer historical ordering from probe snapshots.
- Reopen only for a concrete persisted or uploaded timeline consumer with explicit attribution and recovery semantics.
