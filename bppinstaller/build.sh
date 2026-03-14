#!/usr/bin/env bash
set -euo pipefail

PROD=false

for arg in "$@"; do
    case "$arg" in
        --prod) PROD=true ;;
        *) echo "Unknown argument: $arg"; exit 1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WINDOWS_CONFIG="$SCRIPT_DIR/src-tauri/tauri.windows.conf.json"
WINDOWS_ZIP="$SCRIPT_DIR/src-tauri/resources/BepInExSource/windows/BepInEx.zip"
BUNDLE_DIR="$SCRIPT_DIR/src-tauri/target/release/bundle"
RELEASE_EXE="$SCRIPT_DIR/src-tauri/target/release/bppinstaller.exe"

assert_command() {
    local name="$1"
    local hint="${2:-}"
    if ! command -v "$name" &>/dev/null; then
        if [ -n "$hint" ]; then
            echo "Error: $name not found. $hint" >&2
        else
            echo "Error: $name not found." >&2
        fi
        exit 1
    fi
}

invoke_step() {
    local label="$1"
    shift
    echo "==> $label"
    "$@"
}

cd "$SCRIPT_DIR"

assert_command node "Install Node.js first."
assert_command npm "Install Node.js/npm first."
assert_command cargo "Install Rust toolchain first."

if [ ! -f "$WINDOWS_ZIP" ]; then
    echo "Error: Missing Windows resource zip: $WINDOWS_ZIP" >&2
    exit 1
fi

invoke_step "Installing npm dependencies" npm install

if [ "$PROD" = false ]; then
    invoke_step "Starting dev server" npm run tauri dev
    exit 0
fi

if [ -z "${BPP_UPDATER_PUBKEY:-}" ] || { [ -z "${BPP_UPDATER_ENDPOINTS:-}" ] && [ -z "${BPP_UPDATER_ENDPOINT:-}" ]; }; then
    echo "Warning: updater metadata is not configured." >&2
    echo "Set BPP_UPDATER_PUBKEY and BPP_UPDATER_ENDPOINTS (or BPP_UPDATER_ENDPOINT) before building release artifacts." >&2
fi

if [ -z "${TAURI_SIGNING_PRIVATE_KEY:-}" ]; then
    echo "Warning: TAURI_SIGNING_PRIVATE_KEY is not set." >&2
    echo "Updater artifacts will not be signed correctly without a signing key." >&2
fi

if [ -d "$BUNDLE_DIR/msi" ]; then
    invoke_step "Removing stale MSI bundle artifacts" rm -rf "$BUNDLE_DIR/msi"
fi

invoke_step "Building Windows app binary" \
    npm run tauri build -- --no-bundle --config "$WINDOWS_CONFIG"

invoke_step "Bundling NSIS setup.exe" \
    npm run tauri bundle -- --bundles nsis --config "$WINDOWS_CONFIG"

echo ""
echo "Build complete."
echo "Binary:  $RELEASE_EXE"
echo "Setup:   $BUNDLE_DIR/nsis"
