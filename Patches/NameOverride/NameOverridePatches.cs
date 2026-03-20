#pragma warning disable CS0436
using HarmonyLib;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;

namespace BazaarPlusPlus;

internal static class NameOverrideHelper
{
    private const string ReplacementName = "Anonymous";

    public static bool TryGetReplacementName(string originalName, out string replacementName)
    {
        replacementName = null;

        if (BppRuntimeHost.Config.EnableNameOverrideConfig?.Value != true)
            return false;

        var profileName = Data.Profile?.Username;
        if (string.IsNullOrEmpty(profileName))
        {
            BppLog.Debug(
                "NameOverride",
                "Skipping replacement because profile username is unavailable"
            );
            return false;
        }

        if (originalName != profileName)
            return false;

        replacementName = ReplacementName;
        return true;
    }
}

[HarmonyPatch(typeof(HeroBannerController), "UpdatePlayer")]
public static class UpdatePlayerPatch
{
    [HarmonyPrefix]
    static bool Prefix(
        HeroBannerController __instance,
        ref string userName,
        ref int nameId,
        ref string titlePrefix,
        TheBazaar.ProfileData.ISeasonRank currentSeasonRank,
        int? leaderboardPosition
    )
    {
        if (!NameOverrideHelper.TryGetReplacementName(userName, out var replacementName))
            return true;

        userName = replacementName;
        nameId = 0;
        BppLog.Debug("NameOverride", $"UpdatePlayer replaced username with {replacementName}");
        return true;
    }
}

[HarmonyPatch(typeof(HeroBannerController), "SetHeroName")]
public static class SetHeroNamePatch
{
    [HarmonyPrefix]
    static bool Prefix(ref string newName, ref int usernameId)
    {
        if (!NameOverrideHelper.TryGetReplacementName(newName, out var replacementName))
            return true;

        newName = replacementName;
        usernameId = 0;
        BppLog.Debug("NameOverride", $"SetHeroName replaced username with {replacementName}");
        return true;
    }
}
