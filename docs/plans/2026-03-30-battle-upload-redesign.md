# Battle Upload Redesign Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the split run-plus-replay upload flow with a single battle upload artifact, shrink run payloads, and remove server reliance on `summary_json`.

**Architecture:** Run uploads become run-only summaries. Battle uploads become the single source of truth for ghost query data plus replay payload storage. D1 stores directly queryable battle fields and replay object metadata; R2 stores only replay payload bodies. Ghost downloads use a distinct remote payload format and a separate local cache directory from `CombatReplays`.

**Tech Stack:** C#, Newtonsoft.Json, SQLite, Cloudflare Worker (TypeScript), D1, R2, node:test

---

### Task 1: Lock the server contract with failing tests

**Files:**
- Modify: `ModCFServer/test/uploadRun.test.ts`
- Modify: `ModCFServer/test/uploadReplay.test.ts`
- Modify: `ModCFServer/test/ghostBattles.test.ts`
- Modify: `ModCFServer/test/authValidation.test.ts`

**Steps:**
1. Add tests proving `runs/upload` accepts payloads without `events` and `pvp_battles`.
2. Replace replay upload tests with `/battles/upload` tests that require both `battle_manifest` and `replay_payload`.
3. Add ghost tests proving against-me responses no longer depend on `summary_json`.
4. Run the focused `ModCFServer` test suite and capture the expected failures.

### Task 2: Implement the new Worker battle upload pipeline

**Files:**
- Modify: `ModCFServer/src/index.ts`
- Create: `ModCFServer/src/features/uploadBattle.ts`
- Create: `ModCFServer/src/features/uploadBattlePayload.ts`
- Modify: `ModCFServer/src/features/uploadRun.ts`
- Modify: `ModCFServer/src/features/uploadRunPayload.ts`
- Modify: `ModCFServer/src/features/ghostBattles.ts`
- Modify: `ModCFServer/src/persistence/battleProjections.ts`
- Modify: `ModCFServer/src/persistence/replayUploads.ts`
- Modify: `ModCFServer/src/types/db.ts`
- Modify: `ModCFServer/migrations/0001_initial_schema.sql`
- Modify: `ModCFServer/test/helpers/mockEnv.ts`

**Steps:**
1. Introduce a `battles/upload` parser that validates consistent battle ids across the top-level payload, manifest, and replay payload.
2. Persist replay payload bytes to R2 first, then upsert the D1 `pvp_battles` row with direct display fields and replay object metadata.
3. Remove `summary_json` reads from ghost query responses and build the response body directly from stored columns.
4. Trim `runs/upload` parsing so it no longer requires `pvp_battles` and ignores `events`.
5. Run the focused `ModCFServer` tests until green.

### Task 3: Replace the client replay uploader with a battle uploader

**Files:**
- Modify: `Game/CombatReplay/Upload/BattleUploadPayload.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadSqliteStore.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadApiClient.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadRequestSigner.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadService.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadController.cs`
- Modify: `Game/RunLogging/Upload/RunUploadPayload.cs`
- Modify: `Game/RunLogging/Upload/RunUploadSqliteStore.cs`
- Modify: `Game/RunLogging/Upload/RunUploadRouting.cs`

**Steps:**
1. Change the local battle upload snapshot builder to emit a single battle artifact containing manifest and replay payload.
2. Point the uploader at `/battles/upload` and rename the registration scope/purpose from replay-specific to battle-specific.
3. Remove `pvp_battles` and `events` from run upload snapshots.
4. Build the mod project or the smallest affected test seam to verify compilation.

### Task 4: Introduce a dedicated ghost payload cache and import path

**Files:**
- Create: `Game/HistoryPanel/GhostBattlePayload.cs`
- Create: `Game/HistoryPanel/GhostBattlePayloadStore.cs`
- Modify: `Core/Paths/IPathService.cs`
- Modify: `Core/Paths/BppPathService.cs`
- Modify: `Game/HistoryPanel/GhostBattleApiClient.cs`
- Modify: `Game/HistoryPanel/GhostBattleSyncService.cs`
- Modify: `Game/HistoryPanel/HistoryPanelReplayService.cs`

**Steps:**
1. Define the remote ghost battle payload type that contains both manifest and replay payload.
2. Save downloaded ghost payloads to a new sibling cache directory instead of `CombatReplays`.
3. Convert a cached ghost payload into the local replay inputs just before replay starts.
4. Run the smallest relevant build/test verification again.

### Task 5: Verify the end-to-end migration

**Files:**
- Reuse files above

**Steps:**
1. Run the focused `ModCFServer` tests.
2. Run the smallest meaningful .NET build covering the touched game code.
3. Review for remaining `summary_json` and `/battles/upload` migration dependencies.
