---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master (both gaps shipped 2026-06-21): cf1ac7a9 added ReturnToMenu enum + schema 2.1.0, 43246bd3 dispatches it via RunManager.LoadMainMenu(), 9cfafafe emits it at end-of-run + fixes isClientBusy to IsWaitingForServerResponse||BlockInput; schema later bumped to 2.2.0 by the Continue action (5481cfbb), an additive superset.

# BazaarAgent: complete context/action for unattended run looping

- **Status:** draft (design, pre-implementation)
- **Date:** 2026-06-21
- **Scope:** two confirmed gaps in the BazaarAgent decision surface that block fully
  unattended, programmatic run→run looping. Replay control is already done
  ([ADR-0007](../adr/0007-bazaaragent-external-replay-video-recording.md)) and is **out of scope** here.

## 1. Background & problem

BazaarAgent is pure transport + validation over a self-describing decision surface: `GET
/v1/context` publishes a versioned snapshot, `POST /v1/actions` accepts one action, and the
external decision agent (separate repo `bazaarplusplus-agent`) owns all policy
([wire contract](../archive/reference/bazaar-agent-http-api-v1.md)).

A completeness audit against the decompiled game source found the **action set is complete with
respect to the game's wired command surface**: every `StateOps` flag that has a real command
consumer is covered by an agent action (9/9), and `StateOps.LevelUp` is a dormant flag in the game
itself (no `ExecuteIfAllowed(StateOps.LevelUp)` / no `LevelUpCommand` anywhere in the decompiled
tree), so its absence is faithful, not a gap.

Two real gaps remain, both blocking unattended looping:

### Gap 1 — the end-of-run screen cannot be advanced through `/v1/actions`

`EndRunVictoryState` / `EndRunDefeatState` do **not** override `AllowedOps`, so they inherit the
base default `StateOps.None` (`AppState.AllowedOps` is a virtual auto-property,
[AppState.cs:149](../../decompiled/TheBazaarRuntime/TheBazaar/AppState.cs)); `CanHandleOperation`
is a bit-test against it ([AppState.cs:846](../../decompiled/TheBazaarRuntime/TheBazaar/AppState.cs)).
With no ops available, the context reader emits only `Wait` for these states. But advancing past
the end screen is **not** a `StateOps`-gated `AppState.*Command()` — the end-of-run UI button
handler `EndOfRunScreenController.ReturnToMenuClicked()` drives it directly:

```csharp
// decompiled/.../EndOfRun/EndOfRunScreenController.cs:552-562
internal void ReturnToMenuClicked() {
    if (_sceneLoader == null) _sceneLoader = Services.Get<SceneLoader>();
    if (!_sceneLoader.IsTransitioning)
        Services.Get<RunManager>().LoadMainMenu();   // → MoveToScene() → load HeroSelectScene
}
```

`RunManager.LoadMainMenu()` ([RunManager.cs:54](../../decompiled/TheBazaarRuntime/RunManager.cs))
→ `MoveToScene()` ([RunManager.cs:107](../../decompiled/TheBazaarRuntime/RunManager.cs)) loads
`SceneID.HeroSelectScene`, where the agent's existing `StartOrContinueRun` becomes available again.
Consequence: a pure `/v1/actions` agent stalls forever at the end-of-run screen — the run→run loop
never closes.

> Note: `RunManager.ReturnToMainMenu()` ([RunManager.cs:69](../../decompiled/TheBazaarRuntime/RunManager.cs))
> is a **different** path — it also `DeleteSessionAsync()` + `AppState.Reset()` and is used by the
> in-run pause menu and server-forced returns ([FightMenuDialog.cs:227](../../decompiled/TheBazaarRuntime/FightMenuDialog.cs),
> [NetMessageProcessor.cs:178](../../decompiled/TheBazaarRuntime/TheBazaar/NetMessageProcessor.cs)).
> The end-of-run screen uses `LoadMainMenu()`. We replicate the screen, so we call `LoadMainMenu()`.

### Gap 2 — `isClientBusy` is a constant `false` stub

The context field is hardcoded:

```csharp
// src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs:203
IsClientBusy = false, // TODO v2: track HttpGameClient busy state
```

So the wire field always lies. An unattended agent has no signal for "the client is mid
server-round-trip / mid-transition," so it can POST into a busy window and get bounced. The game
exposes the real signals:

- `AppState.IsWaitingForServerResponse` (static bool, [AppState.cs:131](../../decompiled/TheBazaarRuntime/TheBazaar/AppState.cs))
  — set `true` when a command is sent ([Cmd.cs:105](../../decompiled/TheBazaarRuntime/TheBazaar/Cmd.cs)),
  cleared on response ([Cmd.cs:166](../../decompiled/TheBazaarRuntime/TheBazaar/Cmd.cs)).
- `AppState.BlockInput` (static bool, [AppState.cs:133](../../decompiled/TheBazaarRuntime/TheBazaar/AppState.cs))
  — ref-counted input block during transitions/animations. This is the same gate the native
  end-of-run / recap continue buttons check
  ([BoardRecapReplayButtonsController.cs:54](../../decompiled/TheBazaarRuntime/TheBazaar/BoardRecapReplayButtonsController.cs)).

## 2. Goal & non-goals

**Goal:** the external agent can drive a continuous, unattended `run → … → end-of-run →
hero-select → next run` loop entirely through the existing HTTP surface.

**Non-goals (explicit):**

- Tutorial / first-victory overlays — **out of scope**. The target account has already cleared all
  tutorials, so no tutorial overlay will appear. We add no overlay-dismissal logic.
- Replay control — already shipped (ADR-0007); the PvP combat → replay → continue leg is covered by
  `POST /v1/replay/continue`.
- Combat progression — non-interactive; the game auto-advances when the sim/VFX finish, so the agent
  correctly only `Wait`s there. No change.

## 3. Decision

1. **End-of-run advance = a new action `ReturnToMenu` on `/v1/actions`** (chosen over a dedicated
   endpoint or overloading `ExitState`). It is symmetric to the existing `StartOrContinueRun`: a
   non-`StateOps` action that calls a game service directly. The whole point of `availableActions`
   is to enumerate every legal action per state; the end-of-run states wrongly expose only `Wait`
   when a legal "leave" action exists. This keeps the surface uniform — everything the agent can do
   stays in `availableActions`.
   - *Rejected — dedicated `/v1/run/continue` endpoint:* replay got its own route + queue because
     of multi-MB binary bodies, a 10 s accept window, and out-of-band control. End-of-run advance is
     one lightweight method call; a separate route/queue/doc/test set buys nothing and breaks
     surface uniformity.
   - *Rejected — overload `ExitState`:* `ExitState` is `StateOps`-gated with "exit current sub-state"
     semantics; end-of-run states are `AllowedOps=None`, and overloading one action kind to mean two
     different game operations breaks the clean action→command mapping.
2. **`isClientBusy` reads the real signal:** `IsWaitingForServerResponse || BlockInput`.

## 4. Detailed design

### 4.1 Pure core — `BazaarPlusPlus.BazaarAgent`

- `BazaarAgentActionKind`: add `ReturnToMenu` (append at end to preserve existing ordinals).
- `BazaarAgentActionGroup`: reuse `Flow` (same group as `StartOrContinueRun`).
- `BazaarAgentSchema.Version`: `2.0.0` → `2.1.0` (additive: new enum value + `isClientBusy` becomes
  meaningful — no field renamed/removed).
- **Validator: no logic change.** `BazaarAgentActionValidator` has no per-kind allowlist
  ([BazaarAgentActionValidator.cs:65](../../src/BazaarPlusPlus.BazaarAgent/Decisions/BazaarAgentActionValidator.cs)).
  `ReturnToMenu` passes Rule 1 (`Enum.IsDefined`) once the enum value exists, passes Rule 2 when
  it is present in `availableActions`, is not in `_cardBearingKinds` (Rule 3 skipped), is not
  `StartOrContinueRun` (Rule 4 skipped), and skips Rules 5/5a/5b/6. The 1 s cooldown gate (Rule 8)
  applies to it as a non-`Wait` action — harmless.

### 4.2 Host context reader — `BazaarAgentGameContextReader`

- **`isClientBusy`:** replace the line 203 stub with
  `IsClientBusy = AppState.IsWaitingForServerResponse || AppState.BlockInput`.
- **Emit `ReturnToMenu`:** in `BuildCore`, compute
  `bool endScreenInteractable = !Services.Get<SceneLoader>().IsTransitioning` and thread it into
  `BuildActions`. In `BuildActions`, when
  `stateName is EndRunVictory or EndRunDefeat && endScreenInteractable`, append a `ReturnToMenu`
  option (group `Flow`, `DisplayKey = "ReturnToMenu"`, no card). These states currently fall
  through to a `Wait`-only `availableActions`, so this is purely additive.
  - Guard rationale: gate on `SceneLoader.IsTransitioning` to mirror the native button
    ([EndOfRunScreenController.cs:558](../../decompiled/TheBazaarRuntime/TheBazaar.UI.EndOfRun/EndOfRunScreenController.cs)).
    We deliberately do **not** also gate emission on `BlockInput` — that signal is surfaced
    separately via `isClientBusy`, and an over-tight emit guard would make the option flicker.

### 4.3 Host dispatcher — `BazaarAgentGameActionDispatcher`

Add one case, symmetric to the existing `StartOrContinueRun` case (which calls
`GameInstance.Instance.StartNewRun()` directly — established prior art for a non-`StateOps` action):

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

- **No facade (`IBazaarAgentGameProbe`) change.** The host already has full game access and calls
  `GameInstance` / `AppState` / `Data` directly; `RunManager` / `SceneLoader` are reached the same
  way the game reaches them, via `Services.Get<T>()` (e.g.
  [EndOfRunScreenController.cs:556-560](../../decompiled/TheBazaarRuntime/TheBazaar.UI.EndOfRun/EndOfRunScreenController.cs),
  [StartRunAppState.cs:165](../../decompiled/TheBazaarRuntime/TheBazaar/StartRunAppState.cs)).
- *Considered & rejected — call `EndOfRunScreenController.ReturnToMenuClicked()` on the live
  instance* (reuses the native guard). Rejected for consistency with the `StartOrContinueRun`
  pattern (call the underlying service, replicate the button's guard) and to avoid a fragile
  `FindObjectOfType` lookup. The guard body is two stable lines; we replicate it.

### 4.4 Fire-and-forget semantics

`ReturnToMenu` returns `executed: true` as soon as `LoadMainMenu()` is *dispatched* — it kicks off
an async scene load, exactly like `StartOrContinueRun` kicks off `StartNewRun()`. Per the existing
contract, `executed: true` means "dispatched," not "completed." The external loop then polls
`GET /v1/context` until `canStartOrContinueRun` flips true at hero-select, then POSTs
`StartOrContinueRun`. No new polling primitive is needed.

## 5. Wire contract changes (additive, schema `2.1.0`)

- New `actionKind` value `ReturnToMenu`:

  | ActionKind | Group | Required params | Appears when | Dispatches |
  |---|---|---|---|---|
  | `ReturnToMenu` | `Flow` | — | `stateName ∈ {EndRunVictory, EndRunDefeat}` and the scene loader is not transitioning | `RunManager.LoadMainMenu()` (→ HeroSelectScene) |

- `isClientBusy` description tightened: "true while the client is waiting on a server round-trip or
  input is blocked by a transition/animation (`IsWaitingForServerResponse || BlockInput`)." Clients
  SHOULD treat a busy snapshot as a hint to back off before POSTing non-`Wait` actions.
- Add `ReturnToMenu` to the `actionKind` enum reference list.
- `End-run screens` UI-plumbing note updated: end-run screens are now advanceable via the
  `ReturnToMenu` action (previously "not driven by BazaarAgent").

Client compatibility is preserved: unknown-enum-tolerant clients ignore `ReturnToMenu`; the
`isClientBusy` field type is unchanged.

## 6. Testing

- **Pure core (net10.0, `tests/BazaarAgent.Tests`, runnable without the game):**
  - `BazaarAgentActionValidatorTests`: `ReturnToMenu` present in `availableActions` → `Ok`;
    absent → `409 StaleOrUnavailable`; cooldown gate applies (non-`Wait`).
  - Schema/serialization pin: `BazaarAgentSchema.Version == "2.1.0"`; `ReturnToMenu` serializes as
    the camelCase string `returnToMenu` and round-trips.
- **Host side (no game-less unit test possible):** an `Architecture.Tests` assertion that
  `RunManager.LoadMainMenu` / `Services.Get<RunManager>` appears in exactly one host file
  (the dispatcher), matching the ADR-0007 single-call-site discipline.

## 7. Verification method (no game-less integration test for the game binding)

Per project rule (no standalone probe scaffolding; verify on the main path via build + reload):

1. Build the host: `./run.sh build --with-bazaaragent`.
2. Launch via Steam (App ID 1617400); finish or abandon a run to reach the end-of-run screen.
3. `GET /v1/context` → assert `stateName` is `EndRunVictory`/`EndRunDefeat` and `availableActions`
   now contains `ReturnToMenu`; assert `isClientBusy` toggles `true` during a known busy window
   (e.g. right after a shop action) and returns to `false`.
4. `POST /v1/actions {"actionKind":"ReturnToMenu"}` → expect `200 executed:true`; poll context until
   `canStartOrContinueRun` is true at hero-select.
5. `POST StartOrContinueRun` → confirm the loop closes into a fresh run.

If any step fails, capture `<GameDir>/BepInEx/LogOutput.log` (`[BPP][...]` lines) and root-cause
against decompiled source before patching.

## 8. Risks & open items

- **Dispatch-reject status for the transient race guard.** If `ReturnToMenu` is POSTed while
  `IsTransitioning` (a rare race past the validator), the dispatcher returns
  `(false, "scene transitioning")`, which maps to `500 internal` under the current
  dispatch-result→HTTP mapping (only executed/not-executed → 200/500). Semantically this is a
  transient retryable reject, not an internal error. Primary gating is the context-reader emission
  + validator Rule 2, so the race is narrow. Mapping transient dispatch rejects to a retryable
  status (e.g. 409) is a **pre-existing** limitation affecting all dispatcher guards; left as a
  follow-up, not widened here.
- **`Services.Get<SceneLoader>()` / `Services.Get<RunManager>()` accessibility from the host.** Used
  pervasively in publicized game code; expected to resolve, but the implementation plan must confirm
  the `using`/type resolution compiles in the host assembly.
- **`LoadMainMenu()` vs server session lifetime.** `LoadMainMenu()` disposes the connection via
  `MoveToScene()` but does not `DeleteSessionAsync()`; this matches the native end-of-run button.
  Confirm during verification that a subsequent `StartOrContinueRun` starts cleanly (no stale
  session).

## 9. Evidence index

| Claim | Evidence |
|---|---|
| End-run states have no ops; only `Wait` emitted | `AppState.AllowedOps` virtual default `None` ([AppState.cs:149](../../decompiled/TheBazaarRuntime/TheBazaar/AppState.cs)); `EndRunVictoryState`/`EndRunDefeatState` don't override it |
| End screen advance target | `EndOfRunScreenController.ReturnToMenuClicked()` → `RunManager.LoadMainMenu()` ([EndOfRunScreenController.cs:552](../../decompiled/TheBazaarRuntime/TheBazaar.UI.EndOfRun/EndOfRunScreenController.cs)) |
| `LoadMainMenu` loads hero-select | `MoveToScene()` → `SceneID.HeroSelectScene` ([RunManager.cs:107](../../decompiled/TheBazaarRuntime/RunManager.cs)) |
| `ReturnToMainMenu` is the wrong (heavier) path | deletes session + resets ([RunManager.cs:69](../../decompiled/TheBazaarRuntime/RunManager.cs)) |
| `isClientBusy` real signals | `IsWaitingForServerResponse` ([AppState.cs:131](../../decompiled/TheBazaarRuntime/TheBazaar/AppState.cs), [Cmd.cs:105](../../decompiled/TheBazaarRuntime/TheBazaar/Cmd.cs)); `BlockInput` ([AppState.cs:133](../../decompiled/TheBazaarRuntime/TheBazaar/AppState.cs)) |
| Validator needs no per-kind change | no kind allowlist ([BazaarAgentActionValidator.cs:65](../../src/BazaarPlusPlus.BazaarAgent/Decisions/BazaarAgentActionValidator.cs)) |
| Prior art for a non-`StateOps` action | `StartOrContinueRun` → `GameInstance.Instance.StartNewRun()` ([BazaarAgentGameActionDispatcher.cs:53](../../src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameActionDispatcher.cs)) |
