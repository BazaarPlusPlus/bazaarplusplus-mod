#nullable enable
using System;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.AccountLink;

internal sealed class BazaarDbAccountLinkStore
{
    private const string AnonymousAccountScope = "anonymous";
    private const string PrefsKeyPrefix = "BPP.HistoryPanel.BazaarDbLinkedName";

    public void SaveHint(string accountId, string? displayName)
    {
        PlayerPrefs.SetString(BuildPrefsKey(accountId), NormalizeDisplayName(displayName));
        PlayerPrefs.Save();
    }

    public bool TryLoadHint(string accountId, out string? displayName)
    {
        var key = BuildPrefsKey(accountId);
        if (!PlayerPrefs.HasKey(key))
        {
            displayName = null;
            return false;
        }

        displayName = PlayerPrefs.GetString(key, string.Empty);
        return true;
    }

    public void Clear(string accountId)
    {
        PlayerPrefs.DeleteKey(BuildPrefsKey(accountId));
        PlayerPrefs.Save();
    }

    internal static string BuildPrefsKey(string? accountId)
    {
        var scope = string.IsNullOrWhiteSpace(accountId)
            ? AnonymousAccountScope
            : Uri.EscapeDataString(accountId);
        return $"{PrefsKeyPrefix}.{scope}";
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        return string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName;
    }
}
