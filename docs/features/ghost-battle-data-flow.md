# Ghost Battle Data Flow

End-to-end trace of how a "ghost battle" (someone else fought my mirror) gets
recorded on their client, stored on the server, synced back to my client, and
finally rendered in the History Panel. Source-of-truth files are linked at each
hop so the semantics of `player_*` vs `opponent_*` can be verified layer by
layer.

## Participant Terminology

- **Uploader** — the player who actually sat down and played a PvP match. Their
  own client records and uploads the battle.
- **Local player / me** — the player querying the Ghost Panel. In a ghost
  battle row, I am the one whose mirror (ghost) the uploader fought.

From my point of view, a ghost battle is always "some uploader fought me". From
the uploader's point of view, the same row is just a normal PvP win/loss.

## 1. Recording (Uploader's Client)

File: [`Game/PvpBattles/PvpBattleSnapshotCollector.cs`](../../Game/PvpBattles/PvpBattleSnapshotCollector.cs)

The recorder writes the participant block from the uploader's perspective:

- `PlayerName`, `PlayerAccountId` — the uploader themself (via
  `TryGetPlayerNameSafe` / `TryGetPlayerAccountIdSafe`).
- `OpponentName`, `OpponentAccountId`, `OpponentHero`, etc. — whoever the
  uploader was matched against, captured from the spawn message.

Combat messages (`spawn`, `combat`, `despawn`) are captured from the uploader's
session, so physics/board sides are encoded relative to the uploader as
"player".

## 2. Upload (Uploader → Server)

File: [`bazaarplusplus-server/src/features/runBundles/upload.ts`](../../../bazaarplusplus-server/src/features/runBundles/upload.ts)

The server inserts rows into the `battles` table with no perspective rewriting:

```
INSERT INTO battles (
  ...
  player_account_id,             -- uploader (from request body, NOT NULL in V4)
  player_name, player_hero, player_rank, player_rating, player_level,
  opponent_account_id,           -- whoever the uploader fought
  opponent_name, opponent_hero, opponent_rank, opponent_rating, opponent_level,
  result,                        -- from uploader's POV ("Win" = uploader won)
  is_final_battle,               -- sticky boolean: once 1, MAX() keeps it 1
  ...
)
```

V4 dropped the V3 `player_account_id_in_payload` reconciliation column (server now just trusts the uploader id from the envelope) and dropped `replay_available` (it was always written as `true` in V3 — a true dead field).

**Invariant after this step**: every row on the server holds the uploader's
view. `player_*` is the uploader, `opponent_*` is whoever they fought.
`is_final_battle` is a sticky fact set by the uploader (V3 had the redundant
`is_bundle_final_` prefix); the upsert uses `MAX(battles.is_final_battle,
excluded.is_final_battle)` so out-of-order retransmits cannot flip a final
battle back to non-final.

## 3. Ghost Query (Server → Local Player's Client)

File: [`bazaarplusplus-server/src/features/ghostBattles/query.ts`](../../../bazaarplusplus-server/src/features/ghostBattles/query.ts)

`GET /ghost-battles` returns rows where the **local player appears
in the `opponent` slot of somebody else's upload**:

```sql
WHERE b.opponent_account_id = ?   -- bound to ?player_account_id query param (= me)
  AND b.recorded_at_utc >= ?       -- lookback window
ORDER BY b.recorded_at_utc DESC, b.battle_id DESC
LIMIT ?
```

That filter is what gives the endpoint its "ghost battle" meaning: it surfaces
matches where my mirror was the opponent in someone else's session.

The response is **raw uploader-perspective data** — no flip yet:

- `player_*` = uploader (some other player who fought my ghost)
- `opponent_*` = me
- `result = "Win"` means the uploader won (i.e., *I lost* my mirror match)
- `is_final_battle` (V4 wire key; V3 used `is_bundle_final_battle`) marks
  whether this was the uploader's final battle in that run bundle and also
  passed the server projection gate. The mod's local C# field name remains
  `IsBundleFinalBattle` — only the wire key changed.

## 4. Import (Client Parses Response)

File: [`ModApi/Clients/GhostBattleClient.cs`](../../ModApi/Clients/GhostBattleClient.cs)
(`TryParseBattle`)

Fields are deserialized into `GhostBattleImportRecord` **1:1** — the client
does not rewrite perspective during import. The current server response does
not include `hour`, `encounter_id`, `combat_kind`, `winner_combatant_id`, or
`loser_combatant_id`, so those fields stay null/default unless a later API
version adds them.

- `PlayerName / PlayerAccountId / PlayerHero / ...` = uploader
- `OpponentName / OpponentAccountId / OpponentHero / ...` = me
- `Result` = uploader-perspective win/loss string
- `WinnerCombatantId` ∈ { `Player`, `Opponent` } — `Player` means uploader won.
- `IsBundleFinalBattle` = parsed from V4 wire key `is_final_battle`.
- `ReplayAvailable` = constant `true` (V4 server no longer ships a per-row
  flag; the V4 invariant is "battle row exists ⇒ R2 artifact exists", since
  R2.put precedes the D1 batch and orphan cleanup runs on D1 failure).

## 5. Local Persistence

File: [`Game/HistoryPanel/Storage/HistoryPanelRepository.cs`](../../Game/HistoryPanel/Storage/HistoryPanelRepository.cs)
(`UpsertGhostBattles`, `ReplaceGhostBattles`)

Ghost rows are written to the local SQLite `battles` table with
`source = 'GHOST'` and `local_player_account_id = <me>`. The `player_*` and
`opponent_*` columns continue to carry uploader-perspective values — storage
matches what the server returned.

The local SQLite still uses the column name `is_bundle_final_battle` (mod-side schema, decoupled from the V4 wire rename).

This is a deliberate choice: the repository stores facts, and perspective
translation happens at read time.

## 6. Read + Projection (Storage → UI Model)

Files:
- [`Game/HistoryPanel/Storage/HistoryPanelRepository.cs`](../../Game/HistoryPanel/Storage/HistoryPanelRepository.cs)
  (`ListRecentGhostBattles`)
- [`Game/HistoryPanel/Ghost/GhostBattleLocalProjector.cs`](../../Game/HistoryPanel/Ghost/GhostBattleLocalProjector.cs)

`ListRecentGhostBattles` pulls raw columns and hands them to
`GhostBattleLocalProjector.CreateHistoryBattleRecord`, which flips into
**local-player perspective** when building the `HistoryBattleRecord`:

| HistoryBattleRecord slot (UI semantics) | Source column (raw) | Concrete meaning in UI |
| --- | --- | --- |
| `PlayerHero / PlayerRank / PlayerRating / PlayerLevel` | `opponent_hero / rank / rating / level` | My mirror (= me) |
| `OpponentName / OpponentHero / OpponentRank / OpponentRating / OpponentLevel / OpponentAccountId` | `player_name / hero / rank / rating / level / account_id` | The uploader who fought my mirror |
| `Result` | `ProjectResultToLocal(result)` — `Win`↔`Lost`, `Won`↔`Lost` | My outcome |
| `WinnerCombatantId` | `ProjectCombatantIdToLocal(...)` — `Player`↔`Opponent` | Who won from my POV |
| `LoserCombatantId` | `ProjectCombatantIdToLocal(...)` | Who lost from my POV |
| `IsBundleFinalBattle` | `is_bundle_final_battle` (local SQLite column; mod schema independent of the V4 wire rename) | Whether this was the uploader's final bundle battle and passed the projection gate |
| `Source` | — | `HistoryBattleSource.Ghost` |
| `ReplayAvailable / ReplayDownloaded` | `replay_available / replay_downloaded` | Whether replay payload can/has been fetched |

After this projection the `HistoryBattleRecord` for a ghost source row and the
one for a locally-played row share identical UI semantics (`Player*` = me,
`Opponent*` = the other side, `Result`/`WinnerCombatantId` are from my POV), so
downstream rendering and filtering code does not special-case ghost rows.

## 7. Rendering (UI Model → Screen)

File: [`Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs`](../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs)

The history panel binds the projected record directly:

- `refs.OpponentHeroPill` ← `battle.OpponentHero` — shows the uploader's hero.
- `refs.OpponentName.text` ← `battle.OpponentName` — shows the uploader's name.
- Player-side pills bind to `battle.PlayerHero` / related — show my mirror.
- The selected-battle notice shows "opponent eliminated" only when
  `IsBundleFinalBattle` is true and the projected local-player outcome is a win.

Because projection already reframed the row into local-player perspective, the
view has no ghost-specific branching for participant display.

## 8. Replay Path (Deliberately Unflipped)

Files:
- [`Game/HistoryPanel/HistoryPanelReplayService.cs`](../../Game/HistoryPanel/HistoryPanelReplayService.cs)
  (`ReplayGhostBattleAsync`)
- [`Game/HistoryPanel/Ghost/GhostBattlePayloadStore.cs`](../../Game/HistoryPanel/Ghost/GhostBattlePayloadStore.cs)
- [`Game/HistoryPanel/Ghost/GhostBattleSyncService.cs`](../../Game/HistoryPanel/Ghost/GhostBattleSyncService.cs)
  (`DownloadReplayPayloadAsync` / `BuildBattleManifest`)

When the user presses Replay:

1. If `ReplayDownloaded` is false, the client fetches a download URL and
   downloads a run artifact, extracts the battle, and stores
   `GhostBattlePayload { BattleManifest, ReplayPayload }` on disk.
2. `runtime.ReplayImportedBattle(manifest, payload)` is invoked **with the raw
   uploader-perspective manifest**. No flip.

This is intentional: the replay message stream (`spawn` / `combat` / `despawn`)
is recorded against the uploader as `ECombatantId.Player`. Flipping just the
manifest names would desync manifest labels from the actual board sides, so the
replay viewer presents the uploader as "Player" on the board and my mirror as
"Opponent".

Consequence: **list view** shows me on the Player side, **replay board** shows
me on the Opponent side. Both are correct within their own frame; the mismatch
is a product-level choice, not a bug.

## Summary Diagram

```
[Uploader client]
  PvpBattleSnapshotCollector
    Player=uploader, Opponent=me
        │
        ▼ upload
[Server battles table]
  player_*   = uploader
  opponent_* = me
  result     = uploader POV
  is_bundle_final_battle = raw bundle-final flag
        │
        ▼ GET /ghost-battles
        │  (WHERE opponent_account_id = me)
[Response]  raw uploader-POV rows
        │
        ▼ TryParseBattle
[GhostBattleImportRecord]  raw
        │
        ▼ UpsertGhostBattles
[Local SQLite battles (source='GHOST')]  raw
        │
        ▼ ListRecentGhostBattles
        │  + GhostBattleLocalProjector
[HistoryBattleRecord]  local-POV  ← flip happens here
   Player* = me (mirror)
   Opponent* = uploader
   Result / WinnerCombatantId from my POV
   IsBundleFinalBattle = raw bundle-final flag
        │
        ├─▶ History Panel list / filters (local-POV)
        │
        └─▶ Replay (raw manifest + payload, uploader-POV board)
```

## Invariants Worth Preserving

- Server storage and API responses stay in uploader perspective. Do not rewrite
  `player_*` / `opponent_*` in the sync or persistence path.
- Local SQLite ghost rows mirror the server response (raw). Any perspective
  logic must live in the projector.
- All ghost-specific UI / filtering / summary code consumes projected
  `HistoryBattleRecord` values, not raw columns. If a new ghost feature reads
  raw columns directly, extend the projector instead.
- `is_bundle_final_battle` stays raw through sync/persistence/projection; combine
  it with projected local outcome only at UI decision points.
- Replay payload + manifest are forwarded untouched; do not try to "fix"
  player/opponent labels there without also remapping the combat message
  stream.
