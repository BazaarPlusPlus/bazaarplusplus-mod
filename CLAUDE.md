# CLAUDE.md

Operating rules for AI agents working in this repository (`AGENTS.md` is a symlink to this file). Process rules live here; durable knowledge in `docs/MEMORY.md`, structure in `docs/ARCHITECTURE.md`.

## Build & Test Commands

`./run.sh` with no arguments lists every subcommand and what it does; it works on macOS and Windows (Git Bash). Build through it rather than calling `dotnet build` directly — the raw invocation skips the macOS trampoline repair that `run.sh` performs after every game update. The mod targets `netstandard2.1` (C# 12), and game assemblies resolve via `ManagedPath`, auto-detected from common Steam install paths or passed as `-p:ManagedPath=...`.

Below is only what `run.sh` cannot tell you:

- Running one test project directly: `dotnet test <csproj>` for xUnit projects, `dotnet run --project <csproj>` for exe-runner ones. Which is which is decided by whether the csproj references `Microsoft.NET.Test.Sdk`.
- After changing a direct dependency in `Directory.Packages.props`: run `./run.sh restore-locks`, review the six changed `src/**/packages.lock.json` files, then `./run.sh restore-locked` so graph drift fails locally rather than in the installer build. Test projects get no lock files.
- Building from an isolated ticket worktree: pass `-p:BPPInstallerSourcePath="<absolute-path>/bazaarplusplus-installer/src-tauri/resources"` to projects referencing the main mod, because the default sibling installer path does not exist beside a worktree.
- Running the full `./run.sh test` from a worktree: `run.sh` forwards no `-p:` properties and ignores the environment variable, so create a `bazaarplusplus-installer` symlink in the worktree's parent directory plus `ln -sfn <main-checkout>/decompiled <worktree>/decompiled` (`decompiled/` is a gitignored local artifact that `NativeCardPreviewCompatibility.Tests` hard-depends on). A missing link surfaces as MSB3030 or a missing decompiled source file, which reads like a code regression.

## Logs & Debugging

This mod is a **BepInEx 5.x plugin** (`BepInEx.Core` 5.\*). At runtime, BepInEx writes all console output to disk at `<GameDir>\BepInEx\LogOutput.log` — the sibling of the `BepInEx\plugins\` folder the build copies into. To debug, read that file; mod log lines are structured events shaped `[BPP][<Scope>] event=<id> field=value ...` (logged via `BppLog` → BepInEx `ManualLogSource`). `Debug`-level events only emit from Debug builds; `Info`/`Warning`/`Error` always emit.

For runtime validation that needs launching the game, always launch The Bazaar through Steam (App ID 1617400) so Steam runtime state is present. On macOS: `open "steam://run/1617400"`. On Windows: `start steam://run/1617400`. Do not launch `TheBazaar.app` directly or use `run_bepinex.sh` on macOS — these bypass Steam runtime and cause subtle failures.


## Architecture

Structure lives in `docs/ARCHITECTURE.md`; durable knowledge in `docs/MEMORY.md` (load first); vocabulary in `CONTEXT.md`; rationale in `docs/adr/`. This section keeps only the layering rules — traps, not maps:

- Put reusable adapters over The Bazaar/Unity runtime surfaces in `GameInterop/`. Keep feature workflows, UI state, product policy, filtering/classification rules, upload decisions, and storage orchestration in `Game/`. Do not move logic into `GameInterop/` only because it mentions game enums or DTOs.
- If two features need the same runtime/prefab/static-data behavior, extract the adapter to `GameInterop/<Concept>/` and have both features consume that seam. Do not make one feature import another feature's internal implementation only to reuse a game-runtime adapter.
- Patches may target feature services through `BppPatchHost` (the static service locator; never constructor injection), but shared Harmony reflection helpers or native runtime adapters live in `GameInterop/` or `Infrastructure/`, not inside a feature directory.
- Add or extend architecture tests when establishing a new boundary that the compiler cannot enforce.

# Project Rules

- Do not edit files under `decompiled/`; treat them as read-only reference code for understanding game behavior and APIs
- When a model is serialized with MessagePack in the Unity/Mono runtime, keep the serialized DTO graph `public`
- Treat the current repo code and `decompiled/` as the source of truth; other design docs are reference and may be stale — re-review them against live code, and ground conclusions in `file:line` citations rather than prose reasoning
- For game-behavior bugs, root-cause against the decompiled game source before forming a hypothesis or writing a fix; do not trust draft specs or memory, do not ship a surface fix, and when the obvious fix fails enumerate alternative cause mechanisms instead of writing another speculative patch
- Key game entities (cards, merchants, trainers) by their stable template ID.
- When the user says a problem has failed repeatedly, stop spelunking implementation/decompiled source and first write a doc capturing background, the current problem, candidate approaches, and the verification method
- Run an independent red-team review of a large refactor/design plan before implementing, and revise from it; keep such a review strictly review-only — surface weaknesses/risks/bad assumptions with `file:line` evidence and apply no patches
- After revising a design (or receiving a review), send the revised plan back for confirmation before implementing.
- When replacing a subsystem or migrating to a prototype, remove the old implementation entirely and ship only the new version in-place — do not leave the old path as a fallback or stand up a merged build chain that runs both
- Do not build standalone probe/diagnostic scaffolding to validate a hypothesis — add a temporary probe on the main path (the user builds + reloads to verify), or drop it and record it as a to-verify item in the design doc, then ship
- When CJK text renders as tofu boxes, route the text to a CJK-capable font; do not "fix" it by editing the copy
- When a degradation event is categorized, include the category field in its `BppLogStormPolicy` key; otherwise one category's failure can suppress later categories' logs during the storm window
- Touch only the named target of a delete/change request; do not opportunistically widen scope or adjust unrelated config
- Reuse the game's native UI components and the codebase's established prior-art patterns  instead of hand-rolling a new render/upload chain
- After invoking a native Unity `Button.onClick` programmatically, verify the expected game-state transition before treating the action as successful — native listeners may return silently through interaction gates such as `AllowInteraction` without throwing
- On completion, follow the settled wrap-up: review your own diff, commit, open a PR with `gh pr create`, merge it, then delete branches already merged. `master` is protected — a direct push to it is rejected. Commit only after reviewing your own diff, and only when the user asked for a commit
- Format every Git commit message as Conventional Commits: `<type>(<scope>): <description>`.
- Keep commits scoped: when `./run.sh format`/csharpier reformats files outside your change, revert those formatter-only edits before committing.
- A long-running automation task must self-heal — auto-relaunch the game process on crash/exit and continue until the goal is met, rather than stopping on the first failure
- Never build mod file-write paths from `Application.dataPath` — on macOS its parent is the `.app` bundle root, and unsealed writes there break `codesign` re-signing and the trampoline repair (blocking `./run.sh build` after every game update). Anchor writes on `BepInEx.Paths.GameRootPath` / the `<GameRoot>/BazaarPlusPlusV5/` data dir, which BepInEx special-cases on macOS to the directory containing the `.app`

## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues, operated via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage labels are used as-is: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`.

### Domain docs

Single-context: vocabulary in `CONTEXT.md` at the root, decisions in `docs/adr/`. See `docs/agents/domain.md`. The full documentation map is `docs/README.md`.

Durable project knowledge lives in `docs/MEMORY.md` (load first) with detail in `docs/ARCHITECTURE.md` (the structure/overview layer); historical working documents live only in git history. Task plans, feature requests, and bugs are GitHub issues (see Issue tracker above) — not repo docs; `docs/drafts/` is the temporary write buffer for knowledge documents only (design records, root-cause analyses, decision analyses). Consolidation promotes durable outcomes into MEMORY/ADR/ARCHITECTURE, moves actionable work to issues, and deletes the spent draft. Day-to-day edit policy: `docs/ARCHITECTURE.md` and `docs/adr/` may be corrected anytime; `MEMORY.md` and `docs/README.md` are curated ONLY by consolidation runs — new knowledge goes to `drafts/`, not into them directly. Rationale lives only in `docs/adr/`; elsewhere link, don't restate. Keep `MEMORY.md` under 200 lines: merge, don't append. Boundaries: AGENTS.md/CLAUDE.md = process, MEMORY.md = knowledge, ARCHITECTURE.md = structure, issues = work.

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
