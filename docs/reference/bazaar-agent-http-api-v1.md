# BazaarAgent HTTP API v1

> **Status: optional host plugin.** The host is a separate, optional BepInEx plugin (`BazaarPlusPlus.BazaarAgentHost.dll`, declaring `[BepInDependency(BazaarPlusPlus)]`). Default mod builds ship only `BazaarPlusPlus.dll` and actively scrub the host dlls; build the host on demand with `./run.sh build --with-bazaaragent-host`. Once installed, set `[BazaarAgent] Enabled = true` in `BepInEx/config/BazaarPlusPlus.BazaarAgent.cfg` to start the loopback HTTP server. Field names (`stateName`, `availableActions`, `actionKind`, `cardInstanceId`, `targetSection`, `targetSockets`, `reason`) are stable wire contracts; do not rename. Field-by-field derivation lives in the companion [bazaar-agent-decision-surface.md](bazaar-agent-decision-surface.md).

The BazaarAgent HTTP API exposes the current game state and accepts one action at a time, acting as pure transport and validation. All strategy, persistence, and training logic belong to external tools; the mod makes no decisions itself. The endpoint runs on loopback and is reachable at `http://127.0.0.1:<port>/v1/`.

---

## 1. Overview

BazaarAgent hosts a loopback HTTP server that publishes a versioned snapshot of the current game context (`GET /v1/context`) and accepts action submissions (`POST /v1/actions`). The mod validates each action against the live snapshot, dispatches it to the game on the Unity main thread, and logs the result. External agents own all decision logic.

---

## 2. Binding and request limits

The listener binds to `127.0.0.1:<port>` only. The default port is `47900`; it is configurable via the `HttpListenerPort` cfg entry under section `BazaarAgent`. The port is config-driven and fixed at startup — there is no discovery file.

All POST requests are subject to a 64 KB body cap. Requests whose declared `Content-Length` header exceeds 65536 bytes are rejected immediately with `413` before the body is read. Requests without a declared length are read up to 65537 bytes and rejected if that limit is reached.

---

## 3. Versioning

The major API version is encoded in the URL path (`/v1`). A bump to `/v2` signals breaking changes to the request/response envelope. The body field `schemaVersion` follows semver; minor bumps (e.g. `1.2.0`) signal contract changes — added fields, added or removed enum values, or added or removed `actionKind` values — that clients SHOULD inspect when targeting a specific minor version.

**Client guarantees (required):**

- Ignore unknown fields in any response body.
- Treat unknown enum values as opaque; safe behavior is to skip the corresponding option.
- Use `schemaVersion` comparisons to opt into optional fields added in later minor versions.

**Server guarantees:**

- Enum parsing is case-insensitive. `"karnok"` and `"Karnok"` are equivalent.

---

## 4. Endpoints

### GET /v1/context

Returns the current decision context snapshot.

**Optional request header:** `If-None-Match: "<tickId>"` — enables conditional fetch; the value must be the `ETag` string from a prior `200` response.

| Status | When | Body |
|---|---|---|
| 200 | Snapshot available and ETag mismatch (or no `If-None-Match` sent) | Full context JSON; `ETag: "<tickId>"` response header set |
| 304 | `If-None-Match` matches current ETag | (no body) |
| 503 | Listener not yet started, or no snapshot built yet | `{"error":"unavailable"}` |

**Response body (HTTP 200) — `BazaarAgentContext`:**

Top-level scalar fields:

| Field | Type | Description |
|---|---|---|
| `schemaVersion` | string | Semver string, e.g. `"1.2.0"` |
| `tickId` | uint64 | Monotonically increasing counter; resets to 1 when the listener restarts |
| `serverTimeUtc` | string | ISO-8601 UTC timestamp of snapshot build; excluded from ETag fingerprint |
| `isEnabled` | bool | Whether BazaarAgent is active |
| `isInRun` | bool | Whether the game is inside an active run |
| `hasActiveRun` | bool | Whether a run exists (may be ended) |
| `canStartOrContinueRun` | bool | True when the game is on the hero-select scene with the player profile loaded and no active `AppState`. Covers both fresh-run and resume-run cases — the server distinguishes via the player profile. |
| `isClientBusy` | bool | Whether the game client is busy (e.g. waiting for server) |
| `runId` | string\|null | Run identifier from the game; null when no run is active |
| `stateName` | string | Current run state; see enum reference |
| `playerHero` | string\|null | Current player hero |
| `day` | int\|null | Current run day |
| `hour` | int\|null | Current run hour |
| `wins` | int\|null | Current run win count |
| `losses` | int\|null | Current run loss count |
| `playerGold` | int | Current gold amount |
| `playerIncome` | int\|null | Current income |
| `playerHealth` | int\|null | Current health |
| `playerMaxHealth` | int\|null | Current max health |
| `playerPrestige` | int\|null | Current prestige |
| `playerLevel` | int\|null | Current level |
| `selectionIsFree` | bool | Whether the current selection costs no gold |
| `canExit` | bool | Whether `ExitState` is legal |
| `canReroll` | bool | Whether `Reroll` is legal |
| `rerollCost` | int | Gold cost for next reroll |
| `rerollsRemaining` | int | Rerolls available in this state |
| `currentEncounterId` | string\|null | Template ID of the active encounter card, if any |
| `currentEncounterType` | string\|null | Runtime template type for the active encounter when it can be resolved from live entities |
| `actionCooldownRemainingSeconds` | float64 | Seconds until the cooldown gate clears; 0 when no gate is active |
| `interactableTemplateIds` | string[] \| omitted | When present and non-empty, the game is in target-selection mode (upgrade/enchant). Clients pick from `availableActions` `SelectItem` options targeting owned board/chest item cards whose templateId is in this set. See "Target-selection mode" section. |

Card list fields (each element is an `BazaarAgentCardSnapshot`):

| Field | Description |
|---|---|
| `boardItems` | Items currently in the player's board slots |
| `chestItems` | Items in chest/stash |
| `playerSkills` | Active skill cards |
| `sellableItems` | Items currently eligible for sale |
| `selectionOptions` | Cards being offered for selection this state |

`availableActions` is a list of `BazaarAgentDecisionOption` objects describing every legal action at this tick. Clients should derive all POST parameters from this list.

**`BazaarAgentCardSnapshot` fields:**

| Field | Type | Present for | Description |
|---|---|---|---|
| `instanceId` | string | all | Unique card instance identifier |
| `kind` | string | all | `Item`, `Skill`, `Encounter`, `Unknown` |
| `type` | string\|null | all | Raw game `ECardType` name |
| `templateId` | string\|null | all | Card template identifier |
| `displayName` | string\|null | all | Human-readable card name |
| `tier` | string\|null | all | Card tier |
| `size` | string\|null | all | Card size |
| `enchantment` | string\|null | item | Item enchantment name, when present |
| `socketId` | string\|null | all | Socket the card occupies, if any |
| `location` | string | all | `Selection`, `Board`, `Chest`, `Skill`, `Unknown` |
| `order` | int | all | Display order |
| `tags` | string[] | all | Sorted card tag names |
| `hiddenTags` | string[] | all | Sorted hidden tag names |
| `attributes` | object | all | Card attributes as `{ attributeName: integerValue }`, sorted by attribute name at build time |
| `activeAbilities` | object[] | all | Active ability summaries. Each object may include `id`, `internalName`, `internalDescription`, `trigger`, `action`, `activeIn`, `worksIn`, and `priority`. |
| `buyPrice` | int\|null | selection | Gold cost to purchase |
| `sellPrice` | int\|null | selection | Gold received on sale |
| `canAfford` | bool\|null | selection | Whether the player can afford this card |
| `canFit` | bool\|null | selection | Whether the card fits in available board space |
| `canSelect` | bool\|null | selection | Whether selection is currently allowed |
| `isFree` | bool\|null | selection | Whether the card is free this offer |
| `targetSection` | string\|null | selection | Advisory placement hint |
| `targetSockets` | string\|null | selection | Comma-joined socket hint (informational only; clients must POST `targetSockets` from `availableActions`, not this field) |
| `unavailableReason` | string\|null | selection | Reason selection is blocked, if applicable |
| `canSell` | bool\|null | owned | Whether this card can be sold |

**`BazaarAgentDecisionOption` fields:**

| Field | Type | Description |
|---|---|---|
| `actionKind` | string | The action kind this option permits |
| `group` | string | Action group |
| `displayKey` | string | Display label key |
| `cardInstanceId` | string\|null | Card instance this option applies to |
| `targetSection` | string\|null | Placement section for card-bearing actions |
| `targetSockets` | string[]\|null | Ordered socket list; must be posted verbatim |
| `card` | object\|null | Embedded card snapshot |

---

### POST /v1/actions

Submits one action. The call blocks on the HTTP thread until the mod's Unity main thread dequeues and executes the action, or until the fixed 3 s endpoint timeout elapses (which returns `503`).

**Request body — `BazaarAgentAction`:**

| Field | Type | Required | Description |
|---|---|---|---|
| `actionKind` | string | yes | One of the 11 action kinds |
| `cardInstanceId` | string | for card-bearing kinds | Instance ID of the card to act on |
| `targetSection` | string | for `SelectItem`, `MoveItem` | Placement section: `Hand` or `Stash` |
| `targetSockets` | string[] | for `SelectItem`, `MoveItem` | Ordered socket list; must match an option from `availableActions` exactly |
| `hero` | string | no | Hero name for `StartOrContinueRun` |
| `playMode` | string | no | Play mode for `StartOrContinueRun` |
| `reason` | string | no | Free text; written to decision log |
| `forTickId` | uint64 | strongly recommended | Server returns `409` if the snapshot tickId does not match |

Card-bearing kinds are: `SelectItem`, `SelectSkill`, `SelectEncounter`, `CommitToPedestal`, `MoveItem`, `SellItem`.

**Success response (HTTP 200):**

```jsonc
{
  "schemaVersion": "1.2.0",
  "decisionId": "01HXYZ...",
  "executed": true,
  "tickId": 12346,
  "actionKind": "MoveItem"
}
```

`executed: true` means the mod dispatched the action to the game. It does not guarantee server-side acceptance; the game processes the dispatch asynchronously.

`Wait` is a legal no-op useful for keepalive or log markers. `Wait` does not consume or reset the cooldown gate.

**Error envelope (any non-2xx):**

```jsonc
{"error": "<code>", "details": "<human>", "extra": {}}
```

`extra` is omitted when empty. `details` is omitted when null.

| code | HTTP status | `extra` fields |
|---|---|---|
| `invalid` | 400 or 413 | — |
| `stale-or-unavailable` | 409 | `currentTickId` (uint64) |
| `cooldown` | 429 | `retryAfterSeconds` (float64) |
| `unavailable` | 503 | — |
| `not-found` | 404 | — |
| `internal` | 500 | — |

Future error codes are possible. Clients must tolerate unknown codes: log the response and apply a backoff before retrying.

---

## 5. Validation rules

Rules are applied in order. The first failure terminates validation and the error is returned. Rules 1–6, including 5a/5b, and rule 8 run before dispatch; rule 7 (`forTickId`) runs between rule 6 and rule 8.

| # | Name | Trigger | Failure: status / code / extra |
|---|---|---|---|
| 1 | Known actionKind | `actionKind` value not defined in the enum | 400 `invalid` |
| 2 | Kind in availableActions | `actionKind` not present in any `availableActions` entry (`Wait` is always exempt) | 409 `stale-or-unavailable` + `currentTickId` |
| 3 | Card-bearing exact match | For card-bearing kinds: no `availableActions` entry matches `(cardInstanceId, targetSection, targetSockets)` exactly | 409 `stale-or-unavailable` + `currentTickId` |
| 4 | Hero / PlayMode valid | `hero` supplied but not a known hero name; or `playMode` supplied but not a known play mode (applies to `StartOrContinueRun` only) | 400 `invalid` |
| 5 | CanSelect not false | Matched option's card has `canSelect == false` (applies to `SelectItem`, `SelectSkill`, `SelectEncounter`, `CommitToPedestal`) | 409 `stale-or-unavailable` |
| 5a | CanAfford not false | Matched option's card has `canAfford == false` (applies to `SelectItem`, `SelectSkill`) | 409 `stale-or-unavailable` |
| 5b | CanFit not false | Matched option's card has `canFit == false` (applies to `SelectItem`) | 409 `stale-or-unavailable` |
| 6 | CanSell true | Matched option's card does not have `canSell == true` (applies to `SellItem`) | 409 `stale-or-unavailable` |
| 7 | forTickId match | `forTickId` supplied but does not equal current snapshot `tickId` | 409 `stale-or-unavailable` + `currentTickId` |
| 8 | Cooldown gate | `cooldownRemainingSeconds > 0` for any non-`Wait` action | 429 `cooldown` + `retryAfterSeconds` |

---

## 6. Action set

`SelectItem` and `MoveItem` carry placement — clients must POST `targetSection` and `targetSockets` copied verbatim from the matching `availableActions` entry. The mod enumerates every legal placement for each offered or owned item across all valid sections.

`MoveItem` does not include swap-with-occupied semantics. Moving a card into an occupied socket requires multiple sequential steps.

| ActionKind | Group | Required params | Appears when | Dispatches |
|---|---|---|---|---|
| `Wait` | `Wait` | — | always | (no-op) |
| `StartOrContinueRun` | `Flow` | `hero?`, `playMode?` | hero-select scene with profile loaded and no active `AppState` | `GameInstance.Instance.StartNewRun()` |
| `AbandonRun` | `Flow` | — | in-run, not in combat / replay / end-run | `AppState.CurrentState.AbandonRunCommand()` via reflection |
| `SelectItem` | `Offer` | `cardInstanceId`, `targetSection`, `targetSockets` | item offered, `SelectItem` allowed | `AppState.CurrentState.BuyItemCommand(card, sockets, section)` via reflection |
| `SelectSkill` | `Offer` | `cardInstanceId` | skill offered, `SelectSkill` allowed | `AppState.CurrentState.SelectSkillCommand(skill)` via reflection |
| `SelectEncounter` | `Offer` | `cardInstanceId` | encounter offered, `SelectEncounter` allowed | `AppState.CurrentState.SelectEncounterCommand(instanceId)` via reflection |
| `CommitToPedestal` | `Pedestal` | `cardInstanceId` | `stateName == Pedestal`, `CommitToPedestal` allowed | `AppState.CurrentState.CommitToPedestalCommand(instanceId)` via reflection |
| `MoveItem` | `Move` | `cardInstanceId`, `targetSection`, `targetSockets` | owned item, `MoveItem` allowed | `AppState.CurrentState.MoveCardCommand(card, sockets, section)` via reflection |
| `SellItem` | `Sell` | `cardInstanceId` | sellable item, `SellItem` allowed | `AppState.CurrentState.SellCardCommand(card)` via reflection |
| `Reroll` | `Reroll` | — | `rerollsRemaining > 0` and `playerGold >= rerollCost` and `Reroll` allowed | `AppState.CurrentState.RerollCommand()` via reflection |
| `ExitState` | `Exit` | — | `canExit == true` and `ExitState` allowed | `AppState.CurrentState.ExitStateCommand()` via reflection |

---

## 7. Enum reference

**`actionKind` values:**
`Wait`, `StartOrContinueRun`, `AbandonRun`, `SelectItem`, `SelectSkill`, `SelectEncounter`, `CommitToPedestal`, `MoveItem`, `SellItem`, `Reroll`, `ExitState`

**`EHero` values:**
`Common`, `Pygmalien`, `Vanessa`, `Stelle`, `Jules`, `Dooley`, `Mak`, `Karnok`

**`EPlayMode` values:**
`Unranked`, `Ranked`

**`EContainerSocketId` values:**
`Socket_0`, `Socket_1`, `Socket_2`, `Socket_3`, `Socket_4`, `Socket_5`, `Socket_6`, `Socket_7`, `Socket_8`, `Socket_9`

**`EInventorySection` / `targetSection` values:**
`Hand`, `Stash`

**`stateName` values:**
`Unknown`, `StartRun`, `Choice`, `Encounter`, `Combat`, `PvpCombat`, `Replay`, `LevelUp`, `Loot`, `Pedestal`, `EndRunVictory`, `EndRunDefeat`

All enum values are serialized as strings. Parsing is case-insensitive on the server.

---

## 8. UI plumbing (informative)

The mod auto-handles two zero-decision UI gates so external tools do not need to POST actions for them:

- **Replay auto-advance** — when the game enters `ReplayState` and playback completes, the mod advances to the next state automatically.
- **Known overlay dismissal** — no overlays are dismissed automatically in v1. PvP first-victory tutorial dialog detection was not reliably implemented in the decompiled types.

**End-run screens are not driven by BazaarAgent.** When `stateName` is `EndRunVictory` or `EndRunDefeat`, `availableActions` contains only `Wait` — the player (or whatever drives the game UI directly) advances past the end-of-run screen, after which the next `StartOrContinueRun` becomes available at hero-select.

---

## 8.5. Target-selection mode

Certain encounters (upgrade, enchant, etc.) show a UI dialog asking the player to click one of their own cards. Internally the game populates an interaction filter (a set of templateIds) and accepts clicks on matching owned item cards; other clicks silently no-op.

While the filter is non-empty:

- `context.interactableTemplateIds` is the filter contents.
- `availableActions` does **not** include offer-based `SelectItem` options (the game would reject them).
- For each of the player's owned item cards (in `boardItems` / `chestItems`) whose `templateId` is in the filter, `availableActions` contains a `SelectItem` entry whose `cardInstanceId` is that owned item. `targetSection` and `targetSockets` reflect the card's current placement so the dispatcher's `BuyItemCommand` walks the `CanFuse()` path and the server applies the upgrade.

Client recipe: when `interactableTemplateIds` is present, pick the first `SelectItem` entry from `availableActions` and POST it verbatim. If no `SelectItem` entries exist despite a non-empty filter, none of your owned item cards match — bail with `ExitState` (where allowed) or `AbandonRun`.

---

## 9. Decision logging

Each dequeued action (executed or rejected) appends one JSONL line. The log path is:

```
<gameRoot>/BazaarPlusPlus/BazaarAgent/runs/<sanitized-runId>/decisions.jsonl
```

When `runId` is null or unavailable, the fallback path is:

```
<gameRoot>/BazaarPlusPlus/BazaarAgent/decisions.jsonl
```

`runId` is path-sanitized: all characters returned by `Path.GetInvalidFileNameChars()`, plus `.`, `/`, and `\`, are replaced with `_`. An empty result after sanitization becomes `_`.

**JSONL line schema:**

```jsonc
{
  "ts": "2026-05-17T01:23:45.678Z",
  "tickId": 12345,
  "decisionId": "01HXYZ...",
  "runId": "...",
  "state": "Choice",
  "action": {
    "actionKind": "SelectItem",
    "cardInstanceId": "item-1",
    "targetSection": "Hand",
    "targetSockets": ["Socket_0", "Socket_1"]
  },
  "executed": true,
  "error": null,
  "reason": "free-text from client"
}
```

`error` is null when the action was executed. Rejected actions record the error code string.

---

## 10. No-external-connection semantics

When `Enabled = true` but no external tool posts actions: the server listens and `GET /v1/context` returns the latest snapshot, but no actions advance game state. Replay auto-advance and any automatic UI plumbing still run; those are not decisions.

When `Enabled = false`: the listener stops, no context snapshots are built, and no decision logs are written.

Toggling `Enabled` back to `true` restarts the listener. `tickId` restarts from 1 on each listener start.

---

## 11. Cooldown gate

After every non-`Wait` action that the mod dispatched (`executed: true`), a 1.0 second minimum-delay gate is set. Subsequent non-`Wait` POST requests within that window are rejected with `429 cooldown` and `retryAfterSeconds` inside the `extra` object.

`Wait` neither consumes the gate nor resets it. The cooldown gate is independent of the snapshot tick cadence (a fixed 1.5 s).

---

## 12. Configuration

| cfg key | section | default | description |
|---|---|---|---|
| `Enabled` | `BazaarAgent` | `false` | Runtime switch. Only has an effect when the BazaarAgent host plugin is installed; edit the host cfg file, no in-game UI. |
| `HttpListenerPort` | `BazaarAgent` | `47900` | Loopback port. Changing this value restarts the listener. |

The snapshot tick cadence (`1.5 s`) and the POST blocking timeout (`3 s`) are fixed defaults, no longer configurable.
