#nullable enable
using System.Reflection;
using System.Threading.Tasks;

var runnerType = RequireType("BazaarPlusPlus.Game.Upload.StartupUploadAttemptRunner");
var gateType = RequireType("BazaarPlusPlus.Game.Upload.StartupUploadAttemptGate");

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

Console.WriteLine("Startup upload runner tests passed.");

return;

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
