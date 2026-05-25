#nullable enable
using System;

namespace BazaarPlusPlus.ModApi;

public sealed class ModApiRoutes
{
    private ModApiRoutes(Uri apiBaseUri)
    {
        ApiBaseUri = apiBaseUri;
        UploadRunBundle = BuildAbsolute("/run-bundles");
        QueryGhostBattles = BuildAbsolute("/ghost-battles");
        UploadBazaarDbScreenshot = BuildAbsolute("/bazaardb-screenshots");
        BazaarDbManifestBase = BuildAbsolute("/bazaardb/manifest");
    }

    public Uri ApiBaseUri { get; }

    public string UploadRunBundle { get; }

    public string QueryGhostBattles { get; }

    public string UploadBazaarDbScreenshot { get; }

    public string BazaarDbManifestBase { get; }

    public string CreateReplayLink(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            throw new ArgumentException("Battle id is required.", nameof(battleId));

        return BuildAbsolute($"/ghost-battles/{Uri.EscapeDataString(battleId.Trim())}/replay-link");
    }

    public static ModApiRoutes? TryCreate(string? apiBaseUrl)
    {
        if (
            string.IsNullOrWhiteSpace(apiBaseUrl)
            || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri)
            || (apiBaseUri.Scheme != Uri.UriSchemeHttps && apiBaseUri.Scheme != Uri.UriSchemeHttp)
        )
        {
            return null;
        }

        return new ModApiRoutes(
            new UriBuilder(apiBaseUri) { Path = string.Empty, Query = string.Empty }.Uri
        );
    }

    private string BuildAbsolute(string path)
    {
        return new Uri(ApiBaseUri, path).ToString();
    }
}
