#!/usr/bin/env bash
set -euo pipefail

CYAN='\033[0;36m'
GREEN='\033[0;32m'
RED='\033[0;31m'
RESET='\033[0m'

case "$(uname -s)" in
    Darwin)
        PLATFORM="macOS"
        GAME_ROOT="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar"
        MANAGED="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"
        ;;
    MINGW*|MSYS*|CYGWIN*)
        PLATFORM="Windows (Git Bash)"
        GAME_ROOT="/c/Program Files (x86)/Steam/steamapps/common/The Bazaar"
        MANAGED="/c/Program Files (x86)/Steam/steamapps/common/The Bazaar/TheBazaar_Data/Managed"
        ;;
    *)
        echo -e "${RED}Unsupported platform: $(uname -s)${RESET}" >&2
        exit 1
        ;;
esac

echo -e "${CYAN}== Building on ${GREEN}${PLATFORM}${CYAN} ==${RESET}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME_ROOT="${BPP_GAME_ROOT:-$GAME_ROOT}"
MANAGED="${BPP_MANAGED_PATH:-$MANAGED}"
INSTALLER_SQLITE="$SCRIPT_DIR/../bazaarplusplus-installer/src-tauri/resources/SourceForBuild/macos/BepInEx/plugins/libe_sqlite3.dylib"
GAME_SQLITE="$GAME_ROOT/BepInEx/plugins/libe_sqlite3.dylib"
TRAMPOLINE_REPAIR_SCRIPT="$SCRIPT_DIR/scripts/repair-macos-trampoline.sh"
TRAMPOLINE_STUB="${BPP_TRAMPOLINE_STUB:-$SCRIPT_DIR/../bazaarplusplus-installer/src-tauri/resources/Trampoline/macos/bpp_launcher}"

clear_macos_sqlite_quarantine() {
    [[ "$PLATFORM" == "macOS" ]] || return 0

    local target
    for target in "$INSTALLER_SQLITE" "$GAME_SQLITE"; do
        [[ -f "$target" ]] || continue
        xattr -d com.apple.quarantine "$target" 2>/dev/null || true
    done
}

print_bazaaragent_mode() {
    local bazaaragent="${1:-false}"
    if [[ "$bazaaragent" == "true" ]]; then
        echo -e "${CYAN}== BazaarAgent: ${GREEN}included${CYAN} ==${RESET}"
    else
        echo -e "${CYAN}== BazaarAgent: excluded ==${RESET}"
    fi
}

repair_macos_trampoline() {
    [[ "$PLATFORM" == "macOS" ]] || return 0

    BPP_GAME_ROOT="$GAME_ROOT" \
        BPP_TRAMPOLINE_STUB="$TRAMPOLINE_STUB" \
        bash "$TRAMPOLINE_REPAIR_SCRIPT"
}

build() {
    local bazaaragent="${1:-false}"
    local args=(-verbosity detailed)

    print_bazaaragent_mode "$bazaaragent"
    repair_macos_trampoline
    # The host is its own plugin project that references the main plugin + the pure core,
    # so building it builds and deploys all three. A default build builds only the main
    # plugin, whose build actively scrubs both host dlls from the plugins folder.
    if [[ "$bazaaragent" == "true" ]]; then
        dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj "${args[@]}"
    else
        dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj "${args[@]}"
    fi
}

# The Release half of BuildAll copies the DLL into the installer resources that ship
# to ONLINE users, but online/PTR share one install directory — a Release built while
# the PTR branch is installed would ship a PTR-assembly build. Pin Release builds to
# an online Managed snapshot (game-libs/online-*/Managed, newest) when one exists;
# otherwise require the installed branch to actually be public.
resolve_release_managed() {
    local pinned="${BPP_RELEASE_MANAGED:-}"
    if [[ -n "$pinned" ]]; then
        echo "$pinned"
        return
    fi
    local snaps=("$SCRIPT_DIR"/game-libs/online-*/Managed)
    local last=""
    local snap
    for snap in "${snaps[@]}"; do
        [[ -d "$snap" ]] && last="$snap"
    done
    echo "$last"
}

build_all() {
    local prod="${1:-false}"
    local bazaaragent="${2:-false}"
    local args=(-t:BuildAll -verbosity detailed)

    if [[ "$prod" == "true" ]]; then
        args+=(-p:BuildProductionPackage=true)
    fi

    local release_managed
    release_managed=$(resolve_release_managed)
    if [[ -n "$release_managed" ]]; then
        echo -e "${CYAN}== Release pinned to ${GREEN}${release_managed}${CYAN} ==${RESET}"
        args+=("-p:ManagedPath=$release_managed")
    else
        require_steam_branch public
    fi

    print_bazaaragent_mode "$bazaaragent"
    clear_macos_sqlite_quarantine
    repair_macos_trampoline
    if [[ "$bazaaragent" == "true" ]]; then
        dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj "${args[@]}"
    else
        dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj "${args[@]}"
    fi
    clear_macos_sqlite_quarantine
}

parse_build_options() {
    local bazaaragent=false

    while (($# > 0)); do
        case "$1" in
            --with-bazaaragent) bazaaragent=true ;;
            *)
                usage
                exit 1
                ;;
        esac
        shift
    done

    build "$bazaaragent"
}

test_all() {
    clear_macos_sqlite_quarantine

    local project
    local failures=()
    while IFS= read -r project; do
        echo -e "${CYAN}== Testing ${GREEN}${project}${CYAN} ==${RESET}"
        if grep -q "Microsoft.NET.Test.Sdk" "$project"; then
            if ! dotnet test "$project"; then
                failures+=("$project")
            fi
        else
            if ! dotnet run --project "$project"; then
                failures+=("$project")
            fi
        fi
    done < <(find tests -mindepth 2 -maxdepth 2 -name '*.csproj' | sort)

    clear_macos_sqlite_quarantine

    if ((${#failures[@]} > 0)); then
        echo -e "${RED}Failed test projects:${RESET}" >&2
        printf '  %s\n' "${failures[@]}" >&2
        return 1
    fi
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

# Steam beta branches ("public_test_realm" = PTR) replace the single install
# in place, so the Managed dir silently changes identity on branch switch.
# Guard so PTR bits never overwrite ./decompiled (online reference) and vice versa.
# The appmanifest is located by walking up from MANAGED — the directory the DLLs
# are actually read from — so an overridden BPP_MANAGED_PATH is guarded too.
locate_appmanifest() {
    local dir="$MANAGED"
    local _i
    for _i in 1 2 3 4 5 6 7 8 9 10; do
        dir="$(dirname "$dir")"
        if [[ -f "$dir/appmanifest_1617400.acf" ]]; then
            echo "$dir/appmanifest_1617400.acf"
            return
        fi
        [[ "$dir" == "/" || "$dir" == "." ]] && break
    done
}

installed_steam_branch() {
    local acf
    acf=$(locate_appmanifest)
    [[ -n "$acf" ]] || { echo "unknown"; return; }
    local key
    key=$(awk '/"MountedConfig"/,/^\t\}/' "$acf" | awk -F '"' '/"BetaKey"/ {print $4}')
    echo "${key:-public}"
}

require_steam_branch() {
    local expected="$1"
    [[ "${BPP_SKIP_BRANCH_CHECK:-}" == "1" ]] && return 0
    local branch
    branch=$(installed_steam_branch)
    if [[ "$branch" == "unknown" ]]; then
        echo -e "${RED}Could not find appmanifest_1617400.acf above the Managed path to verify the Steam branch.${RESET}" >&2
        echo -e "${RED}Decompiling a bare copied Managed dir? Set BPP_SKIP_BRANCH_CHECK=1 to override.${RESET}" >&2
        exit 1
    fi
    if [[ "$branch" != "$expected" ]]; then
        echo -e "${RED}Installed Steam branch is '$branch', expected '$expected'.${RESET}" >&2
        echo -e "${RED}Switch The Bazaar's beta branch in Steam first, or set BPP_SKIP_BRANCH_CHECK=1 to override.${RESET}" >&2
        exit 1
    fi
}

decompile() {
    check_ilspy
    local dll="${2:-Assembly-CSharp}"
    local out_root="${BPP_DECOMPILE_OUT:-./decompiled}"
    local out="$out_root/$dll"
    echo "Decompiling $dll to $out..."
    DOTNET_ROLL_FORWARD=Major ilspycmd -p -o "$out" "$MANAGED/$dll.dll"
    echo "Done: $out"
}

decompile_all() {
    for dll in Assembly-CSharp BazaarGameClient BazaarGameShared BazaarBattleService TheBazaarRuntime FMODUnity; do
        decompile _ "$dll"
    done
}

# Archive the currently installed Managed dir keyed by branch + buildid. Because the
# two branches overwrite each other in place, this is the only way to keep both
# assembly sets available (Release pinning + build-matrix consume these snapshots).
snapshot_managed() {
    local acf branch buildid channel dest
    acf=$(locate_appmanifest)
    if [[ -z "$acf" ]]; then
        echo -e "${RED}Could not find appmanifest_1617400.acf above the Managed path.${RESET}" >&2
        exit 1
    fi
    branch=$(installed_steam_branch)
    channel="online"
    [[ "$branch" == "public_test_realm" ]] && channel="ptr"
    if [[ "$branch" != "public" && "$branch" != "public_test_realm" ]]; then
        echo -e "${RED}Installed branch '$branch' is neither public nor public_test_realm; refusing to snapshot.${RESET}" >&2
        exit 1
    fi
    buildid=$(awk -F '"' '/"buildid"/ {print $4; exit}' "$acf")
    dest="$SCRIPT_DIR/game-libs/$channel-$buildid/Managed"
    if [[ -d "$dest" ]]; then
        echo "Snapshot already exists: $dest"
        return
    fi
    mkdir -p "$dest"
    cp -R "$MANAGED/." "$dest/"
    echo -e "${GREEN}Archived $channel (buildid $buildid) Managed -> $dest${RESET}"
}

# Compile the single source tree against every archived Managed snapshot. Uses the
# CompatCheck configuration so neither the Debug plugins-copy nor the Release
# installer-copy post-build steps fire.
build_matrix() {
    local snaps=("$SCRIPT_DIR"/game-libs/*/Managed)
    local found=0 failed=()
    local snap
    for snap in "${snaps[@]}"; do
        [[ -d "$snap" ]] || continue
        found=1
        echo -e "${CYAN}== Matrix build against ${GREEN}${snap}${CYAN} ==${RESET}"
        if ! dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj -c CompatCheck -p:ManagedPath="$snap"; then
            failed+=("$snap")
        fi
    done
    if ((found == 0)); then
        echo -e "${RED}No snapshots under game-libs/. Run './run.sh snapshot-managed' on each branch first.${RESET}" >&2
        exit 1
    fi
    if ((${#failed[@]} > 0)); then
        echo -e "${RED}Matrix build failed against:${RESET}" >&2
        printf '  %s\n' "${failed[@]}" >&2
        exit 1
    fi
    echo -e "${GREEN}Matrix build passed for all snapshots.${RESET}"
}

usage() {
    cat <<EOF
Usage:
  $0 build [--with-bazaaragent]
  $0 all [--prod] [--with-bazaaragent]
  $0 test
  $0 format
  $0 decompile [DllName]
  $0 decompile-all
  $0 decompile-ptr [DllName]
  $0 decompile-all-ptr
  $0 snapshot-managed
  $0 build-matrix

Options:
  --with-bazaaragent  Build and copy the optional BazaarAgent assemblies.
  --prod              With all: also build the production installer package.
EOF
}

case "${1:-}" in
    all)
        shift
        prod=false
        bazaaragent=false
        while (($# > 0)); do
            case "$1" in
                --prod) prod=true ;;
                --with-bazaaragent) bazaaragent=true ;;
                *)
                    usage
                    exit 1
                    ;;
            esac
            shift
        done
        build_all "$prod" "$bazaaragent"
        ;;
    build)
        shift
        parse_build_options "$@"
        ;;
    test)       test_all ;;
    format)     format ;;
    decompile)
        require_steam_branch public
        decompile "$@"
        ;;
    decompile-all)
        require_steam_branch public
        decompile_all
        ;;
    decompile-ptr)
        require_steam_branch public_test_realm
        BPP_DECOMPILE_OUT=./decompiled-vptr decompile "$@"
        ;;
    decompile-all-ptr)
        require_steam_branch public_test_realm
        BPP_DECOMPILE_OUT=./decompiled-vptr decompile_all
        ;;
    snapshot-managed) snapshot_managed ;;
    build-matrix) build_matrix ;;
    *)
        usage
        exit 1
        ;;
esac
