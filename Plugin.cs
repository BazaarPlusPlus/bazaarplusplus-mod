#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatLog;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.RunLogging;
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
        ModState.Logger = Logger;
        BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _ = CheckNetworkAsync();

        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
        ModState.Initialize(configFile);
        ModState.Subscribe();
        CombatStatusBar.InitializeConfig(configFile);

        _runtimeHost = new BppRuntimeHost(gameObject, Logger, configFile);
        _runtimeHost.Install();
        _runtimeHost.Start();

        _harmony.PatchAll();

        MonsterDatabase.Load();
        EncounterTracker.Subscribe();
        gameObject.AddComponent<RunStateSyncController>();
        gameObject.AddComponent<RunLoggingController>();
        gameObject.AddComponent<CombatReplayRuntime>();
        gameObject.AddComponent<CombatLogController>();
        gameObject.AddComponent<CombatStatusBar>();
        gameObject.AddComponent<MonsterPreviewController>();
        gameObject.AddComponent<MonsterPreviewWarmupController>();
        gameObject.AddComponent<MonsterLockShowcaseRuntime>();
        gameObject.AddComponent<TooltipModifierRefreshController>();

        if (ModState.IsDebug)
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
            BppLog.Error("Network", $"Network check FAILED: {ex.GetType().Name} — {ex.Message}");
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
                "winhttp.dll not found in game directory — BepInEx may not be installed correctly."
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
                "winhttp.dll appears to be the system DLL — BepInEx proxy may have been removed by antivirus."
            );
    }
}
