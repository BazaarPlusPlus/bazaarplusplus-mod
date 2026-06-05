# BazaarPlusPlus

[中文](README.md)

BazaarPlusPlus is a BepInEx mod for *The Bazaar*. It adds combat UI enhancements, monster and tooltip previews, run logging, an in-game history panel, local combat replay playback, end-of-run automatic screenshots, and background upload features.

This repository only keeps documentation that still matches the current implementation. If any document conflicts with the code, treat `Plugin.cs`, `Core/`, `Game/`, and `Patches/` as the source of truth.

## Feature Overview

- Combat status bar: shows logical combat time, processed frames, pause state, and discrete speed multipliers in a bottom HUD.
- Monster preview: fully delegates to the game's native monster preview without modification; the CardSet preview reuses `MonsterBoardTooltip` as a host to display custom board content.
- Enchant / upgrade preview: enchant preview has a visibility mode (Off / AutoOnPedestalChoice / Always, default Always); Auto mode shows the matching preview while an enchant pedestal is offered on the choice screen; Ctrl / Shift still work as manual overrides.
- Run Logging and HistoryPanel: active runs are written to SQLite; the in-game panel can browse runs, PvP battles, ghost battles, and saved board snapshots.
- Combat replay: saves local PvP replay payloads; `HistoryPanel` can replay saved battles when the required conditions are met.
- End-of-run automatic screenshots: saves the primary final-run screenshot and SQLite metadata before `Continue`.
- Background upload: run and replay upload, performed only while the client is outside a live run.
- BazaarDB screenshot upload: optional toggle that pushes end-of-run snapshot DTOs to the V4 mod backend (`bazaarplusplus-server` repo, deployed at `mod-api-v4.bazaarplusplus.com`) for BazaarDB to pull through the peek/confirm delivery queue (off by default).
- Anonymous Mode: replaces the local player name with `Anonymous`.
- **BazaarAgent HTTP endpoint** (optional host plugin, not installed by default) — local loopback HTTP server (fixed at `127.0.0.1:47900`) exposing the current decision context (`GET /v1/context`) and accepting external-tool actions (`POST /v1/actions`). The mod itself takes no autonomous decisions. **The host is a separate BepInEx plugin**, built on demand with `./run.sh build --with-bazaaragent`; installing the host dll starts it automatically, while default builds ship only the main plugin and actively scrub the host dlls. See [docs/features/bazaar-agent.md](docs/features/bazaar-agent.md).

## Installation And Configuration

- Runtime prerequisites: *The Bazaar* and BepInEx 5 must already be installed.
- For manual installation, copy `BazaarPlusPlus.dll` and the SQLite runtime dependencies from the build output into the game's `BepInEx/plugins/` directory.
- After the first launch, configuration is written to `BepInEx/config/BazaarPlusPlus.cfg`.
- Detailed notes for feature-specific settings, hotkeys, and debug surfaces live under [docs/reference/](docs/reference/); see [docs/README.md](docs/README.md) for the full documentation index.

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

- Run logging, combat replay, and end-of-run screenshots store local SQLite data, replay payloads, and screenshot files. Cloud sync itself carries no authentication credentials.
- Background upload only scans for uploads while the client is outside a live run.
- The cloud backend (uploads, ghost battles, replay links, BazaarDB snapshot delivery) now lives in a separate repository `bazaarplusplus-server`, deployed at `mod-api-v4.bazaarplusplus.com`. The mod-side HTTP client lives in `BazaarPlusPlus.ModApi.csproj`.

## Repository Layout

> The Chinese [README.md](README.md) is authoritative for the repository layout; this section is a summary.

- `Plugin.cs`: BepInEx runtime entry point.
- `Core/`, `Game/`, `Patches/`: main feature implementation.
- `Data/`: embedded resources (e.g. build recommendation JSON).
- `ModApi/`, `Storage/`: the HTTP-client and local-persistence csprojs (zero game/Unity/BepInEx dependencies), wired into the mod via `BppComposition.cs`.
- `tests/`: feature-focused test projects.
- `run.sh`: local build, test, format, and decompile entry point.

## Documentation Entry Points

- [docs/README.md](docs/README.md): **the documentation index** (all docs organized by audience and lifecycle).
- [docs/mod-features-overview.md](docs/mod-features-overview.md): overview of the currently implemented feature set.
- [docs/features/](docs/features/): living per-feature docs (run logging & upload, combat replay, history panel, screenshots, tooltip preview, …).
- [docs/reference/](docs/reference/): stable contracts / inventories (hotkeys, settings surfaces, SQLite schema, BazaarAgent HTTP API).
- [docs/adr/](docs/adr/): architecture decision records.

## License

This project is released under the MIT License. See `LICENSE`.

## Acknowledgements

- Inspiration: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- Data reference: [bazaardb.gg](https://bazaardb.gg)
- Runtime dependency: [BepInEx](https://github.com/BepInEx/BepInEx)
