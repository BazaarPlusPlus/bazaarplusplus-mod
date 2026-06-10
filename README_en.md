# BazaarPlusPlus

[中文](README.md)

BazaarPlusPlus is a BepInEx mod for *The Bazaar*. It adds combat UI enhancements, monster and tooltip previews, run logging, an in-game history panel, local combat replay playback, end-of-run automatic screenshots, and background upload features.

Current entry-point docs are calibrated against the current implementation; `docs/archive/` is preserved only for historical context. If any document conflicts with the code, treat the actual implementation under `src/BazaarPlusPlus/` (`Plugin.cs`, `Core/`, `Game/`, `Patches/`, ...) as the source of truth.

## Feature Overview

- Combat status bar: shows logical combat time, processed frames, pause state, and discrete speed multipliers in a bottom HUD.
- Monster preview: fully delegates to the game's native monster preview without modification.
- Enchant / upgrade preview: enchant preview has a visibility mode (Off / AutoOnPedestalChoice / Always, default Always); Auto mode shows the matching preview while an enchant pedestal is offered on the choice screen; Ctrl / Shift still work as manual overrides.
- Run Logging and HistoryPanel: active runs are written to SQLite; the in-game panel can browse runs, PvP battles, ghost battles, and saved board snapshots.
- Combat replay: saves local PvP replay payloads; `HistoryPanel` can replay saved battles when the required conditions are met.
- End-of-run automatic screenshots: saves the primary final-run screenshot and SQLite metadata before `Continue`.
- Background upload: run and replay upload, performed only while the client is outside a live run.
- BazaarDB screenshot upload: optional toggle that pushes end-of-run snapshot DTOs to the V4 mod backend (`bazaarplusplus-server` repo, deployed at `mod-api-v4.bazaarplusplus.com`) for BazaarDB to pull through the peek/confirm delivery queue (off by default).
- Collection Panel: a full-screen Item/Skill card browser, opened with Tab or the lobby dock button; supports filtering by hero, quality, size, merchant and trainer source, and run day.
- Live Build Panel: toggled in-run with CapsLock; displays live shop / board / stash and, once you pick candidate items, shows matching ten-win final-build recommendations (data sourced from the cloud analyzer-v4 `tenwin_builds.json`, cached locally with a background refresh, with a bundled seed copy for cold-start fallback).
- Anonymous Mode: replaces the local player name with `Anonymous`.
- **BazaarAgent HTTP endpoint** (optional host plugin, not installed by default) — local loopback HTTP server (fixed at `127.0.0.1:47900`) exposing the current decision context (`GET /v1/context`) and accepting external-tool actions (`POST /v1/actions`). The mod itself takes no autonomous decisions. **The host is a separate BepInEx plugin**, built on demand with `./run.sh build --with-bazaaragent`; installing the host dll starts it automatically, while default builds ship only the main plugin and actively scrub the host dlls. See [docs/ARCHITECTURE.md#bazaaragent-optional-host](docs/ARCHITECTURE.md#bazaaragent-optional-host).

## Installation And Configuration

- Runtime prerequisites: *The Bazaar* and BepInEx 5 must already be installed.
- For manual installation, copy `BazaarPlusPlus.dll`, `BazaarPlusPlus.ModApi.dll`, `BazaarPlusPlus.Storage.dll`, `BazaarPlusPlus.Localization.dll`, and the native SQLite runtime dependency from the build output into the game's `BepInEx/plugins/` directory.
- After the first launch, configuration is written to `BepInEx/config/BazaarPlusPlus.cfg`.
- Current architecture and major runtime notes live in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); see [docs/README.md](docs/README.md) for the documentation index.

## Building From Source

- The project targets `netstandard2.1` and expects a .NET SDK that can build C# 12 projects.
- The main mod project and the test projects that reference game assemblies resolve game assemblies through `ManagedPath`. If auto-detection does not find your local install, pass it explicitly during the build.
- Common commands:

```bash
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj -p:ManagedPath=/path/to/TheBazaar_Data/Managed
./run.sh build
./run.sh all
```

- Build behavior:
  - Debug builds automatically copy the plugin into `BepInEx/plugins/` when the local game directory is detected.
  - Release builds copy artifacts into the adjacent `../bazaarplusplus-installer` repository when it exists.

## Data And Network Behavior

- Run logging, combat replay, and end-of-run screenshots store local SQLite data, replay payloads, and screenshot files. Cloud sync itself carries no authentication credentials.
- Background upload only scans for uploads while the client is outside a live run.
- The cloud backend (uploads, ghost battles, replay links, BazaarDB snapshot delivery) now lives in a separate repository `bazaarplusplus-server`, deployed at `mod-api-v4.bazaarplusplus.com`. The mod-side HTTP client lives in `src/BazaarPlusPlus.ModApi/`.

## Repository Layout

> The Chinese [README.md](README.md) is authoritative for the repository layout; this section is a summary.

- `src/BazaarPlusPlus/`: the main plugin project — `Plugin.cs` (BepInEx runtime entry point), `BppComposition.cs`, and the `Core/`, `GameInterop/`, `Game/`, `Patches/`, `Infrastructure/` layers plus `Data/` embedded resources (e.g. build recommendation JSON).
- `src/BazaarPlusPlus.ModApi/`, `src/BazaarPlusPlus.Storage/`, `src/BazaarPlusPlus.Localization/`: the HTTP-client, local-persistence, and localization assemblies (zero game/Unity/BepInEx dependencies), wired into the mod via `BppComposition.cs`.
- `src/BazaarPlusPlus.BazaarAgent/`, `src/BazaarPlusPlus.BazaarAgentHost/`: the optional BazaarAgent pure core and host plugin (not installed by default; build on demand with `./run.sh build --with-bazaaragent`).
- `tests/`: feature-focused test projects.
- `run.sh`: local build, test, format, and decompile entry point.

## Documentation Entry Points

- [docs/README.md](docs/README.md): documentation index and lifecycle rules.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): living architecture for the current implementation, organized by topic and grounded in code paths.
- [docs/adr/](docs/adr/): architecture decision records. This repository keeps the existing ADR convention instead of a separate `docs/decisions/` tree.
- [docs/plans/](docs/plans/): active or needs-human-decision future work only.
- [docs/archive/](docs/archive/): implemented, superseded, or historical documents. Archived content is not current implementation guidance.

## License

This project is released under the MIT License. See `LICENSE`.

## Acknowledgements

- Inspiration: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- Data reference: [bazaardb.gg](https://bazaardb.gg)
- Runtime dependency: [BepInEx](https://github.com/BepInEx/BepInEx)
