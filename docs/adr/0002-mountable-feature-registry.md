# Mount MonoBehaviour features through a one-line mountable registry

`MonoBehaviour` features are registered as `IBppMountable` entries in `BppComposition` and mounted in one pass by `BppMountableRegistry.MountAll(host)`, rather than hand-wired imperatively in `Plugin.cs`. Turning a feature on or off is adding or removing one `_mountables.Register(...)` line at composition time.

## Context

Non-`MonoBehaviour` modules already had a clean pattern: `IBppFeature` (`Start()`/`Stop()`) + `BppFeatureRegistry`, owned by `BppComposition`. `MonoBehaviour` features had no equivalent — each of ~10 (AutoBazaar, combat replay, history panel, status bar, run logging, screenshots, tooltip refresh, video recorder, …) was hand-wired in three places in `Plugin.cs` (`using`, `AttachRuntimeComponents`, `DetachRuntimeComponents`). To DLL-disable one you had to touch all three, and a cfg flag like `AutoBazaarEnabled` only gated the inner HTTP listener — the `MonoBehaviour`, its `Update` tick, snapshot publisher, and reflection probes still loaded and ran every frame.

AutoBazaar was the first user (see [archived spec](../design/archive/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md)). The same change also extracted AutoBazaar's incidental encounter reads into a pull-based `IEncounterStateProbe`; that adapter now lives under `GameInterop/Encounter/`, matching its game-runtime read surface. This is the probe surface that [ADR-0001](0001-encounter-status-probe-not-timeline-tracker.md) settles on and that [tooltip-preview.md](../features/tooltip-preview.md) consumes.

## Consequences

- A feature's entire lifecycle (no component attached, no tick, no probes, no listener) is controlled by the presence of its registration line. AutoBazaar's `AutoBazaarHostMount` line is currently commented out in `BppComposition.cs` — that is how the subsystem is "parked" (see [autobazaar.md](../features/autobazaar.md)).
- **As-built divergence from the spec**: the spec scoped this to AutoBazaar only and listed migrating the other features as a non-goal. In practice the abstraction generalized to a `ComponentMount<T>` helper and ~9 features now register through it (`ComponentMount<RunLoggingController>`, `ComponentMount<CombatStatusBar>`, ...); only `HistoryPanelMount` stays bespoke (it needs `Func<>` lazy resolution of the online client + combat replay runtime). The current registrations live in `BppComposition.cs`.
- New features are expected to register here rather than re-introduce hand-wiring in `Plugin.cs`. The only bootstrap-special case is `CombatReplayRuntime`, which must be constructed before `composition.Start()`.

Full design history: [docs/design/archive/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md](../design/archive/2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md).
