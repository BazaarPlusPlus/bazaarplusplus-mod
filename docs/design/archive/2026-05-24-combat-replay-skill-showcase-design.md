> **Status: ASPIRATIONAL - never implemented.** The showcase sidecar / installer renderer were not built. Path refs use the stale BazaarPlusPlus/ dir (now BazaarPlusPlusV4/).

# Combat Replay Skill Showcase (Intro + Outro Template)

**Status:** Draft for implementation planning
**Date:** 2026-05-24
**Owner:** BazaarPlusPlus mod + bazaarplusplus-installer

## 1. Background

### 1.1 Combat Replay videos today

The mod can record saved combat replays as MP4 files. See [docs/combat-replay-video-recording.md](../../features/combat-replay.md). Pipeline:

- [CombatReplayVideoRecorder](../../../Game/CombatReplay/Video/CombatReplayVideoRecorder.cs) subscribes to `CombatReplayPlaybackStarting` / `Ended`.
- [ReplayVideoCaptureSession](../../../Game/CombatReplay/Video/ReplayVideoCaptureSession.cs) drives frame capture via `AsyncGPUReadback`.
- [FfmpegRawVideoEncoder](../../../Game/CombatReplay/Video/FfmpegRawVideoEncoder.cs) pipes raw RGBA frames to a child `ffmpeg` process.
- Output: `<GameRoot>/BazaarPlusPlus/CombatReplayVideos/<yyyy-MM-dd>/<battle_id>.<yyyyMMdd-HHmmss>.mp4`.

The video records only the Unity Game View during replay. There is no template, branding, or build summary baked into it.

### 1.2 Battle data already available

Each recorded replay has a [PvpBattleManifest](../../../Game/PvpBattles/PvpBattleManifest.cs) in memory at record time:

- `BattleId`, `RunId`, `RecordedAtUtc`, `Day`, `Hour`, `EncounterId`, `CombatKind`
- [PvpBattleParticipants](../../../Game/PvpBattles/PvpBattleParticipants.cs): player & opponent name / hero / rank / rating / level
- [PvpBattleOutcome](../../../Game/PvpBattles/PvpBattleOutcome.cs): `Result`, `WinnerCombatantId`, `LoserCombatantId`
- [PvpBattleSnapshots](../../../Game/PvpBattles/PvpBattleSnapshots.cs): `PlayerHand`, `PlayerSkills`, `OpponentHand`, `OpponentSkills`, each an ordered list of [CombatReplayCardSnapshot](../../../Game/CombatReplay/CombatReplayCardSnapshot.cs) with `Name`, `Tier` (`ETier` = Bronze/Silver/Gold/Diamond/Legendary), `Enchant`, `Tags`, `Attributes`.

The mod also persists the same payload to `<battle>.payload.mpack.gz` and to the SQLite `battles` / `battle_snapshots` tables.

### 1.3 Existing supporter catalog

[CardSetPreviewSponsorCatalog](../../../Game/MonsterPreview/CardSetPreviewSponsorCatalog.cs) already maintains the BPP supporter list:

- Fetches `https://bpp-static.bazaarplusplus.com/supporter-list.json` (public CDN, no auth) with a 1-hour TTL.
- Disk-caches to `<TempPath>/BazaarPlusPlus/supporter-list-cache.json`.
- Schema: `[{ "name": string, "tier": int }, ...]` where `tier` is `2 = Bronze`, `3 = Silver`, `4 = Gold`.
- Picks one supporter with tier-weighted random sampling (Gold weight 6, Silver 4, Bronze 2) for the MonsterPreview "Supported by X" overlay.
- Falls back to a hardcoded placeholder list if both the network fetch and the disk cache fail.

The showcase feature reuses this catalog rather than introducing a parallel sponsor source.

### 1.4 The gap

Recorded replays are raw gameplay footage. There is no:

- Branding ("Presented by BazaarPlusPlus") in the artifact.
- Build summary that lets a viewer understand the player's skill loadout at a glance.
- Supporter visibility — the supporter list shows in MonsterPreview during gameplay but never in shareable video output.
- Creator surface — there is no UI for the player to find, browse, or export their recorded battles after the fact.

## 2. Goals

1. Each recorded replay is accompanied by a small, stable, self-contained JSON sidecar that describes everything a "showcase intro/outro" template needs to render.
2. Players can open the [bazaarplusplus-installer](../../../../bazaarplusplus-installer/) and find a Battle History page listing their recorded replays.
3. From the Battle History page, the player can click "Export" on any row and receive `<battle>.final.mp4` with a data-driven intro (player's skill loadout) and outro (battle duration + Presented by + sponsors) bookending the original replay.
4. Sponsor names in the outro come from the same `supporter-list.json` already used in-game — no new sponsor pipeline.
5. The mod artifact does not grow. All renderer code lives in the Installer.

## 3. Non-goals

- No in-game UI for the showcase. The mod writes the sidecar and stops. All export UX is in the Installer.
- No live overlay during the replay itself. The skill showcase is only at intro and outro.
- No per-battle sponsor configuration. Sponsors come from the existing catalog with a tier-weighted random pick at sidecar-write time.
- No audio in the intro/outro in v1. The replay middle segment is also silent today (see combat-replay-video-recording.md Phase 4).
- No template variants in v1 (one intro layout, one outro layout). Multi-template support is deferred until the single template ships.
- No batch re-export. The Installer exports one battle per click in v1.
- No server-side rendering. Rendering happens client-side via a Tauri sidecar binary.
- No backfill of pre-existing replays that recorded before this feature shipped. Only replays recorded after the sidecar writer lands get a sidecar; older replays still appear in the list but show no Export button (Status: "No sidecar").

## 4. Architecture

Three independent units. Mod runtime, Installer (Tauri), supporter CDN.

```
┌──────────────────────────────────────────────┐
│ Mod  (BazaarPlusPlus.dll, BepInEx plugin)    │
│  ┌──────────────────────────────────────┐    │
│  │ CombatReplayVideoRecorder            │    │
│  │   ↓ (on successful MP4 write)        │    │
│  │ CombatReplayShowcaseSidecarWriter ───┼── writes <battle>.<ts>.showcase.json
│  │   ↓ (uses)                           │    │
│  │ CardSetPreviewSponsorCatalog.PickN(3)│←── fetches supporter-list.json (existing, 1h TTL)
│  └──────────────────────────────────────┘    │
└──────────────────────────────────────────────┘
                       │ files on disk
                       ▼
       <GameRoot>/BazaarPlusPlus/CombatReplayVideos/<yyyy-MM-dd>/
         <battle>.<ts>.mp4              (raw replay, unchanged)
         <battle>.<ts>.showcase.json    (NEW sidecar)

┌──────────────────────────────────────────────┐
│ Installer  (bazaarplusplus-installer, Tauri) │
│  ┌──────────────────────────────────────┐    │
│  │ Battle History page (Svelte)         │    │
│  │   - lists rows by reading sidecars   │    │
│  │   - "Export" button per row          │    │
│  └──────────────────────────────────────┘    │
│  ┌──────────────────────────────────────┐    │
│  │ Tauri command: export_showcase       │    │
│  │   - spawns showcase-renderer sidecar │    │
│  │   - spawns ffmpeg sidecar for concat │    │
│  └──────────────────────────────────────┘    │
│  ┌──────────────────────────────────────┐    │
│  │ showcase-renderer (Tauri sidecar)    │    │
│  │   = Node + Remotion bundle           │    │
│  │   inputs:  --sidecar <path>          │    │
│  │            --width / --height / --fps│    │
│  │   outputs: intro.mp4 + outro.mp4     │    │
│  └──────────────────────────────────────┘    │
└──────────────────────────────────────────────┘
                       │
                       ▼
                <battle>.<ts>.final.mp4  (intro + raw + outro, concat in-place)
```

Key properties:

- **Mod and Installer share zero process state.** They communicate exclusively through files on disk (`<battle>.mp4`, `<battle>.showcase.json`).
- **Sidecar is self-contained.** Once written, the showcase can be rendered offline; no network access required at export time.
- **Re-export is idempotent.** Same sidecar + same renderer = same `final.mp4` modulo ffmpeg/Remotion version drift.
- **Failure domains are independent.** Sidecar write failure must not block the MP4 write. Sidecar absence must not block listing other rows.

## 5. Mod-side design

### 5.1 New components

| File | Responsibility |
|---|---|
| `Game/CombatReplay/Video/CombatReplayShowcaseSidecarWriter.cs` | Project `PvpBattleManifest` + recording metadata → `showcase.json`. Pure mapping + sponsor pick + file write. |
| `Game/CombatReplay/Video/CombatReplayShowcaseSidecar.cs` | DTOs (`ShowcaseSidecarRoot`, `ShowcasePlayer`, `ShowcaseOpponentSummary`, `ShowcaseSkill`, `ShowcaseSponsor`) with `[JsonProperty]` snake_case names. |

### 5.2 Modified components

| File | Change |
|---|---|
| `Game/CombatReplay/Video/CombatReplayVideoRecorder.cs` | On the successful-MP4 branch (after `File.Move(.recording.mp4 → final mp4 path)`), call `CombatReplayShowcaseSidecarWriter.Write(...)` in a try/catch. Failure logs Warn and continues; never bubbles to recorder. |
| `Game/MonsterPreview/CardSetPreviewSponsorCatalog.cs` | Add `public static IReadOnlyList<SupporterEntry> PickN(int count)`. Reuses `EnsureRefreshScheduled` / `GetCurrentEntries` / `PickWeighted`. Picks `count` without replacement (remove the selected entry from the candidate list each iteration). Returns at most `count` entries; returns fewer if the catalog has fewer than `count` renderable entries. The current `SupporterEntry` is `private sealed`; promote it to `public sealed` so external callers (the sidecar writer) can consume the return type without duplication. |
| `Core/Config/IBppConfig.cs` + `Core/Config/BppConfig.cs` | Add `CombatReplayShowcaseSidecarEnabled` (default `true`). |
| `Core/Paths/BppPathService.cs` | No changes — sidecar lives next to the MP4 in the existing `CombatReplayVideos/<date>/` directory, which is already a known path. |

### 5.3 Sidecar JSON schema v1

```jsonc
{
  "schema_version": 1,

  "battle_id": "abc123",
  "run_id": "run-xyz",                                // nullable
  "recorded_at_utc": "2026-05-24T12:34:56Z",
  "video_relative_path": "2026-05-24/abc123.20260524-123456.mp4",
  "video_width": 1920,
  "video_height": 1080,
  "video_fps": 30,
  "duration_ms": 462000,                              // nullable until recorder knows

  "outcome": "Win",                                   // nullable, mirrors PvpBattleOutcome.Result

  "player": {
    "name": "PlayerName",
    "hero": "Vanessa",                                // nullable
    "rank": "Diamond",                                // nullable
    "rating": 2340,                                   // nullable
    "level": 8,                                       // nullable
    "day": 14                                         // nullable
  },

  "opponent_summary": {                               // ONLY for the Installer's list UI;
                                                      //   intro/outro template MUST NOT use these fields.
    "name": "FlameLord",                              // nullable
    "hero": "Mak"                                     // nullable
  },

  "skills": [                                         // sorted ascending by ETier ordinal
                                                      //   (Bronze < Silver < Gold < Diamond < Legendary).
                                                      //   Ties broken by source order from PlayerSkills.Items.
    {
      "template_id": "...",
      "name": "Whirlwind",
      "tier": "Bronze",                               // ETier name as string
      "enchant": null,                                // nullable
      "tags": ["Physical"],
      "attributes": { "DMG": 18 }
    },
    // ...
    {
      "template_id": "...",
      "name": "Inferno Strike",
      "tier": "Legendary",
      "enchant": "Golden",
      "tags": ["Fire"],
      "attributes": { "DMG": 42, "CRIT": 15 }
    }
  ],

  "sponsors": [                                       // up to 3 entries; fewer if catalog returns fewer.
                                                      //   Picked at sidecar-write time using existing
                                                      //   tier-weighted random; never re-picked.
    { "name": "Gold Sponsor A",   "tier": 4 },
    { "name": "Silver Sponsor B", "tier": 3 },
    { "name": "Bronze Sponsor A", "tier": 2 }
  ]
}
```

Mapping rules:

- `video_width` / `video_height` / `video_fps` come from `CombatReplayVideoRecorder`'s active `ReplayVideoCaptureRequest` (effective width/height after `Screen.width` fallback, and the configured fps). Bundling these into the sidecar lets the renderer match resolution without `ffprobe`.
- `duration_ms` is computed at sidecar-write time: `(DateTime.UtcNow - startedAtUtc)`. If the value is not yet known (early failure path before any frames were encoded), write `null`.
- `outcome` is `manifest.Outcome.Result` verbatim. The Installer interprets `"Win"` / `"Loss"` for visual treatment; any other value is shown as-is.
- `player.day` is `manifest.Day` if non-null; otherwise omitted (`null`).
- `opponent_summary` exists strictly for the Battle History list display. The intro/outro template must not consume it. This boundary is enforced by code review, not by schema (single sidecar file is simpler than splitting list metadata from template data).
- `skills`: source is `manifest.Snapshots.PlayerSkills.Items`. Mapping is field-by-field. Sort: `Array.Sort` by `(int)ETier` ascending, stable on `Items` index. Cards with `Tier == null` or unparseable are placed at the start (treated as below Bronze) and tagged `"tier": null` in the sidecar so the template can render them with a neutral border.
- `sponsors` is `CardSetPreviewSponsorCatalog.PickN(3)`. If `PickN` returns 0 entries, the field is `[]`. The renderer's outro must handle 0-3 gracefully.

Schema versioning: `schema_version: 1` is the only valid value in v1. The renderer rejects any other value with a clear error and falls back to "raw replay only" rendering.

### 5.4 Sidecar write path & atomicity

- Write to `<battle>.<ts>.showcase.json.tmp`, then `File.Move` to the final name.
- If the rename fails (cross-volume rename, permission), fall back to write-then-delete-tmp. Log a Warn either way.
- Do not block the recorder on disk I/O — perform sidecar write synchronously after `final.mp4` is in place, but inside the recorder's existing background completion path, not on the Unity main thread.

### 5.5 Failure semantics

| Failure | Behavior |
|---|---|
| `CardSetPreviewSponsorCatalog` returns 0 entries (network down, no cache, fallback exhausted) | `sponsors: []` in sidecar. Outro renders without sponsor cards. |
| Sidecar write throws | Recorder logs Warn with battle_id and exception summary. MP4 is still valid. |
| Sidecar file is corrupted later (manual edit, partial write that escaped the tmp+rename guard) | Installer skips that row and shows "Sidecar unreadable" in the list. |
| `CombatReplayShowcaseSidecarEnabled = false` | Recorder skips the sidecar write entirely. Installer's Battle History page shows the row as "No sidecar" with no Export button. |

## 6. Installer-side design

This section describes the *contract* the Installer must satisfy. The Installer is a separate repo (`bazaarplusplus-installer`, Tauri + Rust + Svelte); detailed Rust/Svelte file layout is out of scope for this mod-repo spec and will be planned in its own design doc under that repo. What's pinned here is the public surface area Installer must expose so the mod side can verify compatibility.

### 6.1 Battle History page

A new sidebar entry "Battle History". When opened:

1. Resolve `<GameRoot>` (the Installer already knows this).
2. Glob `CombatReplayVideos/**/*.showcase.json`.
3. For each sidecar, parse and render a row. Sort by `recorded_at_utc` descending.
4. Each row shows: `Recorded` (local time), `Opponent` (from `opponent_summary.name`), `Hero` (`player.hero`), `Day` (`player.day`), `Result` (pill, `outcome`), `Duration` (`duration_ms` mm:ss), `Status` (Raw / Exported / Unreadable), and per-row actions.
5. Per-row actions:
   - `Export` (Raw status only) → triggers the export pipeline.
   - `Re-export` (Exported status only) → overwrites `final.mp4`.
   - `Open folder` → reveal raw mp4 in OS file manager.
   - `Open final.mp4` (Exported status only) → open with default video player.
6. The "Status" column compares the mtime of `<battle>.<ts>.final.mp4` against the sidecar; presence of the file = Exported.

Orphaned MP4s with no sidecar (replays recorded before this feature shipped) appear in the list with `Status: No sidecar` and no Export button. Rows whose sidecar parses but is invalid (wrong schema_version, malformed) appear with `Status: Unreadable` and a hover tooltip explaining the reason.

### 6.2 Export pipeline

The Installer ships a Tauri sidecar binary `showcase-renderer` (Node + Remotion bundled via `node-sea` or `pkg`, one binary per target triple). On Export:

1. Read the sidecar JSON.
2. Validate `schema_version == 1`. On mismatch, surface an error toast and stop.
3. Spawn `showcase-renderer` with:
   - `--sidecar <path>` (it reads JSON directly)
   - `--intro-out <tmpdir>/intro.mp4`
   - `--outro-out <tmpdir>/outro.mp4`
4. `showcase-renderer` renders both segments at `(video_width, video_height, video_fps)` from the sidecar.
5. Spawn `ffmpeg` (Installer already ships ffmpeg) to concat using the `concat` demuxer with stream copy (no re-encode). Reject and fall back to `concat` filter with re-encode if codec/parameter mismatch is detected.
6. Output: `<battle>.<ts>.final.mp4` next to the raw MP4.
7. Clean up `<tmpdir>/intro.mp4` and `<tmpdir>/outro.mp4`.
8. Refresh the row's Status.

Progress UI: a small top-of-page progress bar with `Rendering intro... → Rendering outro... → Concatenating... → Done`. No per-frame progress in v1.

### 6.3 Renderer CLI contract

`showcase-renderer` is the only piece that the mod design pins about the Installer:

```bash
showcase-renderer \
  --sidecar /path/to/<battle>.showcase.json \
  --intro-out /path/to/intro.mp4 \
  --outro-out /path/to/outro.mp4
```

Exit codes: `0` success, `1` argument error, `2` sidecar unreadable, `3` schema_version mismatch, `4` render failure. Errors write a JSON object to stdout with `{ "error": "...", "details": "..." }` for the Installer to surface.

The renderer treats the sidecar as the single source of truth. It does not accept overrides for tier ordering, sponsor count, or card durations from the CLI in v1 — the sidecar fixes those.

## 7. Template design

### 7.1 Card-stream rotator

Both intro and outro use the same React composition: a sequence of cards, each shown for `cardSeconds` (default `2.0s` = 60 frames at 30fps), with a fade-in (0.3s) → hold (1.4s) → fade-out (0.3s) curve and a 5-dot progress indicator at the bottom.

Header on every card: `player.name` (left, bold) and a `DAY <day>` badge (right, pill, lighter weight).

`durationInFrames` is computed at render time: `cards.length * cardSeconds * fps`.

### 7.2 Intro composition

Cards array: `skills` from sidecar, in the order they appear (already tier-sorted ascending by the mod). Each card renders:

- Tier tag (top-left, colored by ETier: Bronze → copper, Silver → gray, Gold → yellow, Diamond → cyan-blue, Legendary → orange-red).
- Skill name (large, bold).
- Enchant (italic, subdued; "—" if null).
- Up to 3 attributes from `attributes`, taken in insertion order from the JSON object (renderer must preserve key order when parsing).
- Up to 3 tags (chip row, in source order from the sidecar).
- Card border + outer glow keyed to the same tier color, intensifying for higher tiers (Legendary has the strongest glow as the closing payoff).

Intro duration = `skills.length * 2.0s`. No upper cap. A loadout with 8 skills produces a 16s intro; 12 skills produces 24s.

### 7.3 Outro composition

Cards array, in order:

1. **Battle duration card** — large `mm:ss` from `duration_ms`, label "BATTLE DURATION".
2. **Presented by card** — "PRESENTED BY" label + branded `BazaarPlusPlus` wordmark with gradient fill + small subtitle ("Combat Replay Tools" or similar; pin exact subtitle text in the renderer repo's `constants.ts`).
3. **Sponsors card(s)** — one card per sponsor in `sponsors[]`. Each card label is "SPONSORED BY" with the sponsor name centered, and the card border uses the sponsor's tier color (same palette as skill tiers). If `sponsors` is empty, this section is skipped entirely.

Outro duration = `(1 + 1 + sponsors.length) * 2.0s`, ranging from `4s` (empty sponsors) to `10s` (full 3 sponsors).

Result pill (`outcome`) is not a standalone card in v1 — it surfaces only on the duration card as a colored pill in the corner (`outcome == "Win"` → green, `outcome == "Loss"` → red, anything else → neutral gray). This keeps the outro short and matches the "data card" minimalism direction chosen in brainstorming.

### 7.4 Resolution & framerate

The renderer reads `video_width`, `video_height`, `video_fps` from the sidecar and renders intro/outro at exactly those values. Concat with the raw MP4 then uses stream copy.

If the recorded MP4's actual encoding parameters drifted from the sidecar's recorded values (e.g., the recorder downscaled mid-session for some reason — currently it cannot, but defensive), the Installer falls back to `ffmpeg concat filter` with re-encode using libx264 CRF 23 preset veryfast (matching the recorder's defaults from [combat-replay-video-recording.md](../../features/combat-replay.md) §3.6).

## 8. File structure summary

### 8.1 Mod (this repo)

| File | Status |
|---|---|
| `Game/CombatReplay/Video/CombatReplayShowcaseSidecar.cs` | New (DTOs) |
| `Game/CombatReplay/Video/CombatReplayShowcaseSidecarWriter.cs` | New (writer service) |
| `Game/CombatReplay/Video/CombatReplayVideoRecorder.cs` | Modified (call writer on success) |
| `Game/MonsterPreview/CardSetPreviewSponsorCatalog.cs` | Modified (add `PickN(count)`, expose `SupporterEntry` as needed) |
| `Core/Config/IBppConfig.cs` | Modified (`CombatReplayShowcaseSidecarEnabled`) |
| `Core/Config/BppConfig.cs` | Modified (same) |
| `tests/BazaarPlusPlus.Tests/CombatReplay/Video/CombatReplayShowcaseSidecarWriterTests.cs` | New (manifest → JSON mapping, tier sort, sponsor injection, write atomicity smoke test) |
| `tests/BazaarPlusPlus.Tests/MonsterPreview/CardSetPreviewSponsorCatalogTests.cs` | New or extended (`PickN` returns ≤ N unique entries; tier-weighted; respects fallback) |

### 8.2 Installer (separate repo)

Planned as a sibling design doc under `bazaarplusplus-installer`. Out of scope for the mod-side spec — but the public contract pinned here is:

- Tauri sidecar binary `showcase-renderer` matching the CLI contract in §6.3.
- ffmpeg sidecar (already shipped by Installer for the mod's tools deployment).
- New Svelte route under `src/routes/battle-history/+page.svelte`.
- New Tauri command `export_showcase(sidecar_path: string)`.

## 9. Validation

### 9.1 Mod-side automated tests

- `CombatReplayShowcaseSidecarWriterTests`:
  - Manifest with 5 skills across tiers → JSON has skills ordered ascending by tier.
  - Manifest with `Outcome.Result = "Win"` → JSON `outcome == "Win"`.
  - Sponsor catalog returns 5 → JSON has 3 unique entries (`PickN(3)` output, no duplicates).
  - Sponsor catalog returns 0 → JSON `sponsors == []`.
  - File contents match the schema_version 1 contract (key names snake_case, nullable fields are `null` not absent).
  - Tmp-then-rename works even when the target file already exists (overwrite semantics).
- `CardSetPreviewSponsorCatalogTests`:
  - `PickN(3)` from a 5-entry catalog returns 3 unique entries.
  - `PickN(10)` from a 5-entry catalog returns 5 entries (graceful truncation).
  - `PickN(0)` returns empty.
  - Existing `PickDisplay` test still passes (no regression to current MonsterPreview behavior).

These do not require running Unity (existing test project conventions; see [combat-replay-sfx-impl.md](2026-05-23-combat-replay-sfx-impl.md) for the pattern).

### 9.2 Mod-side manual checks

| Check | Method | Pass condition |
|---|---|---|
| Sidecar appears | Replay one PvP battle with `CombatReplayVideoEnabled = true` | `<battle>.<ts>.showcase.json` exists alongside the MP4 |
| Sidecar shape | Open the JSON in an editor | Matches §5.3, all required fields present, snake_case |
| Tier order | Check `skills` array | Ascending by `Bronze < Silver < Gold < Diamond < Legendary` |
| Sponsor injection | Check `sponsors` array | 0-3 entries, each `{ name, tier }` |
| Toggle off | Set `CombatReplayShowcaseSidecarEnabled = false`, replay | No `.showcase.json` written; MP4 still produced |
| Sponsor catalog offline | Disable network, clear `<TempPath>/BazaarPlusPlus/supporter-list-cache.json`, replay | Sidecar written with `sponsors: []` (or the hardcoded fallback list per `CardSetPreviewSponsorCatalog.FallbackEntries`); recorder does not block |
| Recorder unaffected on sidecar failure | Make sidecar dir read-only mid-recording (chmod) | MP4 is still produced; Warn log line on sidecar write failure |

### 9.3 Installer-side validation (deferred to Installer spec)

Out of scope for this mod-repo spec. The Installer's design doc will own:

- Battle History page list correctness against various sidecar permutations.
- Export pipeline end-to-end (renderer + ffmpeg concat).
- Cross-platform binary sidecar deployment.

The mod side validates only that the sidecar contract is correct.

## 10. Risks and rollback

| Risk | Mitigation |
|---|---|
| `CardSetPreviewSponsorCatalog` internals change shape (currently `private sealed SupporterEntry`) and the sidecar writer breaks | Expose `SupporterEntry` as `public sealed` (preferred) or duplicate as a public DTO; either way pin the contract in tests so a future refactor surfaces the break. |
| MessagePack DTO `internal`/`public` trap (per `.rules`) | Sidecar uses `Newtonsoft.Json`, not MessagePack — `internal` DTOs work fine. Keep `public` only on the DTOs in `CombatReplayShowcaseSidecar.cs` if any future serializer change is anticipated, to match the existing rule. |
| Sidecar grows past a sensible size | Skills are bounded by the player's actual loadout (rarely >12). Attributes per card are bounded by game design (typically 2-5 keys). The sidecar should stay <8KB even for outlier builds. No size limit enforced in v1. |
| Replay recorder receives `Outcome.Result == null` (some encounter types might not set it) | Sidecar `outcome` is nullable; Installer list shows "—" and the result pill is gray. |
| Disk full during sidecar write | Tmp + rename catches partial writes. Warn log; no recovery attempt. |
| User manually edits the sidecar | Renderer's `schema_version` check + best-effort JSON parse means a bad edit produces "Unreadable" status in the list, not a crash. |
| Installer ships with an older Remotion bundle than the sidecar's schema_version expects | Renderer exit code 3 with a clear message; Installer surfaces "Please update BazaarPlusPlus Installer". |

**Rollback path:** `CombatReplayShowcaseSidecarEnabled = false` stops new sidecar writes. Existing sidecars remain on disk and remain readable. The Installer's Battle History page still works on whatever sidecars exist; rows with no sidecar simply show "No sidecar". No code deletion required for rollback.

## 11. Open items for the Installer spec

These belong to the Installer's own design doc, not this one:

- Exact bundling strategy for `showcase-renderer` (node-sea vs pkg vs custom).
- Cross-platform sidecar binary distribution (macOS arm64/x64, Windows x64).
- Progress reporting protocol between renderer and Installer (stdout JSON-lines vs Tauri channels).
- Battle History list virtualization for users with hundreds of recordings.
- Re-export overwrite confirmation UX.
- Locale handling in the outro text strings ("BATTLE DURATION", "PRESENTED BY", "SPONSORED BY").
