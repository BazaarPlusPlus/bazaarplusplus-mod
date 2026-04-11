#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.Identity;

internal static class PlayerAccountIdResolver
{
    internal static string? Resolve(
        string? cachedPlayerAccountId,
        InstallationRecordStore? installationStore
    )
    {
        var normalizedCachedPlayerAccountId = cachedPlayerAccountId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedCachedPlayerAccountId))
            return normalizedCachedPlayerAccountId;

        if (
            installationStore != null
            && installationStore.TryLoad(out var installation)
            && installation != null
        )
        {
            var normalizedInstallationPlayerAccountId = installation.PlayerAccountId?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedInstallationPlayerAccountId))
                return normalizedInstallationPlayerAccountId;
        }

        return null;
    }

    internal static string? ResolveCurrent(InstallationRecordStore? installationStore = null)
    {
        try
        {
            return Resolve(BppClientCacheBridge.TryGetProfileAccountId(), installationStore);
        }
        catch
        {
            return Resolve(null, installationStore);
        }
    }
}
