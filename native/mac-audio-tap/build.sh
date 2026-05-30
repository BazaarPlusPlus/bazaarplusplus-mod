#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

# Output is lib-prefixed (libBppMacAudio.dylib) so [DllImport("BppMacAudio")]
# resolves on Unity-Mono via the proven lib{name}.dylib probe, exactly like
# libe_sqlite3.dylib.
#
# -mmacosx-version-min=11.0 (NOT 14.2) is required: it weak-imports the 14.2 tap
# symbols so the dylib LOADS on macOS 13/14 and IsSupported cleanly returns false
# there. The tap symbols are only ever called after the >=15 gate passes.
clang -arch arm64 -fobjc-arc \
  -framework CoreAudio -framework Foundation \
  -mmacosx-version-min=11.0 \
  -O2 -dynamiclib -o libBppMacAudio.dylib BppMacAudio.m

echo "built libBppMacAudio.dylib"
