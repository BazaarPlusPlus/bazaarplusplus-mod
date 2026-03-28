# Replay Cloudflare Upload Decoupling Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Decouple combat replay upload from run-upload route fallback and derived endpoints while keeping the shared upload enable switch and shared auth primitives.

**Architecture:** Keep install identity and RSA keys shared, but give replay upload its own registration/upload endpoints and its own client-id scope. Update the Cloudflare Worker from a stub to a real protocol endpoint that persists registration state in D1, verifies signed uploads, and stores replay payloads plus metadata.

**Tech Stack:** C# / Unity MonoBehaviour / Microsoft.Data.Sqlite / Newtonsoft.Json / .NET test executables / Cloudflare Workers TypeScript

---

### Task 1: Lock the new replay client contract in tests

**Files:**
- Modify: `tests/CombatReplayUploadSync.Tests/Program.cs`
- Modify: `tests/CombatReplayUploadAuth.Tests/Program.cs`
- Modify: `tests/RunLoggingModels.Tests/Program.cs`

**Step 1: Write the failing assertions**

- Require replay upload construction without route selector/global/cn endpoints.
- Require dedicated replay registration/upload endpoints instead of derived `/runs/upload`.
- Require replay client-id state to be isolated from run route-scoped state.

**Step 2: Run the focused tests and confirm they fail**

```bash
dotnet run --project tests/CombatReplayUploadSync.Tests/CombatReplayUploadSync.Tests.csproj
dotnet run --project tests/CombatReplayUploadAuth.Tests/CombatReplayUploadAuth.Tests.csproj
dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj
```

### Task 2: Decouple replay upload from run route fallback

**Files:**
- Modify: `Core/Config/BppConfig.cs`
- Modify: `Core/Config/IBppConfig.cs`
- Modify: `Game/CombatReplay/Upload/CombatReplayUploadController.cs`
- Modify: `Game/CombatReplay/Upload/CombatReplayUploadService.cs`
- Modify: `Game/RunLogging/Upload/RunUploadRouting.cs`

**Step 1: Implement the minimal client changes**

- Add dedicated replay registration/upload config entries under `RunUpload`.
- Keep `EnableRunUploadConfig` as the shared on/off switch.
- Remove replay dependence on `RunUploadRouteSelector` and replay endpoint derivation.
- Extend client-id state storage so run and replay use separate scopes.

**Step 2: Run the focused tests**

```bash
dotnet run --project tests/CombatReplayUploadSync.Tests/CombatReplayUploadSync.Tests.csproj
dotnet run --project tests/CombatReplayUploadAuth.Tests/CombatReplayUploadAuth.Tests.csproj
dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj
```

### Task 3: Replace the Worker stub with a real signed-upload endpoint

**Files:**
- Modify: `ModCFServer/src/index.ts`
- Modify: `ModCFServer/package.json`

**Step 1: Add failing worker verification**

- Add a minimal Node-based Worker test script that exercises registration, signature verification, replay upload persistence, and rejection paths.

**Step 2: Implement the Worker**

- Persist registered clients in D1 by purpose.
- Verify `X-BPP-*` headers, body hash, timestamp/nonce, and RSA signature.
- Persist replay upload metadata in D1 and payloads in R2 with collision-resistant keys.
- Keep `runs/upload` on the same verified protocol baseline.

**Step 3: Run Worker verification**

```bash
npm test --prefix ModCFServer
npm run check --prefix ModCFServer
```

### Task 4: Run focused end-to-end verification

**Files:**
- No additional files unless verification exposes issues.

**Step 1: Run the touched verification suite**

```bash
dotnet run --project tests/CombatReplayUploadSync.Tests/CombatReplayUploadSync.Tests.csproj
dotnet run --project tests/CombatReplayUploadAuth.Tests/CombatReplayUploadAuth.Tests.csproj
dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj
dotnet run --project tests/RunUploadAuth.Tests/RunUploadAuth.Tests.csproj
dotnet run --project tests/RunUploadSync.Tests/RunUploadSync.Tests.csproj
npm test --prefix ModCFServer
npm run check --prefix ModCFServer
```
