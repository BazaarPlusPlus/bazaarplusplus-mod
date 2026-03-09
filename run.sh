#!/bin/bash
set -e

MANAGED="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"

build() {
    dotnet build
}

format() {
    csharpier format .
}

check_ilspy() {
    if ! command -v ilspycmd &>/dev/null; then
        echo "ilspycmd not found. Installing..."
        dotnet tool install -g ilspycmd
    fi
}

decompile() {
    check_ilspy
    local dll="${2:-Assembly-CSharp}"
    local out="./decompiled/$dll"
    echo "Decompiling $dll to $out..."
    DOTNET_ROLL_FORWARD=Major ilspycmd -p -o "$out" "$MANAGED/$dll.dll"
    echo "Done: $out"
}

decompile_all() {
    for dll in Assembly-CSharp BazaarGameClient BazaarGameShared BazaarBattleService TheBazaarRuntime; do
        decompile _ "$dll"
    done
}

case "$1" in
    build)      build ;;
    format)     format ;;
    decompile)  decompile "$@" ;;
    decompile-all) decompile_all ;;
    *)
        echo "Usage: $0 {build|format|decompile [DllName]|decompile-all}"
        exit 1
        ;;
esac
