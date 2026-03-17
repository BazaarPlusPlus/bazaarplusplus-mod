# BazaarPlusPlus Multi-Repo Split Design

## Goal

Split the current BazaarPlusPlus workspace into five independent code repositories plus one lightweight overview repository, while keeping cross-project context discoverable and keeping code-level coupling at or near zero.

## Scope

This design covers repository boundaries, allowed dependencies, overview repository purpose, and the migration order.

This design does not define CI details, hosting choices, release automation, or package publishing infrastructure beyond the minimum contract needed between the mod and the installer.

## Current State

The local workspace rooted at `../` is a project family workspace rather than a true monorepo.

Observed project directories:

- `./`: core BepInEx mod for The Bazaar, already an independent git repository.
- `../bazaarplusplus-installer/`: Tauri + SvelteKit desktop installer.
- `../bazaarplusplus-site/`: Cloudflare Worker landing site.
- `../bazaarplusplus-leaderboard/`: Cloudflare Worker leaderboard service.
- `../bepinex-mac-patcher/`: standalone macOS compatibility patcher for BepInEx.

Only `./` currently contains its own `.git`. The workspace root is not a git repository.

## Target Structure

The target shape is six repositories:

1. `bazaarplusplus-overview`
2. `bazaarplusplus-mod`
3. `bazaarplusplus-installer`
4. `bazaarplusplus-site`
5. `bazaarplusplus-leaderboard`
6. `bepinex-mac-patcher`

The five code repositories are delivery-oriented and independently operable. The overview repository exists only to explain the ecosystem and point readers to the correct source of truth.

## Repository Responsibilities

### `bazaarplusplus-overview`

Purpose:

- Provide a human-readable project map.
- Record product-level design, roadmap, and major decisions.
- Help contributors find the right repository quickly.

Non-goals:

- No shared runtime code.
- No shared scripts or build logic.
- No duplicate technical facts that belong in a code repository.
- No machine-consumed data expected to stay in sync with implementation.

### `bazaarplusplus-mod`

Purpose:

- Hold the BazaarPlusPlus mod source code, build scripts, packaging, compatibility notes, and release outputs.
- Define the contents and format of what gets installed.

Non-goals:

- No installer UX or environment detection logic.
- No website or leaderboard service implementation.

### `bazaarplusplus-installer`

Purpose:

- Detect supported environments.
- Install, uninstall, and validate BazaarPlusPlus on user machines.
- Patch Steam launch options or equivalent local integration points.

Non-goals:

- No dependence on the mod repository layout.
- No mod business logic.

### `bazaarplusplus-site`

Purpose:

- Present the project publicly.
- Host download entry points, FAQ, support links, and project-facing content.

Non-goals:

- No installation logic.
- No leaderboard backend responsibilities.

### `bazaarplusplus-leaderboard`

Purpose:

- Poll leaderboard data.
- Persist snapshots and history.
- Serve leaderboard-related APIs or views.

Non-goals:

- No installer or mod packaging responsibilities.

### `bepinex-mac-patcher`

Purpose:

- Remain a small standalone compatibility utility for BepInEx on macOS.

Non-goals:

- No implied ownership of BazaarPlusPlus core mod code.
- No requirement to move in lockstep with the installer or mod unless an explicit future decision is made.

## Coupling Rules

The default rule is very loose coupling.

Allowed relationships:

- Repositories may link to each other in docs and release pages.
- `bazaarplusplus-installer` may depend on a versioned release artifact from `bazaarplusplus-mod`.
- `bazaarplusplus-site` may link users to releases or docs hosted elsewhere.
- `bazaarplusplus-overview` may link to every repository.

Disallowed relationships:

- No git submodules or git subtree dependencies between repositories.
- No source imports across repositories.
- No assumptions about sibling directory layouts on disk.
- No shared scripts that require checking out multiple repositories together.
- No duplicate “source of truth” documents in the overview repository.

## Installer-to-Mod Contract

The only intentional dependency is that the installer needs to know what to install.

That dependency should be release-based rather than source-based.

Required contract:

- A versioned installer-consumable release artifact produced by `bazaarplusplus-mod`.
- Stable metadata sufficient for the installer to identify version, target platform, and payload integrity.
- A stable installation layout convention.

Recommended minimum metadata fields:

- `name`
- `version`
- `platform`
- `artifact_url`
- `sha256`
- `installed_paths`

The installer should not know or care how the mod repository is organized internally. It should only care about the release format.

## Overview Repository Design

The overview repository should remain deliberately small.

Recommended contents:

- `README.md`: one-page ecosystem summary.
- `repos.md`: repository list, purpose, and source-of-truth pointers.
- `roadmap.md`: cross-repository roadmap only.
- `status.md`: short current-state summary.
- `decisions/`: major project-level decisions.

Content policy:

- If a document starts describing implementation details, move or link it to the relevant code repository.
- If a fact changes with code, the fact belongs in the code repository, not in overview.

## Naming

Recommended repository names:

- `bazaarplusplus-overview`
- `bazaarplusplus-mod`
- `bazaarplusplus-installer`
- `bazaarplusplus-site`
- `bazaarplusplus-leaderboard`
- `bepinex-mac-patcher`

This naming keeps the BazaarPlusPlus ecosystem searchable while preserving the patcher as a clearly independent tool.

## Migration Order

Recommended migration order:

1. Create `bazaarplusplus-overview` and write the repository map first.
2. Split out `bepinex-mac-patcher`, since it has the cleanest boundary.
3. Define the mod release artifact contract in `bazaarplusplus-mod`.
4. Update `bazaarplusplus-installer` so it consumes the release contract rather than local repository structure.
5. Initialize and harden `bazaarplusplus-site` and `bazaarplusplus-leaderboard` as independent repositories.
6. Keep the current local aggregate directory only as a personal workspace, not as an official architecture boundary.

This order reduces risk because it establishes the rules before moving the most interconnected piece, which is the installer-to-mod relationship.

## Success Criteria

The split is successful when all of the following are true:

- Each code repository can be cloned, built, and reasoned about independently.
- The installer uses only release artifacts from the mod, not local source tree assumptions.
- The overview repository contains navigation and context, not duplicated implementation truth.
- Removing the aggregate local workspace layout does not break any repository’s normal development flow.

## Rejected Alternatives

### Keep everything in one monorepo

Rejected because the stated goal is independent repositories with very low coupling.

### Put overview docs inside one “main” code repository

Rejected because it biases ownership and turns that repository into an accidental central authority.

### Combine `bepinex-mac-patcher` into `bazaarplusplus-mod`

Rejected because the patcher is intentionally a standalone utility with a distinct responsibility and lifecycle.
