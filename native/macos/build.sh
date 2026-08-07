#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
build_dir="$script_dir/build"
bundle_name="GfxPluginBppReplayVideoToolbox.bundle"
bundle_dir="$build_dir/$bundle_name"
executable_dir="$bundle_dir/Contents/MacOS"
executable="$executable_dir/GfxPluginBppReplayVideoToolbox"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "BppReplayVideoToolbox can only be built on macOS." >&2
    exit 1
fi

mkdir -p "$executable_dir"
cp "$script_dir/Info.plist" "$bundle_dir/Contents/Info.plist"

clang++ \
    -arch arm64 \
    -std=c++17 \
    -fobjc-arc \
    -fvisibility=hidden \
    -mmacosx-version-min=11.0 \
    -O2 \
    -Wall -Wextra -Werror -Wno-deprecated-declarations \
    -bundle \
    -framework AVFoundation \
    -framework AudioToolbox \
    -framework CoreMedia \
    -framework CoreVideo \
    -framework Foundation \
    -framework Metal \
    -framework VideoToolbox \
    "$script_dir/BppReplayVideoToolbox.mm" \
    "$script_dir/BppReplayAudioMuxer.mm" \
    -o "$executable"

# Development output is deliberately ad-hoc. The installer repository is the only production
# signing authority and must re-sign this bundle with Team 9Z44S3N293 during release packaging.
codesign --force --sign - --timestamp=none "$bundle_dir"
codesign --verify --deep --strict "$bundle_dir"

echo "built $bundle_dir"

if [[ -n "${BPP_REPLAY_PLUGIN_DESTINATION:-}" ]]; then
    destination="$BPP_REPLAY_PLUGIN_DESTINATION"
    mkdir -p "$destination"
    ditto "$bundle_dir" "$destination/$bundle_name"
    echo "copied $bundle_name -> $destination/"
fi
