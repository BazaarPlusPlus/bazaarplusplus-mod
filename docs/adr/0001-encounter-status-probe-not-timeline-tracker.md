# Use an on-demand encounter status probe, not a run-timeline tracker

We expose the player's current run/encounter state through an on-demand, pull-based probe (`IEncounterStateProbe`) and deliberately do **not** record an event-sourced timeline of a run's decision flow. The probe is split by read cost: lightweight encounter ids, choice-screen pedestal classification, and heavier target-selection legality.

## Context

A full "EncounterTracker" was designed and adversarially reviewed: a live tracker fed by the `GameSimEvent` funnel that would record an ordered timeline of every encounter, acquisition, mutation, and outcome into `run_events` for local run-path reconstruction. We rejected it.

It required a large amount of fragile capture code — PVPCombat attribution carve-out, loot/interrupt attribution edges, reroll divergence, transform/fuse item lineage, gap-detection/`resynced` markers — to serve a reconstruction consumer that **does not exist**: the local exporter (`scripts/export_run_log.py`) was never written (its test skips when the script is absent), and uploading the timeline to the server was out of scope. The status probe already serves the only live consumer (the upgrade/enchant preview "smart" mode) with none of that machinery.

## Consequences

- Encounter state is queryable as **"now"**, not as history. If a real timeline consumer ever materializes (e.g. server-side run-path reconstruction or analytics on decision chains), reopen this decision.
- The dead encounter-selection scaffolding in `Storage/RunLog` — `RunLogOptionSnapshot`, `RunLogPendingSelectionState`, and `RunLogEvent`'s selection fields (`Options`, `SelectionSeq`, `SelectionFingerprint`, `SelectionContextRules`, `Selected*`) — is removed rather than wired up.
- Subsystems that need current encounter facts read the narrow probe snapshot they need, or — for message-bound readers like PvpBattles whose data is a per-battle network-message snapshot, not "now" — share the probe's pure resolvers (id resolution, the PVPCombat carve-out rule) rather than reading the probe directly.
