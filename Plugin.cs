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
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.Online;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Upload;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Patches;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
    private BppComposition? _composition;
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

            CombatReplayRuntime? combatReplayRuntime = null;
            _composition = new BppComposition(Logger, configFile, () => combatReplayRuntime);

            var services = _composition.Services;
            BppLog.Install(services.Logger);
            BppPatchHost.Install(services);

            InstallStaticUtilities(services);

            ApplyHarmonyPatches();

            BppLog.Info("Plugin", "Adding CombatReplayRuntime");
            combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();
            combatReplayRuntime.Initialize(services, _composition.RunLifecycle);

            _composition.Start();

            BuildIdentityAndOnlineServices(services);
            AttachRuntimeComponents(services, combatReplayRuntime);
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
        try
        {
            DetachRuntimeComponents();
            _composition?.Dispose();
            _composition = null;
            DisposeIdentityAndOnlineServices();
            UnpatchHarmony();
        }
        finally
        {
            BppLog.Flush();
            BppPatchHost.Reset();
        }
    }

    private ConfigFile CreatePluginConfigFile()
    {
        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
        HistoryPanelPreviewSettings.Initialize(configFile);
        return configFile;
    }

    private static void InstallStaticUtilities(IBppServices services)
    {
        CardsJsonCache.Install(services.Paths);
        LegendaryPositionDisplayFormatter.Install(services.Config);
        BppChineseLocalization.Install(services.Config);
        BppSettingsDockCatalog.Install(services.Config);
        BppHotkeyService.Install(services.Config);
        RunLoggingGameDataReader.Install(services.RunContext);
    }

    private void BuildIdentityAndOnlineServices(IBppServices services)
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
        IBppServices services,
        CombatReplayRuntime combatReplayRuntime
    )
    {
        BppLog.Info("Plugin", "Attaching runtime components");

        var runLogging = gameObject.AddComponent<RunLoggingController>();
        runLogging.Initialize(services);

        var runUpload = gameObject.AddComponent<RunUploadController>();
        runUpload.Initialize(services);

        AddConfiguredPlayerObservationController();
        AddConfiguredHistoryPanel(services, combatReplayRuntime);

        var statusBar = gameObject.AddComponent<CombatStatusBar>();
        statusBar.Initialize(services);

        gameObject.AddComponent<MonsterPreviewWarmupController>();
        gameObject.AddComponent<CardSetPreviewRuntime>();

        var itemBoardRuntime = gameObject.AddComponent<MonsterPreviewItemBoardRuntime>();
        itemBoardRuntime.Initialize(services);

        var screenshot = gameObject.AddComponent<EndOfRunScreenshotController>();
        screenshot.Initialize(services);

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
        IBppServices services,
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
        _composition?.Dispose();
        _composition = null;
        DisposeIdentityAndOnlineServices();
        UnpatchHarmony();
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
