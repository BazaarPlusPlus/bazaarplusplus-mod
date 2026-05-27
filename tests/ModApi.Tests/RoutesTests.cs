#nullable enable
using System;
using BazaarPlusPlus.ModApi;

internal static class RoutesTests
{
    public static void Run()
    {
        var routes =
            ModApiRoutes.TryCreate("https://mod-api-v4.bazaarplusplus.com")
            ?? throw new InvalidOperationException("TryCreate returned null for valid URL");

        if (routes.UploadRunBundle != "https://mod-api-v4.bazaarplusplus.com/run-bundles")
            throw new InvalidOperationException(
                $"Unexpected UploadRunBundle: {routes.UploadRunBundle}"
            );

        if (routes.QueryGhostBattles != "https://mod-api-v4.bazaarplusplus.com/ghost-battles")
            throw new InvalidOperationException(
                $"Unexpected QueryGhostBattles: {routes.QueryGhostBattles}"
            );

        if (!routes.CreateReplayLink("b-1").EndsWith("/ghost-battles/b-1/replay-link"))
            throw new InvalidOperationException("Unexpected CreateReplayLink shape");

        if (ModApiRoutes.TryCreate(null) != null)
            throw new InvalidOperationException("TryCreate must return null for null input");
        if (ModApiRoutes.TryCreate("not-a-url") != null)
            throw new InvalidOperationException("TryCreate must return null for unparseable input");
    }
}
