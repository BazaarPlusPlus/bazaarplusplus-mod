---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master (commits cf1ac7a9 + 43246bd3 + 9cfafafe); ReturnToMenu action, LoadMainMenu() dispatch, and real isClientBusy all shipped verbatim; schema since advanced 2.1.0 to 2.2.0 by later Continue-action work.

# BazaarAgent Autonomous-Loop Completion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the external agent drive an unattended `run → end-of-run → hero-select → next run` loop by adding a `ReturnToMenu` action and making `isClientBusy` report the real game signal.

**Architecture:** Two gaps, grounded in decompiled source (see the companion design doc `docs/drafts/2026-06-21-bazaaragent-autonomous-loop-completion-design.md`). (1) A new non-`StateOps` action `ReturnToMenu`, symmetric to the existing `StartOrContinueRun`: emitted by the host context reader in the end-of-run states and dispatched via `RunManager.LoadMainMenu()` — the same method the native end screen's "return to menu" button calls. (2) `isClientBusy` reads `AppState.IsWaitingForServerResponse || AppState.BlockInput`. The pure transport core stays `System` + `Newtonsoft.Json` only; all game coupling lives in the host plugin.

**Tech Stack:** C# 12 / netstandard2.1 (mod + host), net10.0 xUnit isolation tests (`tests/BazaarAgent.Tests`), net10.0 xUnit architecture tests (`tests/Architecture.Tests`), Krafs.Publicizer (game internals public at build), BepInEx 5.

---

## File map

| File | Change |
|---|---|
| `src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs` | Add `ReturnToMenu` enum value; bump `BazaarAgentSchema.Version` `2.0.0`→`2.1.0` |
| `tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs` | Add 3 `ReturnToMenu` validator tests |
| `tests/BazaarAgent.Tests/BazaarAgentHttpServerTests.cs` | Update schema-version pin `2.0.0`→`2.1.0` (line 76) |
| `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs` | Add `ReturnToMenu` dispatch case |
| `tests/Architecture.Tests/CoreLayeringTests.cs` | Add single-call-site test for `LoadMainMenu` / no `ReturnToMainMenu` |
| `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs` | `isClientBusy` real value; emit `ReturnToMenu` in end-of-run states |
| `docs/ARCHITECTURE.md` | Note the new action + `isClientBusy` semantics + schema `2.1.0` |

**Why no facade change:** the host already calls `GameInstance`/`AppState`/`Data` directly and reaches services via `Services.Get<T>()`; `RunManager`/`SceneLoader` are reached the same way. `IBazaarAgentGameProbe` is untouched.

---

## Task 0: Branch

- [ ] **Step 1: Create a feature branch**

We are on `master`; branch before committing (project rule: never commit on the default branch).

Run:
```bash
git checkout -b feat/bazaaragent-return-to-menu
```
Expected: `Switched to a new branch 'feat/bazaaragent-return-to-menu'`

---

## Task 1: Pure core — add `ReturnToMenu` kind and bump schema to 2.1.0

**Files:**
- Modify: `src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs`
- Test: `tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs`
- Modify (existing pin): `tests/BazaarAgent.Tests/BazaarAgentHttpServerTests.cs:76`

The validator needs **no logic change** — it has no per-kind allowlist
(`BazaarAgentActionValidator.cs`). `ReturnToMenu` passes Rule 1 (`Enum.IsDefined`) once the enum
value exists, passes Rule 2 when present in `availableActions`, is not card-bearing, and is subject
to the cooldown gate as a non-`Wait` action. These tests pin exactly that.

- [ ] **Step 1: Write the failing validator tests**

Append to `tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs`, immediately before the final
closing `}` of the class (after the `RuleOrder_...` test at line ~445):

```csharp
    // ── ReturnToMenu (end-of-run advance) ─────────────────────────────────────

    [Fact]
    public void ReturnToMenu_InAvailableActions_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.ReturnToMenu));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.ReturnToMenu };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void ReturnToMenu_NotInAvailableActions_RejectsStaleOrUnavailable()
    {
        var snap = MakeSnap(4, SimpleOption(BazaarAgentActionKind.Wait));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.ReturnToMenu };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.NotNull(result.Extra);
        Assert.Equal(4UL, result.Extra["currentTickId"]);
    }

    [Fact]
    public void ReturnToMenu_NonWait_SubjectToCooldown()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.ReturnToMenu));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.ReturnToMenu };
        var result = BazaarAgentActionValidator.Validate(snap, action, cooldownRemainingSeconds: 0.5);
        Assert.Equal(BazaarAgentValidationCode.Cooldown, result.Code);
        Assert.Equal(429, result.HttpStatus);
    }
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run:
```bash
dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj
```
Expected: build error — `'BazaarAgentActionKind' does not contain a definition for 'ReturnToMenu'`.

- [ ] **Step 3: Add the enum value**

In `src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs`, add `ReturnToMenu` as the last
member of `BazaarAgentActionKind` (append at end so existing ordinals are unchanged):

```csharp
public enum BazaarAgentActionKind
{
    Wait,
    StartOrContinueRun,
    AbandonRun,
    SelectItem,
    SelectSkill,
    SelectEncounter,
    CommitToPedestal,
    MoveItem,
    SellItem,
    Reroll,
    ExitState,
    ReturnToMenu,
}
```

(`BazaarAgentActionGroup` is unchanged — `ReturnToMenu` reuses the existing `Flow` group.)

- [ ] **Step 4: Run the tests to verify the new ones pass**

Run:
```bash
dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj
```
Expected: PASS (all tests, including the 3 new `ReturnToMenu_*`). The version is still `2.0.0`, so
`BazaarAgentHttpServerTests` still passes here.

- [ ] **Step 5: Bump the schema version**

In the same file `BazaarAgentDecision.cs`, change the version constant:

```csharp
public static class BazaarAgentSchema
{
    public const string Version = "2.1.0";
}
```

- [ ] **Step 6: Run the tests to see the version pin fail**

Run:
```bash
dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj
```
Expected: FAIL in `BazaarAgentHttpServerTests` — the served body now contains `"2.1.0"` but the
assertion at line 76 still expects `"2.0.0"`.

- [ ] **Step 7: Update the version pin**

In `tests/BazaarAgent.Tests/BazaarAgentHttpServerTests.cs:76`, change:

```csharp
        Assert.Contains("\"schemaVersion\":\"2.1.0\"", body);
```

- [ ] **Step 8: Run the full core test suite to verify green**

Run:
```bash
dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj
```
Expected: PASS (all tests).

- [ ] **Step 9: Commit**

```bash
git add src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs \
        tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs \
        tests/BazaarAgent.Tests/BazaarAgentHttpServerTests.cs
git commit -m "feat(bazaaragent): add ReturnToMenu action kind, bump schema to 2.1.0"
```

---

## Task 2: Host dispatcher — dispatch `ReturnToMenu` via `RunManager.LoadMainMenu()`

**Files:**
- Modify: `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs`
- Test: `tests/Architecture.Tests/CoreLayeringTests.cs`

The architecture test is the red-first gate here (the host binding cannot be unit-tested without the
game; it is a static source scan in the spirit of the existing
`BazaarAgentHost_never_exits_replay_state_...` test). It pins that the end-of-run exit goes through
`LoadMainMenu()` in exactly one host file and never through the heavier session-deleting
`ReturnToMainMenu()`.

- [ ] **Step 1: Write the failing architecture test**

In `tests/Architecture.Tests/CoreLayeringTests.cs`, add this `[Fact]` immediately after the existing
`BazaarAgentHost_never_exits_replay_state_and_main_mod_exits_only_via_continue()` method (it ends
around line 648, before the `private static IEnumerable<string> EnumerateSourceFiles(...)` helper):

```csharp
    // End-of-run states expose no StateOps; the agent advances them with the ReturnToMenu action,
    // which must drive RunManager.LoadMainMenu() — the method the native end-of-run "return to menu"
    // button calls (EndOfRunScreenController.ReturnToMenuClicked). It must NEVER use the heavier
    // RunManager.ReturnToMainMenu() (deletes the session; that path belongs to the in-run pause menu).
    [Fact]
    public void BazaarAgentHost_advances_end_of_run_only_via_LoadMainMenu_in_the_dispatcher()
    {
        var repoRoot = RepoRoot();
        var hostDir = ProjectRoot(repoRoot, "BazaarPlusPlus.BazaarAgentHost");

        var loadMainMenuCallers = new List<string>();
        var wrongPathViolations = new List<string>();
        foreach (var file in EnumerateSourceFiles(hostDir))
        {
            var relative = Path.GetRelativePath(hostDir, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            if (text.Contains("LoadMainMenu(", StringComparison.Ordinal))
                loadMainMenuCallers.Add(relative);
            if (text.Contains("ReturnToMainMenu", StringComparison.Ordinal))
                wrongPathViolations.Add($"{relative}: ReturnToMainMenu (session-deleting path)");
        }

        Assert.Equal(
            new[] { "BazaarAgentGameActionDispatcher.cs" },
            loadMainMenuCallers.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray()
        );
        Assert.True(
            wrongPathViolations.Count == 0,
            "End-of-run advance must use RunManager.LoadMainMenu(), never ReturnToMainMenu(). "
                + "Offending code:\n"
                + string.Join("\n", wrongPathViolations)
        );
    }
```

- [ ] **Step 2: Run the architecture test to verify it fails**

Run:
```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter "FullyQualifiedName~BazaarAgentHost_advances_end_of_run_only_via_LoadMainMenu_in_the_dispatcher"
```
Expected: FAIL — `loadMainMenuCallers` is empty (`Assert.Equal` expected one element, got none),
because the dispatcher does not yet call `LoadMainMenu`.

- [ ] **Step 3: Add the dispatch case**

In `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs`:

First add the `Services` namespace to the `using` block (after the existing `using TheBazaar;` line).
`RunManager` and `SceneLoader` are in the global namespace and need no `using`:

```csharp
using TheBazaar.AppFramework;
```

Then add this case to the `switch (action.ActionKind)` in `Dispatch(...)`, immediately before the
`default:` case (after the `ExitState` case):

```csharp
            case BazaarAgentActionKind.ReturnToMenu:
            {
                var sceneLoader = Services.Get<SceneLoader>();
                if (sceneLoader is null)
                    return new(false, "SceneLoader unavailable");
                if (sceneLoader.IsTransitioning)
                    return new(false, "scene transitioning");
                var runManager = Services.Get<RunManager>();
                if (runManager is null)
                    return new(false, "RunManager unavailable");
                runManager.LoadMainMenu();
                return new(true, null);
            }
```

- [ ] **Step 4: Run the architecture test to verify it passes**

Run:
```bash
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --filter "FullyQualifiedName~BazaarAgentHost_advances_end_of_run_only_via_LoadMainMenu_in_the_dispatcher"
```
Expected: PASS.

- [ ] **Step 5: Build the host to confirm it compiles against the game assemblies**

The host references the game DLLs, so it needs `ManagedPath` (auto-detected from common Steam paths,
or pass it explicitly).

Run:
```bash
./run.sh build --with-bazaaragent
```
Expected: `Build succeeded`. If `ManagedPath` is not auto-detected, run instead:
```bash
dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj -p:ManagedPath="<path>\TheBazaar_Data\Managed"
```

- [ ] **Step 6: Commit**

```bash
git add src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs \
        tests/Architecture.Tests/CoreLayeringTests.cs
git commit -m "feat(bazaaragent-host): dispatch ReturnToMenu via RunManager.LoadMainMenu()"
```

---

## Task 3: Host context reader — real `isClientBusy` and emit `ReturnToMenu`

**Files:**
- Modify: `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs`

No game-less unit test is possible for this file (it reads live game state); it is build-verified
here and behavior-verified in Task 5. `EndRunVictory`/`EndRunDefeat` reach `BuildCore`/`BuildActions`
normally (they are `RunAppState`s with a non-`Unknown` state name, so they do not take the early
lobby return).

- [ ] **Step 1: Add the `Services` namespace import**

In `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs`, add to the `using` block
(after `using TheBazaar;`):

```csharp
using TheBazaar.AppFramework;
```

- [ ] **Step 2: Make `isClientBusy` report the real signal**

Replace the stub at line 203:

```csharp
            IsClientBusy = false, // TODO v2: track HttpGameClient busy state
```

with:

```csharp
            IsClientBusy = AppState.IsWaitingForServerResponse || AppState.BlockInput,
```

- [ ] **Step 3: Compute `endScreenInteractable` in `BuildCore` and pass it to `BuildActions`**

In `BuildCore`, just before the `var actions = BuildActions(` call (currently around line 164), add:

```csharp
        // End-of-run advance: the end screen exposes no StateOps. Mirror the native button's guard
        // (SceneLoader not transitioning) — EndOfRunScreenController.ReturnToMenuClicked.
        var sceneLoader = Services.Get<SceneLoader>();
        bool endScreenInteractable = sceneLoader != null && !sceneLoader.IsTransitioning;
```

Then add `endScreenInteractable` as the final argument of the `BuildActions(...)` call. The call
currently ends with `targeting.PedestalEligibleInstanceIds`; change that line to:

```csharp
            targeting.PedestalEligibleInstanceIds,
            endScreenInteractable
        );
```

- [ ] **Step 4: Add the parameter to the `BuildActions` signature**

In the `BuildActions(...)` method signature, add a final parameter after
`HashSet<string> pedestalEligibleIds`:

```csharp
        HashSet<string> pedestalEligibleIds,
        bool endScreenInteractable
    )
```

- [ ] **Step 5: Emit the `ReturnToMenu` option in the end-of-run states**

Inside `BuildActions`, add this block immediately before the final `return actions;`:

```csharp
        // 10. ReturnToMenu — end-of-run states expose no StateOps; this advances the end screen
        // back to hero-select (dispatched as RunManager.LoadMainMenu()). Guarded to mirror the
        // native button: only while the scene loader is not transitioning.
        if (
            (
                stateName == BazaarAgentRunStateName.EndRunVictory
                || stateName == BazaarAgentRunStateName.EndRunDefeat
            )
            && endScreenInteractable
        )
        {
            actions.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.ReturnToMenu,
                    Group = BazaarAgentActionGroup.Flow,
                    DisplayKey = "ReturnToMenu",
                }
            );
        }
```

- [ ] **Step 6: Build the host to confirm it compiles**

Run:
```bash
./run.sh build --with-bazaaragent
```
Expected: `Build succeeded`.

- [ ] **Step 7: Commit**

```bash
git add src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs
git commit -m "feat(bazaaragent-host): report real isClientBusy and emit ReturnToMenu at end-of-run"
```

---

## Task 4: Docs — update the live architecture doc

**Files:**
- Modify: `docs/ARCHITECTURE.md`

`docs/ARCHITECTURE.md` may be edited day-to-day (project rule). The canonical wire reference
`docs/archive/reference/bazaar-agent-http-api-v1.md` is archived/historical — do **not** edit it
here; flag it for a consolidation refresh in the wrap-up.

- [ ] **Step 1: Extend the BazaarAgent section**

In `docs/ARCHITECTURE.md`, in the "BazaarAgent Optional Host" section (the routes list around line
109-116), add a sentence after the routes describing the new action and the busy signal:

```markdown
The action set (`POST /v1/actions`) adds `ReturnToMenu` (schema `2.1.0`): emitted only in
`EndRunVictory`/`EndRunDefeat` while the scene loader is not transitioning, it advances the
end-of-run screen back to hero-select via `RunManager.LoadMainMenu()` — the same call the native
"return to menu" button makes — closing the unattended `run → next run` loop. `isClientBusy` now
reflects the real client state (`AppState.IsWaitingForServerResponse || AppState.BlockInput`) instead
of a constant `false`.
```

- [ ] **Step 2: Commit**

```bash
git add docs/ARCHITECTURE.md
git commit -m "docs(architecture): note ReturnToMenu action and real isClientBusy (schema 2.1.0)"
```

---

## Task 5: Full verification

**No game-less integration test exists for the host binding; verify on the main path via build +
reload** (project rule: no standalone probe scaffolding).

- [ ] **Step 1: Run all test projects**

Run:
```bash
./run.sh test
```
Expected: all test projects under `tests/` pass.

- [ ] **Step 2: Format**

Run:
```bash
./run.sh format
```
If csharpier reformats files **outside** this change, keep that in a separate commit (project rule:
keep commits scoped).

- [ ] **Step 3: Build + install the host, then launch via Steam**

Run:
```bash
./run.sh build --with-bazaaragent
```
Then launch The Bazaar through Steam (App ID 1617400) — `start steam://run/1617400` — so Steam
runtime state is present (do not launch the exe directly).

- [ ] **Step 4: Manual loop verification**

1. Reach the end-of-run screen (finish or abandon a run).
2. `GET http://127.0.0.1:47900/v1/context` → assert `stateName` is `endRunVictory`/`endRunDefeat`
   and `availableActions` now contains a `returnToMenu` option; assert `isClientBusy` reads `true`
   during a known busy window (e.g. immediately after a shop action) and returns to `false`.
3. `POST http://127.0.0.1:47900/v1/actions` with body `{"actionKind":"ReturnToMenu"}` → expect
   `200` with `"executed":true`.
4. Poll `GET /v1/context` until `canStartOrContinueRun` is `true` at hero-select.
5. `POST /v1/actions` with `{"actionKind":"StartOrContinueRun","hero":"Pygmalien","playMode":"Unranked"}`
   → confirm a fresh run starts (loop closed).

If any step fails, read `<GameDir>/BepInEx/LogOutput.log` (`[BPP][...]` lines) and root-cause against
decompiled source before patching.

- [ ] **Step 5: Wrap-up (when the user approves merging)**

Review the full diff, then — per project wrap-up rule — commit, merge `feat/bazaaragent-return-to-menu`
to `master`, push, and delete the merged branch. **Also flag** that the archived wire-contract
reference `docs/archive/reference/bazaar-agent-http-api-v1.md` is now stale w.r.t. `ReturnToMenu` /
`isClientBusy` (schema `2.1.0`) and should be refreshed in the next docs consolidation run.

---

## Self-review notes

- **Spec coverage:** §4.1 (enum + schema + no-validator-change) → Task 1; §4.2 (isClientBusy + emit)
  → Task 3; §4.3 (dispatcher) → Task 2; §6 (tests: validator + arch single-call-site) → Tasks 1–2;
  §5 (wire contract docs) → Task 4 (live doc) + Task 5 step 5 (archived doc flagged); §7
  (verification) → Task 5.
- **`ReturnToMenu` wire serialization** is handled by the shared `StringEnumConverter` +
  `CamelCasePropertyNamesContractResolver` (same path as every other enum value); it surfaces as
  `returnToMenu` and is verified end-to-end in Task 5 step 4. The schema-version pin is covered by
  the updated `BazaarAgentHttpServerTests` assertion.
- **Type/name consistency:** `BazaarAgentActionKind.ReturnToMenu`, `BazaarAgentActionGroup.Flow`,
  `RunManager.LoadMainMenu()`, `SceneLoader.IsTransitioning`, `Services.Get<T>()`
  (`TheBazaar.AppFramework`), `AppState.IsWaitingForServerResponse`, `AppState.BlockInput`,
  `endScreenInteractable` are used consistently across tasks.
- **Open risk (from the design doc §8):** a `ReturnToMenu` POST that races past the validator while
  `IsTransitioning` returns `(false, "scene transitioning")` → currently maps to HTTP `500`. This is
  a pre-existing dispatch-result→HTTP limitation, intentionally not widened here.
