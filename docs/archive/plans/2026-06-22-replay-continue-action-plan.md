---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master: mod Tasks 1-3 shipped (5481cfbb enum+schema 2.2.0, e4efc6ed reader emit, 5ab53f5b dispatcher route); Task 4 lives in sibling bazaarplusplus-agent repo. ADR-0008 accepted.

# Replay Continue as Agent Action — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the post-combat replay "continue" as a generic `Continue` Flow action in the mod's `availableActions`, routed to the existing `TryContinueReplay` facade, so the replay-agnostic decision agent stops stalling after every battle.

**Architecture:** Mod-side change in three spots (enum + context reader emit + dispatcher route) reusing ADR-0007's single continue facade; the validator and the agent need no behavior change. One agent-side test pins the cross-repo assumption that a `Flow` action is offered in `Replay`. Spec: `docs/adr/0008-replay-continue-as-agent-action.md`.

**Tech Stack:** C# / .NET 10 / xUnit (mod); Bun + TypeScript / bun:test (agent).

---

## Cross-repo layout

This plan spans **two repos**. Each task is tagged with its repo and run from that repo's root.

- **[mod]** `E:\repos\bpp\bazaarplusplus-mod`
  - Modify: `src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs` (enum + schema — pure core)
  - Modify: `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs` (emit — game-coupled)
  - Modify: `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs` (route — game-coupled)
  - Modify: `tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs`, `tests/BazaarAgent.Tests/BazaarAgentHttpServerTests.cs`
- **[agent]** `E:\repos\bpp\bazaarplusplus-agent`
  - Create: `tests/policy/replayFlow.test.ts`

**Build/test commands**
- Mod core tests (pure, no game DLLs): `dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj`
- Mod host build (references game assemblies — supply your local managed path, see [README](../README.md)): `dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj -p:ManagedPath=<TheBazaar_Data/Managed>`
- Agent test: `& "C:\Users\cauyx\.bun\bin\bun.exe" test tests/policy/replayFlow.test.ts`

**Testability note:** `BazaarAgentGameContextReader` and `BazaarAgentGameActionDispatcher` live in `BazaarPlusPlus.BazaarAgentHost`, which reads live Unity/game statics (`AppState.CurrentState`, `Data.Run`, `BazaarAgentGameBridge.CurrentRecorder`) and is **not** unit-testable in the test project (it references only the pure `BazaarPlusPlus.BazaarAgent` core). Tasks 2 and 3 are therefore verified by `dotnet build` + the live checklist, not by unit tests — the same boundary every other Host adapter sits behind. The pure core (enum, validator) IS unit-tested in Task 1.

---

### Task 1 [mod]: Add the `Continue` action kind + bump the schema

**Files:**
- Modify: `src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs`
- Test: `tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs`
- Modify: `tests/BazaarAgent.Tests/BazaarAgentHttpServerTests.cs`

- [ ] **Step 1: Write the failing validator tests**

Append to `tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs` (before the closing brace of the class):

```csharp
    // ── Continue (replay advance) ─────────────────────────────────────────────

    [Fact]
    public void Continue_InAvailableActions_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Continue));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Continue };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Continue_NotInAvailableActions_RejectsStale()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Wait));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Continue };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
    }
```

- [ ] **Step 2: Run the tests to verify they fail (compile error)**

Run: `dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj`
Expected: FAIL — build error, `BazaarAgentActionKind` does not contain `Continue`.

- [ ] **Step 3: Add the enum value and bump the schema**

In `src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs`, add `Continue` as the last entry of `BazaarAgentActionKind`:

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
    Continue,
}
```

And bump the schema version in the same file:

```csharp
public static class BazaarAgentSchema
{
    public const string Version = "2.2.0";
}
```

- [ ] **Step 4: Update the schema-version assertion in the HTTP test**

The HTTP server test pins the version string. In `tests/BazaarAgent.Tests/BazaarAgentHttpServerTests.cs:76`, change:

```csharp
        Assert.Contains("\"schemaVersion\":\"2.1.0\"", body);
```

to:

```csharp
        Assert.Contains("\"schemaVersion\":\"2.2.0\"", body);
```

- [ ] **Step 5: Run the full mod-core suite to verify it passes**

Run: `dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj`
Expected: PASS (new `Continue_*` tests green; the updated HTTP assertion green; no other regressions).

- [ ] **Step 6: Commit**

```bash
git -C /e/repos/bpp/bazaarplusplus-mod add src/BazaarPlusPlus.BazaarAgent/Contract/BazaarAgentDecision.cs tests/BazaarAgent.Tests/BazaarAgentActionValidatorTests.cs tests/BazaarAgent.Tests/BazaarAgentHttpServerTests.cs
git -C /e/repos/bpp/bazaarplusplus-mod commit -m "$(cat <<'EOF'
feat(bazaaragent): add Continue action kind; schema 2.2.0

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2 [mod]: Emit `Continue` from the context reader at `finishedAwaitingContinue`

**Files:**
- Modify: `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs`

This file is game-coupled (not unit-testable). Verify by `dotnet build` + the live checklist in Final Verification.

- [ ] **Step 1: Thread `replayPhase` into `BuildActions` (signature)**

In `BazaarAgentGameContextReader.cs`, add a `replayPhase` parameter as the first argument of `BuildActions`. Change the signature from:

```csharp
    private static IReadOnlyList<BazaarAgentDecisionOption> BuildActions(
        BazaarAgentRunStateName stateName,
        bool isInRun,
```

to:

```csharp
    private static IReadOnlyList<BazaarAgentDecisionOption> BuildActions(
        BazaarAgentRunStateName stateName,
        BazaarAgentReplayPhase replayPhase,
        bool isInRun,
```

- [ ] **Step 2: Pass `replayPhase` at the call site**

In `BuildCore`, the `var actions = BuildActions(` call currently starts with `stateName,`. `replayPhase` is already in scope (computed earlier via `ReadReplayPhase()`). Change the call from:

```csharp
        var actions = BuildActions(
            stateName,
            isInRun,
```

to:

```csharp
        var actions = BuildActions(
            stateName,
            replayPhase,
            isInRun,
```

- [ ] **Step 3: Emit the `Continue` Flow option**

Inside `BuildActions`, immediately after the `// 1. Wait (always first)` block (`actions.Add(WaitOption());`), insert:

```csharp
        // 1b. Continue — replay finished and awaiting the continue button. Surface it as a generic
        // Flow advance so the replay-agnostic agent can proceed; the dispatcher routes Continue to
        // CombatReplayRuntime.TryContinueReplay (ADR-0008). No card, no target.
        if (replayPhase == BazaarAgentReplayPhase.FinishedAwaitingContinue)
        {
            actions.Add(
                new BazaarAgentDecisionOption
                {
                    ActionKind = BazaarAgentActionKind.Continue,
                    Group = BazaarAgentActionGroup.Flow,
                    DisplayKey = "Continue",
                }
            );
        }
```

- [ ] **Step 4: Build the host to verify it compiles**

Run: `dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj -p:ManagedPath=<TheBazaar_Data/Managed>`
Expected: build succeeds (0 errors).

- [ ] **Step 5: Commit**

```bash
git -C /e/repos/bpp/bazaarplusplus-mod add src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs
git -C /e/repos/bpp/bazaarplusplus-mod commit -m "$(cat <<'EOF'
feat(bazaaragent): emit Continue action when replay awaits continue

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3 [mod]: Route `Continue` to `TryContinueReplay` in the dispatcher

**Files:**
- Modify: `src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs`

Game-coupled (not unit-testable). Verify by `dotnet build` + the live checklist.

- [ ] **Step 1: Add the `Continue` case**

In `BazaarAgentGameActionDispatcher.cs`, inside the `switch (action.ActionKind)` in `Dispatch`, add this case immediately before `default:`:

```csharp
            case BazaarAgentActionKind.Continue:
            {
                var recorder = BazaarAgentGameBridge.CurrentRecorder;
                if (recorder is null)
                    return new(false, "replay recorder unavailable");
                var result = recorder.TryContinueReplay();
                return result.Status == BppReplayControlStatus.Accepted
                    ? new(true, null)
                    : new(false, result.FailureReason ?? result.Status.ToString());
            }
```

(`BazaarAgentGameBridge`, `BppReplayControlStatus`, and `BppReplayControlResult` are in `BazaarPlusPlus.GameInterop`, already imported at the top of this file. This is a second caller of the same facade the `/v1/replay/continue` route uses — it adds no new `ReplayState` exit, so ADR-0007's "no `.Exit()` in the host assembly" architecture test still holds.)

- [ ] **Step 2: Build the host to verify it compiles**

Run: `dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj -p:ManagedPath=<TheBazaar_Data/Managed>`
Expected: build succeeds (0 errors).

- [ ] **Step 3: Commit**

```bash
git -C /e/repos/bpp/bazaarplusplus-mod add src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs
git -C /e/repos/bpp/bazaarplusplus-mod commit -m "$(cat <<'EOF'
feat(bazaaragent): dispatch Continue to TryContinueReplay

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4 [agent]: Pin "Replay offers a Flow action ⇒ safe policy continues, not sells"

**Files:**
- Create: `tests/policy/replayFlow.test.ts`

- [ ] **Step 1: Write the failing test**

Create `E:\repos\bpp\bazaarplusplus-agent\tests/policy/replayFlow.test.ts`:

```ts
import { expect, test } from "bun:test";
import { RuntimeDecisionContext } from "../../src/domain/context.ts";
import { SafePlaceholderPolicy } from "../../src/policy/safePlaceholder.ts";

// Pins ADR-0008: in a Replay state the mod offers a Flow `Continue` action, and the safe policy
// must pick it ahead of the (allowed) Sell of the freshly-won item — Replay is a flow state.
test("safe policy picks the Flow Continue over Sell in a Replay state", () => {
  const ctx = RuntimeDecisionContext.fromDict({
    stateName: "Replay",
    isClientBusy: false,
    availableActions: [
      { actionKind: "Wait", group: "Wait" },
      { actionKind: "SellItem", group: "Sell", card: { instanceId: "itm_1", canSell: true } },
      { actionKind: "Continue", group: "Flow" },
    ],
  });

  const outcome = new SafePlaceholderPolicy({ allowSell: true }).decide(ctx, null);

  expect(outcome.decision.actionKind).toBe("Continue");
});
```

- [ ] **Step 2: Run the test to verify it passes**

(`Replay` is already in the safe policy's `FLOW_STATES`, so this should pass against the current agent code — it pins the assumption rather than driving new code.)

Run: `& "C:\Users\cauyx\.bun\bin\bun.exe" test tests/policy/replayFlow.test.ts`
Expected: PASS (1 test). If it FAILS, the agent's `FLOW_STATES`/Flow handling regressed — stop and reconcile with ADR-0008 before continuing.

- [ ] **Step 3: Commit**

```bash
git -C /e/repos/bpp/bazaarplusplus-agent add tests/policy/replayFlow.test.ts
git -C /e/repos/bpp/bazaarplusplus-agent commit -m "$(cat <<'EOF'
test(policy): pin safe policy continues (not sells) in Replay state

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification

- [ ] **[mod]** `dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj` — all PASS (validator `Continue` tests + updated schema assertion).
- [ ] **[mod]** `dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj -p:ManagedPath=<TheBazaar_Data/Managed>` — builds clean (context reader + dispatcher compile).
- [ ] **[agent]** `& "C:\Users\cauyx\.bun\bin\bun.exe" test` — full suite PASS, including the new `tests/policy/replayFlow.test.ts`.
- [ ] **Live (real game, rebuilt mod installed):** with the cockpit connected (`--connect-mod`), fight a battle → at the post-combat replay the cockpit's action surface shows a `Flow / Continue` option → the agent posts `Continue` → the run advances to the next state. Confirm a full loop: shop/encounter → combat → **auto-continue** → next shop. This is the end-to-end goal the unit tests cannot cover.
