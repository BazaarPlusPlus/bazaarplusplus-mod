# BazaarPlusPlus Installer

Desktop installer for BazaarPlusPlus, built with Tauri, SvelteKit, and TypeScript.

## Development

Requirements:

- Node.js and npm
- Rust toolchain

Start the desktop app in development mode:

```bash
./build.sh
```

Or run the underlying commands manually:

```bash
npm install
npm run tauri dev
```

## Build

Build the desktop bundle:

```bash
./build.sh --prod
```

Artifacts are written under `src-tauri/target/release/`.

## Structure

- `src/`: SvelteKit frontend
- `src-tauri/`: native Tauri commands and packaging
- `scripts/prebuild-check.mjs`: build-time validation

## Known Limitation

- On macOS, the current blocker is BepInEx not loading correctly, so the installer is not considered working there yet.
