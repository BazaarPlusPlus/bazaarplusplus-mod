# The Bazaar Agent Guide

This document gives an action-taking agent enough context to play **The Bazaar** through
BazaarPlusPlus's optional BazaarAgent host. It describes the current V3 protocol in this
repository. The game and the host remain authoritative: do not infer an action is legal solely
from this guide.

## What The Bazaar Is

The Bazaar is an asynchronous auto-battler and economic build game. A run alternates between
non-combat encounters and automatic battles:

- Choose a hero and start a run.
- Visit encounters such as merchants, trainers, events, loot, level-up rewards, and pedestals.
- Spend gold to acquire items and skills, then arrange items on the active board or in the chest.
- Build interactions around the card text: item firing cadence, ammo, damage, healing, shields,
  status effects, triggers, skills, and hero mechanics all matter.
- Battles resolve automatically from the assembled build. The next encounter presents choices
  after a battle. `wins`, `losses`, `prestige`, health, day, and hour are exposed as live state.

The winning strategy is contextual. Prefer the current card descriptions, board topology,
economy, and available operations over a fixed card tier list. Do not assume a constant win
target, loss limit, encounter order, or shop offer pool; inspect the current context every time.

## Board Vocabulary

- **Board**: the active 10-slot combat row. Its occupied slots are sent as integer indexes
  `0` through `9`. An item occupies a contiguous number of slots based on its `size`.
- **Chest**: off-board storage. It also has capacity, but V3 deliberately does not expose its
  lock map. When moving or buying into the chest, the host chooses the first fitting placement.
- **Item**: a board/chest card. Its `description` is the native, runtime-valued tooltip text;
  use it as the source for damage, triggers, and special effects.
- **Skill**: a persistent hero card. It has no board position.
- **Selection**: the current offer row. It can contain items, skills, encounters, or an owned
  item that must be selected for a pedestal/target-selection flow.
- **Pedestal**: an encounter that upgrades or enchants an existing item. In a target-selection
  state, choose the owned item offered in `selection`; do not try to buy another offer.

Only `cooldownSeconds` greater than zero is emitted. `ammoMax` is emitted only when positive;
when it is present, `ammo` is the current count and may be zero. `sellPrice` is emitted for
sellable item cards. The protocol intentionally does not send raw attribute dictionaries,
ability graphs, or per-card `canSelect`, `canFit`, `free`, and `canSell` flags — including in
battle lineups. Battle cards use the same complete compact-card shape as ordinary cards.

## Starting The Host

BazaarAgent is optional. Build and deploy it with:

```bash
./run.sh build --with-bazaaragent
```

Start The Bazaar through Steam, not by launching the app bundle directly. The host then listens
only on `http://127.0.0.1:47900`. It exposes a local dashboard at
`http://127.0.0.1:47900/` and writes runtime logs to the game's `BepInEx/LogOutput.log`.

On macOS, a copied DLL is not hot-reloaded. Fully quit the game and launch it again through Steam
after a build before testing the new host. Confirm `agent.host.initialized` and
`agent.listener.started` in `LogOutput.log` before treating the endpoint as available.

## Context Protocol

The two gameplay endpoints are:

```text
GET  /v3/context
POST /v3/actions
```

There are no V1/V2 gameplay routes and no `/v3/cards/query` route. Card mechanics are supplied
in the compact card `description`, so an agent should retain and merge its local card cache rather
than request full cards again.

### Bootstrap and Delta Loop

1. Bootstrap with `GET /v3/context` and **no** BazaarAgent session/revision headers.
2. Store the returned `X-Bazaar-Agent-Session` header and JSON `revision`. A bootstrap response
   has `"full": true` and replaces the entire local state/card cache.
3. For every later context request, send both headers:

   ```text
   X-Bazaar-Agent-Session: <stored session>
   X-Bazaar-Agent-Revision: <stored revision>
   ```

4. Merge the delta, then replace the stored revision with the response's `revision`.
5. Send an action only against that exact current revision. Its successful response contains
   `status` and `next`; merge `next` exactly like a context response before considering another
   action.

The server rejects a partial, unknown, or stale session/revision with
`409 {"error":"resync-required"}`. Discard the local cache and bootstrap again. Do not retry
the same action blindly: inspect the new state first.

Use one serialized request/action loop per session. Concurrent polls or actions can make a valid
revision stale before the other request arrives.

### Merging a Context

`board`, `chest`, and `skills` are independently optional delta groups. A group has:

```json
{
  "upsert": [{ "id": "i00001", "description": "..." }],
  "remove": ["i00002"]
}
```

- `remove` deletes the cached card by session-scoped short ID.
- An ordinary `upsert` patches only the fields present on the cached card. It normally contains
  only `id` plus changed fields, so absent `template` or `name` means **unchanged**, not empty.
- An `upsert` with `"replace": true` replaces that cached card in full; fields absent from this
  replacement are cleared.
- On a full snapshot, discard all cached groups before applying its upserts.
- `state`, `operations`, `lockedBoardSlots`, and `battle` are also deltas. A missing property
  means unchanged. `battleCleared: true` removes the cached battle boundary.

`selection` is different: every Mod-to-Agent context contains the complete current offer row as
an array of complete cards (or `[]` when no offer is present). It is never a delta group and must
replace, rather than merge with, the agent's previous selection.

Short IDs (`i00001`, `s00001`, `e00001`) are scoped to the session. Never reuse them after a
bootstrap/resync.

### Combat Boundaries

Combat does not stream. At the start of `Combat` or `PvpCombat`, the host emits one `battle`
object with `phase: "starting"`, `battleType`, and complete `player` / `opponent` lineups. Each
lineup contains the full `board` and `skills` membership. Cards whose templates are supplied by
the game carry the ordinary compact-card fields (`id`, `template`, `name`, `slots` where
applicable, tier, tags, native description, cooldown, and ammo where applicable).
Skills have neither `slots` nor `size`.

The host publishes no further revisions or deltas during the combat itself. A poll therefore
returns the existing revision until the battle has finished. If a short battle starts and ends
between polls, the next two context responses still arrive in order: the retained `"starting"`
boundary first, then the completed boundary. The first post-combat actionable context replaces
`battle` with `phase: "completed"` and the same complete lineups plus `result` and
combatant-level health / max-health / shield / burn / poison start-end values. It is the single
post-combat decision snapshot. `battleCleared: true` is emitted only after the agent's next
confirmed action acknowledges that completed boundary.

## Choosing Actions

`operations` is the authoritative list of operation names currently worth attempting. If an
operation is absent, do not issue it. `busy: true` means the native client is transitioning or
waiting; poll again rather than guessing a click.

| `op` | Required fields | Meaning |
| --- | --- | --- |
| `start` | `hero`, optionally `mode`, `revision` | Start or continue a run when advertised. |
| `select` | `id`, and for an item offer `target`/possibly `slot`, `revision` | Choose an encounter, skill, offer, or pedestal target. |
| `move` | `id`, `target`, `slot` for board, `revision` | Move an owned board/chest item. |
| `sell` | `id`, `revision` | Sell an owned item. |
| `reroll` | `revision` | Reroll the current selection when advertised. |
| `exit` | `revision` | Leave the current native state when advertised. |
| `continue` | `revision` | Advance an eligible continuation/replay state. |
| `menu` | `revision` | Return to the menu when advertised. |

All actions are JSON POSTs to `/v3/actions` and require the session header. Example, choosing an
encounter or skill:

```json
{ "op": "select", "id": "e00003", "revision": 42 }
```

For an item offer or an owned item move, placement is explicit:

```json
{ "op": "select", "id": "i00004", "target": "board", "slot": 3, "revision": 42 }
{ "op": "move", "id": "i00001", "target": "chest", "revision": 42 }
```

`target: "board"` requires `slot`; the slot is the item's first occupied index and must leave
room for its full size within 0-9 without intersecting `lockedBoardSlots` or another item.
`target: "chest"` must omit `slot`; the host picks a legal chest placement. The host rechecks
availability, board fit, native interaction gates, and the exact revision. A `409` action failure
with `error: "resync-required"` means resync. A `409` with `error: "stale-or-unavailable"`
means the requested operation is no longer legal; fetch/inspect the current context before trying
a different move.

## A Practical Play Loop

1. Bootstrap and merge the full context.
2. If state is `StartRun`, select a hero/mode only when `operations` includes `start`.
3. In `Choice`, `Encounter`, `Loot`, or `LevelUp`, evaluate `selection` descriptions and prices.
   Choose one concrete option or reroll only when the expected improvement is worth its current
   cost and the operation is advertised.
4. When acquiring or rearranging an item, use `size`, existing board slots, and
   `lockedBoardSlots` before requesting a board placement. Prefer the chest when no legal board
   fit exists or when preserving a coherent firing/trigger order matters.
5. In `Pedestal` or target-selection states, select the eligible owned item; use its description
   and the encounter context to decide which effect benefits the current build.
6. On a `battle.phase: "starting"` boundary, assess the complete two-sided lineup, then wait;
   combat itself produces no context changes and no operations. On the `"completed"` boundary,
   incorporate the result and combatant-level changes before choosing the next action.
7. After every action, merge `next`, then repeat from the current context. Resync only when the
   response explicitly says `resync-required`; otherwise inspect current state before choosing
   again.

## Operational Boundaries

- The agent controls only the local host. It does not inject Unity clicks, access remote APIs, or
  bypass the game's native input gates.
- Never treat a successful HTTP enqueue as proof that the game changed. Use the returned `next`
  context or a subsequent delta to verify the intended state transition.
- Do not log or send the full card cache repeatedly. Preserve session state and apply deltas.
- The dashboard is observational. It is useful for diagnosing protocol traffic but is not a
  source of authority over the gameplay endpoint.
- For runtime debugging, read `BepInEx/LogOutput.log`; BazaarAgent log lines start with
  `[BazaarAgent]` and mod lines with `[BPP]`.

For implementation details, see `docs/ARCHITECTURE.md`,
`src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs`, and
`src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentProtocolV3Projector.cs`.
