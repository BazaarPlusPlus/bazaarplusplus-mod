# Installer Scope Reduction Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove the BazaarPlusPlus installer settings page and config backend so the installer is strictly a one-time install tool.

**Architecture:** The change is a scope reduction, not a redesign. Remove the dedicated settings route and the backend config commands, then update installer and README copy so all runtime configuration points to in-game settings instead of the installer.

**Tech Stack:** SvelteKit, TypeScript, Tauri, Rust, Node.js

---

### Task 1: Add a regression test for settings removal

**Files:**
- Create: `bppinstaller/scripts/installer-scope.test.mjs`
- Modify: `bppinstaller/package.json`

**Step 1: Write the failing test**

```javascript
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const installerRoot = path.resolve(import.meta.dirname, "..");
const settingsRoute = path.join(installerRoot, "src", "routes", "settings", "+page.svelte");
const homePage = fs.readFileSync(path.join(installerRoot, "src", "routes", "+page.svelte"), "utf8");

test("installer no longer ships a settings route", () => {
  assert.equal(fs.existsSync(settingsRoute), false);
});

test("installer homepage no longer links to /settings", () => {
  assert.equal(homePage.includes('href="/settings'), false);
  assert.equal(homePage.includes("BazaarPlusPlus Settings"), false);
});
```

**Step 2: Run test to verify it fails**

Run: `cd bppinstaller && node --test scripts/installer-scope.test.mjs`
Expected: FAIL because the settings route still exists and the homepage still links to it.

**Step 3: Write minimal implementation**

Add a script entry:

```json
"test": "node --test"
```

**Step 4: Run test to verify it still fails for the right reason**

Run: `cd bppinstaller && npm test -- scripts/installer-scope.test.mjs`
Expected: FAIL with assertions about the existing settings route and homepage link.

**Step 5: Commit**

```bash
git add bppinstaller/package.json bppinstaller/scripts/installer-scope.test.mjs
git commit -m "test: guard installer settings removal"
```

### Task 2: Remove settings route and backend config commands

**Files:**
- Delete: `bppinstaller/src/routes/settings/+page.svelte`
- Delete: `bppinstaller/src-tauri/src/commands/config.rs`
- Modify: `bppinstaller/src-tauri/src/commands/mod.rs`
- Modify: `bppinstaller/src-tauri/src/lib.rs`
- Test: `bppinstaller/scripts/installer-scope.test.mjs`

**Step 1: Write the failing test**

Use the tests from Task 1. No new test is needed until Task 1 is green.

**Step 2: Run test to verify it fails**

Run: `cd bppinstaller && npm test -- scripts/installer-scope.test.mjs`
Expected: FAIL before deletion.

**Step 3: Write minimal implementation**

- remove the settings route file
- remove the `config` module export
- remove `read_mod_config` and `write_config_value` imports and registrations

**Step 4: Run test to verify it passes**

Run: `cd bppinstaller && npm test -- scripts/installer-scope.test.mjs`
Expected: PASS

**Step 5: Commit**

```bash
git add bppinstaller/src-tauri/src/commands/mod.rs bppinstaller/src-tauri/src/lib.rs
git rm bppinstaller/src/routes/settings/+page.svelte bppinstaller/src-tauri/src/commands/config.rs
git commit -m "refactor: remove installer settings backend"
```

### Task 3: Remove settings UI affordances and rewrite copy

**Files:**
- Modify: `bppinstaller/src/routes/+page.svelte`
- Modify: `bppinstaller/src/lib/i18n.ts`
- Modify: `bppinstaller/src/lib/components/InstallerUpdateHighlights.svelte`
- Modify: `README.md`

**Step 1: Write the failing test**

Extend the existing test with new assertions:

```javascript
test("installer copy points runtime configuration to in-game settings", () => {
  assert.equal(homePage.includes("Open Settings"), false);
  assert.equal(homePage.includes("plugin settings page"), false);
  assert.equal(homePage.includes("in-game BazaarPlusPlus settings"), true);
});
```

**Step 2: Run test to verify it fails**

Run: `cd bppinstaller && npm test -- scripts/installer-scope.test.mjs`
Expected: FAIL because the homepage still contains old settings language.

**Step 3: Write minimal implementation**

- remove the installed-state settings link from the home page
- replace installer settings references with in-game settings references
- remove unused settings-related i18n keys
- update release highlights and root README wording

**Step 4: Run test and type-check to verify it passes**

Run: `cd bppinstaller && npm test -- scripts/installer-scope.test.mjs`
Expected: PASS

Run: `cd bppinstaller && npm run check`
Expected: PASS

**Step 5: Commit**

```bash
git add bppinstaller/src/routes/+page.svelte bppinstaller/src/lib/i18n.ts bppinstaller/src/lib/components/InstallerUpdateHighlights.svelte README.md bppinstaller/scripts/installer-scope.test.mjs
git commit -m "refactor: collapse installer to one-time tool"
```

### Task 4: Final verification sweep

**Files:**
- Modify: `bppinstaller/README.md` if needed after search

**Step 1: Write the failing test**

No new test file required. The verification here is search-based and build-based.

**Step 2: Run verification to surface any remaining stale references**

Run: `rg -n "/settings|Settings|plugin settings|设置页|read_mod_config|write_config_value" bppinstaller README.md`
Expected: only intentional references remain, ideally none related to the removed route or backend commands.

**Step 3: Write minimal implementation**

Clean any leftover stale references discovered by the search.

**Step 4: Run final verification**

Run: `cd bppinstaller && npm test -- scripts/installer-scope.test.mjs`
Expected: PASS

Run: `cd bppinstaller && npm run check`
Expected: PASS

Run: `cd bppinstaller/src-tauri && cargo test`
Expected: PASS

**Step 5: Commit**

```bash
git add README.md bppinstaller/README.md bppinstaller/src bppinstaller/src-tauri bppinstaller/scripts/installer-scope.test.mjs bppinstaller/package.json
git commit -m "chore: remove installer settings surface"
```
