# Drive external battle video recording through primitive replay-control HTTP endpoints

External tools batch-record battle replays to mp4 through three primitives on the existing
BazaarAgent loopback server (`127.0.0.1:47900`): `POST /v1/replay/record` (raw
`GhostBattlePayload` msgpack+gzip in, playback + recording starts), `GET /v1/context`
(`replayPhase`/`replayBattleId` poll), and `POST /v1/replay/continue` (drives the replay
"continue" button). The mod holds no batch state machine; an external script orchestrates the
serial loop. Design history with the decompiled-source evidence:
[archive/2026-06-07-bazaaragent-external-battle-video-recording-design.md](../archive/design/archive/2026-06-07-bazaaragent-external-battle-video-recording-design.md).

## Context

Replay video recording already existed as a manual HistoryPanel feature
(`CombatReplayRuntime.ReplayImportedBattle(manifest, payload, recordVideo)` plus the
event-bus-driven `CombatReplayVideoRecorder`), but had no programmatic entry. Decompilation
established the key lifecycle fact: when a replay finishes, `ReplayState.Replay()` only shows the
Replay/Recap/Continue buttons and flips `IsReplaying=false` — **the state never exits on its
own**, and the recorder only finalizes the mp4 (moov atom) when `ReplayState.Exit()` runs. The
host's former tick plumbing (`BazaarAgentUiPlumbing.TryAdvanceReplay`) auto-called `Exit()` as
soon as playback finished, which would have raced any external orchestration and erased the
observable `finishedAwaitingContinue` window.

## Decision

1. **Primitive facade on the main mod, not a feature in the agent.** Combat replay stays entirely
   in `BazaarPlusPlus.dll`. Alongside the ADR-0006 probe facade, the mod publishes
   `IBazaarAgentReplayRecorder` via `BazaarAgentGameBridge.CurrentRecorder` — three methods whose
   signatures carry only primitives (`byte[]`/`string`/enums/result structs), so GameInterop stays
   free of `Game.*` imports. The implementation is a delegate holder; the composition root
   supplies delegates that decode the `GhostBattlePayload`, cross-check battle ids, run the
   recording guards, and call the internal `CombatReplayRuntime` — reading the runtime lazily,
   because the facade is published before the runtime is attached.
2. **Explicit continue sink is the only programmatic ReplayState exit.** The tick auto-exit was
   deleted (file removed, probe's `IsReplayStartInProgress` with it). The single allowed exit is
   `CombatReplayRuntime.TryContinueReplay`, which mirrors the native continue click (LevelUp
   recap cleanup, then `Exit()`) and rejects unless the replay actually sits at
   `finishedAwaitingContinue`. An architecture test pins both sides: no `.Exit()` call exists in
   the host assembly, and `replay.Exit()` appears in exactly one main-mod file. Exit timing —
   and therefore the recording finalize point and any video tail — belongs to the caller.
3. **Pure-core binary route.** The agent core gains a second command queue
   (`BazaarAgentReplayControlQueue`, same TCS+timeout shape as the action queue, wider 10 s
   accept window) and two routes that move raw bytes — no JSON parsing, no base64, their own
   32 MB cap (provisional pending p99 measurement). The controller drains replay commands at the
   top of every `Tick()`, before the 1.5 s snapshot gate, so accepts are not throttled to the
   snapshot cadence. Outcomes map onto the existing error-code table (202/200, 400 `invalid`,
   409 `stale-or-unavailable`, 503 `unavailable`) — no new codes.
4. **Sync-accept (202) + external polling, one mp4 per battle.** Recording completion is not
   observable from the runtime, so `record` answers 202 ("recording started") and the caller
   polls `replayPhase` then the output file
   (`CombatReplayVideos/<date>/<battleId>.<stamp>.mp4`). Guards are checked up front (replay
   can run + async GPU readback + FFmpeg resolvable + video directory set) precisely because the
   recorder bails silently when any fail — a 202 must never be issued for a session that cannot
   produce a file. Batch concatenation is external (`ffmpeg concat`).

## Consequences

- The active replay battle id moved from `CombatReplayController` (set only on the local-saved
  path, dead for imported ghosts) to the playback session
  (`ReplayPlaybackPublisher.ActiveSessionBattleId`), fixing `replayBattleId` for HTTP-imported
  battles.
- AutoBazaar run loops no longer get a free replay auto-advance: after a PvP combat replay the
  game waits at the continue button until something drives it (`POST /v1/replay/continue`, an
  `ExitState` action, or the player). External drivers must handle the `Replay` state
  explicitly.
- `BazaarAgentContext` grows `replayPhase`/`replayBattleId` (additive, schema-minor); the phase
  enum serializes camelCase on the wire, unlike the other PascalCase enums — pinned by test.
- The wire contract reference is
  [reference/bazaar-agent-http-api-v1.md](../archive/reference/bazaar-agent-http-api-v1.md); the
  recording flow contract (poll → continue → poll file) lives there.
