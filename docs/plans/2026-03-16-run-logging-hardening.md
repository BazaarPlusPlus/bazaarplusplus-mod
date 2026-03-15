# Run Logging Hardening Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Harden run logging so it uses only the server-provided run id, interrupted sessions do not pollute new runs, terminal status is classified correctly, logging failures do not break the game loop, and Windows debug builds ship the required SQLite native runtime.

**Architecture:** Keep the current SQLite-backed run log design, but change run identity to the authoritative server `NetMessageRunInitialized.RunId`. The plugin should cache that id when the socket message arrives, refuse to start a run log session before the id is available, and restore sessions only when the stored id matches the current server id exactly. Around that identity change, keep the controller/session-manager contract tight, classify exit status from run lifecycle events, and isolate persistence failures at the controller boundary so run logging degrades safely instead of crashing the main loop.

**Tech Stack:** C# 12, BepInEx, Harmony, Microsoft.Data.Sqlite, console-style reflection tests, MSBuild copy targets

---

### Task 1: Add regression tests for server run id capture and gating

**Files:**
- Modify: `tests/RunLoggingModels.Tests/Program.cs`
- Modify: `tests/RunLoggingCapture.Tests/Program.cs`

**Step 1: Write the failing tests**

Add tests that cover the new identity model:

- run logging does not create a `RunLogCreateRequest` before a server run id is available
- once a server run id is present, the request uses that exact value as `RunId`

If `ModState` is too coupled to inspect directly from the test project, add a tiny helper on the production side later and test through reflection.

```csharp
Assert(
    !Invoke<bool>(gameDataReaderType, null, "TryCreateRunLogCreateRequest", [null]),
    "Run logging should stay idle until the server provides a run id."
);

Assert(request.RunId == "server-run-123", "Run logging should use the server run id verbatim.");
```

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: FAIL because the current implementation still synthesizes a local run id immediately.

**Step 3: Commit the failing tests**

```bash
git add tests/RunLoggingModels.Tests/Program.cs tests/RunLoggingCapture.Tests/Program.cs
git commit -m "test: require server run id for logging"
```

### Task 2: Capture `NetMessageRunInitialized.RunId` in plugin state

**Files:**
- Modify: `Models/ModState.cs`
- Create: `Patches/RunLogging/RunInitializedPatch.cs`
- Test: `tests/RunLoggingModels.Tests/Program.cs`

**Step 1: Write minimal implementation**

In `ModState.cs`, add:

```csharp
public static string? CurrentServerRunId;
```

Reset it in `Initialize(...)`, `OnRunEnded()`, and `OnRunInterrupted()`.

Create `Patches/RunLogging/RunInitializedPatch.cs` with a Harmony patch on the runtime path that receives `NetMessageRunInitialized`, and copy `message.RunId` into `ModState.CurrentServerRunId`.

```csharp
[HarmonyPatch(typeof(NetMessageProcessor), "ReceiveOrQueue")]
internal static class RunInitializedPatch
{
    [HarmonyPrefix]
    private static void Prefix(INetMessage message)
    {
        if (message is NetMessageRunInitialized runInitialized)
            ModState.CurrentServerRunId = runInitialized.RunId;
    }
}
```

Prefer the earliest stable interception point that sees the full message object and does not depend on internal state transitions.

**Step 2: Run tests**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Expected: PASS once the run id is cached and visible to the tests.

**Step 3: Commit**

```bash
git add Models/ModState.cs Patches/RunLogging/RunInitializedPatch.cs tests/RunLoggingModels.Tests/Program.cs
git commit -m "feat: capture server run id for logging"
```

### Task 3: Add regression tests for interrupted run classification

**Files:**
- Modify: `tests/RunLoggingSqliteStore.Tests/Program.cs`
- Modify: `tests/RunLoggingModels.Tests/Program.cs`

**Step 1: Write the failing tests**

Add a store-level assertion that a completion payload with status `abandoned` is persisted exactly as provided, and add a model-level assertion that the run logging flow distinguishes terminal statuses instead of forcing `completed`.

```csharp
Assert(
    GetString(connection, "SELECT status FROM run_status WHERE run_id = $runId;", runId) == "abandoned",
    "run_status should preserve interrupted/abandoned terminal statuses."
);
```

If the model test is easier to express through a new helper method, assert that helper output instead of reaching into game runtime state.

**Step 2: Run tests to verify they fail**

Run: `dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Expected: FAIL because current completion flow hard-codes `completed`.

**Step 3: Commit the failing tests**

```bash
git add tests/RunLoggingSqliteStore.Tests/Program.cs tests/RunLoggingModels.Tests/Program.cs
git commit -m "test: cover interrupted run status"
```

### Task 4: Persist server run id on restored sessions

**Files:**
- Modify: `Game/RunLogging/Models/RunLogSessionState.cs`
- Modify: `Game/RunLogging/Persistence/SqliteRunLogStore.cs`
- Test: `tests/RunLoggingSqliteRecovery.Tests/Program.cs`

**Step 1: Write the failing test**

Extend `tests/RunLoggingSqliteRecovery.Tests/Program.cs` so a recovered session must preserve the authoritative server run id.

```csharp
Assert(resumed.RunId == "server-run-123", "Recovered session should preserve the server run id.");
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj`

Expected: FAIL because recovery coverage does not yet prove authoritative id round-tripping.

**Step 3: Write minimal implementation**

Keep `RunLogSessionState.RunId` as the authoritative id and make sure the store continues to round-trip it exactly. If helpful for diagnostics, also persist `hero` and `game_mode`, but they are no longer part of admission logic.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj`

Expected: PASS with server run id preserved across recovery.

**Step 5: Commit**

```bash
git add Game/RunLogging/Models/RunLogSessionState.cs Game/RunLogging/Persistence/SqliteRunLogStore.cs tests/RunLoggingSqliteRecovery.Tests/Program.cs
git commit -m "test: verify server run id recovery"
```

### Task 5: Centralize session admission on exact server run id matches and fix `run_resumed`

**Files:**
- Modify: `Game/RunLogging/RunLogSessionManager.cs`
- Modify: `Game/RunLogging/RunLoggingController.cs`
- Test: `tests/RunLoggingCapture.Tests/Program.cs`

**Step 1: Write the failing test**

Extend `tests/RunLoggingCapture.Tests/Program.cs` with two new assertions:

- a restored session with the same server run id emits `run_resumed`
- a restored session with a different server run id is abandoned and replaced by a fresh run

```csharp
Assert(
    resumedStore.AppendedEvents.Select(e => e.Kind).SequenceEqual(["run_resumed"]),
    "Restored sessions should emit run_resumed when the server run id matches."
);

Assert(
    mismatchStore.MarkAbandonedCalls == 1,
    "Sessions from a different server run id should be abandoned."
);
Assert(
    mismatchStore.CreateRunCalls == 1,
    "Sessions from a different server run id should create a fresh run."
);
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: FAIL because admission still ignores the authoritative server run id.

**Step 3: Implement exact-id session compatibility**

Update `EnsureActiveSession(...)`:

```csharp
public RunLogSessionState EnsureActiveSession(RunLogCreateRequest request)
{
    ActiveSession ??= RestoreActiveSession();

    if (ActiveSession != null && !string.Equals(ActiveSession.RunId, request.RunId, StringComparison.Ordinal))
    {
        _store.MarkRunAbandoned(
            ActiveSession.RunId,
            new RunLogAbandonment
            {
                SchemaVersion = ActiveSession.SchemaVersion,
                EndedAtUtc = _utcNow(),
                FinalDay = ActiveSession.Day,
                FinalHour = ActiveSession.Hour,
                Reason = "session_mismatch",
            }
        );
        ActiveSession = null;
    }

    ActiveSession ??= _store.CreateRun(request);
    return ActiveSession;
}
```

**Step 4: Route controller admission through the core every time**

Update `RunLoggingController.EnsureActiveRunFromGame()`:

```csharp
private RunLogSessionState? EnsureActiveRunFromGame()
{
    if (!GameDataReader.TryCreateRunLogCreateRequest(out var request))
        return null;

    return RequireCore().EnsureRunStarted(request);
}
```

Leave `RunLoggingControllerCore.EnsureRunStarted(...)` as the only place that emits `run_started` or `run_resumed`.

**Step 5: Run tests**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Run: `dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj`

Expected: PASS with restored sessions emitting `run_resumed` only on exact server id matches.

**Step 6: Commit**

```bash
git add Game/RunLogging/RunLogSessionManager.cs Game/RunLogging/RunLoggingController.cs tests/RunLoggingCapture.Tests/Program.cs
git commit -m "fix: admit run logging sessions by server id"
```

### Task 6: Remove synthetic run id generation from the main run logging path

**Files:**
- Modify: `Game/GameDataReader.cs`
- Modify: `Game/RunLogging/RunIdFactory.cs`
- Test: `tests/RunLoggingModels.Tests/Program.cs`

**Step 1: Implement server-id-only request creation**

Update `TryCreateRunLogCreateRequest(...)`:

```csharp
var runId = ModState.CurrentServerRunId;
if (string.IsNullOrWhiteSpace(runId))
    return false;

request = new RunLogCreateRequest
{
    SchemaVersion = 1,
    RunId = runId,
    StartedAtUtc = DateTimeOffset.UtcNow,
    Hero = Data.Run.Player.Hero.ToString(),
    GameMode = Data.SelectedPlayMode.ToString(),
    Day = (int?)Data.Run.Day,
    Hour = unchecked((int)(Data.Run.Victories + Data.Run.Losses + 1)),
};
```

Do not call `RunIdFactory.Create(...)` from this path anymore.

**Step 2: Narrow `RunIdFactory` usage**

If nothing else uses `RunIdFactory`, delete the file and any related references. If tests or scripts still need it, leave it in place but document that it is no longer used by runtime logging.

**Step 3: Run tests**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: PASS with request creation gated on server id only.

**Step 4: Commit**

```bash
git add Game/GameDataReader.cs Game/RunLogging/RunIdFactory.cs tests/RunLoggingModels.Tests/Program.cs
git commit -m "fix: remove synthetic run ids from logging"
```

### Task 7: Classify interrupted runs correctly

**Files:**
- Modify: `Models/ModState.cs`
- Modify: `Game/GameDataReader.cs`
- Test: `tests/RunLoggingModels.Tests/Program.cs`

**Step 1: Add explicit exit-kind tracking**

In `ModState.cs`:

```csharp
internal enum RunExitKind
{
    Normal,
    Interrupted,
}

public static RunExitKind LastRunExitKind = RunExitKind.Normal;
```

Set it like this:

```csharp
private static void OnRunStarted()
{
    LastRunExitKind = RunExitKind.Normal;
    EncounterTracker.ResetEncounterState("Run started");
    SetInGameRun(true, "Run started");
}

private static void OnRunEnded()
{
    LastRunExitKind = RunExitKind.Normal;
    SetInGameRun(false, "Run ended");
}

private static void OnRunInterrupted()
{
    LastRunExitKind = RunExitKind.Interrupted;
    SetInGameRun(false, "Run interrupted");
}
```

**Step 2: Use exit kind when building terminal completion**

In `GameDataReader.cs`:

```csharp
var status = ModState.LastRunExitKind == RunExitKind.Interrupted
    ? "abandoned"
    : "completed";
```

Then assign that status in `BuildRunLogCompletion(...)`.

**Step 3: Run tests**

Run: `dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj`

Run: `dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`

Expected: PASS with interrupted runs no longer forced to `completed`.

**Step 4: Commit**

```bash
git add Models/ModState.cs Game/GameDataReader.cs tests/RunLoggingModels.Tests/Program.cs tests/RunLoggingSqliteStore.Tests/Program.cs
git commit -m "fix: classify interrupted runs as abandoned"
```

### Task 8: Isolate controller-side persistence failures

**Files:**
- Modify: `Game/RunLogging/RunLoggingController.cs`
- Test: `tests/RunLoggingCapture.Tests/Program.cs`

**Step 1: Write the failing test**

Add a seam where `AppendEvent(...)` throws, and assert the controller-facing polling path swallows the exception after logging.

If the current controller is too coupled to test directly, extract a tiny internal helper and test that helper:

```csharp
Assert(
    didNotThrow,
    "Run logging controller should swallow persistence failures and keep polling alive."
);
```

**Step 2: Run test to verify it fails**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: FAIL because exceptions currently escape through the polling path.

**Step 3: Implement minimal exception isolation**

Wrap both `PollRunState()` and `CaptureSelectionFromCurrentState()` bodies:

```csharp
try
{
    // existing body
}
catch (Exception ex)
{
    BppLog.Error("RunLoggingController", "Run logging poll failed", ex);
}
```

Use a second message string for `CaptureSelectionFromCurrentState()` so logs identify the failing path precisely.

**Step 4: Run test to verify it passes**

Run: `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`

Expected: PASS with the failure isolated.

**Step 5: Commit**

```bash
git add Game/RunLogging/RunLoggingController.cs tests/RunLoggingCapture.Tests/Program.cs
git commit -m "fix: isolate run logging polling failures"
```

### Task 9: Harden SQLite connection and command usage

**Files:**
- Modify: `Game/RunLogging/Persistence/SqliteRunLogStore.cs`
- Test: `tests/RunLoggingSqliteStore.Tests/Program.cs`

**Step 1: Move serialization ahead of connection open**

Update `AppendEvent(...)`:

```csharp
var payloadJson = JsonConvert.SerializeObject(entry, SerializerSettings);

using var connection = OpenConnection();
using var command = connection.CreateCommand();
command.CommandTimeout = 2;
command.Parameters.AddWithValue("$payloadJson", payloadJson);
```

**Step 2: Make `OpenConnection()` exception-safe**

```csharp
private SqliteConnection OpenConnection()
{
    var connection = new SqliteConnection($"Data Source={_databasePath}");
    connection.Open();

    try
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = 2;
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }
    catch
    {
        connection.Dispose();
        throw;
    }
}
```

Also set `CommandTimeout = 2` on every command created in this file.

**Step 3: Add a small regression assertion**

At minimum, keep the existing store tests green after the refactor. If practical, add a fake-serialization failure seam through an internal helper, but do not expand scope into a custom serializer abstraction unless the test is impossible otherwise.

**Step 4: Run tests**

Run: `dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj`

Run: `dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj`

Expected: PASS with no behavior regression.

**Step 5: Commit**

```bash
git add Game/RunLogging/Persistence/SqliteRunLogStore.cs tests/RunLoggingSqliteStore.Tests/Program.cs
git commit -m "fix: harden sqlite run log persistence"
```

### Task 10: Ship Windows native SQLite runtime in Debug builds

**Files:**
- Modify: `BazaarPlusPlus.csproj`
- Test: `bppinstaller/scripts/prebuild-check.test.mjs`

**Step 1: Add the missing Windows native runtime to the Debug target**

In the Debug `CopyToBepInExPlugins` target:

```xml
<FilesToDelete Include="$(GamePath)\BepInEx\plugins\e_sqlite3.dll" />
<WindowsSqliteNativeFile Include="$(NuGetPackageRoot)sqlitepclraw.lib.e_sqlite3/*/runtimes/win-x64/native/e_sqlite3.dll" />
```

Then add:

```xml
<Copy
  SourceFiles="@(WindowsSqliteNativeFile)"
  DestinationFolder="$(GamePath)\BepInEx\plugins"
  SkipUnchangedFiles="true"
  Condition="'@(WindowsSqliteNativeFile)' != ''" />
```

**Step 2: Run packaging regression test**

Run: `node --test bppinstaller/scripts/prebuild-check.test.mjs`

Expected: PASS. The test already covers release bundle contents; this step ensures the csproj/debug copy change does not drift from packaging assumptions.

**Step 3: Optional local build smoke check**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: PASS.

**Step 4: Commit**

```bash
git add BazaarPlusPlus.csproj
git commit -m "fix: copy sqlite native runtime in debug builds"
```

### Task 11: Full regression pass

**Files:**
- Modify: none
- Test: existing test projects and packaging check

**Step 1: Run the targeted regression suite**

Run:

```bash
dotnet run --project tests/RunLoggingSession.Tests/RunLoggingSession.Tests.csproj
dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj
dotnet run --project tests/RunLoggingSqliteRecovery.Tests/RunLoggingSqliteRecovery.Tests.csproj
dotnet run --project tests/RunLoggingSqliteStore.Tests/RunLoggingSqliteStore.Tests.csproj
dotnet run --project tests/RunLoggingModels.Tests/RunLoggingModels.Tests.csproj
node --test bppinstaller/scripts/prebuild-check.test.mjs
```

Expected:

- `RunLogging session checks passed.`
- `RunLogging capture checks passed.`
- `RunLogging SQLite recovery checks passed.`
- `RunLogging SQLite store checks passed.`
- `RunLogging model checks passed.` or equivalent final success line
- Node test summary with all tests passing

**Step 2: Run a broader build sanity check**

Run: `dotnet build BazaarPlusPlus.csproj`

Expected: PASS.

**Step 3: Commit verification-only state if needed**

If code changed during verification fixes:

```bash
git add Game/RunLogging Models BazaarPlusPlus.csproj tests
git commit -m "test: verify run logging hardening"
```

If no code changed, do not create a commit.
