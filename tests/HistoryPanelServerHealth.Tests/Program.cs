#nullable enable
using System.Reflection;
using BazaarPlusPlus.Localization;

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

var textType = RequireType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelText");
var languageProvider = new MutableLanguageProvider("zh-CN");
var modeProvider = new MutableLocaleModeProvider(BppChineseLocaleMode.Mainland);
L.Install(languageProvider, modeProvider);
var mainlandSample = InvokeStatic(textType, "FontAtlasSample") as string;
modeProvider.CurrentMode = BppChineseLocaleMode.Taiwan;
var taiwanSample = InvokeStatic(textType, "FontAtlasSample") as string;
Assert(
    !string.IsNullOrWhiteSpace(mainlandSample) && mainlandSample.Contains('对'),
    "Mainland font atlas sample should include simplified Chinese glyphs."
);
Assert(
    !string.IsNullOrWhiteSpace(taiwanSample) && taiwanSample.Contains('對'),
    "Taiwan font atlas sample should include traditional Chinese glyphs after a mode switch."
);
Assert(
    !string.Equals(mainlandSample, taiwanSample, StringComparison.Ordinal),
    "FontAtlasSample cache should vary by Chinese locale mode, not only language code."
);

var accountLinkType =
    textType.GetNestedType("AccountLink", BindingFlags.NonPublic | BindingFlags.Public)
    ?? throw new InvalidOperationException("HistoryPanelText.AccountLink should exist.");

languageProvider.CurrentLanguageCode = string.Empty;
modeProvider.CurrentMode = BppChineseLocaleMode.Mainland;
Assert(
    InvokeStatic(accountLinkType, "Title") as string == "Link BazaarDB account",
    "Account link title should resolve in English."
);
Assert(
    InvokeStatic(accountLinkType, "LinkedAs", "Player One") as string == "Linked as @Player One",
    "LinkedAs should resolve before interpolating the game display name."
);
Assert(
    InvokeStatic(accountLinkType, "LinkedAs", "") as string == "Linked to BazaarDB",
    "LinkedAs should use fallback copy when the game display name is blank."
);
Assert(
    InvokeStatic(accountLinkType, "Identity", "") as string == "This account",
    "Identity should use fallback copy when the game display name is blank."
);
Assert(
    InvokeStatic(accountLinkType, "InvalidOrExpired") as string
        == "Code invalid or expired - generate a new one",
    "Invalid code text should use repo-compatible ASCII punctuation."
);

languageProvider.CurrentLanguageCode = "zh-CN";
modeProvider.CurrentMode = BppChineseLocaleMode.Mainland;
Assert(
    InvokeStatic(accountLinkType, "Identity", "玩家") as string == "当前账号：@玩家",
    "Identity should resolve simplified Chinese before interpolation."
);
Assert(
    InvokeStatic(accountLinkType, "Offline") as string == "无法连接 BazaarDB，请检查网络",
    "Offline error should resolve simplified Chinese."
);

modeProvider.CurrentMode = BppChineseLocaleMode.Taiwan;
Assert(
    InvokeStatic(accountLinkType, "Relink") as string == "重新綁定",
    "Relink should resolve traditional Chinese when Taiwan mode is active."
);
Assert(
    InvokeStatic(accountLinkType, "ServerBusy") as string == "BazaarDB 暫時無法使用，請稍後重試",
    "ServerBusy should resolve traditional Chinese when Taiwan mode is active."
);

var storeType = RequireType(
    "BazaarPlusPlus.Game.HistoryPanel.AccountLink.BazaarDbAccountLinkStore"
);
Assert(
    InvokeStatic(storeType, "BuildPrefsKey", "player@example.com") as string
        == "BPP.HistoryPanel.BazaarDbLinkedName.player%40example.com",
    "Account link store should escape account-scoped PlayerPrefs keys."
);
Assert(
    InvokeStatic(storeType, "BuildPrefsKey", "   ") as string
        == "BPP.HistoryPanel.BazaarDbLinkedName.anonymous",
    "Account link store should use anonymous scope for blank account ids."
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

internal sealed class MutableLanguageProvider(string languageCode) : ILanguageProvider
{
    public string CurrentLanguageCode { get; set; } = languageCode;
}

internal sealed class MutableLocaleModeProvider(BppChineseLocaleMode mode) : ILocaleModeProvider
{
    public BppChineseLocaleMode CurrentMode { get; set; } = mode;
}
