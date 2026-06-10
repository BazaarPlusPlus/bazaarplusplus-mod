---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# PvP Battles

`Game/PvpBattles` is the shared battle-evidence module for Bazaar++ PvP combat records.

It is not owned by `CombatReplay`, `HistoryPanel`, or `RunLogging`:

- `CombatReplay` uses it to recognize PvP combat windows, collect snapshots, and build replay artifacts.
- `HistoryPanel` uses its snapshot models to project historical player/opponent boards.
- `RunLogging` uses `PvpBattleRecorded` and persisted battle ids to attach combat evidence to a run.

Keep this module in `Game/PvpBattles` because it contains feature-owned battle semantics, storage models, and event payloads. Do not move it to `GameInterop/`: `GameInterop/` is for reusable adapters over game/Unity runtime surfaces, while `PvpBattles` is BPP-owned battle evidence consumed by multiple features.

## Key Files

- `Game/PvpBattles/PvpBattleSnapshotCollector.cs`
- `Game/PvpBattles/PvpBattleSequenceMatcher.cs`
- `Game/PvpBattles/PvpBattleManifestFactory.cs`
- `Game/PvpBattles/PvpReplayPayloadFactory.cs`
- `Game/PvpBattles/PvpBattleRecorded.cs`
- `Game/PvpBattles/Persistence/PvpBattleSqliteStore.cs`

## Consumers

- `Game/CombatReplay/CombatReplayCaptureService.cs`
- `Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs`
- `Game/RunLogging/RunLoggingModule.cs`
