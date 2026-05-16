#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using TheBazaar;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.AutoBazaar;

/// <summary>Determines whether the game is sitting on the hero-select lobby
/// with no active session, i.e. it is safe to invoke
/// <c>GameInstance.Instance.StartNewRun()</c> via the dispatcher. All checks
/// are conservative — a false negative just means the action isn't offered
/// this tick, while a false positive would let StartNewRun fire when it
/// would be rejected client- or server-side.</summary>
internal static class AutoBazaarSceneProbe
{
    private const string HeroSelectSceneName = "HeroSelectScene";

    private static FieldInfo? _clientCacheProfileField;
    private static PropertyInfo? _profileValueProp;
    private static bool _reflectionAttempted;

    public static bool IsAtHeroSelectAndReadyForNewRun()
    {
        try
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (!string.Equals(sceneName, HeroSelectSceneName, StringComparison.Ordinal)) return false;

            if (AppState.CurrentState != null) return false;

            if (!TryReadProfileLoaded()) return false;

            return true;
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", "IsAtHeroSelectAndReadyForNewRun failed", ex);
            return false;
        }
    }

    private static bool TryReadProfileLoaded()
    {
        if (!_reflectionAttempted)
        {
            _reflectionAttempted = true;
            var clientCacheType = AccessTools.TypeByName("TheBazaar.ClientCache");
            if (clientCacheType is null)
            {
                BppLog.Info("AutoBazaar", "TheBazaar.ClientCache not found via reflection");
                return false;
            }
            _clientCacheProfileField = clientCacheType.GetField("Profile",
                BindingFlags.Static | BindingFlags.Public);
            if (_clientCacheProfileField is not null)
            {
                _profileValueProp = _clientCacheProfileField.FieldType.GetProperty("Value");
            }
        }
        if (_clientCacheProfileField is null || _profileValueProp is null) return false;
        var profileCache = _clientCacheProfileField.GetValue(null);
        if (profileCache is null) return false;
        var profile = _profileValueProp.GetValue(profileCache);
        return profile is not null;
    }
}
