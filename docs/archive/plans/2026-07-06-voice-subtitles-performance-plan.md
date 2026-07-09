---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at master c6829e91 (2026-07-06); P0/P1/P2 all shipped — enabled-gate on every VO entry point, indexed CatalogSnapshot, Verbose call-site logging, ConcurrentQueue display, volatile+Stopwatch bridge, scanner GameObject.Find cheap-probe + 5s backoff.

# Voice Subtitles Performance Optimization Plan

Date: 2026-07-06
Status: confirmed by user — implement all three priorities (P0 + P1 + P2) in one working branch.
Executor: implementation agent. A separate review pass happens BEFORE merge — commit to a feature
branch off `master`, do NOT merge or push to `master` yourself.

## Background

The VoiceSubtitles feature (BazaarLine integration) shows bilingual VO subtitles. The pipeline is:

- Harmony patches on `VOPlayer` (`src/BazaarPlusPlus/Patches/VoiceSubtitles/VOPlayerPatches.cs`)
- Interop bridge `VoiceLineVoObserverBridge` (`src/BazaarPlusPlus/GameInterop/VoiceSubtitles/VoiceLineVoObserverBridge.cs`)
- Catalog + display in `src/BazaarPlusPlus/Game/VoiceSubtitles/`
- Main-thread handoff via `VoiceLineDisplay.QueueShow` → `VoiceLineDisplayDispatcher.Update`

Threading facts (verified against decompiled game source):

- `VOPlayer.PlayVO` invokes `VODebugPrint` on the **main thread** (`decompiled/TheBazaarRuntime/VOPlayer.cs:168`),
  so `VoiceLineVoObserverBridge.OnVoDebugPrint` runs synchronously inside `PlayVO`.
- `StaticEventCallback → OnVOStopInternal` is a `[MonoPInvokeCallback]` invoked on the **FMOD callback
  thread** (`decompiled/TheBazaarRuntime/VOPlayer.cs:172-188`), so `OnVoSoundPlayed` /
  `OnVoPlaybackStopped` run off the main thread.
- The transpiler in `VOPlayerPatches.cs:55-105` widens the callback mask to `STOPPED | SOUND_PLAYED`.
  It is one-time, harmless, and stays unchanged.

The feature default is OFF (`src/BazaarPlusPlus/Core/Config/BppConfig.cs:73-78`), and the catalog has
5032 lines (`src/BazaarPlusPlus/Data/VoiceSubtitles/voice-lines.json`).

## Problems (evidence)

1. **Disabled users pay full cost.** `IsEnabled()` is checked only AFTER resolution and logging:
   `VoiceLineVoObserverBridge.cs:213` (after `ResolveLine` at :182) and :313 (after `ResolveLine` at
   :267). `VOPlayerPlayVOPatch.Prefix` (`VOPlayerPatches.cs:38-47`) unconditionally runs
   `CreateVoiceAttempt` (FMOD `GetEventDescription`/`getPath`/`getLength` native calls +
   `Services.Get<SoundManager>()` + a large interpolated Info log). `VersionLabelScanner.Update`
   never checks the toggle either.
2. **Catalog resolution is O(N) with ~15k allocations per lookup.** `ResolveExactDetailed`
   (`VoiceLineCatalog.cs:159-207`) calls `NormalizeVoiceStem(line.Stem)` freshly for every one of
   5032 lines on every lookup (StringBuilder + ToString + Replace each). Each VO event resolves
   **twice** (main-thread `OnVoDebugPrint` + FMOD-thread `OnVoSoundPlayed`), and the main-thread
   resolution for `origin == "PlayVO"` is pure diagnostics — it returns at
   `VoiceLineVoObserverBridge.cs:298-299` without showing anything. `ResolveCharacterHookFallback`
   (`VoiceLineCatalog.cs:240-283`) allocates `$"_{characterName}"` per line via `IsCharacterLine`.
   Unity's Boehm GC is stop-the-world, so FMOD-thread garbage still becomes main-thread pauses.
3. **`VersionLabelScanner` full-scans every 0.75 s.** `Resources.FindObjectsOfTypeAll<TextMeshProUGUI>()`
   (`VersionLabelScanner.cs:33`) walks every TMP object in memory (including inactive) and allocates
   an array each pass, forever, whenever the label is unmounted — including scenes that will never
   contain a `Version:` label, and including users with the feature disabled.
4. **Per-VO log volume.** One VO event emits 6-9 Info lines of 200-400 chars. `BppLog.Info` always
   emits (`src/BazaarPlusPlus/Infrastructure/BppLog.cs:36-37`). Note `BppLog.Debug` checks
   `BppBuild.IsDebug` *inside* the method, so call-site string interpolation is still paid in
   Release unless guarded at the call site.
5. **Cross-thread details.** `_pendingContext`/`_activeContext` are 9-field mutable static structs
   written on the main thread and read on the FMOD thread with no volatile/lock
   (`VoiceLineVoObserverBridge.cs:20-21`) — torn reads are possible.
   `AgeSeconds` reads `Time.unscaledTime` on the FMOD thread via `OnVoPlaybackStopped`
   (`VoiceLineVoObserverBridge.cs:242,392`) — Unity main-thread-only API.
6. **Idle per-frame work.** `VoiceLineOverlayLifetime.Update` keeps running after the label is
   hidden; `VoiceLineDisplayDispatcher.Update` takes a lock every frame to check an empty queue
   (`VoiceLineDisplay.cs:159-174`).

## Goals / Non-goals

Goals: near-zero cost when the feature is disabled; O(tokens) dictionary-first resolution with zero
per-line allocation when enabled; per-VO logging free in Release builds; scanner no longer
full-scanning at 0.75 s cadence indefinitely.

Non-goals: no behavior change to what subtitle text is shown for a given VO (one deliberate
exception documented below); no changes to `voice-lines.json` schema, repository download/cache flow,
fonts, layout, or settings UI; no changes to the transpiler.

---

## P0 — Gate every entry point on the enabled flag

All checks must be the FIRST thing in the code path, before any FMOD call, resolution, or log
string construction.

1. Add an `internal static bool IsSubtitleObservationEnabled()` helper on
   `VoiceLineVoObserverBridge` that returns `_callbacks.IsEnabled()` (already safe:
   `VoiceSubtitleObserverCallbacks.Empty.IsEnabled` returns `false`, see
   `VoiceSubtitleObserverCallbacks.cs:8-12`, so the bridge is inert before `VoiceSubtitlesModule.Start`
   and after `Stop`). Wrap in try/catch like the existing private `IsEnabled()`.
2. `VOPlayerPlayVOPatch.Prefix` and `VOPlayerPlayTutorialVOPatch.Prefix`: early-return when
   disabled, skipping `CreateVoiceAttempt` + `BeginVoiceAttempt` entirely. The `Postfix` clear can
   stay unconditional (it is cheap and keeps state hygiene).
3. `OnVoSoundPlayed`: `if (!enabled) return;` at the top — before promoting `_pendingContext`,
   before `ResolveSoundName`/`ResolveSoundDurationSeconds`, before any logging.
4. `OnVoDebugPrint`: same top-of-method gate.
5. `OnVoPlaybackStopped`: keep the `_activeContext = Unknown` clear unconditional; gate only the
   log line.
6. `VersionLabelScanner.Update`: after the interval check, `if (!VoiceSubtitlesGate.IsEnabled()) return;`
   before the mounted check and scan. Do NOT unmount an already-mounted label when the user
   disables mid-session — `VoiceLineDisplay.Show/QueueShow` are already gated.

Toggle semantics to preserve: enabling mid-session must self-heal — the next scanner tick (≤0.75 s)
mounts the label, the next VO event gets a subtitle. A VO already in flight when the user toggles
may be missed; that is acceptable.

## P1 — Index the catalog; kill the redundant resolution

### 1. Immutable indexed snapshot in `VoiceLineCatalog`

Replace the `(_catalogLines, _catalogName)` pair + per-call lock with a single immutable snapshot
class swapped atomically (`Volatile.Read`/`Volatile.Write` on a static reference; keep the existing
lock only for writers if convenient). Build the snapshot inside `ReplaceCatalog` — all callers are
already on background threads (`VoiceLinesRepository` warm-up and refresh), and `Reset` builds a
trivial empty snapshot.

Snapshot contents, precomputed once per catalog load:

- `Entry[] entries` where `Entry = { VoiceLine Line, string NormalizedStem }` (normalized via the
  existing `NormalizeVoiceStem`), preserving catalog order.
- `Dictionary<string, VoiceLine> exactByNormalizedStem` (`StringComparer.OrdinalIgnoreCase`,
  first occurrence wins) for token-equality hits.
- Character+hook fallback index: `Dictionary<string, CharacterHookBucket>` keyed
  `"{character}:{hookToken}"` (OrdinalIgnoreCase) where the bucket stores `Count` and the single
  `VoiceLine` when `Count == 1`. Build it in one pass over lines × (8 character aliases + 11 hook
  tokens) using the existing `IsCharacterLine` / `HookTokens` semantics — one-time cost on the
  background thread.

### 2. Resolution algorithm (`ResolveExactDetailed`)

- Tokenize `lookupText` once with a non-LINQ, low-allocation split (keep the existing separator set
  and `NormalizeVoiceStem` per token).
- **Tier 1:** for each normalized token with `Length >= 8`, try `exactByNormalizedStem` — a hit
  returns immediately with strategy `"event-stem"`.
- **Tier 2:** single linear pass over `entries` preserving the current per-line semantics —
  `lookupText.IndexOf(entry.Line.Stem, OrdinalIgnoreCase)`, else (when `entry.NormalizedStem.Length >= 8`)
  token-contains-stem checks against the precomputed `NormalizedStem` — with **zero allocations
  inside the loop**. First catalog-order match wins, exactly as today.
- Keep the `SampleLines` fallback pass as-is (11 entries, negligible).

**Deliberate behavior change (accepted):** tier 1 can return a later catalog line by exact
normalized-stem equality where today an earlier line could win via containment-only match. Exact
equality is a strictly stronger match; document this in the commit message. Everything else must be
byte-identical, and the existing test `Catalog_resolves_exact_stem_from_embedded_seed`
(`tests/VoiceSubtitles.Tests/VoiceSubtitlesTests.cs:159`) must pass unchanged.

### 3. Character-hook fallback

`ResolveCharacterHookFallback` becomes a dictionary lookup on the precomputed index. Preserve the
existing strategy strings (`"character-hook-unique"`, `"character-hook-ambiguous"`), the
`MatchedToken` format `"{character}:{token}"`, and `CandidateCount`.

### 4. `ResolveCharacterName` probes

Precompute the probe strings (`"/{alias}/"`, `"VO_{alias}_"`, `"_{alias}_"`) once in a static
array of `(string probe1, string probe2, string probe3, string canonical)` instead of interpolating
them on every call (`VoiceLineCatalog.cs:290-298`).

### 5. Drop the diagnostics-only resolution in `OnVoDebugPrint`

Restructure `OnVoDebugPrint` so the `origin == "PlayVO"` early return
(`VoiceLineVoObserverBridge.cs:298-299`) happens BEFORE `ResolveLine`. For the PlayVO origin the
subtitle is produced later by `OnVoSoundPlayed`; the debug-callback resolution and its two Info
blocks are diagnostics only — keep at most a verbose-gated Debug line (see P2).

## P2 — Logging, scanner backoff, idle updates, threading hygiene

### 1. Logging

Add `internal static bool Verbose => BppBuild.IsDebug;` (a static readonly field is fine) to
`VoiceSubtitlesLog` and `VoiceSubtitlesInteropLog`. Convert all per-VO-event logs to
call-site-guarded Debug:

```csharp
if (VoiceSubtitlesLog.Verbose)
    VoiceSubtitlesLog.Debug("VO sound played " + ...);
```

Per-VO lines to gate: attempt begin/clear, sound played, playback stopped, debug callback deferred,
debug category resolution, subtitle skipped (no match), subtitle resolved (both variants), show
request, show-skipped-empty, label updated, lifetime start, subtitle hidden.

Keep at Info (one-time or rare): observer installed, transpiler patch count, catalog load/refresh
results, label mount, renderer selection, font diagnostics/creation. Keep ALL Warn/Error
unconditional. The point of the call-site guard: `BppLog.Debug` checks `BppBuild.IsDebug` inside
the method, so without the guard Release still pays the interpolation.

### 2. `VersionLabelScanner` cheap-probe + backoff

- Cache the full transform path (the existing `BuildPath` output) after each successful mount.
- On rescan, first try `GameObject.Find(cachedPath)`; if it yields a `TextMeshProUGUI` passing
  `IsUsableVersionLabel`, mount from it without a full scan.
- Full `Resources.FindObjectsOfTypeAll` scans must not run at 0.75 s cadence indefinitely: after 3
  consecutive full-scan misses, raise the scan interval to 5 s; reset to 0.75 s whenever a mount
  succeeds (a fresh scene transition will then re-find the label within one slow tick — acceptable,
  the subtitle label is only cosmetic-mount-latency sensitive).

### 3. Idle per-frame work

- `VoiceLineOverlayLifetime`: set `enabled = false` at the end of `Hide` and initially; set
  `enabled = true` in `ShowUntilVoiceStops`. Update loop body otherwise unchanged.
- `VoiceLineDisplay`: replace `Queue<VoiceSubtitleCue> + lock` with
  `ConcurrentQueue<VoiceSubtitleCue>`; `ProcessQueuedShows` fast-paths on `IsEmpty` without
  locking; `Reset` drains via `TryDequeue`.

### 4. Threading hygiene in the bridge

- Convert `VoiceAttemptContext` from a struct to a **sealed immutable class**; make
  `_pendingContext`/`_activeContext` `volatile` static fields (reference assignment is atomic —
  removes torn reads). Keep the `Unknown` sentinel instance and `IsKnown` semantics, or switch to
  null + null-checks; either is fine as long as all reads snapshot the field once into a local
  (the current code already does this).
- Replace `Time.unscaledTime`-based age tracking (`CreatedAtSeconds`, `AgeSeconds`) with
  `System.Diagnostics.Stopwatch.GetTimestamp()` (thread-safe, available on netstandard2.1).
  `Time.unscaledTime` in `VoiceLineOverlayLifetime` (main-thread `Update`) stays as-is.
- `CreateCue`'s `playbackStateText` closure exists only for logs — make it null when
  `!VoiceSubtitlesLog.Verbose` and have consumers fall back to `"<none>"`.

---

## Tests

Existing: `dotnet test tests/VoiceSubtitles.Tests/VoiceSubtitles.Tests.csproj` (xunit) must pass,
especially `Catalog_resolves_exact_stem_from_embedded_seed`.

Add behavioral tests to the same project (no coverage theater — each asserts resolution outcomes):

1. A lookup text whose token carries a file-extension suffix (e.g. sound name
   `"001_VanessaPvPDefeat1.wav"`) resolves to the same line as the bare stem — this pins the tier-2
   containment path.
2. Character+hook fallback: a lookup with no stem match but a `/Vanessa/` path and hook
   `OnPvPVictoryDefeat` resolves via `"character-hook-unique"` when exactly one candidate exists
   (build the fixture with `ReplaceCatalog` on a small synthetic catalog).
3. Ambiguous character+hook (two candidates) returns unresolved with strategy
   `"character-hook-ambiguous"` and `CandidateCount == 2`.
4. `ReplaceCatalog` → `Reset` → resolve returns unresolved (snapshot swap correctness).

## Verification

1. `dotnet test tests/VoiceSubtitles.Tests/VoiceSubtitles.Tests.csproj`
2. `./run.sh build` (Debug) — clean build.
3. `./run.sh format` — commit only files you touched (repo rule: keep commits scoped if csharpier
   reformats unrelated files).
4. Manual (the user runs in-game, list these in the PR/summary as the validation matrix):
   - Feature OFF (default): `LogOutput.log` shows no per-VO `[BPP][VoiceSubtitles]` lines during
     shop/combat; no scanner mount logs.
   - Feature ON: hero idle/click VO shows the bilingual subtitle and hides on VO end; toggling ON
     mid-session mounts the label within ~1 s (scanner tick); Release build log stays quiet
     per-VO.

## Executor constraints

- Branch off `master`; commit there; do NOT merge/push to `master` — a review pass happens first.
- Do not edit `decompiled/**`, `BazaarPlusPlus.csproj`, or the transpiler in `VOPlayerPatches.cs`.
- Keep layer boundaries: catalog/display/scanner logic stays in `Game/VoiceSubtitles/`, bridge in
  `GameInterop/VoiceSubtitles/`, patches in `Patches/VoiceSubtitles/`.
- Keep the log component name `"VoiceSubtitles"` and existing strategy/matched-token strings —
  tests and log tooling key off them.
- No scope widening: nothing outside the files named in this plan except the test project.
