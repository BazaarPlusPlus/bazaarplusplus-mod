# Mod API Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the split run/replay upload model with a single client identity, battle-first remote model, and new Worker schema and endpoints without legacy compatibility.

**Architecture:** The Cloudflare Worker becomes the single source of truth for client registration, player binding, run summaries, battle artifacts, ghost battle queries, and replay token issuance. The mod uses one client identity and one signed-request stack; run uploads become small summaries, while battle uploads become the only source for ghost query and replay download data.

**Tech Stack:** C# / Unity mod runtime, Cloudflare Workers, D1, R2, TypeScript, existing targeted test projects

---

## File Map

### Worker

- Modify: `ModCFServer/src/index.ts`
- Modify: `ModCFServer/src/features/registerClient.ts`
- Modify: `ModCFServer/src/features/verifiedClient.ts`
- Modify: `ModCFServer/src/types/api.ts`
- Modify: `ModCFServer/src/types/db.ts`
- Modify: `ModCFServer/src/http/request.ts`
- Modify: `ModCFServer/migrations/0001_initial_schema.sql`
- Create: `ModCFServer/src/features/bindPlayer.ts`
- Create: `ModCFServer/src/features/uploadRunSummary.ts`
- Create: `ModCFServer/src/features/uploadBattleArtifact.ts`
- Create: `ModCFServer/src/features/queryGhostBattles.ts`
- Create: `ModCFServer/src/features/createReplayLink.ts`
- Create: `ModCFServer/src/features/downloadReplay.ts`
- Create: `ModCFServer/src/persistence/playerLinks.ts`
- Create: `ModCFServer/src/persistence/runs.ts`
- Create: `ModCFServer/src/persistence/battles.ts`
- Create: `ModCFServer/src/persistence/replayTokens.ts`
- Delete: `ModCFServer/src/features/bindClient.ts`
- Delete: `ModCFServer/src/features/uploadRun.ts`
- Delete: `ModCFServer/src/features/uploadRunPayload.ts`
- Delete: `ModCFServer/src/features/uploadBattle.ts`
- Delete: `ModCFServer/src/features/uploadBattlePayload.ts`
- Delete: `ModCFServer/src/features/ghostBattles.ts`
- Delete: `ModCFServer/src/persistence/bindings.ts`
- Delete: `ModCFServer/src/persistence/runUploads.ts`
- Delete: `ModCFServer/src/persistence/battleProjections.ts`

### Mod client

- Modify: `Game/RunLogging/Upload/RunUploadDefaults.cs`
- Modify: `Game/RunLogging/Upload/BppAuthenticatedRouteClient.cs`
- Modify: `Game/RunLogging/Upload/RunUploadRegistrationClient.cs`
- Modify: `Game/RunLogging/Upload/RunUploadRequestSigner.cs`
- Modify: `Game/RunLogging/Upload/RunUploadIdentityStore.cs`
- Modify: `Game/RunLogging/Upload/RunUploadKeyStore.cs`
- Modify: `Game/RunLogging/Upload/RunUploadBootstrapContext.cs`
- Modify: `Game/RunLogging/Upload/RunUploadService.cs`
- Modify: `Game/RunLogging/Upload/RunUploadSqliteStore.cs`
- Modify: `Game/RunLogging/Upload/RunUploadController.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadService.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadSqliteStore.cs`
- Modify: `Game/CombatReplay/Upload/BattleUploadController.cs`
- Modify: `Game/HistoryPanel/GhostBattleApiClient.cs`
- Modify: `Game/HistoryPanel/GhostBattleSyncService.cs`
- Modify: `Game/HistoryPanel/HistoryPanel.Ghost.cs`
- Delete: `Game/CombatReplay/Upload/BattleUploadRequestSigner.cs`
- Delete: `Game/CombatReplay/Upload/BattleUploadApiClient.cs`
- Delete: `Game/RunLogging/Upload/RunUploadApiClient.cs`
- Delete: `Game/RunLogging/Upload/RunUploadRouting.cs`
- Create: `Game/RunLogging/Upload/ModApiRoutes.cs`
- Create: `Game/RunLogging/Upload/ModApiClientStateStore.cs`
- Create: `Game/RunLogging/Upload/SignedJsonApiClient.cs`

### Tests

- Modify: `ModCFServer/test/registerClient.test.ts`
- Modify: `ModCFServer/test/schema.test.ts`
- Replace: `ModCFServer/test/bindClient.test.ts` with `ModCFServer/test/bindPlayer.test.ts`
- Replace: `ModCFServer/test/uploadRun.test.ts` with `ModCFServer/test/uploadRunSummary.test.ts`
- Replace: `ModCFServer/test/uploadReplay.test.ts` with `ModCFServer/test/uploadBattleArtifact.test.ts`
- Modify: `ModCFServer/test/ghostBattles.test.ts`
- Add: `ModCFServer/test/replayDownload.test.ts`
- Modify: `ModCFServer/test/helpers/mockEnv.ts`
- Modify: `tests/RunUploadAuth.Tests/Program.cs`
- Modify: `tests/RunUploadSync.Tests/Program.cs`
- Modify: `tests/CombatReplayUploadSync.Tests/Program.cs`
- Modify: `tests/GhostBattleSync.Tests/Program.cs`

## Phase 1: Rebuild Worker Schema And Routes

- [ ] Replace `ModCFServer/migrations/0001_initial_schema.sql` with a clean-break schema defining only `clients`, `player_links`, `runs`, `battles`, and `replay_tokens`, plus the indexes needed for `player_account_id`, `opponent_account_id`, `run_id`, and token expiry lookups.
- [ ] Remove `purpose` from `ModCFServer/src/types/api.ts`, `ModCFServer/src/types/db.ts`, `ModCFServer/src/http/request.ts`, `ModCFServer/src/features/registerClient.ts`, and `ModCFServer/src/features/verifiedClient.ts`, so registration and verification operate on a single client identity model.
- [ ] Rewrite `ModCFServer/src/index.ts` to expose only the new endpoints: `POST /clients/register`, `POST /clients/bind-player`, `POST /runs`, `POST /battles`, `GET /players/me/ghost-battles`, `POST /players/me/ghost-battles/:battleId/replay-link`, and `GET /replays/:token`.
- [ ] Replace the old persistence modules with `clients.ts`, `playerLinks.ts`, `runs.ts`, `battles.ts`, and `replayTokens.ts`, each owning a single table and no legacy compatibility helpers.
- [ ] Delete the old feature and persistence files that encode `purpose`, projection status, or legacy battle projection responsibilities.
- [ ] Run `cd ModCFServer && npx tsx --test test/schema.test.ts test/registerClient.test.ts` and confirm the schema and registration tests pass against the new model.

## Phase 2: Implement New Worker Flows

- [ ] Implement `ModCFServer/src/features/bindPlayer.ts` so a signed client request upserts exactly one current `player_links` row per `client_id`.
- [ ] Implement `ModCFServer/src/features/uploadRunSummary.ts` so `POST /runs` accepts only run-summary fields, upserts the `runs` row, and optionally stores the summary object in R2 without projection lifecycle state.
- [ ] Implement `ModCFServer/src/features/uploadBattleArtifact.ts` so `POST /battles` validates manifest/replay consistency, uploads replay payload bytes to R2, and upserts the `battles` row as the only ghost/replay source of truth.
- [ ] Implement `ModCFServer/src/features/queryGhostBattles.ts`, `createReplayLink.ts`, and `downloadReplay.ts` so ghost query and replay download depend only on `player_links`, `battles`, and `replay_tokens`.
- [ ] Update `ModCFServer/test/helpers/mockEnv.ts` to model the new tables and remove handling for `run_uploads`, `pvp_battles`, and `purpose`.
- [ ] Replace Worker tests with the new route names and payload contracts, then run `cd ModCFServer && npx tsx --test test/bindPlayer.test.ts test/uploadRunSummary.test.ts test/uploadBattleArtifact.test.ts test/ghostBattles.test.ts test/replayDownload.test.ts`.

## Phase 3: Unify Mod API Client State And Routing

- [ ] Replace per-purpose routes with a single route builder in `Game/RunLogging/Upload/ModApiRoutes.cs`, rooted at one API base URL from `RunUploadDefaults.cs`.
- [ ] Extract `RunUploadClientStateStore` from `RunUploadRouting.cs` into `ModApiClientStateStore.cs`, reduce its persisted shape to one `client_id` and one bound `player_account_id`, and remove scope maps.
- [ ] Generalize `RunUploadRequestSigner.cs`, `RunUploadIdentityStore.cs`, `RunUploadKeyStore.cs`, and `RunUploadRegistrationClient.cs` into purpose-free building blocks that serve run upload, battle upload, and ghost query equally.
- [ ] Replace `RunUploadBootstrapContext.cs` with a purpose-free bootstrap context that validates local paths plus API base URL, not derived upload endpoints.
- [ ] Delete `RunUploadRouting.cs` entirely once no call sites depend on `RunUploadScopes` or `RunUploadEndpointSet`.
- [ ] Run the narrowest affected C# test seam that covers registration/auth bootstrap first, starting with `tests/RunUploadAuth.Tests`.

## Phase 4: Convert Run Upload To Run Summary Upload

- [ ] Update `Game/RunLogging/Upload/RunUploadSqliteStore.cs` snapshot generation so run uploads emit only run summary fields and exclude battle payloads or event-heavy artifacts.
- [ ] Refactor `Game/RunLogging/Upload/RunUploadService.cs` and `RunUploadController.cs` to target the new `POST /runs` endpoint through the unified signed API client and single client state.
- [ ] Keep the local dirty-tracking and retry semantics, but remove any assumptions about server-side projection status or replay-specific routing.
- [ ] Update `tests/RunUploadSync.Tests/Program.cs` and `tests/RunUploadAuth.Tests/Program.cs` to assert the new run-summary contract and single-client registration flow.
- [ ] Run the targeted run-upload test projects instead of a full repo build.

## Phase 5: Convert Battle Upload To Battle Artifact Upload

- [ ] Replace `BattleUploadRequestSigner.cs` and `BattleUploadApiClient.cs` with the unified signed API client, and update `BattleUploadService.cs` to send directly to `POST /battles` without endpoint derivation.
- [ ] Keep `BattleUploadSqliteStore.cs` focused on assembling one battle artifact per pending replay, containing the battle manifest plus replay payload.
- [ ] Update `BattleUploadController.cs` so it shares the same client identity, registration, and binding state as run upload rather than registering a separate replay client.
- [ ] Update `tests/CombatReplayUploadSync.Tests/Program.cs` to verify the single-client battle upload path and the absence of run-derived battle endpoints.
- [ ] Run `tests/CombatReplayUploadSync.Tests` after the C# changes compile.

## Phase 6: Rebuild Ghost Query And Replay Download

- [ ] Update `Game/HistoryPanel/GhostBattleApiClient.cs` to call the new query and replay-link endpoints through `ModApiRoutes`.
- [ ] Update `Game/HistoryPanel/GhostBattleSyncService.cs` so player binding, ghost query, and replay download all use the same client identity and no longer depend on run upload endpoints or purpose-specific registration.
- [ ] Adjust `HistoryPanel.Ghost.cs` and related call sites so configuration flows from the single API base URL.
- [ ] Update `tests/GhostBattleSync.Tests/Program.cs` to cover the new bind/query/download route names and the replay token download flow.
- [ ] Run `tests/GhostBattleSync.Tests` as the narrowest client-side verification seam for history sync.

## Phase 7: Remove Dead Legacy Surface

- [ ] Delete the old Worker tests and helpers that only exercise `purpose`, `run_uploads`, `pvp_battles`, or legacy upload route names once the replacements are green.
- [ ] Delete the old mod-side files replaced by the unified API client path and remove any stale references from project files.
- [ ] Update the minimum project docs that mention upload endpoints or purpose names so they describe the new single-client model only; keep edits narrowly focused on changed behavior.
- [ ] Run the combined targeted verification set:
  - `cd ModCFServer && npx tsx --test test/registerClient.test.ts test/bindPlayer.test.ts test/uploadRunSummary.test.ts test/uploadBattleArtifact.test.ts test/ghostBattles.test.ts test/replayDownload.test.ts test/schema.test.ts`
  - the four C# targeted test projects under `tests/RunUploadAuth.Tests`, `tests/RunUploadSync.Tests`, `tests/CombatReplayUploadSync.Tests`, and `tests/GhostBattleSync.Tests`
- [ ] If a wider build is still warranted after those pass, choose the smallest additional build that exercises touched projects before considering `BuildAll`.
