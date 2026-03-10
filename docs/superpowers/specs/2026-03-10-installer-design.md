# BazaarPlusPlus Installer Design

**Date:** 2026-03-10
**Status:** Approved

## Overview

A standalone desktop installer for BazaarPlusPlus, built with Tauri 2 (Rust backend) + Svelte frontend. Targets macOS and Windows. Presents a single-page dashboard that detects the environment, then installs BepInEx and the mod with one click.

## Project Structure

```
BazaarPlusPlus/
  installer/
    src-tauri/
      src/
        main.rs
        commands/
          detect.rs      # Steam path + .NET detection
          bepinex.rs     # BepInEx extraction & installation
          vdf.rs         # VDF read/write
        lib.rs
      Cargo.toml
      tauri.conf.json    # BepInEx zip listed under resources
    src/
      App.svelte         # Single-page dashboard
      lib/types.ts       # Shared types between frontend and backend
    package.json
```

## Backend Modules

### detect.rs

Runs automatically on app startup. Exposes a single `detect_environment` Tauri command returning a struct with all detection results.

- **Steam directory:** Windows — registry `HKCU\Software\Valve\Steam`; macOS — `~/Library/Application Support/Steam`
- **Game path:** Parse `Steam/steamapps/libraryfolders.vdf` to find The Bazaar (App ID `2138550`) across all Steam library folders
- **.NET runtime:** Run `dotnet --list-runtimes`, check for `Microsoft.NETCore.App 6.x+` (BepInEx 6 requirement) and presence of netstandard2.1-compatible runtime
- **BepInEx status:** Check for `<game_dir>/BepInEx/core/BepInEx.Core.dll`

### bepinex.rs

- Locate bundled BepInEx zip via `app.path().resource_dir()`
- Extract zip contents into the game directory
- No network required; BepInEx is bundled at build time via `tauri.conf.json` resources

### vdf.rs

- **Read** `Steam/userdata/<userid>/config/localconfig.vdf`
- **Locate** App ID `2138550` → `LaunchOptions` key
- **Inject** BepInEx doorstop launch arguments (platform-appropriate)
- **Backup** original file to `localconfig.vdf.bak` before writing
- **Write** modified VDF back to disk

## Frontend Dashboard

Single Svelte page, three vertical sections:

### Detection Status Cards (top)
Four cards updated automatically on startup:
| Card | States |
|------|--------|
| Steam Path | Found (path) / Not Found |
| The Bazaar Path | Found (path) / Not Found |
| .NET Runtime | Version string / Requirements not met |
| BepInEx | Already installed / Not installed |

### Action Area (middle)
- **Install / Reinstall** button — enabled only when Steam and game path are found
- Manual game path override: text input + native folder picker (`dialog::open`)

### Log Area (bottom)
- Scrolling log output
- Backend emits `installer://log` events with progress messages during installation
- Final state: success banner or error message with details

## Key Rust Dependencies

```toml
keyvalues-parser = "0.2"   # VDF parsing
reqwest = { features = ["json", "stream"] }  # Future network requests
zip = "2"                   # BepInEx extraction
tauri = { version = "2", features = ["dialog"] }
```

## Error Handling

- All Tauri commands return `Result<T, String>` — errors surface in the frontend log area
- VDF write is atomic: backup first, write to temp file, then rename
- If .NET requirements are not met, the Install button shows a warning tooltip but remains enabled (informational only, not a hard block)

## Platform Notes

| | macOS | Windows |
|---|---|---|
| Steam path | `~/Library/Application Support/Steam` | Registry `HKCU\Software\Valve\Steam` |
| BepInEx doorstop args | `DYLD_INSERT_LIBRARIES=./doorstop_libs/libdoorstop.dylib` | `--doorstop-enable true` via winhttp proxy |
| .NET detection | `dotnet` via PATH | `dotnet` via PATH or `%PROGRAMFILES%\dotnet` |
