# BazaarPlusPlus

[中文](README.md)

BazaarPlusPlus is a BepInEx mod for *The Bazaar*. It adds combat UI enhancements, monster and tooltip previews, run logging, an in-game history panel, local combat replay playback, and background upload features.

This repository only keeps documentation that still matches the current implementation. If any document conflicts with the code, treat `Plugin.cs`, `Core/`, `Game/`, `Patches/`, and `Data/` as the source of truth.

## Feature Overview

- Combat status bar: shows logical combat time, processed frames, pause state, and discrete speed multipliers in a bottom HUD.
- Monster preview: the default flow uses the game's native monster preview; Bazaar++ adds targeted tooltip augmentation and reuses `MonsterBoardTooltip` for custom board displays.
- Enchant / upgrade preview: appends enchant text to the native tooltip flow and enters the native upgrade preview path while the upgrade modifier key is held.
- Run Logging and HistoryPanel: active runs are written to SQLite; the in-game panel can browse runs, PvP battles, ghost battles, and saved board snapshots.
- Combat replay: saves local PvP replay payloads; `HistoryPanel` and the debug panel can replay saved battles when the required conditions are met.
- Background upload: run and replay upload, performed only while the client is outside a live run.
- Anonymous Mode: replaces the local player name with `Anonymous`.

## Installation And Configuration

- Runtime prerequisites: *The Bazaar* and BepInEx 5 must already be installed.
- For manual installation, copy `BazaarPlusPlus.dll` and the SQLite runtime dependencies from the build output into the game's `BepInEx/plugins/` directory.
- After the first launch, configuration is written to `BepInEx/config/BazaarPlusPlus.cfg`.
- Detailed notes for feature-specific settings, hotkeys, and debug surfaces live under `docs/reference/`.

## Building From Source

- The project targets `netstandard2.1` and expects a .NET SDK that can build C# 12 projects.
- The main mod project and most test projects resolve game assemblies through `ManagedPath`. If auto-detection does not find your local install, pass it explicitly during the build.
- Common commands:

```bash
dotnet build
dotnet build -p:ManagedPath=/path/to/TheBazaar_Data/Managed
./run.sh build
./run.sh all
```

- Build behavior:
  - Debug builds automatically copy the plugin into `BepInEx/plugins/` when the local game directory is detected.
  - Release builds copy artifacts into the adjacent `../bazaarplusplus-installer` repository when it exists.

## Data And Network Behavior

- Run logging and combat replay store local SQLite data and replay payloads.
- Background upload only scans for uploads while the client is outside a live run.
- The `ModCFServerV3/` directory contains the current Cloudflare Worker backend used for uploads, ghost battles, and replay download links.

## Repository Layout

- `Plugin.cs`: BepInEx runtime entry point.
- `Core/`, `Game/`, `Patches/`, `Data/`: main feature implementation.
- `tests/`: feature-focused test projects.
- `scripts/`: build helpers and utility scripts.
- `ModCFServerV3/`: cloud sync backend.

## Documentation Entry Points

- `docs/mod-features-overview.md`: overview of the currently implemented feature set.
- `docs/run-logging.md`: run logging, history panel, and ghost battles.
- `docs/run-upload.md`: upload behavior, trust model, and constraints.
- `docs/reference/`: hotkeys, settings surfaces, SQLite schema, tooltip internals, and related reference material.

## License

This project is released under the MIT License. See `LICENSE`.

## Acknowledgements

- Inspiration: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- Data reference: [bazaardb.gg](https://bazaardb.gg)
- Runtime dependency: [BepInEx](https://github.com/BepInEx/BepInEx)
