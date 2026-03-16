# BepInEx macOS Patcher

Fixes three independent incompatibilities between BepInEx 5.4.x and macOS 26 (Sequoia 26+) on Apple Silicon.

## Why BepInEx Fails on macOS 26 / Apple Silicon

### Problem 1 — macOS 26 kills unsigned processes

BepInEx is typically installed via the [`gib`](https://github.com/toebeann/gib) tool, which removes the
app's code signature to allow `DYLD_INSERT_LIBRARIES` injection. On macOS 26, the kernel requires every
process to carry at least an ad-hoc code signature. Launching an unsigned binary results in an immediate
`SIGKILL` — the game never starts.

**Fix:** Re-sign the `.app` bundle with an ad-hoc signature plus the required entitlements, every time
before launch. This is done in `run_bepinex.sh`.

---

### Problem 2 — BepInEx Preloader crashes on macOS 26's Mono runtime

Even after the game starts, the BepInEx preloader crashes before any plugins load. The crash happens in
`BepInEx.Preloader.RuntimeFixes` during the three calls below:

```
BepInEx.Preloader.RuntimeFixes.ConsoleSetOutFix.Apply()
BepInEx.Preloader.RuntimeFixes.HarmonyInteropFix.Apply()
BepInEx.Preloader.RuntimeFixes.UnityPatches.Apply()
```

Each of these calls `MonoMod.RuntimeDetour.DetourHelper.GetIdentifiable()`, which returns `null` on
macOS 26's Mono runtime instead of a valid method reference. The null is passed to code that dereferences
it unconditionally, producing a `NullReferenceException` that kills the preloader.

These three methods only affect console output formatting and debug stack-trace symbols — none of them are
required for mod functionality.

**Fix:** Patch `BepInEx.Preloader.dll` to replace the three `Apply()` method bodies with a single `ret`
instruction (no-op). This is done by `Program.cs` / `patcher.csproj` using
[dnlib](https://github.com/0xd4d/dnlib).

---

### Problem 3 — Harmony patches are silently blocked on Apple Silicon (arm64)

Even with problems 1 and 2 fixed, plugins load but all Harmony patches do nothing. No config files are
generated, patched methods are never redirected.

The root cause is hardware-level enforcement. Apple Silicon CPUs enforce **W⊕X** (write XOR execute) at
the hardware level for arm64 processes: a memory page cannot be both writable and executable at the same
time. Harmony requires `mprotect` with `PROT_WRITE | PROT_EXEC` to rewrite method bodies at runtime.
That call is silently denied, so every patch is a silent no-op.

`gib` sets `ARCHPREFERENCE="arm64,x86_64"` for Universal Binary games, which causes macOS to prefer the
arm64 slice. Running under **Rosetta 2** (x86_64 mode) sidesteps the problem: Rosetta itself already
requires JIT-style writable+executable memory, so the OS grants the permission to x86_64 processes.

**Fix:** Launch the game with `arch -x86_64` to force Rosetta. Because `arch` strips `DYLD_*` environment
variables, `DYLD_INSERT_LIBRARIES` must be passed explicitly via `-e`. This is done in `run_bepinex.sh`.

---

## What's in This Directory

| File | Purpose |
|------|---------|
| `run_bepinex.sh` | Drop-in replacement for the BepInEx launch script. Handles problems 1 and 3. |
| `Program.cs` | Patcher source — patches `BepInEx.Preloader.dll` for problem 2. |
| `patcher.csproj` | .NET 6 project file for the patcher. |

---

## Setup Instructions

### Prerequisites

- macOS 26 or later
- Apple Silicon Mac (M1 / M2 / M3 / M4 …)
- BepInEx 5.4.23.5 installed into the game directory (manual install, **not** via gib)
- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) — `brew install dotnet@6`

### Step 1 — Manual BepInEx install (skip gib)

Download `BepInEx_macos_x64_5.4.23.5.zip` from the
[BepInEx releases page](https://github.com/BepInEx/BepInEx/releases) and unzip it into the game
directory. Do **not** use gib; it removes the code signature without re-signing, which breaks macOS 26.

### Step 2 — Replace `run_bepinex.sh`

Copy `run_bepinex.sh` from this directory into the game directory, replacing the one that came with
BepInEx:

```sh
cp run_bepinex.sh "/path/to/game/run_bepinex.sh"
chmod +x "/path/to/game/run_bepinex.sh"
```

For The Bazaar via Steam the game directory is:
`~/Library/Application Support/Steam/steamapps/common/The Bazaar/`

### Step 3 — Patch `BepInEx.Preloader.dll`

Run the patcher once (re-run after every BepInEx update):

```sh
cd bepinex-mac-patcher
dotnet run
```

The patcher defaults to The Bazaar's BepInEx path. To target a different game, pass the path explicitly:

```sh
dotnet run -- "/path/to/BepInEx/core/BepInEx.Preloader.dll"
```

A backup is saved as `BepInEx.Preloader.dll.bak` next to the patched file.

### Step 4 — Steam launch option

In Steam → game Properties → Launch Options, set:

```
/usr/bin/arch "-x86_64" /bin/bash "/path/to/run_bepinex.sh" %command%
```

Replace `/path/to/run_bepinex.sh` with the absolute path to the script in the game directory.

---

## Distributing to Others

The patched `BepInEx.Preloader.dll` and the `run_bepinex.sh` can be distributed directly — recipients do
not need to run the patcher themselves. They still need to perform Steps 1 and 4 (manual BepInEx install
and Steam launch option).

`run_bepinex.sh` re-signs the app bundle on every launch, so no additional code-signing step is needed on
the recipient's machine.

---

## Changes vs. Stock BepInEx

### `run_bepinex.sh`

Two sections were modified from the original BepInEx-bundled script:

**Code signing block** (added after the `app_path` gib workaround):

```sh
# macOS 26+: remove original signature and re-sign with ad-hoc + JIT entitlements.
app_path="${executable_path%/Contents/MacOS*}"
if command -v codesign &>/dev/null; then
    _entitlements_file="$(mktemp /tmp/bepinex_ents.XXXXXX.plist)"
    cat > "$_entitlements_file" << 'ENTEOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "...">
<plist version="1.0"><dict>
    <key>com.apple.security.cs.allow-jit</key><true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
    <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
ENTEOF
    codesign --remove-signature "$app_path" 2>/dev/null || true
    codesign --force --deep --sign - --entitlements "$_entitlements_file" "$app_path"
    rm -f "$_entitlements_file"
fi
```

**Exec block** (replaces the original `ARCHPREFERENCE`-based Apple Silicon branch):

```sh
if [ -n "${is_apple_silicon}" ]; then
    exec arch -x86_64 -e DYLD_INSERT_LIBRARIES="${DYLD_INSERT_LIBRARIES}" "$executable_path" "$@"
else
    exec "$executable_path" "$@"
fi
```

### `BepInEx.Preloader.dll`

Three method bodies replaced with `ret`:

- `BepInEx.Preloader.RuntimeFixes.ConsoleSetOutFix.Apply()`
- `BepInEx.Preloader.RuntimeFixes.HarmonyInteropFix.Apply()`
- `BepInEx.Preloader.RuntimeFixes.UnityPatches.Apply()`
