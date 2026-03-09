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
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        _harmony.PatchAll();

        var configFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "BazaarPlusPlus.cfg"), true);
        ModState.Initialize(configFile);

        ItemDatabase.Load();
        EncounterTracker.Subscribe();
        gameObject.AddComponent<DebugOverlay>();
    }
}
