#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using System.Net.Http;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Identity;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.Online;
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
    private IdentityDatabase? _identityDatabase;
    private ModOnlineClient? _onlineClient;
    private AuthStore? _authStore;
    private PlayerObservationStore? _playerObservationStore;
    private bool _patchesApplied;

    protected virtual void Awake()
    {
        try
        {
            BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");
            BppPluginVersion.Initialize(Info.Location);

            var configFile = CreatePluginConfigFile();
            var runtime = CreateAndStartRuntime(configFile);

            BuildIdentityAndOnlineServices(runtime.Services);

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
        DisposeIdentityAndOnlineServices();
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

    private void BuildIdentityAndOnlineServices(BppRuntimeServices services)
    {
        var identityDatabasePath = services.Paths.IdentityDatabasePath;
        if (string.IsNullOrWhiteSpace(identityDatabasePath))
        {
            BppLog.Warn(
                "Plugin",
                "Identity database path unavailable; online services will be inactive."
            );
            return;
        }

        _identityDatabase = new IdentityDatabase(identityDatabasePath);
        _identityDatabase.Open();
        _authStore = new AuthStore(_identityDatabase);
        _playerObservationStore = new PlayerObservationStore(_identityDatabase);

        var routes = V3Routes.TryCreate(V3UploadDefaults.ApiBaseUrl);
        if (routes == null)
        {
            BppLog.Warn("Plugin", "V3 API base URL invalid; online services will be inactive.");
            return;
        }

        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(10, V3UploadDefaults.RequestTimeoutSeconds)),
        };
        _onlineClient = new ModOnlineClient(httpClient, routes);
        BppLog.Info("Plugin", "Identity database opened and online client ready.");
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
        AddConfiguredPlayerObservationController();
        AddConfiguredHistoryPanel(services, combatReplayRuntime);
        gameObject.AddComponent<CombatStatusBar>();
        gameObject.AddComponent<MonsterPreviewWarmupController>();
        gameObject.AddComponent<CardSetPreviewRuntime>();
        gameObject.AddComponent<MonsterPreviewItemBoardRuntime>();
        gameObject.AddComponent<EndOfRunScreenshotController>();
        AddConfiguredTooltipModifierRefreshController(services.Config);
        BppLog.Info("Plugin", "Runtime components attached");
    }

    private void AddConfiguredPlayerObservationController()
    {
        var controller = gameObject.AddComponent<PlayerObservationController>();
        if (_playerObservationStore == null || _authStore == null || _onlineClient == null)
        {
            BppLog.Warn(
                "Plugin",
                "Skipping PlayerObservationController configuration; identity services unavailable."
            );
            return;
        }

        controller.Configure(_playerObservationStore, _authStore, _onlineClient);
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

        if (_onlineClient == null || _authStore == null)
        {
            BppLog.Warn(
                "Plugin",
                "Skipping HistoryPanel online wiring; identity services unavailable."
            );
            return;
        }

        historyPanel.Configure(
            HistoryPanelFactory.Create(historyPanelRuntime, _onlineClient, _authStore)
        );
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
        DisposeIdentityAndOnlineServices();
        UnpatchHarmony();
    }

    private void StopRuntimeHost()
    {
        _runtimeHost?.Stop();
        _runtimeHost = null;
    }

    private void DisposeIdentityAndOnlineServices()
    {
        _onlineClient?.Dispose();
        _onlineClient = null;
        _authStore = null;
        _playerObservationStore = null;
        _identityDatabase?.Dispose();
        _identityDatabase = null;
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
