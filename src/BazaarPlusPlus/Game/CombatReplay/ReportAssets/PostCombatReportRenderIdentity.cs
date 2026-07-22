#nullable enable
using System.Globalization;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

internal sealed record PostCombatReportRenderIdentity(
    TCardBase Template,
    string GameDataIdentity,
    string ResolvedTemplateVersion,
    string ResolvedSkinIdentity
);

internal static class PostCombatReportRenderIdentityResolver
{
    internal static bool TryResolve(
        Guid templateId,
        string gameBuild,
        out PostCombatReportRenderIdentity identity
    )
    {
        identity = null!;
        if (templateId == Guid.Empty)
            return false;

        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            var template = BppStaticDataAccess.GetCardTemplate(staticData, templateId);
            if (template == null)
                return false;

            var sourceInfo = BppStaticDataAccess.TryCaptureGameDataSourceInfo(staticData);
            var gameDataIdentity = ResolveGameDataIdentity(
                sourceInfo,
                gameBuild,
                template.Version,
                template.ArtKey
            );
            var artKey = Normalize(template.ArtKey, "invalid-art-key");
            identity = new PostCombatReportRenderIdentity(
                template,
                gameDataIdentity,
                Normalize(template.Version, "unknown-template-version"),
                $"native-art-key:{artKey}|premium:false"
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveGameDataIdentity(
        BppGameDataSourceInfo? sourceInfo,
        string gameBuild,
        string? templateVersion,
        string? artKey
    )
    {
        var resource =
            sourceInfo == null
                ? string.Empty
                : (sourceInfo.DataBaseUrl ?? string.Empty).TrimEnd('/') + "/GameData.db.zip";
        if (sourceInfo != null && TryReadGameDataEtag(sourceInfo.ManifestPath, out var etag))
            return $"etag:{resource}|{etag}";

        if (
            sourceInfo != null
            && !string.IsNullOrWhiteSpace(sourceInfo.DatabasePath)
            && File.Exists(sourceInfo.DatabasePath)
        )
        {
            try
            {
                var info = new FileInfo(sourceInfo.DatabasePath);
                if (
                    (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                    == 0
                )
                {
                    return "file:"
                        + resource
                        + "|"
                        + info.Length.ToString(CultureInfo.InvariantCulture)
                        + "|"
                        + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) when (IsExpectedFileException(ex))
            {
                // Fall back to the renderer-visible template identity below.
            }
        }

        return "fallback:"
            + Normalize(gameBuild, "unknown-build")
            + "|"
            + Normalize(templateVersion, "unknown-template-version")
            + "|"
            + Normalize(artKey, "invalid-art-key");
    }

    private static bool TryReadGameDataEtag(string manifestPath, out string etag)
    {
        etag = string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return false;

        try
        {
            var document = JObject.Parse(File.ReadAllText(manifestPath));
            etag = document.SelectToken("Entries.GameData.ETag")?.Value<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(etag);
        }
        catch (Exception ex)
            when (IsExpectedFileException(ex) || ex is Newtonsoft.Json.JsonException)
        {
            etag = string.Empty;
            return false;
        }
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsExpectedFileException(Exception exception) =>
        exception is IOException
        || exception is UnauthorizedAccessException
        || exception is NotSupportedException
        || exception is ArgumentException;
}
