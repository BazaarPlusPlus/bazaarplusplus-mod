# AutoBazaar HTTP Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Spec source-of-truth is the user's Goal document (this plan's §0). When the plan and the Goal disagree, **the Goal wins** — file a question rather than diverging silently.

**Goal:** Ship `AutoBazaarRuntime` — a BepInEx MonoBehaviour that hosts a loopback HTTP server (`GET /v1/context`, `POST /v1/actions`) as pure transport + validator. External tools own all strategy, persistence, and replay/training. No in-mod decision logic.

**Architecture:** Unity main thread builds an immutable, fingerprint-versioned `AutoBazaarContextSnapshot` per tick and publishes the reference via a `volatile` field. A `System.Net.HttpListener` on `127.0.0.1:<port>` runs on the thread pool; `GET` reads the snapshot lock-free with `ETag`/`If-None-Match`, `POST` enqueues a `Pending(decision, TCS)` into a `ConcurrentQueue`. The main thread dequeues, validates against the **current** snapshot, dispatches via `Cmd.GetInstance().*` (or reflection for the EndRun button), and completes the TCS. A server-side timer fails pending TCS with `503` after `HttpEndpointTimeoutSeconds`. UI plumbing (replay auto-advance, known overlay dismissal) runs on the same main-thread tick.

**Tech Stack:**
- Mod project: `BazaarPlusPlus.csproj` → `netstandard2.1`, Newtonsoft.Json 13.0.3, BepInEx 5.x
- Test project: `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj` → `net10.0`, xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1
- HTTP server: `System.Net.HttpListener` (built-in, no NuGet)
- Concurrency: `ConcurrentQueue`, `TaskCompletionSource`, `Volatile.Read`/`Volatile.Write`, `System.Threading.Timer`
- ULID: hand-rolled (Crockford base32 + `RNGCryptoServiceProvider`)
- Decompiled references in `decompiled/` are read-only; consult but never edit

---

## 0. Discovered corrections to Goal spec (must read before Phase 2)

Investigation of master + `decompiled/` revealed the following deltas the implementer must honor. Ask the user when unsure; do **not** silently diverge.

| # | Finding | Impact |
|---:|---|---|
| **C1** | `Cmd.SelectItem(ItemCard itemCard, List<EContainerSocketId> sockets, EInventorySection targetSection, Action undoOnFailure = null)` — selecting an offer **requires** placement (sockets + section), same shape as `SendMoveItem`. | `SelectItem` `ActionKind` must carry `targetSection` + `targetSockets`. `availableActions` must enumerate every legal placement per offered item, exactly like `MoveItem`. Schema §7.4 in Goal needs to extend "`仅 MoveItem`" to cover `SelectItem` too. Snapshot's `selectionOptions[i]` should carry the resolved `targetSection`/`targetSockets` it would place into if accepted as-is (advisory; client picks from `availableActions`). |
| **C2** | `Cmd` methods: `SelectItem`, `SelectEncounter`, `SendSelectSkill`, `SendMoveItem`, `SendSellCard`, `SendCommitToPedestal`, `SendReRollSelection`, `SendExitCurrentState`, `SendAbandonRun`. All hang off `Cmd.GetInstance().XXX()`. | Goal §8 table referenced `Cmd.SelectSkill` — actual name is `SendSelectSkill`. `AbandonRun` is **not** a hand-rolled `AbandonRunCommand` send — it's `Cmd.GetInstance().SendAbandonRun()`. Use these exact names. |
| **C3** | `Data.SelectedHero` / `Data.SelectedPlayMode` are **read-only getters** that forward to `ClientCache.RunConfig.Value.Hero` / `.RunType`. | `StartOrContinueRun` cannot assign `Data.SelectedHero`. Dispatcher must mutate `ClientCache.RunConfig.Value` (or wait — verify the actual writer entry; see Phase 0 task `T0.3`). If no writer is reachable, document the limitation and pass `hero`/`playMode` to `GameInstance.StartNewRun` if it accepts them, else flag for spec amendment. |
| **C4** | `StateOps` is a `[Flags] enum` (`SelectSkill=1, SelectItem=2, SelectEncounter=4, MoveItem=8, SellItem=0x10, ExitState=0x20, LevelUp=0x40, Reroll=0x80, CommitToPedestal=0x100, AbandonRun=0x200`). | Predicates are bitmask checks: `(rules.StateOps & StateOps.SelectItem) != 0`. The field holding it (likely on `TSelectionContextRules` or the current `AppState`) must be discovered in Phase 2 — see `T0.4`. |
| **C5** | `RunState.StateName` is a **public field** (not property), typed `ERunState`. `ERunState` covers `Choice, Encounter, Combat, LevelUp, Loot, Pedestal, PVPCombat, EndRunVictory, EndRunDefeat`. Goal lists `PvpCombat`; actual enum is `PVPCombat`. | Map `ERunState.PVPCombat` → mod's `RunStateName.PvpCombat` to preserve the public schema. `StartRun` and `Replay` come from `AppState` subclass identification, not `ERunState`. |
| **C6** | `EContainerSocketId` has 10 values (`Socket_0` … `Socket_9`). `EInventorySection` has 2 (`Hand`, `Stash`). `EHero` has 8 (`Common, Pygmalien, Vanessa, Stelle, Jules, Dooley, Mak, Karnok`). `EPlayMode` has 2 (`Unranked, Ranked`). | Use these exact identifiers in JSON enum serialization (case-insensitive on parse). |
| **C7** | `InstanceId` is a `readonly record struct InstanceId([property: Key(0)] string Value)`. | Public schema field `cardInstanceId` is `string`; mod constructs `new InstanceId(value)` at dispatch boundary. Empty / null → 400. |
| **C8** | `EndOfRunScreenController.OnContinueClick` not visible in decompiled excerpt — needs spot-check at Phase 3 (`T0.5`). | If `OnContinueClick` is absent / renamed, find the click handler bound to the Continue button in the controller's `Awake`/`Start` and reflect on whatever it actually is. Document the chosen method name in `AutoBazaarUiPlumbing.cs` with a one-line comment. |
| **C9** | `GameInstance.Instance` static getter not visible in decompiled excerpt — needs spot-check (`T0.6`). `StartNewRun` signature unknown. | Verify before writing the `StartOrContinueRun` dispatcher branch. Likely a singleton with `Instance` property; if not, find the canonical accessor. |
| **C10** | No `Directory.Build.props` test inheritance on master; each test csproj declares its own SDK/refs. | Mirror an existing test csproj (e.g. `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`) when scaffolding `AutoBazaar.Tests.csproj`. |

**Spec ambiguity to surface with user before Phase 2 closes:** Goal §7.4 says `selectionOptions` cards carry `targetSection`/`targetSockets` as informational fields but lists them under "Selection-only" while §7.5/§8 imply only `MoveItem` requires sockets. With C1, both `SelectItem` and `MoveItem` `availableActions` carry sockets. Confirm the snapshot schema before locking DTO names.

---

## 1. File structure

**Mod source (`Game/AutoBazaar/`):**
| File | Responsibility |
|---|---|
| `AutoBazaarRuntime.cs` | MonoBehaviour; owns tick loop, listener lifecycle, context publishing, action queue draining, UI plumbing. ~400 LOC. |
| `AutoBazaarDecision.cs` | Public DTOs + enums: `AutoBazaarActionKind`, `AutoBazaarActionGroup`, `AutoBazaarRunStateName`, `AutoBazaarCardKind`, `AutoBazaarCardLocation`, `AutoBazaarTargetSection`, `AutoBazaarCardSnapshot`, `AutoBazaarDecisionOption`, `AutoBazaarContext`, `AutoBazaarAction`. No logic. |
| `AutoBazaarContextBuilder.cs` | `Build(IBppServices) → AutoBazaarContext` — reads `AppState`, `Data.Run.*`, `SelectionSet`, hand/stash/skills, derives `availableActions` (`Wait` always; flow/route/exit/reroll/sell/move/offer/pedestal/advance-end-run conditionally). |
| `AutoBazaarContextSnapshot.cs` | Immutable wrapper around `AutoBazaarContext` + `tickId` (ulong, monotonic per listener lifetime) + `etag` string. Content-fingerprint comparison via explicit field-by-field equality, **ignoring `serverTimeUtc`**. |
| `AutoBazaarActionValidator.cs` | 8 ordered rules → `ValidationResult { Code, HttpStatus, Details, Extra }`. Pure function `Validate(snapshot, action, cooldownRemainingSec)`. |
| `AutoBazaarActionDispatcher.cs` | `Execute(action, snapshot) → DispatchResult` — switch on `ActionKind`, call `Cmd.GetInstance().*` / reflection. Main thread only. |
| `AutoBazaarUiPlumbing.cs` | `Tick(IBppServices)` — replay auto-advance (`ReplayState.GoToNextState()`) + known overlay dismissal (v1: PvP first-victory tutorial). Main thread only. |
| `AutoBazaarActionQueue.cs` | `ConcurrentQueue<PendingAction>` + `TryEnqueue`/`TryDequeue` + per-pending `TaskCompletionSource<HttpResponse>` + timeout reaper (`System.Threading.Timer`). |
| `AutoBazaarHttpServer.cs` | `HttpListener` lifecycle, route dispatch (`/v1/context` GET, `/v1/actions` POST), ETag handling, error envelope, 64 KB body cap, JSON serializer factory, `endpoint.json` write/delete. |
| `AutoBazaarUlid.cs` | 26-char Crockford-base32 ULID: 48-bit ms timestamp + 80-bit randomness via `RNGCryptoServiceProvider`. Strictly monotonic within process (bump randomness when ts unchanged). |
| `AutoBazaarDecisionLog.cs` | JSONL append per dequeued action (including rejected ones). Path: `<gameRoot>/BazaarPlusPlus/AutoBazaar/runs/<sanitized-runId>/decisions.jsonl` (fallback to flat `decisions.jsonl` if `runId` is null). Sanitize via `Path.GetInvalidFileNameChars` → `_`. |

**Config:**
| File | Change |
|---|---|
| `Core/Config/BppConfig.cs` | Add 4 entries under section `AutoBazaar`. |
| `Core/Config/IBppConfig.cs` | Mirror 4 properties. |

**Plugin wiring:**
| File | Change |
|---|---|
| `Plugin.cs` | One line in `AttachRuntimeComponents`: `gameObject.AddComponent<AutoBazaarRuntime>().Initialize(services);`. |

**Tests (`tests/AutoBazaar.Tests/`):**
| File | What it tests |
|---|---|
| `AutoBazaar.Tests.csproj` | net10.0 + xunit + ref to `BazaarPlusPlus.csproj` (via `Compile Include="..\..\<file>" Link="..."` per project convention) + `ManagedPath` injection. |
| `AutoBazaarUlidTests.cs` | Length=26, monotonic over 10k iterations, lexicographically sortable when ts increases. |
| `AutoBazaarContextSnapshotTests.cs` | Same context fields → same `tickId`; differing in any non-`serverTimeUtc` field → bumped `tickId`; differing only in `serverTimeUtc` → same `tickId`. |
| `AutoBazaarActionValidatorTests.cs` | Each of 8 rules: 1 accept + 1+ reject case. Uses hand-constructed `AutoBazaarContextSnapshot`. |
| `AutoBazaarHttpServerTests.cs` | Integration: `HttpClient` against real listener on ephemeral port. Covers GET 200/304/503, POST 200/400/409/413/429/503, concurrent POST race, ETag, body cap. |
| `AutoBazaarDecisionLogTests.cs` | Path sanitization, fallback path when `runId` is null, JSONL append correctness. |

**Docs (Phase 5):**
| File | What |
|---|---|
| `docs/reference/auto-bazaar-http-api-v1.md` | Pure spec extracted from Goal §7, §8, §13. |
| `docs/reference/auto-bazaar-decision-surface.md` | Internal field semantics (informational). |
| `docs/mod-features-overview.md` | Add one bullet. |
| `README.md` / `README_en.md` | Add one bullet each. |

---

## 2. Cross-cutting conventions

- **Namespaces:** All new files live under `BazaarPlusPlus.Game.AutoBazaar` (or follow whatever namespace `Game/*` files use on master — verify in Phase 1 `T1.0`).
- **JSON:** All HTTP I/O via a single `JsonSerializerSettings` instance with `CamelCasePropertyNamesContractResolver`, `StringEnumConverter` (case-insensitive on parse), `NullValueHandling.Ignore`. **Do not introduce `System.Text.Json`** — `.rules` and the Goal both forbid it.
- **Threading discipline:** 100% of `Cmd.*`, `HttpGameClient`, reflection-into-`MonoBehaviour` calls happen in `AutoBazaarRuntime.Update()`. HTTP listener callbacks only read the volatile snapshot, enqueue, and `await` the TCS. If a method must touch Unity types, it gets a `// Main thread only` line comment.
- **Body size cap:** Enforced in `AutoBazaarHttpServer` by reading `Content-Length` header first; if `> 65536` return `413` without reading body. If header missing, read up to 65537 bytes and reject if > 64 KB.
- **Logging:** All log lines go through `BppLog.Info("AutoBazaar", ...)` / `BppLog.Error(...)`. No direct `BepInEx` logger calls.
- **No `Task.Wait`/`Task.Result` on the main thread.** Bridge to async by enqueueing and continuing the next `Update()`; HTTP thread `await`s the TCS.
- **Commits:** One commit per task. Conventional prefix optional; the .rules only constrain PR titles, not commit titles. Use short imperative subjects.
- **Verification:** After each task that touches code, run the smallest relevant target (the test project or `dotnet build` of the affected csproj). Full `BuildAll` only for the integration/HTTP phase pre-merge check.

---

## Phase 0 — Pre-flight investigation (do this before code)

These tasks resolve open items C3 / C4 / C8 / C9 / `AppState` accessor / `SelectionSet` shape. Output is a short notes file the implementer keeps in their head (or scratch buffer); nothing committed.

### Task T0.1: Confirm namespace + folder convention used by master

**Files:** Read only.

- [ ] Read `Game/CombatReplay/CombatReplayRuntime.cs` (or any one Game/*/* MonoBehaviour) on master and note the top-of-file `namespace` line.
- [ ] Confirm `BazaarPlusPlus.Game.AutoBazaar` matches the pattern (or adopt whatever the codebase uses).

### Task T0.2: Find `AppState` access + `RunState` access

**Files:** Read only `decompiled/TheBazaarRuntime/TheBazaar/AppState.cs`, `decompiled/TheBazaarRuntime/TheBazaar/Data.cs`.

- [ ] Identify how the current `AppState` is retrieved at runtime. Candidates: `AppState.Current`, a static on `Game`, a service on `GameInstance`, a field on `Data`. Note the exact accessor.
- [ ] Identify how `RunState` (the live one, not the type) is retrieved: likely `Data.Run.State` or `appState.RunState`. Note exact accessor.

### Task T0.3: Find `ClientCache.RunConfig` write path

**Files:** Read only `decompiled/`.

- [ ] Grep `decompiled/` for `ClientCache.RunConfig` writes (`grep -r "RunConfig.Value" decompiled/ -l`).
- [ ] Locate where `SelectedHero` / `SelectedPlayMode` get assigned before `StartNewRun`. Document the call chain.
- [ ] **If no in-process writer exists**, escalate: ask user whether to (a) write to `ClientCache.RunConfig.Value` directly via reflection, (b) call `GameInstance.StartNewRun(hero, playMode)` if such an overload exists, or (c) ignore `hero`/`playMode` parameters when not on hero-select scene (degenerate to "continue existing run" only).

### Task T0.4: Find selection rules / `StateOps` storage

**Files:** Read only `decompiled/`.

- [ ] In `decompiled/TheBazaarRuntime/TheBazaar/AppState.cs` and `TSelectionContextRules.cs`, find which field of the current `AppState` (or its `RunState`) exposes the bitmask of allowed ops + the `TSelectionContextRules` (with `CanExit`, `RerollRules`, etc.).
- [ ] Note the exact property path used by ContextBuilder (e.g. `appState.RunState.SelectionContext.Rules.CanExit`).

### Task T0.5: Locate `EndOfRunScreenController` continue handler

**Files:** Read only `decompiled/TheBazaarRuntime/TheBazaar.UI.EndOfRun/EndOfRunScreenController.cs`.

- [ ] Search the file for `Continue`, `OnContinueClick`, `_continueButton`, button bindings in `Awake`/`Start`.
- [ ] Pick the exact private method bound to the Continue button. Record name + signature. Use that for `AutoBazaarUiPlumbing` reflection target.

### Task T0.6: Locate `GameInstance.Instance` + `StartNewRun`

**Files:** Read only `decompiled/TheBazaarRuntime/TheBazaar/GameInstance.cs` (and surrounding).

- [ ] Confirm `GameInstance.Instance` (or whatever the canonical singleton accessor is).
- [ ] Find `StartNewRun` (or the canonical "start a new run from hero-select scene" call). Record signature.

### Task T0.7: Find `SelectionSet` shape

**Files:** Read only `decompiled/`.

- [ ] Grep `decompiled/` for `SelectionSet` definition + `Offer`/`Selection`/`Choice` data shape.
- [ ] Identify how offered items / skills / encounters are reachable from `AppState` or `Data.Run`.

**Output of Phase 0:** A short scratchpad of resolved property paths. Nothing committed.

---

## Phase 1 — Skeleton

> Goal: `Enabled=true` mod loads, attaches `AutoBazaarRuntime`, runtime ticks with empty `Update()`, no HTTP, no errors, no regressions.

### Task T1.1: Add 4 config entries to `BppConfig` + `IBppConfig`

**Files:**
- Modify: `Core/Config/BppConfig.cs`
- Modify: `Core/Config/IBppConfig.cs`

- [ ] **Step 1 — Add interface properties.** Open `Core/Config/IBppConfig.cs`. Add inside the interface body, grouped together:

```csharp
ConfigEntry<bool>? AutoBazaarEnabled { get; }
ConfigEntry<float>? AutoBazaarDecisionIntervalSeconds { get; }
ConfigEntry<int>? AutoBazaarHttpListenerPort { get; }
ConfigEntry<float>? AutoBazaarHttpEndpointTimeoutSeconds { get; }
```

- [ ] **Step 2 — Add concrete bindings.** Open `Core/Config/BppConfig.cs`. Add the four `Bind` calls inside `Initialize(ConfigFile config)`, grouped under a `// AutoBazaar` comment block:

```csharp
// AutoBazaar
AutoBazaarEnabled = config.Bind(
    "AutoBazaar",
    "Enabled",
    true,
    "Master switch for the AutoBazaar HTTP endpoint. When true, a loopback HTTP server starts on the configured port. There is no in-game UI for this toggle; edit the cfg file to disable.");

AutoBazaarDecisionIntervalSeconds = config.Bind(
    "AutoBazaar",
    "DecisionIntervalSeconds",
    1.5f,
    "Mod tick cadence for snapshot publication, in seconds. Clamped to [0.5, 10] at runtime.");

AutoBazaarHttpListenerPort = config.Bind(
    "AutoBazaar",
    "HttpListenerPort",
    47900,
    "Loopback port for the AutoBazaar HTTP listener. Changing this restarts the listener.");

AutoBazaarHttpEndpointTimeoutSeconds = config.Bind(
    "AutoBazaar",
    "HttpEndpointTimeoutSeconds",
    3.0f,
    "Maximum time (seconds) the server will block on a POST /v1/actions before returning 503.");
```

- [ ] **Step 3 — Backing-field declarations (if BppConfig uses explicit interface impl).** Match the existing pattern in the file (verify by reading first). If the file uses auto-properties, the `config.Bind` assignments above are sufficient. If it uses backing fields, add `public ConfigEntry<bool>? AutoBazaarEnabled { get; private set; }` (etc.) in the appropriate region.

- [ ] **Step 4 — Build.** Run:

```powershell
dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet
```

Expected: success, no warnings about missing interface members.

- [ ] **Step 5 — Commit.**

```powershell
git add Core/Config/BppConfig.cs Core/Config/IBppConfig.cs
git commit -m "Add AutoBazaar config entries"
```

### Task T1.2: Scaffold `AutoBazaarRuntime` MonoBehaviour (empty `Update()`)

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarRuntime.cs`

- [ ] **Step 1 — Write skeleton.** Create the file with this body (adapt namespace per `T0.1`):

```csharp
using UnityEngine;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal sealed class AutoBazaarRuntime : MonoBehaviour
{
    private IBppServices? _services;
    private float _lastTickTime;

    public void Initialize(IBppServices services)
    {
        _services = services;
        BppLog.Info("AutoBazaar", "AutoBazaarRuntime initialized");
    }

    private void Update()
    {
        if (_services is null) return;
        if (_services.Config.AutoBazaarEnabled?.Value != true) return;

        var interval = Mathf.Clamp(_services.Config.AutoBazaarDecisionIntervalSeconds?.Value ?? 1.5f, 0.5f, 10f);
        if (Time.unscaledTime - _lastTickTime < interval) return;
        _lastTickTime = Time.unscaledTime;

        // Phase 2+ fills this in.
    }

    private void OnDestroy()
    {
        // Phase 4 will stop listener here.
    }
}
```

- [ ] **Step 2 — Build.** `dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet`. Expected: success.

- [ ] **Step 3 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarRuntime.cs
git commit -m "Scaffold AutoBazaarRuntime MonoBehaviour"
```

### Task T1.3: Wire `AutoBazaarRuntime` into `Plugin.AttachRuntimeComponents`

**Files:**
- Modify: `Plugin.cs`

- [ ] **Step 1 — Add attach line.** Open `Plugin.cs`, find `AttachRuntimeComponents`, append before the final `BppLog.Info(...attached)` line:

```csharp
var autoBazaar = gameObject.AddComponent<BazaarPlusPlus.Game.AutoBazaar.AutoBazaarRuntime>();
autoBazaar.Initialize(services);
```

(If the file already has `using BazaarPlusPlus.Game.AutoBazaar;`, drop the qualifier.)

- [ ] **Step 2 — Build.** `dotnet build BazaarPlusPlus.csproj -nologo -clp:NoSummary -v:quiet`. Expected: success.

- [ ] **Step 3 — Smoke test.** Launch the game with the built dll in `BepInEx/plugins`. Check the BepInEx console for `[AutoBazaar] AutoBazaarRuntime initialized`. No exceptions. Toggle `Enabled=false` in cfg, restart, confirm `Update()` short-circuits (no log spam).

- [ ] **Step 4 — Commit.**

```powershell
git add Plugin.cs
git commit -m "Attach AutoBazaarRuntime in plugin bootstrap"
```

---

## Phase 2 — Context surface

> Goal: each tick, build an immutable snapshot, bump `tickId` on content change, expose via volatile reference. No HTTP yet — verify via debug log line every N ticks.

### Task T2.1: Define enums + DTOs in `AutoBazaarDecision.cs`

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarDecision.cs`

The file is data-only. Includes every enum + DTO needed for both context emission and action parsing. Use `internal` types; serialization happens through a single configured `JsonSerializerSettings` (Phase 4) so internals are fine.

- [ ] **Step 1 — Write the file.** Full body:

```csharp
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal enum AutoBazaarActionKind
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
    AdvanceEndRun,
}

internal enum AutoBazaarActionGroup
{
    Wait, Flow, Offer, Route, Pedestal, Move, Sell, Reroll, Exit, UiFlow,
}

internal enum AutoBazaarRunStateName
{
    Unknown, StartRun, Choice, Encounter, Combat, PvpCombat, Replay,
    LevelUp, Loot, Pedestal, EndRunVictory, EndRunDefeat,
}

internal enum AutoBazaarCardKind { Item, Skill, Encounter, Unknown }
internal enum AutoBazaarCardLocation { Selection, Board, Chest, Skill, Unknown }
internal enum AutoBazaarTargetSection { Hand, Stash, Skill, Fuse }

internal sealed class AutoBazaarCardSnapshot
{
    public string InstanceId { get; init; } = "";
    public AutoBazaarCardKind Kind { get; init; }
    public string? TemplateId { get; init; }
    public string? DisplayName { get; init; }
    public string? Tier { get; init; }
    public string? Size { get; init; }
    public string? SocketId { get; init; }
    public AutoBazaarCardLocation Location { get; init; }
    public int Order { get; init; }

    // Selection-only
    public int? BuyPrice { get; init; }
    public int? SellPrice { get; init; }
    public bool? CanAfford { get; init; }
    public bool? CanFit { get; init; }
    public bool? CanSelect { get; init; }
    public bool? IsFree { get; init; }
    public AutoBazaarTargetSection? TargetSection { get; init; }
    public string? TargetSockets { get; init; }    // comma-joined
    public string? UnavailableReason { get; init; }

    // Owned (Board/Chest/Skill)
    public bool? CanSell { get; init; }
}

internal sealed class AutoBazaarDecisionOption
{
    public AutoBazaarActionKind ActionKind { get; init; }
    public AutoBazaarActionGroup Group { get; init; }
    public string DisplayKey { get; init; } = "";
    public string? CardInstanceId { get; init; }
    public AutoBazaarTargetSection? TargetSection { get; init; }
    public IReadOnlyList<string>? TargetSockets { get; init; }
    public AutoBazaarCardSnapshot? Card { get; init; }
}

internal sealed class AutoBazaarContext
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public ulong TickId { get; init; }
    public string ServerTimeUtc { get; init; } = "";

    public bool IsEnabled { get; init; }
    public bool IsInRun { get; init; }
    public bool HasActiveRun { get; init; }
    public bool CanStartOrContinueRun { get; init; }
    public bool IsClientBusy { get; init; }

    public string? RunId { get; init; }
    public AutoBazaarRunStateName StateName { get; init; }
    public int PlayerGold { get; init; }
    public bool SelectionIsFree { get; init; }
    public bool CanExit { get; init; }
    public bool CanReroll { get; init; }
    public int RerollCost { get; init; }
    public int RerollsRemaining { get; init; }
    public string? CurrentEncounterId { get; init; }
    public double ActionCooldownRemainingSeconds { get; init; }

    public IReadOnlyList<AutoBazaarCardSnapshot> BoardItems { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarCardSnapshot> ChestItems { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarCardSnapshot> PlayerSkills { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarCardSnapshot> SellableItems { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarCardSnapshot> SelectionOptions { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarDecisionOption> AvailableActions { get; init; } = System.Array.Empty<AutoBazaarDecisionOption>();
}

internal sealed class AutoBazaarAction
{
    public string? SchemaVersion { get; set; }
    public AutoBazaarActionKind ActionKind { get; set; }
    public string? CardInstanceId { get; set; }
    public AutoBazaarTargetSection? TargetSection { get; set; }
    public IReadOnlyList<string>? TargetSockets { get; set; }
    public string? Hero { get; set; }
    public string? PlayMode { get; set; }
    public string? Reason { get; set; }
    public ulong? ForTickId { get; set; }
}
```

- [ ] **Step 2 — Build.** `dotnet build BazaarPlusPlus.csproj`. Expected: success.

- [ ] **Step 3 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarDecision.cs
git commit -m "Define AutoBazaar enums and DTOs"
```

### Task T2.2: Scaffold the test project

**Files:**
- Create: `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj`

- [ ] **Step 1 — Mirror an existing test project.** Read `tests/CombatStatusBarState.Tests/CombatStatusBarState.Tests.csproj`. Use it as the template. Adjust:
  - Project name → `AutoBazaar.Tests`
  - `<Compile Include="..\..\Game\AutoBazaar\AutoBazaarDecision.cs" Link="Game\AutoBazaar\AutoBazaarDecision.cs" />` (and add more `Compile` includes as later tasks introduce testable files; do **not** bulk-include the runtime MonoBehaviour, only pure-data/pure-logic files).
  - Set `<RootNamespace>AutoBazaarTests</RootNamespace>` (or whatever the sibling projects use).

- [ ] **Step 2 — Verify it restores and builds.**

```powershell
dotnet build tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj -nologo -clp:NoSummary
```

Expected: succeeds, no tests yet.

- [ ] **Step 3 — Commit.**

```powershell
git add tests/AutoBazaar.Tests/
git commit -m "Scaffold AutoBazaar test project"
```

### Task T2.3: `AutoBazaarContextSnapshot` content fingerprint (TDD)

**Files:**
- Create: `tests/AutoBazaar.Tests/AutoBazaarContextSnapshotTests.cs`
- Create: `Game/AutoBazaar/AutoBazaarContextSnapshot.cs`
- Modify: `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj` (add `Compile` include)

- [ ] **Step 1 — Write failing tests.** File `AutoBazaarContextSnapshotTests.cs`:

```csharp
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarContextSnapshotTests
{
    private static AutoBazaarContext MakeCtx(ulong tickId = 0, string serverTime = "t1")
        => new() { TickId = tickId, ServerTimeUtc = serverTime, StateName = AutoBazaarRunStateName.Choice, PlayerGold = 10 };

    [Fact]
    public void Publish_FirstSnapshot_AssignsTickIdOne()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var snap = pub.Publish(MakeCtx());
        Assert.Equal(1ul, snap.TickId);
        Assert.Equal("\"1\"", snap.ETag);
    }

    [Fact]
    public void Publish_IdenticalContent_KeepsSameTickId()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var s1 = pub.Publish(MakeCtx(serverTime: "t1"));
        var s2 = pub.Publish(MakeCtx(serverTime: "t2"));   // only serverTime differs
        Assert.Equal(s1.TickId, s2.TickId);
        Assert.Same(s1, s2);
    }

    [Fact]
    public void Publish_DifferentPlayerGold_BumpsTickId()
    {
        var pub = new AutoBazaarContextSnapshotPublisher();
        var s1 = pub.Publish(MakeCtx());
        var s2 = pub.Publish(MakeCtx() with { /* won't compile if not record — use explicit init */ });
        // ...adjust per actual DTO shape
    }
}
```

(If `AutoBazaarContext` is a class, replace the `with` expression with explicit `new()` initialization.)

- [ ] **Step 2 — Run, observe failure.**

```powershell
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo
```

Expected: compile error — `AutoBazaarContextSnapshotPublisher` not defined.

- [ ] **Step 3 — Implement `AutoBazaarContextSnapshot.cs`:**

```csharp
using System.Collections.Generic;
using System.Threading;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal sealed class AutoBazaarContextSnapshot
{
    public AutoBazaarContext Context { get; }
    public ulong TickId => Context.TickId;
    public string ETag { get; }
    public AutoBazaarContextSnapshot(AutoBazaarContext context)
    {
        Context = context;
        ETag = "\"" + context.TickId + "\"";
    }
}

internal sealed class AutoBazaarContextSnapshotPublisher
{
    private ulong _tickId;
    private AutoBazaarContextSnapshot? _current;

    /// <summary>Main-thread only. Returns the live snapshot, mutating in-place when content changed.</summary>
    public AutoBazaarContextSnapshot Publish(AutoBazaarContext candidate)
    {
        if (_current is not null && EqualsIgnoreTime(_current.Context, candidate))
        {
            return _current;
        }
        _tickId++;
        var stamped = CloneWithTickId(candidate, _tickId);
        var snap = new AutoBazaarContextSnapshot(stamped);
        _current = snap;
        return snap;
    }

    public AutoBazaarContextSnapshot? Current => Volatile.Read(ref _current);

    public void Reset()
    {
        _tickId = 0;
        Volatile.Write(ref _current, null);
    }

    private static AutoBazaarContext CloneWithTickId(AutoBazaarContext src, ulong tickId)
        => new()
        {
            SchemaVersion = src.SchemaVersion,
            TickId = tickId,
            ServerTimeUtc = src.ServerTimeUtc,
            IsEnabled = src.IsEnabled,
            IsInRun = src.IsInRun,
            HasActiveRun = src.HasActiveRun,
            CanStartOrContinueRun = src.CanStartOrContinueRun,
            IsClientBusy = src.IsClientBusy,
            RunId = src.RunId,
            StateName = src.StateName,
            PlayerGold = src.PlayerGold,
            SelectionIsFree = src.SelectionIsFree,
            CanExit = src.CanExit,
            CanReroll = src.CanReroll,
            RerollCost = src.RerollCost,
            RerollsRemaining = src.RerollsRemaining,
            CurrentEncounterId = src.CurrentEncounterId,
            ActionCooldownRemainingSeconds = src.ActionCooldownRemainingSeconds,
            BoardItems = src.BoardItems,
            ChestItems = src.ChestItems,
            PlayerSkills = src.PlayerSkills,
            SellableItems = src.SellableItems,
            SelectionOptions = src.SelectionOptions,
            AvailableActions = src.AvailableActions,
        };

    private static bool EqualsIgnoreTime(AutoBazaarContext a, AutoBazaarContext b)
    {
        if (a.IsEnabled != b.IsEnabled || a.IsInRun != b.IsInRun || a.HasActiveRun != b.HasActiveRun) return false;
        if (a.CanStartOrContinueRun != b.CanStartOrContinueRun || a.IsClientBusy != b.IsClientBusy) return false;
        if (a.RunId != b.RunId || a.StateName != b.StateName) return false;
        if (a.PlayerGold != b.PlayerGold || a.SelectionIsFree != b.SelectionIsFree) return false;
        if (a.CanExit != b.CanExit || a.CanReroll != b.CanReroll) return false;
        if (a.RerollCost != b.RerollCost || a.RerollsRemaining != b.RerollsRemaining) return false;
        if (a.CurrentEncounterId != b.CurrentEncounterId) return false;
        if (a.ActionCooldownRemainingSeconds != b.ActionCooldownRemainingSeconds) return false;
        if (!CardsEqual(a.BoardItems, b.BoardItems)) return false;
        if (!CardsEqual(a.ChestItems, b.ChestItems)) return false;
        if (!CardsEqual(a.PlayerSkills, b.PlayerSkills)) return false;
        if (!CardsEqual(a.SellableItems, b.SellableItems)) return false;
        if (!CardsEqual(a.SelectionOptions, b.SelectionOptions)) return false;
        if (!OptionsEqual(a.AvailableActions, b.AvailableActions)) return false;
        return true;
    }

    private static bool CardsEqual(IReadOnlyList<AutoBazaarCardSnapshot> a, IReadOnlyList<AutoBazaarCardSnapshot> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (!CardEquals(a[i], b[i])) return false;
        return true;
    }

    private static bool CardEquals(AutoBazaarCardSnapshot a, AutoBazaarCardSnapshot b)
    {
        return a.InstanceId == b.InstanceId
            && a.Kind == b.Kind
            && a.TemplateId == b.TemplateId
            && a.DisplayName == b.DisplayName
            && a.Tier == b.Tier
            && a.Size == b.Size
            && a.SocketId == b.SocketId
            && a.Location == b.Location
            && a.Order == b.Order
            && a.BuyPrice == b.BuyPrice
            && a.SellPrice == b.SellPrice
            && a.CanAfford == b.CanAfford
            && a.CanFit == b.CanFit
            && a.CanSelect == b.CanSelect
            && a.IsFree == b.IsFree
            && a.TargetSection == b.TargetSection
            && a.TargetSockets == b.TargetSockets
            && a.UnavailableReason == b.UnavailableReason
            && a.CanSell == b.CanSell;
    }

    private static bool OptionsEqual(IReadOnlyList<AutoBazaarDecisionOption> a, IReadOnlyList<AutoBazaarDecisionOption> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i]; var y = b[i];
            if (x.ActionKind != y.ActionKind) return false;
            if (x.Group != y.Group) return false;
            if (x.DisplayKey != y.DisplayKey) return false;
            if (x.CardInstanceId != y.CardInstanceId) return false;
            if (x.TargetSection != y.TargetSection) return false;
            if (!SocketsEqual(x.TargetSockets, y.TargetSockets)) return false;
        }
        return true;
    }

    private static bool SocketsEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
```

- [ ] **Step 4 — Add `Compile` includes for the two files into the test csproj.**

- [ ] **Step 5 — Run tests, observe pass.**

```powershell
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo
```

Expected: all 3+ tests pass.

- [ ] **Step 6 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarContextSnapshot.cs tests/AutoBazaar.Tests/
git commit -m "Add AutoBazaarContextSnapshot publisher with content fingerprint"
```

### Task T2.4: `AutoBazaarContextBuilder` (no test seam for happy path — minimal smoke)

Per `.rules`: ContextBuilder requires constructing live `AppState`/`Data.Run` to test meaningfully; the game state can't be assembled in xunit without loading Unity. Skip happy-path tests. Add only:
- A null-services smoke test (returns a context with `IsEnabled=false`, `StateName=Unknown`, empty collections, `AvailableActions=[Wait]`).
- Manual verification via debug log line in `Update()`.

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarContextBuilder.cs`

- [ ] **Step 1 — Write the builder.** The implementer fills in field-by-field mapping per Goal §7.4 using the property paths resolved in `T0.2`/`T0.4`/`T0.7`. Skeleton:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal static class AutoBazaarContextBuilder
{
    public static AutoBazaarContext Build(IBppServices services, double actionCooldownRemainingSeconds)
    {
        var ctx = new AutoBazaarContext
        {
            SchemaVersion = "1.0.0",
            ServerTimeUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            IsEnabled = services.Config.AutoBazaarEnabled?.Value == true,
            ActionCooldownRemainingSeconds = actionCooldownRemainingSeconds,
            StateName = AutoBazaarRunStateName.Unknown,
            AvailableActions = new[] { OptWait() },
        };

        // -- Resolve current AppState --
        var appState = TryGetCurrentAppState();
        if (appState is null) return ctx;

        // -- StateName --
        // ... implementation maps AppState subclass + RunState.StateName to AutoBazaarRunStateName
        //     per Goal §8 "Run state 枚举" + correction C5 (PVPCombat → PvpCombat).

        // -- Boards / chest / skills / sellable / selection --
        // ... implementation reads Data.Run.Player.Hand.Container, .Stash.Container, .Skills
        //     and translates to AutoBazaarCardSnapshot list

        // -- availableActions derivation --
        // Build by checking StateOps bitmask + RerollRules.RerollCost + CanExit etc.
        // Always include Wait. Add Move/Sell options per CardOperationUtility.MoveItem
        // enumeration over (EInventorySection, EContainerSocketId combinations).

        return ctx;
    }

    private static object? TryGetCurrentAppState() => null; // T0.2 fills this in

    private static AutoBazaarDecisionOption OptWait() => new()
    {
        ActionKind = AutoBazaarActionKind.Wait,
        Group = AutoBazaarActionGroup.Wait,
        DisplayKey = "Wait",
    };
}
```

- [ ] **Step 2 — Implement field-by-field mapping.** Reference Goal §7.4 exhaustively. For the cooldown-remaining calculation, the runtime owns the timestamp; pass it in. **Do not** call Unity API from the builder; the builder is pure — pass in everything it needs.

  - Refactor signature once you discover what dependencies are needed:
    `Build(IBppServices services, double cooldownRemainingSec, AppStateRef appState, RunRef? run)` — keep the surface easy to unit-test if any seam emerges.

- [ ] **Step 3 — Build.** `dotnet build BazaarPlusPlus.csproj`. Expected: success.

- [ ] **Step 4 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarContextBuilder.cs
git commit -m "Implement AutoBazaarContextBuilder field mapping"
```

### Task T2.5: Wire builder + publisher into `AutoBazaarRuntime.Update()`

**Files:**
- Modify: `Game/AutoBazaar/AutoBazaarRuntime.cs`

- [ ] **Step 1 — Add publisher field + tick wiring.**

```csharp
private readonly AutoBazaarContextSnapshotPublisher _snapshots = new();
private float _lastActionTime = float.NegativeInfinity;
private const float ActionMinDelaySeconds = 1.0f;

internal AutoBazaarContextSnapshot? CurrentSnapshot => _snapshots.Current;
```

- [ ] **Step 2 — Build the snapshot each tick.** Inside `Update()` after the interval gate:

```csharp
var cooldownLeft = ComputeCooldownLeft();
var ctx = AutoBazaarContextBuilder.Build(_services, cooldownLeft);
var snap = _snapshots.Publish(ctx);
// Phase 3+ will drain action queue + UI plumbing here.
```

- [ ] **Step 3 — Add `ComputeCooldownLeft()`:**

```csharp
private double ComputeCooldownLeft()
{
    var elapsed = Time.unscaledTime - _lastActionTime;
    var remaining = ActionMinDelaySeconds - elapsed;
    return remaining > 0 ? remaining : 0;
}
```

- [ ] **Step 4 — Debug visibility.** Add a one-shot log when `snap.TickId == 1`:

```csharp
if (snap.TickId == 1) BppLog.Info("AutoBazaar", $"First snapshot published. state={ctx.StateName}");
```

- [ ] **Step 5 — Build, smoke.** Launch game, confirm log line appears once, no exceptions, no spam.

- [ ] **Step 6 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarRuntime.cs
git commit -m "Publish AutoBazaar snapshot each runtime tick"
```

---

## Phase 3 — Action dispatch + UI plumbing + decision log

> Goal: validator, dispatcher, ULID, decision logger, UI plumbing all exist and are tested. Still no HTTP — actions invoked via a unit-test seam.

### Task T3.1: `AutoBazaarUlid` (TDD)

**Files:**
- Create: `tests/AutoBazaar.Tests/AutoBazaarUlidTests.cs`
- Create: `Game/AutoBazaar/AutoBazaarUlid.cs`
- Modify: `tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj`

- [ ] **Step 1 — Write tests:**

```csharp
using System;
using System.Collections.Generic;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarUlidTests
{
    [Fact]
    public void NewUlid_HasLength26()
    {
        var u = AutoBazaarUlid.New();
        Assert.Equal(26, u.Length);
    }

    [Fact]
    public void NewUlid_UsesOnlyCrockfordAlphabet()
    {
        const string alpha = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var u = AutoBazaarUlid.New();
        foreach (var c in u) Assert.Contains(c, alpha);
    }

    [Fact]
    public void NewUlid_Monotonic_OverManyIterations()
    {
        var prev = AutoBazaarUlid.New();
        for (var i = 0; i < 10_000; i++)
        {
            var next = AutoBazaarUlid.New();
            Assert.True(string.CompareOrdinal(prev, next) < 0, $"non-monotonic at {i}: {prev} → {next}");
            prev = next;
        }
    }
}
```

- [ ] **Step 2 — Run, observe failure.**

```powershell
dotnet test tests/AutoBazaar.Tests/AutoBazaar.Tests.csproj --nologo --filter FullyQualifiedName~Ulid
```

Expected: type not found.

- [ ] **Step 3 — Implement `AutoBazaarUlid.cs`:**

```csharp
using System;
using System.Security.Cryptography;
using System.Threading;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal static class AutoBazaarUlid
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";  // Crockford base32
    private static readonly object _gate = new();
    private static long _lastTs;
    private static readonly byte[] _lastRand = new byte[10];
    private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

    public static string New()
    {
        Span<byte> rand = stackalloc byte[10];
        long ts;
        lock (_gate)
        {
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (ts <= _lastTs)
            {
                ts = _lastTs;
                IncrementRandomness(_lastRand);
            }
            else
            {
                _rng.GetBytes(_lastRand);
            }
            _lastTs = ts;
            new ReadOnlySpan<byte>(_lastRand).CopyTo(rand);
        }

        Span<char> chars = stackalloc char[26];
        EncodeTs(ts, chars[..10]);
        EncodeRand(rand, chars[10..]);
        return new string(chars);
    }

    private static void IncrementRandomness(byte[] r)
    {
        for (var i = r.Length - 1; i >= 0; i--)
        {
            if (++r[i] != 0) return;
        }
        // overflow — extremely unlikely in same millisecond; just reseed
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(r);
    }

    private static void EncodeTs(long ts, Span<char> dst)
    {
        for (var i = 9; i >= 0; i--)
        {
            dst[i] = Alphabet[(int)(ts & 0x1F)];
            ts >>= 5;
        }
    }

    private static void EncodeRand(ReadOnlySpan<byte> rand, Span<char> dst)
    {
        // 10 bytes (80 bits) → 16 chars (5 bits each)
        ulong hi = ((ulong)rand[0] << 32) | ((ulong)rand[1] << 24) | ((ulong)rand[2] << 16)
                 | ((ulong)rand[3] << 8) | rand[4];
        ulong lo = ((ulong)rand[5] << 32) | ((ulong)rand[6] << 24) | ((ulong)rand[7] << 16)
                 | ((ulong)rand[8] << 8) | rand[9];
        for (var i = 7; i >= 0; i--) { dst[i] = Alphabet[(int)(hi & 0x1F)]; hi >>= 5; }
        for (var i = 15; i >= 8; i--) { dst[i] = Alphabet[(int)(lo & 0x1F)]; lo >>= 5; }
    }
}
```

- [ ] **Step 4 — Run tests, observe pass.**

- [ ] **Step 5 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarUlid.cs tests/AutoBazaar.Tests/
git commit -m "Add monotonic Crockford ULID generator"
```

### Task T3.2: `AutoBazaarActionValidator` (TDD — all 8 rules)

**Files:**
- Create: `tests/AutoBazaar.Tests/AutoBazaarActionValidatorTests.cs`
- Create: `Game/AutoBazaar/AutoBazaarActionValidator.cs`

- [ ] **Step 1 — Define the result shape.** Inside `AutoBazaarActionValidator.cs`:

```csharp
namespace BazaarPlusPlus.Game.AutoBazaar;

internal enum AutoBazaarValidationCode
{
    Ok, Invalid, StaleOrUnavailable, Cooldown, Unavailable,
}

internal readonly record struct AutoBazaarValidationResult(
    AutoBazaarValidationCode Code,
    int HttpStatus,
    string? Details,
    System.Collections.Generic.IReadOnlyDictionary<string, object?>? Extra);
```

- [ ] **Step 2 — Write tests covering all 8 rules.** One accept case + one reject case per rule. Sample subset:

```csharp
using System.Collections.Generic;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarActionValidatorTests
{
    private static AutoBazaarContextSnapshot Snap(params AutoBazaarDecisionOption[] avail)
        => new(new AutoBazaarContext
        {
            TickId = 7,
            AvailableActions = avail,
        });

    [Fact]
    public void Rule1_UnknownActionKindReturns400()
    {
        var snap = Snap();
        var action = new AutoBazaarAction { ActionKind = (AutoBazaarActionKind)9999 };
        var r = AutoBazaarActionValidator.Validate(snap, action, cooldownRemainingSeconds: 0);
        Assert.Equal(400, r.HttpStatus);
        Assert.Equal(AutoBazaarValidationCode.Invalid, r.Code);
    }

    [Fact]
    public void Rule2_ActionKindNotInAvailableReturns409_WaitExempted()
    {
        var snap = Snap();  // no SelectItem available
        var action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.SelectItem };
        var r = AutoBazaarActionValidator.Validate(snap, action, 0);
        Assert.Equal(409, r.HttpStatus);

        var wait = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait };
        var rw = AutoBazaarActionValidator.Validate(snap, wait, 0);
        Assert.Equal(AutoBazaarValidationCode.Ok, rw.Code);
    }

    [Fact]
    public void Rule7_StaleForTickIdReturns409_WithCurrentTickIdExtra()
    {
        var snap = Snap(new AutoBazaarDecisionOption { ActionKind = AutoBazaarActionKind.Wait });
        var action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait, ForTickId = 6 };
        var r = AutoBazaarActionValidator.Validate(snap, action, 0);
        Assert.Equal(409, r.HttpStatus);
        Assert.NotNull(r.Extra);
        Assert.Equal(7ul, r.Extra!["currentTickId"]);
    }

    [Fact]
    public void Rule8_CooldownGate_NonWaitReturns429_WaitExempt()
    {
        var snap = Snap(new AutoBazaarDecisionOption { ActionKind = AutoBazaarActionKind.Wait },
                        new AutoBazaarDecisionOption { ActionKind = AutoBazaarActionKind.Reroll, Group = AutoBazaarActionGroup.Reroll, DisplayKey = "Reroll" });
        var nonWait = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Reroll };
        var r = AutoBazaarActionValidator.Validate(snap, nonWait, cooldownRemainingSeconds: 0.4);
        Assert.Equal(429, r.HttpStatus);
        Assert.Equal(0.4, (double)r.Extra!["retryAfterSeconds"], 3);

        var wait = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait };
        var rw = AutoBazaarActionValidator.Validate(snap, wait, cooldownRemainingSeconds: 5.0);
        Assert.Equal(AutoBazaarValidationCode.Ok, rw.Code);
    }
    // ... rules 3 (card-bearing match), 4 (hero/playMode enum), 5 (canSelect != false), 6 (canSell)
}
```

- [ ] **Step 3 — Run, observe failures.**

- [ ] **Step 4 — Implement `Validate`:**

```csharp
public static class AutoBazaarActionValidator
{
    public static AutoBazaarValidationResult Validate(
        AutoBazaarContextSnapshot snapshot,
        AutoBazaarAction action,
        double cooldownRemainingSeconds)
    {
        // Rule 1: known actionKind
        if (!System.Enum.IsDefined(typeof(AutoBazaarActionKind), action.ActionKind))
            return new(AutoBazaarValidationCode.Invalid, 400, "unknown actionKind", null);

        var isWait = action.ActionKind == AutoBazaarActionKind.Wait;

        // Rule 2: in availableActions (Wait exempt)
        if (!isWait)
        {
            var matched = FindMatchingOption(snapshot.Context.AvailableActions, action);
            if (matched is null)
                return new(AutoBazaarValidationCode.StaleOrUnavailable, 409,
                    "actionKind not in availableActions",
                    new System.Collections.Generic.Dictionary<string, object?> { ["currentTickId"] = snapshot.TickId });
        }

        // Rule 3 + 5 + 6: card-bearing & predicate checks
        // ... see Goal §7.5 validation list

        // Rule 4: hero/playMode enum if provided
        if (action.ActionKind == AutoBazaarActionKind.StartOrContinueRun)
        {
            if (action.Hero is not null && !TryParseEnum<EHero>(action.Hero, out _))
                return new(AutoBazaarValidationCode.Invalid, 400, "unknown hero", null);
            if (action.PlayMode is not null && !TryParseEnum<EPlayMode>(action.PlayMode, out _))
                return new(AutoBazaarValidationCode.Invalid, 400, "unknown playMode", null);
        }

        // Rule 7: forTickId
        if (action.ForTickId is { } want && want != snapshot.TickId)
            return new(AutoBazaarValidationCode.StaleOrUnavailable, 409,
                "stale tickId",
                new System.Collections.Generic.Dictionary<string, object?> { ["currentTickId"] = snapshot.TickId });

        // Rule 8: cooldown
        if (!isWait && cooldownRemainingSeconds > 0)
            return new(AutoBazaarValidationCode.Cooldown, 429,
                "action min-delay not yet elapsed",
                new System.Collections.Generic.Dictionary<string, object?> { ["retryAfterSeconds"] = cooldownRemainingSeconds });

        return new(AutoBazaarValidationCode.Ok, 200, null, null);
    }

    private static AutoBazaarDecisionOption? FindMatchingOption(
        System.Collections.Generic.IReadOnlyList<AutoBazaarDecisionOption> options,
        AutoBazaarAction action)
    {
        foreach (var opt in options)
        {
            if (opt.ActionKind != action.ActionKind) continue;
            if (opt.CardInstanceId != action.CardInstanceId) continue;
            if (opt.TargetSection != action.TargetSection) continue;
            if (!SocketsEqual(opt.TargetSockets, action.TargetSockets)) continue;
            return opt;
        }
        return null;
    }

    private static bool SocketsEqual(
        System.Collections.Generic.IReadOnlyList<string>? a,
        System.Collections.Generic.IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return (a?.Count ?? 0) == (b?.Count ?? 0);
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static bool TryParseEnum<TEnum>(string s, out TEnum value) where TEnum : struct
        => System.Enum.TryParse(s, ignoreCase: true, out value);
}
```

(`EHero`/`EPlayMode` types live in the decompiled assemblies — reference them via the same assembly reference path the mod already uses, or accept a `string`-based pre-check and let the dispatcher convert. Match how the rest of the project does it.)

- [ ] **Step 5 — Run, observe pass.**

- [ ] **Step 6 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarActionValidator.cs tests/AutoBazaar.Tests/
git commit -m "Add AutoBazaar action validator with 8-rule ladder"
```

### Task T3.3: `AutoBazaarDecisionLog` (TDD)

**Files:**
- Create: `tests/AutoBazaar.Tests/AutoBazaarDecisionLogTests.cs`
- Create: `Game/AutoBazaar/AutoBazaarDecisionLog.cs`

- [ ] **Step 1 — Tests:** path sanitization (`runId="../../etc"` → no traversal), fallback when `runId == null`, JSONL line format (one trailing `\n`, valid JSON per line).

```csharp
using System.IO;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarDecisionLogTests
{
    [Fact]
    public void Append_NullRunId_WritesToFallbackPath()
    {
        using var tmp = new TempDir();
        var log = new AutoBazaarDecisionLog(tmp.Path);
        log.Append(new AutoBazaarDecisionLogEntry { TickId = 1, DecisionId = "01H", RunId = null,
            State = "Choice", Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait }, Executed = true });
        Assert.True(File.Exists(Path.Combine(tmp.Path, "decisions.jsonl")));
    }

    [Fact]
    public void Append_PathTraversalRunId_IsSanitized()
    {
        using var tmp = new TempDir();
        var log = new AutoBazaarDecisionLog(tmp.Path);
        log.Append(new AutoBazaarDecisionLogEntry { RunId = "../evil/run", State = "X",
            Action = new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait }, Executed = true });
        Assert.False(Directory.Exists(Path.Combine(tmp.Path, "..", "evil")));
        Assert.True(Directory.Exists(Path.Combine(tmp.Path, "runs", "___evil_run")) ||
                    Directory.Exists(Path.Combine(tmp.Path, "runs", "_._.evil_run")));
        // exact sanitized form is impl detail — assert no traversal happened
    }
}

internal sealed class TempDir : System.IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
    public TempDir() => System.IO.Directory.CreateDirectory(Path);
    public void Dispose() { try { System.IO.Directory.Delete(Path, recursive: true); } catch { } }
}
```

- [ ] **Step 2 — Implement:**

```csharp
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal sealed class AutoBazaarDecisionLogEntry
{
    public string Ts { get; init; } = "";
    public ulong TickId { get; init; }
    public string DecisionId { get; init; } = "";
    public string? RunId { get; init; }
    public string State { get; init; } = "";
    public AutoBazaarAction Action { get; init; } = new();
    public bool Executed { get; init; }
    public string? Error { get; init; }
    public string? Reason { get; init; }
}

internal sealed class AutoBazaarDecisionLog
{
    private readonly string _rootDir;
    private static readonly JsonSerializerSettings _json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
    };

    public AutoBazaarDecisionLog(string rootDir) => _rootDir = rootDir;

    public void Append(AutoBazaarDecisionLogEntry entry)
    {
        var path = ResolvePath(entry.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonConvert.SerializeObject(entry, _json);
        File.AppendAllText(path, json + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string ResolvePath(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId)) return Path.Combine(_rootDir, "decisions.jsonl");
        var safe = Sanitize(runId);
        return Path.Combine(_rootDir, "runs", safe, "decisions.jsonl");
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(System.Array.IndexOf(invalid, c) >= 0 || c == '.' || c == '/' || c == '\\' ? '_' : c);
        }
        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "_" : result;
    }
}
```

(`AutoBazaarDecisionLogEntry`: also auto-fill `Ts = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)` if blank, or push that responsibility into the caller — match what the test expects.)

- [ ] **Step 3 — Run tests, fix assertion exact strings to match impl.**

- [ ] **Step 4 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarDecisionLog.cs tests/AutoBazaar.Tests/
git commit -m "Add AutoBazaar JSONL decision log with path sanitization"
```

### Task T3.4: `AutoBazaarActionDispatcher` (minimal-seam approach)

Dispatch directly calls `Cmd.GetInstance().X()` / reflection. Hard to test without Unity. Per `.rules`: no mock-call-sequence tests. Strategy: write one unit test that confirms switch-completeness (every `AutoBazaarActionKind` value has a non-`NotImplementedException` branch), plus manual smoke. No real-engine tests.

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarActionDispatcher.cs`
- Create: `tests/AutoBazaar.Tests/AutoBazaarActionDispatcherTests.cs` *(switch-completeness only)*

- [ ] **Step 1 — Define result shape:**

```csharp
namespace BazaarPlusPlus.Game.AutoBazaar;

internal readonly record struct AutoBazaarDispatchResult(bool Executed, string? Error);
```

- [ ] **Step 2 — Implement dispatch.** Each case fully spelled out:

```csharp
using TheBazaar;             // Cmd, AppState, ...
using BazaarGameClient.Domain.Models;   // EHero, EPlayMode, EInventorySection, EContainerSocketId, ItemCard
using BazaarGameShared.Domain.Core;     // InstanceId

namespace BazaarPlusPlus.Game.AutoBazaar;

internal static class AutoBazaarActionDispatcher
{
    /// <summary>Main thread only.</summary>
    public static AutoBazaarDispatchResult Execute(AutoBazaarAction action, AutoBazaarContextSnapshot snapshot)
    {
        try
        {
            switch (action.ActionKind)
            {
                case AutoBazaarActionKind.Wait:
                    return new(true, null);

                case AutoBazaarActionKind.StartOrContinueRun:
                    return ExecStartOrContinueRun(action);

                case AutoBazaarActionKind.AbandonRun:
                    Cmd.GetInstance().SendAbandonRun();
                    return new(true, null);

                case AutoBazaarActionKind.SelectItem:
                    return ExecSelectItem(action, snapshot);

                case AutoBazaarActionKind.SelectSkill:
                    Cmd.GetInstance().SendSelectSkill(new InstanceId(action.CardInstanceId ?? ""));
                    return new(true, null);

                case AutoBazaarActionKind.SelectEncounter:
                    Cmd.GetInstance().SelectEncounter(new InstanceId(action.CardInstanceId ?? ""));
                    return new(true, null);

                case AutoBazaarActionKind.CommitToPedestal:
                    Cmd.GetInstance().SendCommitToPedestal(new InstanceId(action.CardInstanceId ?? ""));
                    return new(true, null);

                case AutoBazaarActionKind.MoveItem:
                    return ExecMoveItem(action, snapshot);

                case AutoBazaarActionKind.SellItem:
                    Cmd.GetInstance().SendSellCard(new InstanceId(action.CardInstanceId ?? ""));
                    return new(true, null);

                case AutoBazaarActionKind.Reroll:
                    Cmd.GetInstance().SendReRollSelection();
                    return new(true, null);

                case AutoBazaarActionKind.ExitState:
                    Cmd.GetInstance().SendExitCurrentState();
                    return new(true, null);

                case AutoBazaarActionKind.AdvanceEndRun:
                    return ExecAdvanceEndRun();

                default:
                    return new(false, "unsupported actionKind");
            }
        }
        catch (System.Exception ex)
        {
            BazaarPlusPlus.Infrastructure.BppLog.Error("AutoBazaar", "dispatch threw", ex);
            return new(false, "dispatcher exception: " + ex.GetType().Name);
        }
    }

    private static AutoBazaarDispatchResult ExecSelectItem(AutoBazaarAction action, AutoBazaarContextSnapshot snap)
    {
        var card = FindItemCard(action.CardInstanceId);
        if (card is null) return new(false, "item not found in run");
        var section = (EInventorySection)(int)(action.TargetSection ?? AutoBazaarTargetSection.Hand);
        var sockets = ParseSockets(action.TargetSockets);
        Cmd.GetInstance().SelectItem(card, sockets, section);
        return new(true, null);
    }

    private static AutoBazaarDispatchResult ExecMoveItem(AutoBazaarAction action, AutoBazaarContextSnapshot snap)
    {
        var card = FindItemCard(action.CardInstanceId);
        if (card is null) return new(false, "item not found in run");
        var section = (EInventorySection)(int)(action.TargetSection ?? AutoBazaarTargetSection.Hand);
        var sockets = ParseSockets(action.TargetSockets);
        Cmd.GetInstance().SendMoveItem(card, sockets, section);
        return new(true, null);
    }

    private static AutoBazaarDispatchResult ExecStartOrContinueRun(AutoBazaarAction action)
    {
        // T0.3 result fills in hero/playMode write path.
        // Then call GameInstance.Instance.StartNewRun() (or whatever T0.6 found).
        // ...
        return new(true, null);
    }

    private static AutoBazaarDispatchResult ExecAdvanceEndRun()
    {
        // T0.5 result names the private method.
        var controller = UnityEngine.Object.FindObjectOfType<TheBazaar.UI.EndOfRun.EndOfRunScreenController>();
        if (controller is null) return new(false, "EndOfRunScreenController not active");
        var method = typeof(TheBazaar.UI.EndOfRun.EndOfRunScreenController)
            .GetMethod("OnContinueClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (method is null) return new(false, "OnContinueClick not found via reflection");
        method.Invoke(controller, null);
        return new(true, null);
    }

    private static ItemCard? FindItemCard(string? instanceId)
    {
        // Walk Data.Run.Player.Hand.Container + .Stash.Container + selection set; return matching ItemCard.
        // Implementation per T0.2 / T0.7.
        return null;
    }

    private static System.Collections.Generic.List<EContainerSocketId> ParseSockets(System.Collections.Generic.IReadOnlyList<string>? raw)
    {
        var list = new System.Collections.Generic.List<EContainerSocketId>();
        if (raw is null) return list;
        foreach (var s in raw)
        {
            if (System.Enum.TryParse<EContainerSocketId>(s, ignoreCase: true, out var v)) list.Add(v);
        }
        return list;
    }
}
```

- [ ] **Step 3 — Switch-completeness test:** assert that `Execute` does not throw `NotImplementedException` for any `AutoBazaarActionKind` value. (Won't actually invoke `Cmd.*` in test — wrap Execute in a try/catch and just verify the catch path doesn't contain "NotImplemented"… or simpler: use reflection to confirm the switch covers every enum value. This test stays a structural sanity check, not a behavior assertion — keep it small or skip per `.rules` if it leans toward mock-sequence-style testing.)

- [ ] **Step 4 — Build.** `dotnet build BazaarPlusPlus.csproj`. Expected: success.

- [ ] **Step 5 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarActionDispatcher.cs tests/AutoBazaar.Tests/
git commit -m "Add AutoBazaar action dispatcher"
```

### Task T3.5: `AutoBazaarUiPlumbing`

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarUiPlumbing.cs`

- [ ] **Step 1 — Implement.** Two responsibilities only (Goal §9):

```csharp
namespace BazaarPlusPlus.Game.AutoBazaar;

internal static class AutoBazaarUiPlumbing
{
    /// <summary>Main thread only. Idempotent; safe to call every tick.</summary>
    public static void Tick()
    {
        TryAdvanceReplay();
        TryDismissKnownOverlays();
    }

    private static void TryAdvanceReplay()
    {
        // appState is ReplayState && !IsReplaying && _cachedGameSim != null  →  appState.GoToNextState()
        // Find current AppState via T0.2 accessor.
        // Use reflection if _cachedGameSim is private (likely).
    }

    private static void TryDismissKnownOverlays()
    {
        // v1: PvP first-victory tutorial dialog only.
        // Locate via UnityEngine.Object.FindObjectsOfType<DialogX>(). Close if visible.
    }
}
```

(Replay + overlay details depend on inspecting `decompiled/CombatState.cs` / `ReplayState.cs` / the tutorial dialog class. Defer specifics to the implementer; reference Goal §9.)

- [ ] **Step 2 — Build, commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarUiPlumbing.cs
git commit -m "Add AutoBazaar UI plumbing for replay and overlays"
```

### Task T3.6: Wire dispatcher + log + UI plumbing into `Update()` (without HTTP yet)

**Files:**
- Modify: `Game/AutoBazaar/AutoBazaarRuntime.cs`

- [ ] **Step 1 — Add fields:**

```csharp
private AutoBazaarDecisionLog? _decisionLog;
private string GetLogRoot() => System.IO.Path.Combine(
    UnityEngine.Application.dataPath, "..", "BazaarPlusPlus", "AutoBazaar");
```

- [ ] **Step 2 — Tick body (still no HTTP):**

```csharp
AutoBazaarUiPlumbing.Tick();

// Phase 4 will replace this no-op with actual queue draining:
// var pending = _queue.TryDequeue();
// if (pending is not null) ProcessPending(pending, snap);
```

- [ ] **Step 3 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarRuntime.cs
git commit -m "Wire AutoBazaar UI plumbing into runtime tick"
```

---

## Phase 4 — HTTP endpoint

> Goal: real HTTP server, end-to-end POST/GET round-trip, integration tests pass.

### Task T4.1: `AutoBazaarActionQueue` (TDD)

**Files:**
- Create: `tests/AutoBazaar.Tests/AutoBazaarActionQueueTests.cs`
- Create: `Game/AutoBazaar/AutoBazaarActionQueue.cs`

- [ ] **Step 1 — Tests:**

```csharp
using System.Threading.Tasks;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarActionQueueTests
{
    [Fact]
    public async Task EnqueueDequeueSetResult_CompletesAwait()
    {
        var q = new AutoBazaarActionQueue(timeoutMilliseconds: 1_000);
        var task = q.EnqueueAndAwaitAsync(new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait });
        var pending = q.TryDequeue();
        Assert.NotNull(pending);
        pending!.SetResponse(new AutoBazaarServerResponse(200, "{\"ok\":true}"));
        var res = await task;
        Assert.Equal(200, res.HttpStatus);
    }

    [Fact]
    public async Task Timeout_CompletesWith503AndMarksDiscarded()
    {
        var q = new AutoBazaarActionQueue(timeoutMilliseconds: 50);
        var task = q.EnqueueAndAwaitAsync(new AutoBazaarAction { ActionKind = AutoBazaarActionKind.Wait });
        var res = await task;
        Assert.Equal(503, res.HttpStatus);

        // Now even if main thread dequeues, it sees IsDiscarded
        var pending = q.TryDequeue();
        Assert.True(pending is null || pending.IsDiscarded);
    }
}
```

- [ ] **Step 2 — Implement.** Each enqueue creates a `PendingAction { Decision, Tcs, IsDiscarded, _timer }`. Timer fires `SetResponse(503)` and flips `IsDiscarded`. `SetResponse` is idempotent (uses `TrySetResult`).

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal sealed class AutoBazaarServerResponse
{
    public int HttpStatus { get; }
    public string JsonBody { get; }
    public AutoBazaarServerResponse(int status, string body) { HttpStatus = status; JsonBody = body; }
}

internal sealed class PendingAction
{
    public AutoBazaarAction Action { get; }
    public TaskCompletionSource<AutoBazaarServerResponse> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _discardedOrDone;
    private Timer? _timer;
    public bool IsDiscarded => Volatile.Read(ref _discardedOrDone) != 0;

    internal PendingAction(AutoBazaarAction a, int timeoutMs)
    {
        Action = a;
        _timer = new Timer(_ => SetResponse(new AutoBazaarServerResponse(503, "{\"error\":\"unavailable\"}")), null, timeoutMs, Timeout.Infinite);
    }

    public void SetResponse(AutoBazaarServerResponse r)
    {
        if (Interlocked.CompareExchange(ref _discardedOrDone, 1, 0) != 0) return;
        try { _timer?.Dispose(); } catch { }
        Tcs.TrySetResult(r);
    }
}

internal sealed class AutoBazaarActionQueue
{
    private readonly ConcurrentQueue<PendingAction> _q = new();
    private readonly int _timeoutMs;

    public AutoBazaarActionQueue(int timeoutMilliseconds) => _timeoutMs = timeoutMilliseconds;

    public Task<AutoBazaarServerResponse> EnqueueAndAwaitAsync(AutoBazaarAction a)
    {
        var p = new PendingAction(a, _timeoutMs);
        _q.Enqueue(p);
        return p.Tcs.Task;
    }

    public PendingAction? TryDequeue()
    {
        while (_q.TryDequeue(out var p))
        {
            if (p.IsDiscarded) continue;
            return p;
        }
        return null;
    }
}
```

- [ ] **Step 3 — Run tests, observe pass.**

- [ ] **Step 4 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarActionQueue.cs tests/AutoBazaar.Tests/
git commit -m "Add AutoBazaar action queue with TCS-bridged timeout"
```

### Task T4.2: `AutoBazaarHttpServer` skeleton + endpoint.json

**Files:**
- Create: `Game/AutoBazaar/AutoBazaarHttpServer.cs`

- [ ] **Step 1 — Define interface:** ctor accepts `(int port, string endpointJsonPath, Func<AutoBazaarContextSnapshot?> snapshotGetter, AutoBazaarActionQueue queue)`. Methods: `Start()`, `Stop()`. Internally manages `HttpListener` + ThreadPool dispatch.

- [ ] **Step 2 — Implement Start/Stop + listener loop + endpoint.json write/delete:**

```csharp
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal sealed class AutoBazaarHttpServer
{
    private readonly int _port;
    private readonly string _endpointJsonPath;
    private readonly Func<AutoBazaarContextSnapshot?> _snapshotGetter;
    private readonly AutoBazaarActionQueue _queue;
    private readonly JsonSerializerSettings _json;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public AutoBazaarHttpServer(int port, string endpointJsonPath, Func<AutoBazaarContextSnapshot?> snapshotGetter, AutoBazaarActionQueue queue)
    {
        _port = port; _endpointJsonPath = endpointJsonPath; _snapshotGetter = snapshotGetter; _queue = queue;
        _json = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter() },
        };
    }

    public int Port => _port;

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = AcceptLoop(_cts.Token);
        WriteEndpointJson();
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        DeleteEndpointJson();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        var listener = _listener;
        if (listener is null) return;
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext? ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { return; }
            _ = HandleContextAsync(ctx);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (string.Equals(path, "/v1/context", StringComparison.OrdinalIgnoreCase) && ctx.Request.HttpMethod == "GET")
                await HandleContext(ctx);
            else if (string.Equals(path, "/v1/actions", StringComparison.OrdinalIgnoreCase) && ctx.Request.HttpMethod == "POST")
                await HandleActions(ctx);
            else
                WriteErr(ctx, 404, "not-found", "unknown route");
        }
        catch (Exception ex)
        {
            try { WriteErr(ctx, 500, "internal", ex.GetType().Name); } catch { }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private async Task HandleContext(HttpListenerContext ctx)
    {
        var snap = _snapshotGetter();
        if (snap is null) { WriteErr(ctx, 503, "unavailable", null); return; }
        var inm = ctx.Request.Headers["If-None-Match"];
        if (inm == snap.ETag) { ctx.Response.StatusCode = 304; return; }
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["ETag"] = snap.ETag;
        var body = JsonConvert.SerializeObject(snap.Context, _json);
        var bytes = Encoding.UTF8.GetBytes(body);
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    private async Task HandleActions(HttpListenerContext ctx)
    {
        // 64KB cap
        var lenHeader = ctx.Request.Headers["Content-Length"];
        if (long.TryParse(lenHeader, out var declared) && declared > 65536)
        { WriteErr(ctx, 413, "invalid", "body too large"); return; }

        using var ms = new MemoryStream();
        var buf = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await ctx.Request.InputStream.ReadAsync(buf, 0, buf.Length);
            if (read <= 0) break;
            total += read;
            if (total > 65536) { WriteErr(ctx, 413, "invalid", "body too large"); return; }
            ms.Write(buf, 0, read);
        }

        AutoBazaarAction? action;
        try
        {
            var json = Encoding.UTF8.GetString(ms.ToArray());
            action = JsonConvert.DeserializeObject<AutoBazaarAction>(json, _json);
        }
        catch (JsonException) { WriteErr(ctx, 400, "invalid", "malformed json"); return; }

        if (action is null) { WriteErr(ctx, 400, "invalid", "empty body"); return; }

        var res = await _queue.EnqueueAndAwaitAsync(action);
        ctx.Response.StatusCode = res.HttpStatus;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(res.JsonBody);
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    private void WriteErr(HttpListenerContext ctx, int status, string code, string? details)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var envelope = new { error = code, details };
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope, _json));
        try { ctx.Response.OutputStream.Write(bytes, 0, bytes.Length); } catch { }
    }

    private void WriteEndpointJson()
    {
        var payload = new { baseUrl = $"http://127.0.0.1:{_port}", schemaVersion = "1.0.0", pid = System.Diagnostics.Process.GetCurrentProcess().Id };
        var json = JsonConvert.SerializeObject(payload, _json);
        Directory.CreateDirectory(Path.GetDirectoryName(_endpointJsonPath)!);
        var tmp = _endpointJsonPath + ".tmp";
        File.WriteAllText(tmp, json);
        try { File.Delete(_endpointJsonPath); } catch { }
        File.Move(tmp, _endpointJsonPath);
    }

    private void DeleteEndpointJson()
    {
        try { File.Delete(_endpointJsonPath); } catch { }
    }
}
```

- [ ] **Step 2 — Build.** Expected: success.

- [ ] **Step 3 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarHttpServer.cs
git commit -m "Add AutoBazaarHttpServer routes and endpoint.json"
```

### Task T4.3: Integration tests against real `HttpListener`

**Files:**
- Create: `tests/AutoBazaar.Tests/AutoBazaarHttpServerTests.cs`

- [ ] **Step 1 — Test fixture.** Each test:
  - picks an ephemeral port (try 47900 + random offset, retry on conflict);
  - creates a fake `snapshotGetter` returning a constructed `AutoBazaarContextSnapshot`;
  - creates a real `AutoBazaarActionQueue`;
  - starts server, runs HTTP roundtrip via `HttpClient`, stops server.

- [ ] **Step 2 — Cases to cover:**
  1. `GET /v1/context` returns 200 + valid JSON + `ETag` header
  2. `GET /v1/context` with matching `If-None-Match` returns 304
  3. `GET /v1/context` with `snapshotGetter()==null` returns 503
  4. `POST /v1/actions {"actionKind":"Wait"}` round-trips when a background "dispatcher loop" task dequeues + sets `200` response
  5. `POST /v1/actions` with malformed JSON returns 400
  6. `POST /v1/actions` with `Content-Length: 100000` returns 413 *before* reading body
  7. `POST /v1/actions` with no dispatcher draining → times out after `timeoutMs` with 503
  8. Two concurrent `POST` actions: only one dispatcher dequeue allowed; second still pending → eventually 503 if no second dequeue

- [ ] **Step 3 — Run tests, observe pass.**

- [ ] **Step 4 — Commit.**

```powershell
git add tests/AutoBazaar.Tests/AutoBazaarHttpServerTests.cs
git commit -m "Add AutoBazaarHttpServer integration tests"
```

### Task T4.4: Wire HTTP server into `AutoBazaarRuntime` lifecycle

**Files:**
- Modify: `Game/AutoBazaar/AutoBazaarRuntime.cs`

- [ ] **Step 1 — Add listener lifecycle:** track current port + running flag; restart when port changes; stop when `Enabled=false`.

```csharp
private AutoBazaarHttpServer? _http;
private AutoBazaarActionQueue? _queue;
private int _currentPort = -1;
private float _lastListenerReconcileTime;
private const float ListenerReconcileDebounceSeconds = 0.5f;

private void ReconcileListener()
{
    if (_services is null) return;
    if (Time.unscaledTime - _lastListenerReconcileTime < ListenerReconcileDebounceSeconds) return;
    _lastListenerReconcileTime = Time.unscaledTime;

    var enabled = _services.Config.AutoBazaarEnabled?.Value == true;
    var desiredPort = _services.Config.AutoBazaarHttpListenerPort?.Value ?? 47900;
    var desiredTimeoutMs = (int)((_services.Config.AutoBazaarHttpEndpointTimeoutSeconds?.Value ?? 3f) * 1000);

    if (!enabled && _http is not null)
    {
        try { _http.Stop(); } catch { }
        _http = null; _queue = null; _currentPort = -1;
        _snapshots.Reset();
        return;
    }
    if (enabled && (_http is null || desiredPort != _currentPort))
    {
        try { _http?.Stop(); } catch { }
        _queue = new AutoBazaarActionQueue(desiredTimeoutMs);
        var endpointPath = System.IO.Path.Combine(GetLogRoot(), "endpoint.json");
        _http = new AutoBazaarHttpServer(desiredPort, endpointPath, () => _snapshots.Current, _queue);
        try { _http.Start(); _currentPort = desiredPort; }
        catch (System.Exception ex) { BppLog.Error("AutoBazaar", $"listener failed on port {desiredPort}", ex); _http = null; _queue = null; _currentPort = -1; }
    }
}
```

- [ ] **Step 2 — In `Update()`, call `ReconcileListener()` first.** Then on enabled+listening, do tick (build snapshot, drain queue, dispatch).

```csharp
ReconcileListener();
if (_http is null || _queue is null) return;

// existing snapshot publish path...

var pending = _queue.TryDequeue();
if (pending is null) return;

ProcessPending(pending, snap);
```

- [ ] **Step 3 — Implement `ProcessPending`:** validate → if invalid, log + respond with error envelope (status from validation); if ok, dispatch + log + respond 200. Always log.

```csharp
private void ProcessPending(PendingAction pending, AutoBazaarContextSnapshot snap)
{
    var action = pending.Action;
    var cooldownLeft = ComputeCooldownLeft();
    var validation = AutoBazaarActionValidator.Validate(snap, action, cooldownLeft);
    if (validation.Code != AutoBazaarValidationCode.Ok)
    {
        var err = BuildErrorEnvelope(validation);
        pending.SetResponse(new AutoBazaarServerResponse(validation.HttpStatus, err));
        LogDecision(action, snap, decisionId: AutoBazaarUlid.New(), executed: false, error: validation.Code.ToString().ToLowerInvariant());
        return;
    }
    var result = AutoBazaarActionDispatcher.Execute(action, snap);
    var did = AutoBazaarUlid.New();
    if (result.Executed && action.ActionKind != AutoBazaarActionKind.Wait)
        _lastActionTime = Time.unscaledTime;
    var body = JsonConvert.SerializeObject(new {
        schemaVersion = "1.0.0",
        decisionId = did,
        executed = result.Executed,
        tickId = snap.TickId,
        actionKind = action.ActionKind.ToString(),
    }, /* configured settings */ );
    pending.SetResponse(new AutoBazaarServerResponse(result.Executed ? 200 : 500, body));
    LogDecision(action, snap, did, result.Executed, result.Error);
}
```

- [ ] **Step 4 — `OnDestroy`:** stop server, delete endpoint.json.

```csharp
private void OnDestroy()
{
    try { _http?.Stop(); } catch { }
    _http = null; _queue = null;
}
```

- [ ] **Step 5 — Build, smoke.** Game running with `Enabled=true`:
  - `cat <gameRoot>/BazaarPlusPlus/AutoBazaar/endpoint.json` → shows port + pid
  - `curl http://127.0.0.1:47900/v1/context` → 200 + JSON
  - `curl -X POST -H "Content-Type: application/json" -d "{\"actionKind\":\"Wait\"}" http://127.0.0.1:47900/v1/actions` → 200 with `executed:true`
  - Flip `Enabled=false` in cfg, wait < 1 s → endpoint.json gone, port closed
  - Flip back → endpoint.json reappears, port re-listens

- [ ] **Step 6 — Commit.**

```powershell
git add Game/AutoBazaar/AutoBazaarRuntime.cs
git commit -m "Wire HTTP server lifecycle into AutoBazaarRuntime"
```

### Task T4.5: End-to-end smoke checklist

**Files:** none (manual).

- [ ] Start a fresh run via `POST {"actionKind":"StartOrContinueRun","hero":"Karnok"}` from hero-select scene → confirm `tickId` advances, `StateName` transitions to `Choice`/`StartRun`.
- [ ] In `Choice`, `POST {"actionKind":"Reroll"}` → confirm `selectionOptions` swap on next `GET`.
- [ ] In `Choice` with at least one offered item, pick the first `SelectItem` from `availableActions` → `POST` it verbatim → confirm `BoardItems` includes the item on next `GET`.
- [ ] Hit `POST {"actionKind":"Wait"}` repeatedly through combat → confirm `StateName` cycles `Combat → Replay → Loot/Choice`.
- [ ] At end of run, `GET` shows `StateName=EndRunDefeat`/`Victory` → `POST {"actionKind":"AdvanceEndRun"}` → confirm return to lobby.
- [ ] No exceptions in BepInEx console throughout.

---

## Phase 5 — Docs

### Task T5.1: API reference doc

**Files:**
- Create: `docs/reference/auto-bazaar-http-api-v1.md`

- [ ] **Step 1 — Extract Goal §7 (entire HTTP API v1), §8 (action set), §13 (decision log).** Reformat into reference style (no "we will", just declarative). Include:
  - Discovery (endpoint.json) + bind / loopback / port
  - Versioning (URL major + body minor)
  - GET /v1/context (request, response, ETag, status codes, body schema)
  - POST /v1/actions (request body, response body, error envelope, status codes)
  - Action set table with parameters + when-it-appears + downstream `Cmd.*` (annotate **C1**: `SelectItem` takes sockets+section)
  - Decision log format

- [ ] **Step 2 — Commit.**

```powershell
git add docs/reference/auto-bazaar-http-api-v1.md
git commit -m "Document AutoBazaar HTTP API v1"
```

### Task T5.2: Internal decision surface doc

**Files:**
- Create: `docs/reference/auto-bazaar-decision-surface.md`

- [ ] Document every field on `AutoBazaarContext`, `AutoBazaarCardSnapshot`, `AutoBazaarDecisionOption` with its derivation path (which game-state property feeds it). This is internal — mention it's a companion to the API reference, not a separate spec.

- [ ] Commit.

### Task T5.3: One-bullet mentions

**Files:**
- Modify: `docs/mod-features-overview.md`
- Modify: `README.md`
- Modify: `README_en.md`

- [ ] Per `.rules`: docs minimal. One bullet each, linking to `auto-bazaar-http-api-v1.md`.

- [ ] Commit.

---

## Self-review (do this when plan is "complete")

When all tasks are checked off:

1. **Spec coverage:** walk Goal §3 (Scope In/Out) → each "In" item maps to one task. Each "Out" item is **not** implemented (confirm no rogue features crept in).
2. **Switch completeness:** every `AutoBazaarActionKind` value has a dispatcher branch and a validator-recognized path.
3. **Threading invariant:** grep the codebase for `Cmd.GetInstance` and `HttpGameClient`. Every call site is reachable only from `AutoBazaarRuntime.Update()` (and helpers it calls synchronously).
4. **Config invariant:** flipping `Enabled` cfg toggle stops the listener and deletes `endpoint.json`. Flipping `HttpListenerPort` restarts on the new port. Both within `ListenerReconcileDebounceSeconds` * 2.
5. **`.rules` compliance:** no `BuildAll` chains for code-only changes; no source-text tests; no mock call sequence tests; `decompiled/` untouched; Debug copy flow in `BazaarPlusPlus.csproj` untouched; no rewritten READMEs beyond the one-bullet mention.

---

## Open questions to escalate (do not implement without resolution)

1. **C1 — Schema for `SelectItem`:** confirm with user that `availableActions` enumerates placements per offered item exactly like `MoveItem` (this plan assumes yes).
2. **C3 — Hero/playMode write path for `StartOrContinueRun`:** decision depends on `T0.3` finding. Surface back to user if no clean writer exists.
3. **Run-id source:** the Goal mentions `runId: "uuid-or-null"` on context but doesn't pin the field source. Confirm in `T0.7` whether it's `Data.Run.Id`, `appState.RunId`, etc. — keep the field nullable until confirmed.

---

*End of plan.*
