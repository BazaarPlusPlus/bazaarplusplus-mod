#pragma warning disable CS0436
#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.ModApi.Http;
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
    private ModOnlineClient? _onlineClient;
    private BazaarDbLinkClient? _bazaarDbLinkClient;
    private bool _patchesApplied;

    protected virtual void Awake()
    {
        try
        {
            BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");
            BppPluginVersion.Initialize(Info.Location);

            var configFile = CreatePluginConfigFile();

            var gameBuild = GameBuildInfoResolver.Resolve();
            _composition = new BppComposition(Logger, configFile, gameBuild);

            var services = _composition.Services;
            BppLog.Install(services.Logger);
            BppPatchHost.Install(services);

            BppLog.Info("Plugin", $"Game build: '{gameBuild.RawVersion}' → {gameBuild.Channel}");
            if (gameBuild.DetectionWarning != null)
                BppLog.Warn("Plugin", gameBuild.DetectionWarning);

            InstallStaticUtilities(services, _composition.SettingsDockRegistry);

            ApplyHarmonyPatches();

            BppLog.Info("Plugin", "Adding CombatReplayRuntime");
            // CombatReplayRuntime is constructed before composition.Start() because RunLifecycle
            // and several features take a reference through CombatReplayModule. Not a mountable.
            var combatReplayRuntime = gameObject.AddComponent<CombatReplayRuntime>();
            combatReplayRuntime.Initialize(
                services,
                _composition.RunLifecycle,
                _composition.PvpBattleCatalog
            );
            _composition.AttachCombatReplayRuntime(combatReplayRuntime);

            _composition.Start();

            BuildOnlineServices();
            _composition.AttachOnlineClient(_onlineClient);
            _composition.AttachAccountLinkClient(_bazaarDbLinkClient);

            BppLog.Info("Plugin", "Attaching runtime components");
            _composition.Mountables.MountAll(gameObject, services);
            BppLog.Info("Plugin", "Runtime components attached");

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
        RunTeardownSteps();
        RunTeardownStep("flush logs", BppLog.Flush);
        BppPatchHost.Reset();
    }

    // Every step runs in isolation and Harmony is unpatched first: a throw in any single
    // step must never strand the "patches applied but static utilities uninstalled"
    // zombie state (patched game code rebuilding UI from reset catalogs).
    private void RunTeardownSteps()
    {
        RunTeardownStep("unpatch Harmony", UnpatchHarmony);
        RunTeardownStep(
            "unmount components",
            () => _composition?.Mountables.UnmountAll(gameObject)
        );
        RunTeardownStep(
            "destroy CombatReplayRuntime",
            DestroyComponentIfPresent<CombatReplayRuntime>
        );
        RunTeardownStep(
            "dispose composition",
            () =>
            {
                _composition?.Dispose();
                _composition = null;
            }
        );
        RunTeardownStep("dispose online services", DisposeOnlineServices);
        RunTeardownStep("uninstall static utilities", UninstallStaticUtilities);
    }

    private static void RunTeardownStep(string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            try
            {
                BppLog.Error("Plugin", $"Teardown step failed: {name}", ex);
            }
            catch
            {
                // Logging must never break teardown isolation (the log listener may
                // already be disposed during application quit).
            }
        }
    }

    private ConfigFile CreatePluginConfigFile()
    {
        return new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
    }

    private static void InstallStaticUtilities(
        IBppServices services,
        SettingsDockEntryRegistry settingsDockRegistry
    )
    {
        LegendaryPositionDisplayFormatter.Install(services.Config);
        L.Install(new GameLanguageProvider(), new ChineseLocaleModeProvider(services.Config));
        BppUiFont.Install(() =>
            services.Config.UiFontKindConfig?.Value ?? BppConfig.DefaultUiFontKind
        );
        BppSettingsDockCatalog.Install(services.Config, settingsDockRegistry);
        BPPSupporterCatalog.Install(services.Config);
        BppHotkeyService.Install(services.Config);
        RunLoggingGameDataReader.Install(services.RunContext, services.GameBuild);
    }

    private static void UninstallStaticUtilities()
    {
        LegendaryPositionDisplayFormatter.Reset();
        L.Reset();
        BppUiFont.Reset();
        BppSettingsDockCatalog.Reset();
        BPPSupporterCatalog.Reset();
        BppHotkeyService.Reset();
        RunLoggingGameDataReader.Reset();
    }

    private void BuildOnlineServices()
    {
        // BazaarDB account linking targets a different host (bazaardb.gg) and is independent of the
        // mod-api-v4 base URL, so build it regardless of mod-api routes validity. Its dedicated bare
        // HttpClient carries no mod-api auth/base address; a 30s timeout keeps a hung redeem from
        // stalling the link card for the HttpClient default of ~100s.
        var linkHttpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            userAgentSuffix: "BazaarDbLink",
            timeout: TimeSpan.FromSeconds(30)
        );
        _bazaarDbLinkClient = new BazaarDbLinkClient(
            linkHttpClient,
            new Uri(BazaarDbLinkClient.DefaultRedeemEndpoint)
        );

        var routes = ModApiRoutes.TryCreate(ModApiUploadDefaults.ApiBaseUrl);
        if (routes == null)
        {
            BppLog.Warn("Plugin", "ModApi base URL invalid; online services will be inactive.");
            return;
        }

        var httpClient = BppHttpClientFactory.Create(
            productVersion: BppPluginVersion.Current,
            userAgentSuffix: "OnlineClient",
            timeout: TimeSpan.FromSeconds(Math.Max(10, ModApiUploadDefaults.RequestTimeoutSeconds))
        );
        _onlineClient = new ModOnlineClient(httpClient, routes);
        BppLog.Info("Plugin", "Online client ready.");
    }

    private void ApplyHarmonyPatches()
    {
        BppLog.Info("Plugin", "Applying Harmony patches");
        // Patch classes are applied one by one instead of PatchAll(): PatchAll aborts at
        // the first failing class, leaving earlier classes applied and later ones not —
        // one broken game target (e.g. after a game update or on the PTR branch) must
        // degrade only its own feature, never the whole plugin. The flag is set before
        // the loop so a partial application is always unpatched during teardown.
        _patchesApplied = true;
        var failedClasses = 0;
        // GetTypesFromAssembly (not Assembly.GetTypes) tolerates types that fail to load;
        // CreateClassProcessor(type).Patch() is a no-op for non-patch types, so this
        // covers exactly PatchAll's discovery set.
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            try
            {
                _harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                failedClasses++;
                BppLog.Error("Plugin", $"Harmony patch class failed: {type.FullName}", ex);
            }
        }

        if (failedClasses > 0)
            BppLog.Warn(
                "Plugin",
                $"{failedClasses} Harmony patch class(es) failed to apply; the affected features are degraded, everything else continues."
            );
        BppLog.Info("Plugin", "Harmony patches applied");
    }

    private void CleanupFailedInitialization()
    {
        RunTeardownSteps();
    }

    private void DisposeOnlineServices()
    {
        _bazaarDbLinkClient?.Dispose();
        _bazaarDbLinkClient = null;
        _onlineClient?.Dispose();
        _onlineClient = null;
    }

    private void UnpatchHarmony()
    {
        if (!_patchesApplied)
            return;

        _harmony.UnpatchSelf();
        _patchesApplied = false;
    }

    private void DestroyComponentIfPresent<T>()
        where T : Component
    {
        var component = GetComponent<T>();
        if (component != null)
            UnityEngine.Object.DestroyImmediate(component);
    }
}
