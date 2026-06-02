# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

The mod targets `netstandard2.1` (C# 12). Game assemblies are resolved via `ManagedPath` — auto-detected from common Steam install paths, or pass explicitly:

```powershell
# Build the mod (Debug, auto-copies to BepInEx/plugins/ if game found)
dotnet build BazaarPlusPlus.csproj

# Build with explicit game assembly path
dotnet build BazaarPlusPlus.csproj -p:ManagedPath="D:\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed"

# Build both Debug + Release (Release copies to installer repo if present)
dotnet build BazaarPlusPlus.csproj -t:BuildAll

# Run a single test project
dotnet test tests\RunLifecycleState.Tests\RunLifecycleState.Tests.csproj

# Run a non-SDK test project (ones without Microsoft.NET.Test.Sdk)
dotnet run --project tests\ChoiceScreenPedestalResolver.Tests\ChoiceScreenPedestalResolver.Tests.csproj

# Format
csharpier format .
```

On macOS/Linux, `run.sh` wraps these: `./run.sh build`, `./run.sh all`, `./run.sh test`, `./run.sh format`, `./run.sh decompile [DllName]`.

Test projects under `tests/` are split per-feature. Some use xUnit + `Microsoft.NET.Test.Sdk` (run via `dotnet test`), others are executable (run via `dotnet run --project`). Check whether the csproj has `Microsoft.NET.Test.Sdk` to determine which.

## Logs & Debugging

This mod is a **BepInEx 5.x plugin** (`BepInEx.Core` 5.*). At runtime, BepInEx writes all console output to disk at `<GameDir>\BepInEx\LogOutput.log` — the sibling of the `BepInEx\plugins\` folder the build copies into. To debug, read that file; mod log lines are prefixed `[BPP][<Component>]` (logged via `BppLog` → BepInEx `ManualLogSource`). `Debug`-level lines are only emitted from Debug builds; `Info`/`Warning`/`Error` always emit.

For runtime validation that needs launching the game, launch The Bazaar through Steam so Steam runtime state is present; on macOS run `open "steam://run/1617400"` (Steam App ID 1617400). Direct `TheBazaar.app` or `run_bepinex.sh` launches are fallback paths only when explicitly requested.

## Architecture

**Three assemblies** ship as the mod:
- `BazaarPlusPlus.dll` — the main BepInEx plugin; references game DLLs, Unity, BepInEx
- `BazaarPlusPlus.ModApi.dll` — HTTP client + DTOs for the cloud backend; zero game/Unity/BepInEx references
- `BazaarPlusPlus.Storage.dll` — SQLite persistence layer; zero game/Unity/BepInEx references

All three csproj files live in the repo root. `ModApi/` and `Storage/` are the source trees for their respective csproj (via `<Compile Include="...">`). `Directory.Build.props` gives them separate `obj/`/`bin/` dirs.

**Plugin lifecycle** — `Plugin.cs` (BepInEx entry) → `BppComposition` (the manual composition root, no DI container). BppComposition wires:
1. **Features** (`IBppFeature`) — non-Unity logic modules registered via `BppFeatureRegistry`. Started/stopped with the plugin.
2. **Mountables** (`IBppMountable`) — Unity-aware components attached to the plugin's `GameObject` via `BppMountableRegistry`. Most use the generic `ComponentMount<T>` adapter.
3. **Settings dock entries** (`ISettingsDockEntry`) — in-game settings UI entries registered via `SettingsDockEntryRegistry`.

**Layer boundaries:**
- `Core/` — pure abstractions (config, event bus, paths, runtime interfaces). Zero game DLL references.
- `GameInterop/` — game DLL coupling layer (`BppClientCacheBridge`, `GameStateProbe`, `RunContextStore`, `IRunContext`, game-typed events like `CombatSimObserved`/`NetMessageObserved`, shared native adapters like encounter reads, static card data, card preview prefabs, and hero portrait assets).
- `Game/` — feature implementations organized by subdirectory (CombatReplay, HistoryPanel, RunLogging, Screenshots, Tooltips, etc.).
- `Patches/` — Harmony patches, organized by feature area. `BppPatchHost` provides the static service locator that patches use to reach `IBppServices`.
- `Infrastructure/` — cross-cutting utilities (logging, fonts, UI design tokens).

**Architecture layering rules:**
- Put reusable adapters over The Bazaar/Unity runtime surfaces in `GameInterop/`: `AppState`/`Data` status reads, `ClientCache` reflection, static card data, native card-preview prefabs/reflection, shared game asset lookup, and game-typed event payloads.
- Keep feature workflows, UI state, product policy, filtering/classification rules, upload decisions, and storage orchestration in `Game/`. Do not move logic into `GameInterop/` only because it mentions game enums or DTOs.
- If two features need the same runtime/prefab/static-data behavior, extract the adapter to `GameInterop/<Concept>/` and have both features consume that seam. Do not make one feature import another feature's internal implementation only to reuse a game-runtime adapter.
- Patches may target feature services through `BppPatchHost`, but shared Harmony reflection helpers or native runtime adapters should live in `GameInterop/` or `Infrastructure/`, not inside a feature directory.
- Add or extend architecture tests when establishing a new boundary that the compiler cannot enforce.

**Key patterns:**
- Game assemblies are publicized at build time (`<PublicizeAll>true</PublicizeAll>` via Krafs.Publicizer), so all `internal` game types/members are accessible.
- `decompiled/` contains ILSpy output of game DLLs — read-only reference, never edited.
- Harmony patches reach mod services through the static `BppPatchHost` (installed once at startup), not through constructor injection.
- The event bus (`IBppEventBus`) is in-memory pub/sub used for decoupling features (combat frame events, run lifecycle changes, replay persistence signals).
- `IEncounterStateProbe` is a pull-based status query ("where is the player now"), deliberately not a timeline tracker (see ADR-0001).

# Project Rules

- Choose verification proportional to the change:
  - Docs-only changes do not require `BuildAll`
  - Run the full `BuildAll` target when touching build logic, packaging, or before release-oriented validation
  - Targeted code changes should run the smallest relevant test project or build first, not the whole repo by default
  - If there is no meaningful automated test seam for a change, it is acceptable to ship without adding a new test
  - Do not add tests whose primary purpose is demonstrating coverage or asserting mock call sequences — these are considered invalid tests
  - Do not add source-text tests that only assert exact code snippets or lines exist in a file; those are not meaningful verification
- The main mod project and most test projects resolve game assemblies through `ManagedPath`; if auto-detection does not match the local install, pass `ManagedPath` explicitly instead of changing project references or treating the failure as a code regression
- Do not edit files under `decompiled/`; treat them as read-only reference code for understanding game behavior and APIs
- On Windows, require PowerShell 7.6.0 or later before proceeding; check `PSVersionTable.PSVersion.ToString()` to confirm the installed version
- Preserve the build-and-copy behavior in `BazaarPlusPlus.csproj`; do not change the Debug `BepInEx/plugins` copy flow or the Release installer copy flow unless the task is explicitly about packaging
- Keep agent-authored docs minimal; do not add or rewrite README-style files unless the task explicitly requires user-facing documentation
- When a model is serialized with MessagePack in the Unity/Mono runtime, keep the serialized DTO graph `public`; `internal` DTOs can still fail at runtime with `MethodAccessException` even when their properties are public
- When designing server-side SQL, analyze each query for scan shape before shipping: determine whether it can degrade into a full table scan, estimate the expected read/write count cost at production scale, and check for fan-out or write/read amplification on hot paths
- Treat the current repo code and `decompiled/` as the source of truth; other design docs are reference and may be stale — re-review them against live code, and ground conclusions in `file:line` citations rather than prose reasoning
- For game-behavior bugs, root-cause against the decompiled game source before forming a hypothesis or writing a fix; do not trust draft specs or memory, do not ship a surface fix, and when the obvious fix fails enumerate alternative cause mechanisms instead of writing another speculative patch
- Before asserting a native/platform API's signature or availability (e.g. macOS audio capture), verify it against the real SDK headers; never state native API facts from memory
- Before writing filtering/classification rules over game data, query the live static data (e.g. the game's static card data / `GameData.db`) to confirm the fields actually exist and the lookup is reachable
- Key game entities (merchants, trainers, cards) by their stable template ID, not by display text — display names are ambiguous and unstable
- When the user says a problem has failed repeatedly, stop spelunking implementation/decompiled source and first write a doc capturing background, the current problem, candidate approaches, and the verification method
- Run an independent red-team review of a large refactor/design plan before implementing, and revise from it; keep such a review strictly review-only — surface weaknesses/risks/bad assumptions with `file:line` evidence and apply no patches
- After revising a design (or receiving a review), send the revised plan back for confirmation before implementing — do not continue straight into code
- When refactoring for cleanliness, take the breaking change for the cleanest end-state and bump the major version rather than preserving back-compat shims
- When replacing a subsystem or migrating to a prototype, remove the old implementation entirely and ship only the new version in-place — do not leave the old path as a fallback or stand up a merged build chain that runs both
- When the user rejects a design/layout direction, scrap the approach and redesign holistically — do not patch the rejected design
- Before keeping a DTO/model, verify it has a real producer and a real consumer across the repo; treat unused DTO scaffolding as dead code to delete, not to preserve
- Do not build standalone probe/diagnostic scaffolding to validate a hypothesis — add a temporary probe on the main path (the user builds + reloads to verify), or drop it and record it as a to-verify item in the design doc, then ship
- Do not silently enter a mode or auto-advance UI state without a visible, user-exitable indication
- When CJK text renders as tofu boxes, route the text to a CJK-capable font; do not "fix" it by editing the copy
- Touch only the named target of a delete/change request; do not opportunistically widen scope or adjust unrelated config
- Reuse the game's native UI components and the codebase's established prior-art patterns (e.g. `MonsterBoardTooltip`/`MonsterPreview`/`CardPreviewBase`, `CardSetPreview`, the run-bundle upload path) instead of hand-rolling a new render/upload chain
- On completion, follow the settled wrap-up: review your own diff, commit, merge the working branch to `master`, push, then delete branches already merged; do not commit before reviewing or when not asked
- Keep commits scoped: when `./run.sh format`/csharpier reformats files outside your change, revert those formatter-only edits before committing
- A long-running automation task must self-heal — auto-relaunch the game process on crash/exit and continue until the goal is met, rather than stopping on the first failure

# Rules Hygiene

These rules are read by every agent session. Keep them high-signal.

## After any agentic session

If you discover a non-obvious pattern that would help future sessions, include a **"Suggested rule additions"** heading in your wrap-up summary (or the commit message) with the proposed text. Do **not** edit these rules inline during normal feature or fix work. The user decides what gets added.

## High bar for new rules

Editing or clarifying existing rules is always welcome. New rules must meet all three criteria:

1. Non-obvious — someone familiar with the codebase would still get it wrong without the rule
2. Repeatedly encountered — it came up more than once (multiple hits in one session counts)
3. Specific enough to act on — a concrete instruction, not a vague principle

Rules that apply to a single module or feature area belong in that area's own rules file, not the repo root.

## What not to put in these rules

Avoid architectural descriptions of a module or feature area. Rules should be traps to avoid, not maps to follow.

## No drive-by additions

Rules emerge from validated patterns, not one-off observations. The workflow is:

1. Agent notes a pattern during a session
2. Team validates the pattern in code review
3. A dedicated commit adds the rule with context on why it exists

## Domain docs

Single-context: project vocabulary lives in `CONTEXT.md`, design decisions in `docs/adr/`. The full documentation map is `docs/README.md`.
