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

### Updater Configuration

`bppinstaller` now uses the standard Tauri updater for self-updates.

Provide these environment variables at build time so the updater configuration is compiled into the app:

- `BPP_UPDATER_PUBKEY`: Tauri updater public key
- `BPP_UPDATER_ENDPOINTS`: comma-separated updater endpoints
- `BPP_UPDATER_ENDPOINT`: single updater endpoint, if you do not use `BPP_UPDATER_ENDPOINTS`

Release builds that publish updater artifacts also need:

- `TAURI_SIGNING_PRIVATE_KEY`
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` when your signing key is password-protected

After building, you can generate a static `latest.json` manifest from the updater artifacts:

```bash
node scripts/generate-updater-manifest.mjs \
  --version 1.0.4 \
  --base-url https://native-updater.bazaarplusplus.com/latest \
  --default-platform windows-x86_64 \
  --flatten-names \
  --notes-file ./release-notes.txt
```

The script scans `src-tauri/target/release/bundle`, reads each updater `.sig`, and writes `latest.json` to the project root by default. With `--flatten-names`, it also copies updater assets to normalized filenames without spaces before generating URLs.

## Structure

- `src/`: SvelteKit frontend
- `src-tauri/`: native Tauri commands and packaging
- `scripts/prebuild-check.mjs`: build-time validation
- `scripts/generate-updater-manifest.mjs`: generate static updater `latest.json`

## Known Limitation

- On macOS, the current blocker is BepInEx not loading correctly, so the installer is not considered working there yet.
