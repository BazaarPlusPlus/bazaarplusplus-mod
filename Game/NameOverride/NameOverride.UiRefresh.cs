using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.NameOverride;

internal static class NameOverrideUiRefresh
{
    internal static void TryRefreshVisibleHeroBanners()
    {
        var profileName = Data.Profile?.Username;
        if (string.IsNullOrWhiteSpace(profileName))
            return;

        var banners = Object.FindObjectsOfType<HeroBannerController>();
        if (banners == null || banners.Length == 0)
            return;

        foreach (var banner in banners)
        {
            if (banner == null || !banner.isActiveAndEnabled)
                continue;

            banner.SetHeroName(profileName, 0);
        }
    }
}
