#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CardSetPreview;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Upload;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Screenshots.Upload;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.GameInterop.Encounter;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.Paths;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
#if BPP_AUTOBAZAAR_HOST
using BazaarPlusPlus.Game.AutoBazaarHost;
#endif


namespace BazaarPlusPlus;

internal sealed class BppComposition : IDisposable
{
    private readonly InMemoryBppEventBus _eventBus = new();
    private readonly BppConfig _config = new();
    private readonly BepInExPathProvider _paths = new();
    private readonly RunContextStore _runContext = new();
    private readonly GameStateProbe _gameStateProbe = new();
    private readonly EncounterStateProbe _encounterStateProbe = new();
    private readonly BppRuntimeServices _services;
    private readonly BppFeatureRegistry _featureRegistry = new();
    private readonly BppMountableRegistry _mountables = new();
    private readonly SettingsDockEntryRegistry _settingsDockRegistry = new();
    private readonly RunLifecycleModule _runLifecycle;
    private readonly CombatReplayModule _combatReplayModule;
    private readonly CombatStatusBarModule _combatStatusBarModule;
    private ModOnlineClient? _onlineClientRef;

    public IBppServices Services => _services;
    public RunLifecycleModule RunLifecycle => _runLifecycle;
    public BppMountableRegistry Mountables => _mountables;
    public SettingsDockEntryRegistry SettingsDockRegistry => _settingsDockRegistry;
    public ModOnlineClient? OnlineClient => _onlineClientRef;

    public BppComposition(ManualLogSource logger, ConfigFile configFile)
    {
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));
        if (configFile == null)
            throw new ArgumentNullException(nameof(configFile));

        _config.Initialize(configFile);
        _paths.Initialize();
        _runContext.Reset();

        _services = new BppRuntimeServices(
            _eventBus,
            _config,
            _paths,
            _runContext,
            _gameStateProbe,
            _encounterStateProbe,
            logger
        );

        _runLifecycle = new RunLifecycleModule(_eventBus, _gameStateProbe, _runContext);
        _combatReplayModule = new CombatReplayModule(_eventBus);
        _combatStatusBarModule = new CombatStatusBarModule(_eventBus, _runContext);

        _featureRegistry.Register(_runLifecycle);
        _featureRegistry.Register(_combatReplayModule);
        _featureRegistry.Register(_combatStatusBarModule);

        _settingsDockRegistry.Register(new BazaarDbScreenshotUploadSettingsDockEntry());
        _settingsDockRegistry.Register(new ChineseLocaleModeSettingsDockEntry(_eventBus));
        _settingsDockRegistry.Register(new CollectionPanelSettingsDockEntry());
        _settingsDockRegistry.Register(new CombatStatusBarSettingsDockEntry());
        _settingsDockRegistry.Register(new HistoryPanelSettingsDockEntry());
        _settingsDockRegistry.Register(new ItemEnchantPreviewSettingsDockEntry());
        _settingsDockRegistry.Register(new LegendaryPositionSettingsDockEntry());
        _settingsDockRegistry.Register(new NameOverrideSettingsDockEntry());

        _mountables.Register(
            new ComponentMount<BazaarDbScreenshotUploadController>((c, s) => c.Initialize(s))
        );
        _mountables.Register(new ComponentMount<CardSetPreviewRuntime>());
        _mountables.Register(new CollectionPanelMount());
        _mountables.Register(
            new ComponentMount<CombatReplayVideoRecorder>((c, s) => c.Initialize(s))
        );
        _mountables.Register(new ComponentMount<CombatStatusBar>((c, s) => c.Initialize(s)));
        _mountables.Register(
            new ComponentMount<EndOfRunScreenshotController>((c, s) => c.Initialize(s))
        );
        _mountables.Register(
            new HistoryPanelMount(
                combatReplayRuntime: () => _combatReplayModule.Runtime,
                onlineClient: () => _onlineClientRef
            )
        );
        _mountables.Register(new ComponentMount<RunLoggingController>((c, s) => c.Initialize(s)));
        _mountables.Register(new ComponentMount<RunUploadController>((c, s) => c.Initialize(s)));
        _mountables.Register(
            new ComponentMount<TooltipModifierRefreshController>(
                (c, s) => c.Initialize(s.Config, s.EncounterState)
            )
        );

#if BPP_AUTOBAZAAR_HOST
        _mountables.Register(new AutoBazaarHostMount(configFile));
#endif
    }

    public void AttachCombatReplayRuntime(CombatReplayRuntime runtime) =>
        _combatReplayModule.AttachRuntime(runtime);

    public void AttachOnlineClient(ModOnlineClient? client) => _onlineClientRef = client;

    public void Start() => _featureRegistry.Start();

    public void Dispose() => _featureRegistry.Stop();
}
