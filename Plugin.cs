#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatLog;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Upload;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Upload;
using BazaarPlusPlus.Game.Tooltips;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace BazaarPlusPlus;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
    private BppRuntimeHost? _runtimeHost;

    protected virtual void Awake()
    {
        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
        HistoryPanelPreviewSettings.Initialize(configFile);

        CombatReplayRuntime? combatReplayRuntime = null;
        _runtimeHost = new BppRuntimeHost(
            gameObject,
            Logger,
            configFile,
            () => combatReplayRuntime
        );
        _runtimeHost.Install();
        combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();
        _runtimeHost.Start();
        var services = _runtimeHost.Services;
        BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _ = CheckNetworkAsync();

        _harmony.PatchAll();

        var runStateSyncController = gameObject.AddComponent<RunStateSyncController>();
        runStateSyncController.Initialize(services.EventBus, _runtimeHost.LifecycleModule);
        gameObject.AddComponent<RunLoggingController>();
        gameObject.AddComponent<RunUploadController>();
        gameObject.AddComponent<CombatReplayUploadController>();
        var historyPanel = gameObject.AddComponent<HistoryPanel>();
        historyPanel.Configure(
            new PluginHistoryPanelRuntime(
                services.RunContext,
                services.Paths.RunLogDatabasePath ?? string.Empty,
                services.Paths.CombatReplayDirectoryPath ?? string.Empty,
                () => combatReplayRuntime
            )
        );
        gameObject.AddComponent<HistoryCollectionsEntryBridge>();
        gameObject.AddComponent<CombatStatusBar>();
        gameObject.AddComponent<CombatLogController>();
        gameObject.AddComponent<CombatLogOverlay>();
        gameObject.AddComponent<MonsterPreviewController>();
        gameObject.AddComponent<MonsterPreviewWarmupController>();
        gameObject.AddComponent<MonsterLockShowcaseRuntime>();
        var tooltipModifierRefreshController =
            gameObject.AddComponent<TooltipModifierRefreshController>();
        tooltipModifierRefreshController.Initialize(services.Config);

        if (BppBuild.IsDebug)
        {
            gameObject.AddComponent<DebugPanel>();
            gameObject.AddComponent<MonsterPreviewDebugController>();
        }

        BppLog.Info("Plugin", "MonsterPreview components attached");
    }

    protected virtual void OnDestroy()
    {
        _runtimeHost?.Stop();
        BppLog.Flush();
    }

    private static async Task CheckNetworkAsync()
    {
        CheckWinhttpProxy();

        // Test actual connectivity using the same HTTP stack the game uses
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var resp = await client.GetAsync("https://playthebazaar.com/");
            BppLog.Info("Network", $"Network check passed ({(int)resp.StatusCode})");
        }
        catch (Exception ex)
        {
            BppLog.Error("Network", $"Network check FAILED: {ex.GetType().Name} - {ex.Message}");
            BppLog.Error("Network", "This likely explains the login failure. Possible causes:");
            BppLog.Error("Network", "  1. Antivirus blocked or quarantined BepInEx winhttp.dll");
            BppLog.Error("Network", "  2. VPN or proxy software conflicts with winhttp.dll hook");
            BppLog.Error("Network", "  3. Corporate/school network firewall");
            BppLog.Error("Network", "Fix: temporarily disable antivirus, then reinstall BepInEx.");
        }
    }

    private static void CheckWinhttpProxy()
    {
        var gameDir = AppDomain.CurrentDomain.BaseDirectory;
        var proxyPath = Path.Combine(gameDir, "winhttp.dll");

        if (!File.Exists(proxyPath))
        {
            BppLog.Warn(
                "Network",
                "winhttp.dll not found in game directory - BepInEx may not be installed correctly."
            );
            return;
        }

        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(proxyPath);
        BppLog.Info(
            "Network",
            $"winhttp.dll: {info.FileDescription} v{info.FileVersion} by {info.CompanyName}"
        );
        if (info.CompanyName?.Contains("Microsoft") == true)
            BppLog.Warn(
                "Network",
                "winhttp.dll appears to be the system DLL - BepInEx proxy may have been removed by antivirus."
            );
    }

    private sealed class PluginHistoryPanelRuntime : IHistoryPanelRuntime
    {
        private readonly Core.RunContext.IRunContext _runContext;

        public PluginHistoryPanelRuntime(
            Core.RunContext.IRunContext runContext,
            string runLogDatabasePath,
            string combatReplayDirectoryPath,
            Func<CombatReplayRuntime?> combatReplayRuntimeAccessor
        )
        {
            _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
            RunLogDatabasePath = runLogDatabasePath ?? string.Empty;
            CombatReplayDirectoryPath = combatReplayDirectoryPath ?? string.Empty;
            CombatReplayRuntimeAccessor =
                combatReplayRuntimeAccessor
                ?? throw new ArgumentNullException(nameof(combatReplayRuntimeAccessor));
        }

        public bool IsInGameRun => _runContext.IsInGameRun;

        public string? CurrentServerRunId => _runContext.CurrentServerRunId;

        public string RunLogDatabasePath { get; }

        public string CombatReplayDirectoryPath { get; }

        public Func<CombatReplayRuntime?> CombatReplayRuntimeAccessor { get; }
    }
}
