#nullable enable
using System.Reflection;

var formatterType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.HistoryPanelServerHealthFormatter"
);
var probeResultType = RequireModApiType("BazaarPlusPlus.ModApi.Clients.ModApiHealthProbeResult");

var idle = InvokeStatic(formatterType, "Idle");
Assert(GetString(idle, "ButtonText") == "Check Server", "Idle button should invite a probe.");
Assert(GetBool(idle, "ButtonEnabled"), "Idle button should be enabled.");
Assert(GetNullableString(idle, "StatusMessage") == null, "Idle status should stay quiet.");

var checking = InvokeStatic(formatterType, "Checking");
Assert(GetString(checking, "ButtonText") == "Checking...", "Checking button should show progress.");
Assert(!GetBool(checking, "ButtonEnabled"), "Checking button should be disabled.");
Assert(
    GetString(checking, "StatusMessage") == "Checking game-server connectivity...",
    "Checking status should explain what is happening."
);

var success = InvokeStatic(
    probeResultType,
    "Success",
    new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc),
    142L,
    "ok",
    new DateTime(2026, 6, 3, 0, 0, 1, DateTimeKind.Utc)
);
var successDisplay = InvokeStatic(formatterType, "FromProbeResult", success);
Assert(GetString(successDisplay, "ButtonText") == "Check Server", "Success returns to idle label.");
Assert(GetBool(successDisplay, "ButtonEnabled"), "Success should re-enable the button.");
Assert(
    GetString(successDisplay, "StatusMessage") == "Game and server connected in 142 ms.",
    "Success status should include RTT."
);

var failure = InvokeStatic(
    probeResultType,
    "Failure",
    new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc),
    87L,
    "http_503"
);
var failureDisplay = InvokeStatic(formatterType, "FromProbeResult", failure);
Assert(GetString(failureDisplay, "ButtonText") == "Check Server", "Failure returns to idle label.");
Assert(GetBool(failureDisplay, "ButtonEnabled"), "Failure should re-enable the button.");
Assert(
    GetString(failureDisplay, "StatusMessage") == "Game-server check failed after 87 ms: http_503",
    "Failure status should include RTT and stable error."
);

Console.WriteLine("HistoryPanelServerHealth checks passed.");

static Type RequireType(string fullName)
{
    var assembly = LoadAssembly("BazaarPlusPlus");
    return assembly.GetType(fullName)
        ?? throw new InvalidOperationException($"{fullName} should exist.");
}

static Type RequireModApiType(string fullName)
{
    var assembly = LoadAssembly("BazaarPlusPlus.ModApi");
    return assembly.GetType(fullName)
        ?? throw new InvalidOperationException($"{fullName} should exist.");
}

static Assembly LoadAssembly(string assemblyName)
{
    return AppDomain
            .CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName)
        ?? Assembly.Load(assemblyName);
}

static object InvokeStatic(Type type, string methodName, params object[] args)
{
    var method =
        type.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException($"{type.FullName}.{methodName} should exist.");

    return method.Invoke(null, args)
        ?? throw new InvalidOperationException(
            $"{type.FullName}.{methodName} should return a value."
        );
}

static string GetString(object target, string propertyName)
{
    return GetNullableString(target, propertyName)
        ?? throw new InvalidOperationException($"{propertyName} should not be null.");
}

static string? GetNullableString(object target, string propertyName)
{
    return GetProperty(target, propertyName).GetValue(target) as string;
}

static bool GetBool(object target, string propertyName)
{
    return GetProperty(target, propertyName).GetValue(target) is bool value
        ? value
        : throw new InvalidOperationException($"{propertyName} should be a bool.");
}

static PropertyInfo GetProperty(object target, string propertyName)
{
    return target.GetType().GetProperty(propertyName)
        ?? throw new InvalidOperationException($"{propertyName} should exist.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
