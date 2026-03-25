# Docs

This directory keeps only documents that still describe the current `bazaarplusplus-mod`
codebase. Source files under `Plugin.cs`, `Core/`, `Game/`, `Patches/`, `Data/`, `scripts/`,
and `tests/` remain the source of truth.

## Start Here

- `../README.md`: repository-level feature summary and build / packaging notes
- `combat-status-bar.md`: runtime HUD, speed control, and combat-frame timing model
- `monster-preview-design.md`: monster preview overlay pipeline and data sources
- `run-logging.md`: run log capture, SQLite persistence, history UI, and export flow

## Reference Docs

- `reference/combat-replay-recording.md`: saved replay capture and playback architecture
- `reference/upgrade-tooltip-implementation.md`: enchant and upgrade tooltip behavior
- `reference/settings-and-debug-surfaces.md`: gameplay settings toggles, tooltip keybind rows, and debug-only panels
- `reference/sqlite-schema-reference.md`: current SQLite schema and read/write paths
- `reference/netmessage-data-reference.md`: useful `NetMessage*` field notes from decompiled runtime
- `reference/card-board-type-reference.md`: inventory / board enum notes
- `reference/run-history-decompiled-analysis.md`: why BazaarPlusPlus records run history itself

## Notes

- Historical implementation plans were removed from `docs/` because they no longer reflect the
  live repository state.
- If historical execution context is needed, use git history instead of stale plan files.
- `reference/run-history-decompiled-analysis.md` is retained as background for the shipped
  `Game/RunLogging/` implementation, not as a pending design doc.
