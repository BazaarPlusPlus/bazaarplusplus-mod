#nullable enable
using System;
using TheBazaar;
using UnityEngine.SceneManagement;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockSceneContext
{
    private const string FallbackChestOpeningSceneName = "ChestOpening";

    internal static BppSettingsDockSceneKind ResolveCurrentSceneKind()
    {
        var activeSceneName = SceneManager.GetActiveScene().name;
        return ResolveSceneKind(activeSceneName, ResolveChestOpeningSceneName());
    }

    internal static BppSettingsDockSceneKind ResolveSceneKind(
        string activeSceneName,
        string? chestOpeningSceneName
    )
    {
        if (
            IsSceneName(activeSceneName, chestOpeningSceneName)
            || IsSceneName(activeSceneName, FallbackChestOpeningSceneName)
        )
            return BppSettingsDockSceneKind.ChestOpening;

        return BppSettingsDockSceneKind.Default;
    }

    private static string? ResolveChestOpeningSceneName()
    {
        try
        {
            return SceneLoader.CatalogSO?.GetName(SceneID.ChestOpening);
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
