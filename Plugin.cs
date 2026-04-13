#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Identity;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Upload;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Tooltips;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
    private BppRuntimeHost? _runtimeHost;
    private bool _patchesApplied;

    protected virtual void Awake()
    {
        try
        {
            BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");
            BppPluginVersion.Initialize(Info.Location);

            var configFile = CreatePluginConfigFile();
            var runtime = CreateAndStartRuntime(configFile);

            ApplyHarmonyPatches();
            AttachRuntimeComponents(runtime.Services, runtime.CombatReplayRuntime);
            BppLog.Info("Plugin", "Plugin initialization completed");
        }
        catch (Exception ex)
        {
            BppLog.Error("Plugin", "Plugin initialization failed", ex);
            CleanupFailedInitialization();
            throw;
        }
    }

    protected virtual void OnDestroy()
    {
        StopRuntimeHost();
        UnpatchHarmony();
        BppLog.Flush();
    }

    private ConfigFile CreatePluginConfigFile()
    {
        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
        HistoryPanelPreviewSettings.Initialize(configFile);
        return configFile;
    }

    private (BppRuntimeServices Services, CombatReplayRuntime CombatReplayRuntime) CreateAndStartRuntime(
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

        BppLog.Info("Plugin", "Installing runtime host");
        _runtimeHost.Install();

        BppLog.Info("Plugin", "Adding CombatReplayRuntime");
        combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();

        BppLog.Info("Plugin", "Starting runtime host");
        _runtimeHost.Start();

        return (_runtimeHost.Services, combatReplayRuntime);
    }

    private void ApplyHarmonyPatches()
    {
        BppLog.Info("Plugin", "Applying Harmony patches");
        _harmony.PatchAll();
        _patchesApplied = true;
        BppLog.Info("Plugin", "Harmony patches applied");
    }

    private void AttachRuntimeComponents(
        BppRuntimeServices services,
        CombatReplayRuntime combatReplayRuntime
    )
    {
        BppLog.Info("Plugin", "Attaching runtime components");
        gameObject.AddComponent<RunLoggingController>();
        gameObject.AddComponent<RunUploadController>();
        gameObject.AddComponent<PlayerObservationController>();
        AddConfiguredHistoryPanel(services, combatReplayRuntime);
        gameObject.AddComponent<CombatStatusBar>();
        gameObject.AddComponent<MonsterPreviewWarmupController>();
        gameObject.AddComponent<CardSetPreviewRuntime>();
        gameObject.AddComponent<MonsterPreviewItemBoardRuntime>();
        gameObject.AddComponent<EndOfRunScreenshotController>();
        AddConfiguredTooltipModifierRefreshController(services.Config);
        BppLog.Info("Plugin", "Runtime components attached");
    }

    private void AddConfiguredHistoryPanel(
        BppRuntimeServices services,
        CombatReplayRuntime combatReplayRuntime
    )
    {
        BppLog.Info("Plugin", "Adding HistoryPanel");
        var historyPanel = gameObject.AddComponent<HistoryPanel>();

        var historyPanelRuntime = new HistoryPanelRuntime(
            services.RunContext,
            services.Paths.RunLogDatabasePath,
            services.Paths.CombatReplayDirectoryPath,
            () => combatReplayRuntime
        );
        historyPanel.Configure(HistoryPanelFactory.Create(historyPanelRuntime));
    }

    private void AddConfiguredTooltipModifierRefreshController(IBppConfig config)
    {
        BppLog.Info("Plugin", "Adding TooltipModifierRefreshController");
        var tooltipModifierRefreshController =
            gameObject.AddComponent<TooltipModifierRefreshController>();
        tooltipModifierRefreshController.Initialize(config);
        BppLog.Info("Plugin", "TooltipModifierRefreshController initialized");
    }

    private void CleanupFailedInitialization()
    {
        DetachRuntimeComponents();
        StopRuntimeHost();
        UnpatchHarmony();
    }

    private void StopRuntimeHost()
    {
        _runtimeHost?.Stop();
        _runtimeHost = null;
    }

    private void UnpatchHarmony()
    {
        if (!_patchesApplied)
            return;

        _harmony.UnpatchSelf();
        _patchesApplied = false;
    }

    private void DetachRuntimeComponents()
    {
        DestroyComponentIfPresent<TooltipModifierRefreshController>();
        DestroyComponentIfPresent<EndOfRunScreenshotController>();
        DestroyComponentIfPresent<MonsterPreviewItemBoardRuntime>();
        DestroyComponentIfPresent<CardSetPreviewRuntime>();
        DestroyComponentIfPresent<MonsterPreviewWarmupController>();
        DestroyComponentIfPresent<CombatStatusBar>();
        DestroyComponentIfPresent<HistoryPanel>();
        DestroyComponentIfPresent<PlayerObservationController>();
        DestroyComponentIfPresent<RunUploadController>();
        DestroyComponentIfPresent<RunLoggingController>();
        DestroyComponentIfPresent<CombatReplayRuntime>();
    }

    private void DestroyComponentIfPresent<T>()
        where T : Component
    {
        var component = GetComponent<T>();
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
    }
}
