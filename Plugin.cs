#pragma warning disable CS0436
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace BazaarPlusPlus;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

    protected virtual void Awake()
    {
        ModState.Logger = Logger;
        BppLog.Info("Plugin", $"Plugin {MyPluginInfo.PLUGIN_GUID} loaded");

        _harmony.PatchAll();

        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
        ModState.Initialize(configFile);
        ModState.Subscribe();
        CombatStatusBar.InitializeConfig(configFile);

        MonsterDatabase.Load();
        EncounterTracker.Subscribe();
        gameObject.AddComponent<CombatStatusBar>();
        gameObject.AddComponent<MonsterPreviewController>();
        gameObject.AddComponent<MonsterLockShowcaseRuntime>();

        if (ModState.IsDebug)
        {
            gameObject.AddComponent<DebugPanel>();
            gameObject.AddComponent<MonsterPreviewDebugController>();
        }

        BppLog.Info("Plugin", "MonsterPreview components attached");
    }

    protected virtual void OnDestroy()
    {
        BppLog.Flush();
    }
}
