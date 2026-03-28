# Combat Replay Upload Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a minimal client-side combat replay upload pipeline that asynchronously uploads persisted replay payload files outside live runs, plus a separate `ModCFServer/` Worker skeleton.

**Architecture:** Keep local SQLite and local replay payload files as the source of truth. Mirror the existing run-upload design by adding replay-specific sync state, upload service/controller, and auth-aware API client, while reusing the existing install identity, route selection, and RSA signing infrastructure. Keep the Cloudflare Worker code in a standalone project under `ModCFServer/` so it is not compiled into the mod DLL.

**Tech Stack:** C# / Unity MonoBehaviour / Microsoft.Data.Sqlite / Newtonsoft.Json / .NET test executables / Cloudflare Workers TypeScript

---

### Task 1: Add replay-upload coverage

**Files:**
- Create: `tests/CombatReplayUploadSync.Tests/CombatReplayUploadSync.Tests.csproj`
- Create: `tests/CombatReplayUploadSync.Tests/Program.cs`
- Create: `tests/CombatReplayUploadAuth.Tests/CombatReplayUploadAuth.Tests.csproj`
- Create: `tests/CombatReplayUploadAuth.Tests/Program.cs`
- Modify: `tests/ArchitectureModularization.Tests/Program.cs`

**Step 1: Write the failing tests**

- Cover replay sync-state schema and snapshot building.
- Cover signed replay upload headers and body hashing.
- Cover runtime composition: new controller mounted in `Plugin`.

**Step 2: Run the replay upload tests to verify they fail**

Run:

```bash
dotnet run --project tests/CombatReplayUploadSync.Tests/CombatReplayUploadSync.Tests.csproj
dotnet run --project tests/CombatReplayUploadAuth.Tests/CombatReplayUploadAuth.Tests.csproj
dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj
```

Expected: failures for missing replay-upload types and wiring.

### Task 2: Add client replay-upload infrastructure

**Files:**
- Create: `Game/CombatReplay/Upload/CombatReplayUploadPayload.cs`
- Create: `Game/CombatReplay/Upload/CombatReplayUploadSqliteStore.cs`
- Create: `Game/CombatReplay/Upload/CombatReplayUploadApiClient.cs`
- Create: `Game/CombatReplay/Upload/CombatReplayUploadService.cs`
- Create: `Game/CombatReplay/Upload/CombatReplayUploadController.cs`
- Modify: `Game/RunLogging/Persistence/Sqlite/RunLogSqliteSchema.cs`
- Modify: `Plugin.cs`

**Step 1: Write the minimal implementation**

- Add local `replay_sync_state`.
- Add payload snapshot assembly from `pvp_battles` + `CombatReplays/*.payload.json`.
- Add background upload service/controller with existing route + auth reuse.

**Step 2: Run focused tests**

Run:

```bash
dotnet run --project tests/CombatReplayUploadSync.Tests/CombatReplayUploadSync.Tests.csproj
dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj
```

Expected: passing.

### Task 3: Mark persisted replays dirty and support signed upload auth

**Files:**
- Modify: `Game/CombatReplay/CombatReplayRuntime.cs`
- Modify: `Game/RunLogging/Upload/RunUploadRouting.cs`
- Modify: `Game/RunLogging/Upload/RunUploadRequestSigner.cs`
- Modify: `Game/RunLogging/Upload/RunUploadErrorFormatter.cs`
- Modify: `Game/RunLogging/Upload/RunUploadService.cs` (only if shared helpers are needed)

**Step 1: Implement minimal shared helpers**

- Derive replay upload endpoint from configured run upload endpoint.
- Add replay request canonicalization and signed request creation.
- Mark replay payloads dirty only after persistence succeeds.

**Step 2: Run auth and sync tests**

Run:

```bash
dotnet run --project tests/CombatReplayUploadAuth.Tests/CombatReplayUploadAuth.Tests.csproj
dotnet run --project tests/CombatReplayUploadSync.Tests/CombatReplayUploadSync.Tests.csproj
```

Expected: passing.

### Task 4: Add standalone Worker skeleton under `ModCFServer/`

**Files:**
- Create: `ModCFServer/package.json`
- Create: `ModCFServer/tsconfig.json`
- Create: `ModCFServer/wrangler.toml`
- Create: `ModCFServer/src/index.ts`
- Create: `ModCFServer/README.md` only if essential (skip unless needed)

**Step 1: Add minimal Worker project**

- `POST /health`
- `POST /clients/register`
- `POST /runs/upload`
- `POST /replays/upload`

**Step 2: Keep it outside the mod build**

- Verify `BazaarPlusPlus.csproj` already excludes non-`tests` TypeScript content from compilation.
- Do not add Worker files to the C# project.

### Task 5: Verify end-to-end focused scope

**Files:**
- No code changes required unless verification fails.

**Step 1: Run the focused verification suite**

Run:

```bash
dotnet run --project tests/CombatReplayUploadSync.Tests/CombatReplayUploadSync.Tests.csproj
dotnet run --project tests/CombatReplayUploadAuth.Tests/CombatReplayUploadAuth.Tests.csproj
dotnet run --project tests/ArchitectureModularization.Tests/ArchitectureModularization.Tests.csproj
dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj
dotnet run --project tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj
dotnet run --project tests/RunUploadAuth.Tests/RunUploadAuth.Tests.csproj
```

Expected: all pass.
