#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

internal static class RandomHeroPoolPlayerPrefs
{
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomHeroPool.Selected";
    private const string KnownUnlockedPrefsKeyPrefix = "BPP.RandomHeroPool.KnownUnlocked";
    private const string AnonymousAccountScope = "anonymous";

    public static IReadOnlyCollection<string>? LoadSelectedHeroIds()
    {
        return LoadHeroIdCollection(BuildScopedPrefsKey(SelectedPoolPrefsKeyPrefix));
    }

    public static void SaveSelectedHeroIds(IEnumerable<string> heroIds)
    {
        SaveHeroIdCollection(BuildScopedPrefsKey(SelectedPoolPrefsKeyPrefix), heroIds);
    }

    public static IReadOnlyCollection<string>? LoadKnownUnlockedHeroIds()
    {
        return LoadHeroIdCollection(BuildScopedPrefsKey(KnownUnlockedPrefsKeyPrefix));
    }

    public static void SaveKnownUnlockedHeroIds(IEnumerable<string> heroIds)
    {
        SaveHeroIdCollection(BuildScopedPrefsKey(KnownUnlockedPrefsKeyPrefix), heroIds);
    }

    public static IReadOnlyList<string> ResolveEffectivePool(IEnumerable<string> unlockedHeroIds)
    {
        var unlockedHeroIdArray = unlockedHeroIds
            .Where(heroId => !string.IsNullOrWhiteSpace(heroId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unlockedHeroIdArray.Length == 0)
        {
            return Array.Empty<string>();
        }

        var mergedPool = RandomHeroPoolPreferences.MergeWithKnownUnlockedHeroIds(
            unlockedHeroIdArray,
            LoadSelectedHeroIds(),
            LoadKnownUnlockedHeroIds()
        );
        SaveSelectedHeroIds(mergedPool);
        SaveKnownUnlockedHeroIds(unlockedHeroIdArray);

        return new RandomHeroPoolSelector().BuildCandidateHeroIds(unlockedHeroIdArray, mergedPool);
    }

    private static IReadOnlyCollection<string>? LoadHeroIdCollection(string key)
    {
        if (!PlayerPrefs.HasKey(key))
            return null;

        var raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<string[]>(raw);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroPool", $"Failed to parse saved random hero pool '{key}': {ex.Message}");
            return null;
        }
    }

    private static void SaveHeroIdCollection(string key, IEnumerable<string> heroIds)
    {
        var normalized = heroIds
            .Where(heroId => !string.IsNullOrWhiteSpace(heroId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            PlayerPrefs.DeleteKey(key);
        }
        else
        {
            PlayerPrefs.SetString(key, JsonConvert.SerializeObject(normalized));
        }

        PlayerPrefs.Save();
    }

    private static string BuildScopedPrefsKey(string keyPrefix)
    {
        return $"{keyPrefix}.{ResolveAccountScopeForPrefs()}";
    }

    private static string ResolveAccountScopeForPrefs()
    {
        try
        {
            var dataType = AccessTools.TypeByName("Data");
            if (dataType == null)
                return AnonymousAccountScope;

            var profileProperty = AccessTools.Property(dataType, "Profile");
            var profile = profileProperty?.GetValue(null);
            if (profile == null)
                return AnonymousAccountScope;

            var accountIdProperty = AccessTools.Property(profile.GetType(), "AccountId");
            var accountId = accountIdProperty?.GetValue(profile)?.ToString();
            if (!string.IsNullOrWhiteSpace(accountId))
                return Uri.EscapeDataString(accountId);

            var usernameProperty = AccessTools.Property(profile.GetType(), "Username");
            var username = usernameProperty?.GetValue(profile)?.ToString();
            if (!string.IsNullOrWhiteSpace(username))
                return Uri.EscapeDataString(username);
        }
        catch
        {
        }

        return AnonymousAccountScope;
    }
}
