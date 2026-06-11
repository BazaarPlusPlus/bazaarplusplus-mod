---
status: implemented
archived: 2026-06-12
calibrated: 2026-06-11
superseded-by: code
---

> Status: IMPLEMENTED. The live-game package-card art resolution goal was met — both `ItemVisualsController` and `RewardController` paths apply custom package art (verified `src/BazaarPlusPlus/Patches/CardArtReplacement/`). Retained as the runtime-validation goal record.

# Package Card Art Live Game Resolution Goal

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to execute this Goal task-by-task. Use `superpowers:systematic-debugging` before any source edit if runtime evidence does not match the primary hypothesis. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fully fix the remaining live-game package-card art replacement failure, validate it in the running game with Computer Use, remove any proven-dead replacement path, and finish with a scoped commit pushed to `master`.

**Architecture:** Treat code, runtime logs, and `decompiled/` as the source of truth. Reclassify the current failing surface before patching: the latest runtime evidence points at an `ItemVisualsController` item-card path rather than the previously added `RewardController` path. Keep package identity centralized on `EHiddenTag.Package` through `PackageIdentity`; do not add name or `ArtKey` heuristics.

**Tech Stack:** C# 12, BepInEx 5.x, Harmony patches, Unity `Material` / `Texture2D`, The Bazaar publicized game assemblies, Computer Use, xUnit, `./run.sh`.

---

## Current Inputs

- Handoff: `/tmp/bpp-package-art-fix-handoff.md`
- Previous implementation plan: `docs/plans/package-card-art-live-game-replacement-fix.md`
- Runtime log: `~/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log`
- Runtime config: `~/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/config/BazaarPlusPlus.cfg`
- Runtime custom-art directory: `~/Library/Application Support/Steam/steamapps/common/The Bazaar/BazaarPlusPlusV4/CustomCardArt`
- Current game state: continuing into the game should show the package event; use Computer Use to capture the visible surface before changing code.

## Source Anchors

- `Plugin` installs services, applies Harmony patches, and starts composition before runtime mountables (`src/BazaarPlusPlus/Plugin.cs:45-67`).
- Package art replacement is feature-registered by composition (`src/BazaarPlusPlus/BppComposition.cs:83-95`).
- The package-art setting defaults off in config and is read through `EnablePackageArtReplacementConfig` (`src/BazaarPlusPlus/Core/Config/BppConfig.cs:92-96`).
- Package identity is a hidden-tag-only predicate (`src/BazaarPlusPlus/GameInterop/Cards/PackageIdentity.cs:9-21`).
- Live item art replacement currently runs in the `ItemVisualsController.SetCardFrameMaterial` postfix and gates on `activeInHierarchy` (`src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:28-67`).
- The active gate itself is at `src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:42-45`.
- Live card identity is tracked in the `ItemVisualsController.Setup(Card, BazaarCollectionLoadout)` prefix (`src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:14-25`).
- Runtime package identity falls back from runtime tags to template tags and then ready static data (`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs:60-83`).
- `CardArtInjector.Apply(ItemVisualsController, Texture2D)` writes the texture through `cardIllustrationRenderer.sharedMaterial` (`src/BazaarPlusPlus/GameInterop/CardArtReplacement/CardArtInjector.cs:91-117`).
- The current RewardController patch wraps `RewardController.Setup(string, Card)` and writes `_MainTex` on `instancedMaterial` (`src/BazaarPlusPlus/Patches/CardArtReplacement/RewardControllerArtReplacePatch.cs:15-72`).
- Native card dispatch uses runtime `Card.Type`, not template type (`decompiled/TheBazaarRuntime/AssetLoader.cs:266-288`).
- Native item-card instantiation sets the item object inactive, awaits `ItemController.Setup`, then activates it (`decompiled/TheBazaarRuntime/AssetLoader.cs:459-464`).
- Native item setup calls `visualsController.Setup(bazaarCard)` before later card initialization (`decompiled/TheBazaarRuntime/ItemController.cs:852-878`).
- Native item visuals call `SetCardFrameMaterial` from `Setup(CardAssetDataSO, ...)` (`decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs:267-280`).
- Native `SetCardFrameMaterial` creates the per-card material instance and assigns it to `cardIllustrationRenderer.sharedMaterial` (`decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs:189-217`).
- Native reward cards route through `ConstructInstantiateReward` only for runtime `ECardType.EncounterStep` (`decompiled/TheBazaarRuntime/AssetLoader.cs:277-278`, `decompiled/TheBazaarRuntime/AssetLoader.cs:354-377`).
- Native reward setup writes `_MainTex` on `instancedMaterial` (`decompiled/TheBazaarRuntime/RewardController.cs:138-153`).

## Primary Hypothesis

The current visible package event is likely not a `RewardController` failure. The latest observed `ShopForecast` log for the package event listed the four package choices as runtime `type=Item`, with package item template IDs. For item cards, the native object is inactive while `ItemController.Setup` and `ItemVisualsController.Setup` run, and the current BPP postfix returns early when `activeInHierarchy` is false. If that evidence still holds in the fresh game session, the fix should target the `ItemVisualsController` item path first.

Do not assume this hypothesis is true. Confirm it with a fresh screenshot and the current `LogOutput.log` before editing code.

## Success Criteria

- The current package event choice cards show custom package art with `Package Swap` / `掉包快递` enabled.
- CollectionPanel package previews still show custom package art.
- A normal live package item visual that uses `ItemVisualsController` shows custom package art.
- Non-package item cards and non-package encounter choices keep native art.
- Turning `Package Swap` / `掉包快递` off prevents replacement on newly loaded visuals.
- Live hover/tooltip previews through `CardPreviewItem` outside CollectionPanel remain out of scope and are not treated as a failure.
- `RewardControllerArtReplacePatch` is deleted only if runtime evidence proves it is not needed for any real package surface after the item-path fix.
- No source edit touches `decompiled/`.
- No package-name or `ArtKey` substring fallback is introduced.
- No temporary probe remains in the final diff.
- `dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj` passes.
- `./run.sh test` passes or any unrelated failure is reported with the exact failure string.
- `./run.sh build` succeeds and copies the Debug plugin into the detected BepInEx plugins folder.
- The final runtime log has no repeated `CardArtReplacement` warning spam.

## Tool Boundaries

- Use Computer Use for local game UI observation and screenshots.
- Computer Use may click through to the current package event, open settings, and capture screenshots.
- Do not buy, select, or commit to a package choice through Computer Use unless validation truly requires it and the user has explicitly allowed that action.
- Always launch the game through Steam for runtime validation:

```bash
open "steam://run/1617400"
```

- Do not launch `TheBazaar.app` directly and do not use `run_bepinex.sh` on macOS.

## Task 1: Capture The Current Runtime Failure

**Files:**
- Read: `/tmp/bpp-package-art-fix-handoff.md`
- Read: `docs/plans/package-card-art-live-game-replacement-fix.md`
- Read: `~/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log`
- Read: `~/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/config/BazaarPlusPlus.cfg`
- Create local evidence: `/tmp/bpp-package-event-current.png`

- [ ] **Step 1: Read the handoff and previous plan**

Run:

```bash
nl -ba /tmp/bpp-package-art-fix-handoff.md | sed -n '1,140p'
nl -ba docs/plans/package-card-art-live-game-replacement-fix.md | sed -n '1,140p'
```

Expected: the handoff identifies the last shipped commits and says runtime validation is still the next work; the previous plan explains the old `RewardController` hypothesis.

- [ ] **Step 2: Confirm the runtime config**

Run:

```bash
CFG="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/config/BazaarPlusPlus.cfg"
nl -ba "$CFG" | rg -n -C 3 "CardArtReplacement|EnablePackageArtReplacement"
```

Expected: `EnablePackageArtReplacement = true`. If it is false, enable it in-game or in config only after recording the current value; do not call the code broken until the setting is enabled.

- [ ] **Step 3: Capture the visible failure**

Use Computer Use to continue into the current game until the package event is visible. Save a screenshot at:

```text
/tmp/bpp-package-event-current.png
```

Expected: the screenshot captures the package-choice surface before any package is selected.

- [ ] **Step 4: Read the fresh runtime log**

Run:

```bash
LOG="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log"
stat -f '%Sm %N' "$LOG"
rg -n "BPP|CardArtReplacement|Harmony|Reward postfix|Postfix failed|ShopForecast|Plugin .*loaded|Custom card art catalog|Package" "$LOG" | tail -n 220
```

Expected: the log is fresh for the current game session, includes BPP startup, includes Harmony patch application, includes custom card art catalog count, and includes the current package event's `ShopForecast` lines.

- [ ] **Step 5: Verify runtime custom-art files for the visible package choices**

For each package template ID found in the `ShopForecast` lines, run:

```bash
ART="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/BazaarPlusPlusV4/CustomCardArt"
ls "$ART/<template-id>".* 2>/dev/null || echo "missing <template-id>"
```

Expected: every visible package template has at least one custom art file in the runtime directory. If a file is missing, inspect the bundled source under `src/BazaarPlusPlus/Resources/CustomCardArt/` before changing code.

## Task 2: Classify The Failing Surface

**Files:**
- Read: `src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs`
- Read: `decompiled/TheBazaarRuntime/AssetLoader.cs`
- Read: `decompiled/TheBazaarRuntime/ItemController.cs`
- Read: `decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs`
- Read: `decompiled/TheBazaarRuntime/RewardController.cs`

- [ ] **Step 1: Interpret the current `ShopForecast` selection lines**

Inspect the current log lines. The formatter records runtime card type and template details from `Data.Entities` (`src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs:119-130`).

Expected:

- If the failing package choices are `type=Item`, classify the surface as `ItemVisualsController` item path.
- If the failing package choices are `type=EncounterStep`, classify the surface as `RewardController` path.
- If the surface is hover/tooltip only, record it as out of scope unless the user explicitly expands scope.

- [ ] **Step 2: Prove or disprove the item inactive-gate hypothesis**

Use these evidence anchors:

- Item cards are instantiated through `ConstructAndInstantiateCard` (`decompiled/TheBazaarRuntime/AssetLoader.cs:459-464`).
- Item visual setup runs before the object is activated (`decompiled/TheBazaarRuntime/AssetLoader.cs:459-464`).
- `ItemController.Setup` calls `visualsController.Setup(bazaarCard)` (`decompiled/TheBazaarRuntime/ItemController.cs:852-859`).
- `ItemVisualsController.Setup(CardAssetDataSO, ...)` calls `SetCardFrameMaterial` (`decompiled/TheBazaarRuntime/TheBazaar.Game.CardFrames/ItemVisualsController.cs:267-272`).
- BPP currently returns when `activeInHierarchy` is false (`src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs:42-45`).

Expected: if the current surface is `type=Item`, this chain explains why the postfix can be skipped before the card becomes visible.

- [ ] **Step 3: Re-evaluate the RewardController path**

Use these evidence anchors:

- Dispatch to reward happens for runtime `ECardType.EncounterStep` (`decompiled/TheBazaarRuntime/AssetLoader.cs:266-288`).
- `ConstructInstantiateReward` calls `RewardController.Setup` (`decompiled/TheBazaarRuntime/AssetLoader.cs:354-377`).
- `RewardController.Setup` writes `_MainTex` (`decompiled/TheBazaarRuntime/RewardController.cs:138-153`).
- The current BPP reward patch targets that setup (`src/BazaarPlusPlus/Patches/CardArtReplacement/RewardControllerArtReplacePatch.cs:15-72`).

Expected: if the fresh log shows `type=Item`, do not continue investing in `RewardController` for the current failure. Treat the existing reward patch as a path that may be dead or future-only until runtime evidence proves otherwise.

## Task 3: Add A Minimal Runtime Probe Only If Needed

**Files:**
- Modify temporarily: `src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs`

- [ ] **Step 1: Decide whether the probe is necessary**

Add a temporary probe only if Tasks 1 and 2 do not provide enough evidence to identify the failure. If the item inactive-gate evidence is already decisive, skip this task.

- [ ] **Step 2: Add one compact probe in the item postfix**

If needed, add a `BppLog.Info` line inside `ItemVisualsArtReplacePatch.Postfix` before the early returns. Include:

- `card.TemplateId`
- `card.Type`
- `__instance.gameObject.activeSelf`
- `__instance.gameObject.activeInHierarchy`
- result of `CardArtInjector.IsPackageCard(card)`
- result of `CardArtReplacementFeature.Current.TryGetTexture(card.TemplateId, out _, out sourcePath)`
- `sourcePath`

Expected: the probe distinguishes "not executing", "not package", "missing texture", and "skipped because inactive".

- [ ] **Step 3: Build, run, and remove the probe after validation**

Run:

```bash
./run.sh build
open "steam://run/1617400"
```

Use Computer Use to re-enter the package event and read the log. After the probe proves the root cause, remove the probe before final verification.

## Task 4: Implement The Minimal Fix

**Files:**
- Modify: `src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs`
- Test if needed: `tests/CardArtReplacement.Tests/CardArtReplacementTests.cs`
- Delete if proven dead: `src/BazaarPlusPlus/Patches/CardArtReplacement/RewardControllerArtReplacePatch.cs`
- Modify if RewardController conclusion changes: `docs/plans/package-card-art-live-game-replacement-fix.md`

- [ ] **Step 1: Fix the item path first if the current surface is `type=Item`**

The likely fix is to remove or relax the `activeInHierarchy` gate from `ItemVisualsArtReplacePatch.Postfix`, while preserving these guards:

- setting enabled through `PackageCardArtReplacementPolicy.IsEnabled`
- non-null `ItemVisualsController`
- resolvable runtime card identity
- `CardArtInjector.IsPackageCard(card)`
- available custom texture for `card.TemplateId`
- successful material application through `CardArtInjector.Apply`

Expected: inactive setup-time item visuals can receive the custom texture before they become visible, while non-package cards remain protected by hidden-tag identity checks.

- [ ] **Step 2: Do not change package identity policy**

Do not add checks based on `InternalName`, displayed name, `ArtKey`, or filename substrings. The only package identity source is `EHiddenTag.Package` through `PackageIdentity`.

- [ ] **Step 3: Do not broaden hover/tooltip preview replacement**

Do not remove the `CollectionPanelOwnedMarker` gate from `CardPreviewItemArtReplacePatch`. Normal gameplay item coverage belongs in `ItemVisualsController`; live hover/tooltip preview remains out of scope.

- [ ] **Step 4: Decide whether `RewardControllerArtReplacePatch` is dead**

Delete `RewardControllerArtReplacePatch.cs` only if all of these are true:

- the current package event failure is proven to be `type=Item`;
- the item-path fix makes the current package event render correctly;
- no runtime log or screenshot evidence shows an actual `EncounterStep` package surface needing reward replacement;
- CollectionPanel and normal live item package surfaces remain correct after the item-path fix.

If the patch is deleted, update `docs/plans/package-card-art-live-game-replacement-fix.md` so the document no longer presents RewardController as confirmed required for the current issue. Record the revised conclusion as a new dated revision entry rather than silently rewriting history.

## Task 5: Verify In Tests, Build, And Game

**Files:**
- Read: `~/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log`
- Create local evidence: `/tmp/bpp-package-event-fixed.png`

- [ ] **Step 1: Run focused tests**

Run:

```bash
dotnet test tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Run the repository test entrypoint**

Run:

```bash
./run.sh test
```

Expected: all projects pass. If an executable test reports failure while the process exits zero, keep the exact failure string and do not mark verification complete.

- [ ] **Step 3: Build the mod**

Run:

```bash
./run.sh build
```

Expected: Debug build succeeds and copies the plugin into the detected BepInEx plugins folder.

- [ ] **Step 4: Launch through Steam**

Run:

```bash
open "steam://run/1617400"
```

Expected: The Bazaar starts through Steam with the new build. Do not launch the app directly.

- [ ] **Step 5: Validate visible behavior**

Use Computer Use and save the fixed screenshot:

```text
/tmp/bpp-package-event-fixed.png
```

Validate:

- current package event choice cards show custom package art;
- CollectionPanel package previews still show custom package art;
- a normal live package item visual shows custom package art;
- non-package item cards and non-package encounter choices keep native art;
- turning `Package Swap` / `掉包快递` off prevents replacement on newly loaded visuals;
- live hover/tooltip preview remains out of scope if it still shows native art.

- [ ] **Step 6: Check the BepInEx log**

Run:

```bash
LOG="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/LogOutput.log"
rg -n "CardArtReplacement|Reward postfix|Postfix failed|Harmony|Custom card art catalog|ShopForecast" "$LOG" | tail -n 220
```

Expected: no repeated `CardArtReplacement` warning spam, startup still reports the custom card art catalog count, and the current package event log matches the validated surface.

## Task 6: Review, Commit, And Push

**Files:**
- Modify: source files and docs actually needed by the fix

- [ ] **Step 1: Review the diff**

Run:

```bash
git status --short
git diff -- src/BazaarPlusPlus/Patches/CardArtReplacement docs/plans tests/CardArtReplacement.Tests
```

Expected:

- no edits under `decompiled/`;
- no package-name or `ArtKey` substring fallback;
- no unrelated formatting churn;
- no temporary probe;
- docs and code conclusions agree;
- `RewardControllerArtReplacePatch.cs` is either kept with a runtime-backed reason or deleted with plan-doc updates.

- [ ] **Step 2: Run a placeholder scan on changed docs**

Run:

```bash
pattern=$(printf '%s' 'TO''DO|TB''D|fill'' in|implement'' later|appro''priate')
rg -n "$pattern" docs/plans/package-card-art-live-game-resolution-goal.md docs/plans/package-card-art-live-game-replacement-fix.md
```

Expected: no matches in changed docs.

- [ ] **Step 3: Stage only intended files**

Run:

```bash
git add \
  src/BazaarPlusPlus/Patches/CardArtReplacement/ItemVisualsArtReplacePatch.cs \
  src/BazaarPlusPlus/Patches/CardArtReplacement/RewardControllerArtReplacePatch.cs \
  docs/plans/package-card-art-live-game-replacement-fix.md \
  tests/CardArtReplacement.Tests/CardArtReplacementTests.cs \
  tests/CardArtReplacement.Tests/CardArtReplacement.Tests.csproj
git diff --cached --check
git diff --cached --stat
```

Expected: staged files are scoped to this fix and `git diff --cached --check` is clean.

- [ ] **Step 4: Commit**

Run:

```bash
git commit -m "Fix live package card art replacement"
```

Expected: commit succeeds with only the scoped fix.

- [ ] **Step 5: Ensure master contains the fix and push**

If already on `master`, run:

```bash
git status --short --branch
git push origin master
```

If on a topic branch, merge the completed branch into `master` without sweeping unrelated changes, then push `master`.

Expected: `master` includes the fix and is pushed to `origin/master`.

## Final Report Contract

The final response must be in Chinese and include:

- current failing surface;
- root cause with key `file:line` and log-line evidence;
- fix summary;
- whether `RewardControllerArtReplacePatch` was kept or deleted, with reason;
- before screenshot path: `/tmp/bpp-package-event-current.png`;
- after screenshot path: `/tmp/bpp-package-event-fixed.png`;
- focused test result;
- full test result;
- build result;
- runtime validation result;
- commit hash;
- any **Suggested rule additions** if a non-obvious repeated pattern was proven.
