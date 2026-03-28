#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Upload;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.RunLifecycle;
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
        BppPluginVersion.Initialize(Info.Location);
        var configFile = CreateConfigFile();
        var runtime = InstallRuntimeHost(configFile);
        var services = runtime.Services;
        var lifecycleModule = runtime.LifecycleModule;
        var combatReplayRuntime = runtime.CombatReplayRuntime;
        BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");

        _harmony.PatchAll();
        AttachRuntimeComponents(services, lifecycleModule, () => combatReplayRuntime);
        AttachDebugComponents();
        BppLog.Info("Plugin", "Plugin components attached");
    }

    protected virtual void OnDestroy()
    {
        _runtimeHost?.Stop();
        BppLog.Flush();
    }

    private ConfigFile CreateConfigFile()
    {
        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
        HistoryPanelPreviewSettings.Initialize(configFile);
        return configFile;
    }

    private (
        BppRuntimeServices Services,
        RunLifecycleModule LifecycleModule,
        CombatReplayRuntime CombatReplayRuntime
    ) InstallRuntimeHost(
        ConfigFile configFile
    )
    {
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
        return (_runtimeHost.Services, _runtimeHost.LifecycleModule, combatReplayRuntime);
    }

    private void AttachRuntimeComponents(
        BppRuntimeServices services,
        RunLifecycleModule lifecycleModule,
        Func<CombatReplayRuntime?> combatReplayRuntimeAccessor
    )
    {
        var runStateSyncController = gameObject.AddComponent<RunStateSyncController>();
        runStateSyncController.Initialize(services.EventBus, lifecycleModule);

        gameObject.AddComponent<RunLoggingController>();
        gameObject.AddComponent<RunUploadController>();
        gameObject.AddComponent<CombatReplayUploadController>();
        gameObject
            .AddComponent<HistoryPanel>()
            .Configure(
                new HistoryPanelRuntime(
                    services.RunContext,
                    services.Paths.RunLogDatabasePath,
                    services.Paths.CombatReplayDirectoryPath,
                    combatReplayRuntimeAccessor
                )
            );
        gameObject.AddComponent<HistoryCollectionsEntryBridge>();
        gameObject.AddComponent<CombatStatusBar>();
        gameObject.AddComponent<MonsterPreviewController>();
        gameObject.AddComponent<MonsterPreviewWarmupController>();
        gameObject.AddComponent<MonsterLockShowcaseRuntime>();

        var tooltipModifierRefreshController =
            gameObject.AddComponent<TooltipModifierRefreshController>();
        tooltipModifierRefreshController.Initialize(services.Config);
    }

    private void AttachDebugComponents()
    {
        if (!BppBuild.IsDebug)
            return;

        gameObject.AddComponent<DebugPanel>();
        gameObject.AddComponent<MonsterPreviewDebugController>();
    }
}
