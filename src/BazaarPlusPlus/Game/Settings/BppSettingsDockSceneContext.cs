#nullable enable
using System;
using TheBazaar;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockSceneContext
{
    private const string FallbackChestOpeningSceneName = "ChestOpening";
    private const string FallbackStoreSceneName = "Store";

    internal static BppSettingsDockSceneKind ResolveCurrentSceneKind()
    {
        var activeSceneName = SceneManager.GetActiveScene().name;
        return ResolveSceneKind(
            activeSceneName,
            ResolveSceneName(SceneID.ChestOpening),
            ResolveSceneName(SceneID.Store)
        );
    }

    internal static int ResolveCurrentSceneHandle() => SceneManager.GetActiveScene().handle;

    internal static BppSettingsDockSceneKind ResolveSceneKind(
        string activeSceneName,
        string? chestOpeningSceneName,
        string? storeSceneName
    )
    {
        if (
            IsSceneName(activeSceneName, chestOpeningSceneName)
            || IsSceneName(activeSceneName, FallbackChestOpeningSceneName)
            || IsSceneName(activeSceneName, storeSceneName)
            || IsSceneName(activeSceneName, FallbackStoreSceneName)
        )
            return BppSettingsDockSceneKind.RightDockStacked;

        return BppSettingsDockSceneKind.Default;
    }

    private static string? ResolveSceneName(SceneID sceneId)
    {
        try
        {
            return SceneLoader.CatalogSO?.GetName(sceneId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsSceneName(string activeSceneName, string? expectedSceneName) =>
        !string.IsNullOrEmpty(expectedSceneName)
        && string.Equals(activeSceneName, expectedSceneName, StringComparison.Ordinal);
}
