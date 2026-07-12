#nullable enable
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;

var runnerType = RequireType("BazaarPlusPlus.Game.Upload.StartupUploadAttemptRunner");
var gateType = RequireType("BazaarPlusPlus.Game.Upload.StartupUploadAttemptGate");
AssertMetadataTypeMissing(
    runnerType.Assembly.Location,
    "BazaarPlusPlus.Game.RunLogging.Upload.RunUploadController"
);
AssertMetadataTypeMissing(
    runnerType.Assembly.Location,
    "BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadController"
);
RequireType("BazaarPlusPlus.Game.Upload.IUploadFeed");
RequireType("BazaarPlusPlus.Game.RunLogging.Upload.RunBundleUploadFeed");
RequireType("BazaarPlusPlus.Game.Screenshots.Upload.BazaarDbSnapshotUploadFeed");

var runner = Activator.CreateInstance(
    runnerType,
    "RunnerTests",
    "Skipping startup upload because a live run is active.",
    "Starting startup upload attempt.",
    "Startup upload failed"
);
Assert(runner != null, "StartupUploadAttemptRunner should be constructible.");

var gate = Activator.CreateInstance(gateType, 5f, 10f);
Assert(gate != null, "StartupUploadAttemptGate should be constructible.");

var tickMethod = runnerType.GetMethod("Tick", BindingFlags.Public | BindingFlags.Instance);
Assert(tickMethod != null, "StartupUploadAttemptRunner should expose Tick.");

var hasPendingTaskProperty = runnerType.GetProperty("HasPendingTask");
Assert(
    hasPendingTaskProperty != null,
    "StartupUploadAttemptRunner should expose pending task state."
);
var tryDrainPendingTaskOnShutdownMethod = runnerType.GetMethod(
    "TryDrainPendingTaskOnShutdown",
    BindingFlags.Public | BindingFlags.Instance
);
Assert(
    tryDrainPendingTaskOnShutdownMethod != null,
    "StartupUploadAttemptRunner should expose bounded shutdown drain."
);

var startCount = 0;
Task StartAsync(CancellationToken _)
{
    startCount++;
    return Task.CompletedTask;
}

tickMethod!.Invoke(
    runner,
    [gate, 3f, false, (Func<CancellationToken, Task>)StartAsync, CancellationToken.None]
);
Assert(startCount == 0, "Runner should wait until the startup gate becomes eligible.");

tickMethod.Invoke(
    runner,
    [gate, 5f, false, (Func<CancellationToken, Task>)StartAsync, CancellationToken.None]
);
Assert(startCount == 1, "Runner should start the upload attempt once when eligible.");
Assert(
    (bool)(hasPendingTaskProperty!.GetValue(runner) ?? false),
    "Runner should retain the in-flight task until a completion tick processes it."
);

tickMethod.Invoke(
    runner,
    [gate, 6f, false, (Func<CancellationToken, Task>)StartAsync, CancellationToken.None]
);
Assert(startCount == 1, "Runner should not restart the upload before the retry interval elapses.");
Assert(
    !(bool)(hasPendingTaskProperty.GetValue(runner) ?? true),
    "Runner should clear the pending task after processing completion."
);

tickMethod.Invoke(
    runner,
    [gate, 14f, false, (Func<CancellationToken, Task>)StartAsync, CancellationToken.None]
);
Assert(startCount == 1, "Runner should continue waiting until the retry interval elapses.");

tickMethod.Invoke(
    runner,
    [gate, 15f, false, (Func<CancellationToken, Task>)StartAsync, CancellationToken.None]
);
Assert(startCount == 2, "Runner should restart the upload when the retry interval elapses.");

var liveRunRunner = Activator.CreateInstance(
    runnerType,
    "RunnerTests",
    "Skipping startup upload because a live run is active.",
    "Starting startup upload attempt.",
    "Startup upload failed"
)!;
var liveRunGate = Activator.CreateInstance(gateType, 0f, 10f)!;
var liveRunStarts = 0;
Task StartLiveRunAsync(CancellationToken _)
{
    liveRunStarts++;
    return Task.CompletedTask;
}

tickMethod.Invoke(
    liveRunRunner,
    [
        liveRunGate,
        0f,
        true,
        (Func<CancellationToken, Task>)StartLiveRunAsync,
        CancellationToken.None,
    ]
);
tickMethod.Invoke(
    liveRunRunner,
    [
        liveRunGate,
        1f,
        false,
        (Func<CancellationToken, Task>)StartLiveRunAsync,
        CancellationToken.None,
    ]
);
Assert(
    liveRunStarts == 1,
    "Runner should retry once the live run ends instead of consuming the startup opportunity."
);

var drainRunner = Activator.CreateInstance(
    runnerType,
    "RunnerTests",
    "Skipping startup upload because a live run is active.",
    "Starting startup upload attempt.",
    "Startup upload failed"
)!;
var drainGate = Activator.CreateInstance(gateType, 0f, 10f)!;
var drainUpload = new TaskCompletionSource<object?>(
    TaskCreationOptions.RunContinuationsAsynchronously
);
Task StartDrainAsync(CancellationToken _) => drainUpload.Task;
tickMethod.Invoke(
    drainRunner,
    [drainGate, 0f, false, (Func<CancellationToken, Task>)StartDrainAsync, CancellationToken.None]
);
var drainCleanupCount = 0;
_ = Task.Run(async () =>
{
    await Task.Delay(50);
    drainUpload.SetResult(null);
});
var drained = (bool)(
    tryDrainPendingTaskOnShutdownMethod!.Invoke(
        drainRunner,
        [TimeSpan.FromSeconds(1), (Action)(() => drainCleanupCount++)]
    ) ?? false
);
Assert(
    drained && drainCleanupCount == 1,
    "Shutdown observation should synchronously drain a promptly completing upload before returning."
);

var shutdownRunner = Activator.CreateInstance(
    runnerType,
    "RunnerTests",
    "Skipping startup upload because a live run is active.",
    "Starting startup upload attempt.",
    "Startup upload failed"
)!;
var shutdownGate = Activator.CreateInstance(gateType, 0f, 10f)!;
var pendingUpload = new TaskCompletionSource<object?>(
    TaskCreationOptions.RunContinuationsAsynchronously
);
Task StartPendingAsync(CancellationToken _) => pendingUpload.Task;

tickMethod.Invoke(
    shutdownRunner,
    [
        shutdownGate,
        0f,
        false,
        (Func<CancellationToken, Task>)StartPendingAsync,
        CancellationToken.None,
    ]
);
Assert(
    (bool)(hasPendingTaskProperty.GetValue(shutdownRunner) ?? false),
    "Runner should retain a pending upload task before shutdown observation."
);

var cleanupCount = 0;
var drainStopwatch = Stopwatch.StartNew();
var timedOutDrain = (bool)(
    tryDrainPendingTaskOnShutdownMethod.Invoke(
        shutdownRunner,
        [TimeSpan.FromMilliseconds(20), (Action)(() => cleanupCount++)]
    ) ?? true
);
drainStopwatch.Stop();
Assert(
    !(bool)(hasPendingTaskProperty.GetValue(shutdownRunner) ?? true),
    "Shutdown observation should detach the pending task from the runner."
);
Assert(!timedOutDrain, "Shutdown drain should report an incomplete task at its timeout.");
Assert(
    drainStopwatch.Elapsed < TimeSpan.FromSeconds(1),
    "Shutdown drain timeout must remain bounded."
);
Assert(cleanupCount == 0, "Shutdown cleanup should wait for the pending upload to finish.");

pendingUpload.SetException(new InvalidOperationException("upload failed after shutdown"));
Assert(
    SpinWait.SpinUntil(() => cleanupCount == 1, TimeSpan.FromSeconds(2)),
    "Shutdown observation should consume completion and then run cleanup."
);

var cancellationRunner = Activator.CreateInstance(
    runnerType,
    "RunnerTests",
    "Skipping startup upload because a live run is active.",
    "Starting startup upload attempt.",
    "Startup upload failed"
)!;
var cancellationGate = Activator.CreateInstance(gateType, 0f, 10f)!;
using var cancellation = new CancellationTokenSource();
Task StartCancellationAsync(CancellationToken token) => Task.Delay(Timeout.Infinite, token);
tickMethod.Invoke(
    cancellationRunner,
    [
        cancellationGate,
        0f,
        false,
        (Func<CancellationToken, Task>)StartCancellationAsync,
        cancellation.Token,
    ]
);
cancellation.Cancel();
var cancellationCleanupCount = 0;
var cancelledDrain = (bool)(
    tryDrainPendingTaskOnShutdownMethod.Invoke(
        cancellationRunner,
        [TimeSpan.FromSeconds(1), (Action)(() => cancellationCleanupCount++)]
    ) ?? false
);
Assert(
    cancelledDrain && cancellationCleanupCount == 1,
    "A cancelled upload should drain and clean up synchronously before logger flush."
);

Console.WriteLine("Startup upload runner tests passed.");

return;

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void AssertMetadataTypeMissing(string assemblyPath, string fullName)
{
    if (MetadataContainsType(assemblyPath, fullName))
        throw new InvalidOperationException($"{fullName} should not be compiled.");
}

static bool MetadataContainsType(string assemblyPath, string fullName)
{
    using var stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream);
    var metadataReader = peReader.GetMetadataReader();
    foreach (var handle in metadataReader.TypeDefinitions)
    {
        var type = metadataReader.GetTypeDefinition(handle);
        var @namespace = metadataReader.GetString(type.Namespace);
        var name = metadataReader.GetString(type.Name);
        var actual = string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        if (string.Equals(actual, fullName, StringComparison.Ordinal))
            return true;
    }

    return false;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
