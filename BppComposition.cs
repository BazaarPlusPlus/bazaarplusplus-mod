#nullable enable
using System;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Paths;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.AutoBazaar;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.Encounter;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Game.RunLogging.Upload;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Game.Screenshots.Upload;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.UpgradePreview;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Storage.Paths;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

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

    public IBppServices Services => _services;
    public RunLifecycleModule RunLifecycle => _runLifecycle;
    public BppMountableRegistry Mountables => _mountables;
    public SettingsDockEntryRegistry SettingsDockRegistry => _settingsDockRegistry;

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
        _settingsDockRegistry.Register(new CombatStatusBarSettingsDockEntry());
        _settingsDockRegistry.Register(new ItemEnchantPreviewSettingsDockEntry());
        _settingsDockRegistry.Register(new LegendaryPositionSettingsDockEntry());
        _settingsDockRegistry.Register(new NameOverrideSettingsDockEntry());
        _settingsDockRegistry.Register(new UpgradePreviewSettingsDockEntry());

        _mountables.Register(new BazaarDbScreenshotUploadMount());
        _mountables.Register(new CardSetPreviewMount());
        _mountables.Register(new CombatReplayVideoRecorderMount());
        _mountables.Register(new CombatStatusBarMount());
        _mountables.Register(new EndOfRunScreenshotMount());
        _mountables.Register(new MonsterPreviewItemBoardMount());
        _mountables.Register(new MonsterPreviewWarmupMount());
        _mountables.Register(new RunLoggingMount());
        _mountables.Register(new RunUploadMount());

        // _mountables.Register(new AutoBazaarMount());
    }

    public void AttachCombatReplayRuntime(CombatReplayRuntime runtime) =>
        _combatReplayModule.AttachRuntime(runtime);

    public void Start() => _featureRegistry.Start();

    public void Dispose() => _featureRegistry.Stop();
}
