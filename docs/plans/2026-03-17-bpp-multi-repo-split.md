# BazaarPlusPlus Multi-Repo Split Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Split the current BazaarPlusPlus workspace into five independent code repositories plus one overview repository with no code-level coupling and only a release-based dependency from installer to mod.

**Architecture:** The split is repository-first and contract-driven. The overview repository becomes the only shared narrative layer, while each code repository remains operationally independent and the installer consumes mod releases through a minimal artifact contract rather than local source assumptions.

**Tech Stack:** Git, Markdown, C#/.NET/BepInEx, Tauri/SvelteKit/TypeScript, Cloudflare Workers, shell tooling

---

### Task 1: Record the split contract in the existing mod repository

**Files:**
- Create: `docs/plans/2026-03-17-bpp-multi-repo-split-design.md`
- Create: `docs/plans/2026-03-17-bpp-multi-repo-split.md`
- Modify later if needed: `README.md`

**Step 1: Verify the plan documents exist**

Run: `ls docs/plans/2026-03-17-bpp-multi-repo-split*`
Expected: both plan files are listed

**Step 2: Read the design document and confirm boundaries**

Run: `sed -n '1,220p' docs/plans/2026-03-17-bpp-multi-repo-split-design.md`
Expected: repository responsibilities, coupling rules, and migration order match the approved design

**Step 3: Read the implementation plan and confirm task order**

Run: `sed -n '1,260p' docs/plans/2026-03-17-bpp-multi-repo-split.md`
Expected: the plan is task-based, uses exact paths, and keeps installer-to-mod dependence release-based

**Step 4: Commit the planning docs**

```bash
git add docs/plans/2026-03-17-bpp-multi-repo-split-design.md docs/plans/2026-03-17-bpp-multi-repo-split.md
git commit -m "docs: add multi-repo split design and plan"
```

### Task 2: Create the overview repository skeleton

**Files:**
- Create: `../bazaarplusplus-overview/README.md`
- Create: `../bazaarplusplus-overview/repos.md`
- Create: `../bazaarplusplus-overview/roadmap.md`
- Create: `../bazaarplusplus-overview/status.md`
- Create: `../bazaarplusplus-overview/decisions/2026-03-17-multi-repo-split.md`

**Step 1: Create the repository directory structure**

Run: `mkdir -p ../bazaarplusplus-overview/decisions`
Expected: the directory exists without touching any code repository

**Step 2: Write the overview README**

Write a short document that states:

- this repository is the ecosystem map
- implementation truth lives in the code repositories
- links to all five code repositories will live here

**Step 3: Write `repos.md`**

Document for each repository:

- purpose
- tech stack
- where to look for the latest implementation truth

**Step 4: Write the first decision record**

Record why the architecture uses six repositories and why the overview repository is intentionally non-authoritative for implementation details.

**Step 5: Initialize the overview repository**

```bash
cd ../bazaarplusplus-overview
git init
git add .
git commit -m "docs: initialize BazaarPlusPlus overview repository"
```

### Task 3: Split out the mac patcher first

**Files:**
- Move from: `../bepinex-mac-patcher/*`
- Move to: `../../bepinex-mac-patcher/*`
- Modify: `../../bepinex-mac-patcher/README.md`

**Step 1: Copy the directory into its new standalone location**

Run: `cp -R ../bepinex-mac-patcher ../../`
Expected: a standalone patcher directory exists outside the aggregate workspace

**Step 2: Review the README for stale relative assumptions**

Run: `sed -n '1,260p' ../../bepinex-mac-patcher/README.md`
Expected: no text implies this tool lives inside another repository

**Step 3: Remove workspace-specific wording if present**

Edit the README so it describes the patcher as a standalone repository.

**Step 4: Initialize git**

```bash
cd ../../bepinex-mac-patcher
git init
git add .
git commit -m "feat: initialize standalone BepInEx macOS patcher repository"
```

### Task 4: Rename and normalize the mod repository

**Files:**
- Existing repository root: `./`
- Modify: `README.md`
- Create if needed: `docs/release-contract.md`

**Step 1: Decide whether to rename the local checkout now or later**

Run: `git remote -v`
Expected: you can see whether a hosted repository name change is also required

**Step 2: Add a release contract document**

Document:

- artifact naming
- supported platforms
- checksum field
- installed file layout
- which fields the installer may rely on

**Step 3: Update the mod README**

Add a short section clarifying:

- this repository owns the installable payload
- the installer consumes releases, not source structure

**Step 4: Verify there are no installer-specific implementation assumptions**

Run: `rg -n "installer|tauri|SvelteKit" .`
Expected: any mention is documentation-level only, not a source-level dependency

**Step 5: Commit**

```bash
cd .
git add README.md docs/release-contract.md
git commit -m "docs: define mod release contract for installer"
```

### Task 5: Make the installer release-contract-driven

**Files:**
- Modify: `../bazaarplusplus-installer/README.md`
- Search in: `../bazaarplusplus-installer/src`
- Search in: `../bazaarplusplus-installer/src-tauri`
- Create if needed: `../bazaarplusplus-installer/docs/mod-artifact-contract.md`

**Step 1: Search for local path assumptions**

Run: `rg -n "BazaarPlusPlus|../|bpp_codes|release|artifact" ../bazaarplusplus-installer`
Expected: any direct dependence on a sibling repository path becomes visible

**Step 2: Write a short installer-side contract document**

Document exactly which mod metadata fields the installer expects and where they come from.

**Step 3: Remove or rewrite any source-layout assumptions**

If code or docs assume a sibling directory such as `../BazaarPlusPlus`, replace that assumption with a release artifact source.

**Step 4: Verify behavior still reflects the intended scope**

Run: `npm test`
Expected: existing installer tests still pass

**Step 5: Commit**

```bash
cd ../bazaarplusplus-installer
git init
git add .
git commit -m "docs: make installer depend on mod release contract"
```

### Task 6: Initialize the site repository independently

**Files:**
- Repository root: `../bazaarplusplus-site`
- Modify: `../bazaarplusplus-site/README.md`

**Step 1: Review the README for ownership boundaries**

Run: `sed -n '1,220p' ../bazaarplusplus-site/README.md`
Expected: the README focuses on site responsibilities only

**Step 2: Add a repository relationship note**

Clarify that the site links to releases and docs but does not own installer or mod implementation.

**Step 3: Verify local checks still pass**

```bash
cd ../bazaarplusplus-site
npm test
npm run typecheck
```

Expected: both commands pass

**Step 4: Initialize git if the repository has not yet been initialized**

```bash
cd ../bazaarplusplus-site
git init
git add .
git commit -m "feat: initialize standalone site repository"
```

### Task 7: Initialize the leaderboard repository independently

**Files:**
- Repository root: `../bazaarplusplus-leaderboard`
- Modify: `../bazaarplusplus-leaderboard/README.md` if created

**Step 1: Add or update a README**

State:

- this repository owns polling, storage, and leaderboard API behavior
- no installer or mod source dependency exists

**Step 2: Review deployment config for standalone operation**

Run: `sed -n '1,220p' ../bazaarplusplus-leaderboard/wrangler.toml`
Expected: the service can be configured independently with its own KV, D1, and secrets

**Step 3: Verify TypeScript compiles if a typecheck command is added**

Run: `npx tsc --noEmit`
Expected: no type errors

**Step 4: Initialize git**

```bash
cd ../bazaarplusplus-leaderboard
git init
git add .
git commit -m "feat: initialize standalone leaderboard repository"
```

### Task 8: Clean up the aggregate local workspace role

**Files:**
- Optional create: `../README.md`

**Step 1: Decide whether to keep `../` as a private convenience workspace**

This directory should be treated as a personal local aggregate, not as an official project boundary.

**Step 2: If keeping it, add a local-only README**

The README should state:

- this directory is a convenience workspace
- each contained project is independently authoritative
- no repository may assume sibling checkout presence

**Step 3: Verify no repository depends on aggregate layout**

Run: `rg -n "bpp_codes|\\.\\./bazaarplusplus-mod|\\.\\./bazaarplusplus" ..`
Expected: no production dependency relies on the aggregate local layout

**Step 4: Commit local README only if this directory becomes a tracked repository**

If the aggregate directory remains untracked, skip commit.

Plan complete and saved to `docs/plans/2026-03-17-bpp-multi-repo-split.md`. Two execution options:

**1. Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

**2. Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

**Which approach?**
