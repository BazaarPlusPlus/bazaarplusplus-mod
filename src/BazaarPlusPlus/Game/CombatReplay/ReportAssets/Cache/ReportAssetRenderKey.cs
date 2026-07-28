#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

internal sealed record ReportAssetRenderAttribute(string Name, int Value);

internal sealed record ReportAssetCaptureProfile(
    string Name,
    string Version,
    int FixedCanvasWidth,
    int FixedCanvasHeight,
    int CapturePixelWidth,
    int CapturePixelHeight,
    int MaxWidthBasisPoints,
    int MaxHeightBasisPoints,
    int BackplatePaddingPixels,
    string BackplateColor,
    int ExportLayer,
    string ColorSpace,
    string Encoder,
    string EncoderVersion
);

internal sealed record ReportAssetRenderKey(
    int SchemaVersion,
    string GameBuild,
    string GameDataIdentity,
    string ResolvedTemplateVersion,
    string ResolvedSkinIdentity,
    string Renderer,
    string RendererVersion,
    string AssetType,
    string TemplateId,
    string Locale,
    string Size,
    string Tier,
    string Enchantment,
    string Socket,
    string Variant,
    ReportAssetCaptureProfile CaptureProfile,
    IReadOnlyList<ReportAssetRenderAttribute> Attributes
)
{
    internal static ReportAssetRenderKey CreateTemplateAsset(
        int schemaVersion,
        string gameBuild,
        string gameDataIdentity,
        string resolvedTemplateVersion,
        string resolvedSkinIdentity,
        string renderer,
        string rendererVersion,
        string assetType,
        string templateId,
        string size,
        string variant,
        ReportAssetCaptureProfile captureProfile
    ) =>
        new(
            schemaVersion,
            gameBuild,
            gameDataIdentity,
            resolvedTemplateVersion,
            resolvedSkinIdentity,
            renderer,
            rendererVersion,
            assetType,
            templateId,
            "und",
            size,
            "None",
            "None",
            "None",
            variant,
            captureProfile,
            Array.Empty<ReportAssetRenderAttribute>()
        );

    internal string CanonicalJson => ReportAssetCanonicalJson.Serialize(this);

    internal string RenderKeyHash => ReportAssetHash.Sha256Utf8(CanonicalJson);
}

internal static class ReportAssetCanonicalJson
{
    internal static string Serialize(ReportAssetRenderKey key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (key.CaptureProfile == null)
            throw new ArgumentException("Capture profile is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(key.TemplateId) || key.SchemaVersion <= 0)
        {
            throw new ArgumentException(
                "A stable template ID and positive schema are required.",
                nameof(key)
            );
        }
        var stableTemplateId = key.TemplateId.Trim();
        if (Guid.TryParse(stableTemplateId, out var templateId))
        {
            if (templateId == Guid.Empty)
                throw new ArgumentException("Stable template ID cannot be empty.", nameof(key));
            stableTemplateId = templateId.ToString("N");
        }

        var builder = new StringBuilder(1024);
        builder.Append('{');
        AppendNumber(builder, "schemaVersion", key.SchemaVersion, firstProperty: true);
        AppendString(builder, "gameBuild", key.GameBuild);
        AppendString(builder, "gameDataIdentity", key.GameDataIdentity);
        AppendString(builder, "resolvedTemplateVersion", key.ResolvedTemplateVersion);
        AppendString(builder, "resolvedSkinIdentity", key.ResolvedSkinIdentity);
        AppendString(builder, "renderer", key.Renderer);
        AppendString(builder, "rendererVersion", key.RendererVersion);
        AppendString(builder, "assetType", key.AssetType);
        AppendString(builder, "templateId", stableTemplateId);
        AppendString(builder, "locale", key.Locale);
        AppendString(builder, "size", key.Size);
        AppendString(builder, "tier", key.Tier);
        AppendString(builder, "enchantment", key.Enchantment);
        AppendString(builder, "socket", key.Socket);
        AppendString(builder, "variant", key.Variant);
        AppendPropertyName(builder, "captureProfile");
        AppendCaptureProfile(builder, key.CaptureProfile);
        AppendPropertyName(builder, "attributes");
        builder.Append('[');
        var first = true;
        foreach (
            var attribute in (key.Attributes ?? Array.Empty<ReportAssetRenderAttribute>())
                .Where(attribute => attribute != null)
                .OrderBy(attribute => attribute.Name ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(attribute => attribute.Value)
        )
        {
            if (!first)
                builder.Append(',');
            first = false;
            builder.Append('{');
            AppendString(builder, "name", attribute.Name, firstProperty: true);
            AppendNumber(builder, "value", attribute.Value);
            builder.Append('}');
        }
        builder.Append(']');
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendCaptureProfile(
        StringBuilder builder,
        ReportAssetCaptureProfile profile
    )
    {
        builder.Append('{');
        AppendString(builder, "name", profile.Name, firstProperty: true);
        AppendString(builder, "version", profile.Version);
        AppendNumber(builder, "fixedCanvasWidth", profile.FixedCanvasWidth);
        AppendNumber(builder, "fixedCanvasHeight", profile.FixedCanvasHeight);
        AppendNumber(builder, "capturePixelWidth", profile.CapturePixelWidth);
        AppendNumber(builder, "capturePixelHeight", profile.CapturePixelHeight);
        AppendNumber(builder, "maxWidthBasisPoints", profile.MaxWidthBasisPoints);
        AppendNumber(builder, "maxHeightBasisPoints", profile.MaxHeightBasisPoints);
        AppendNumber(builder, "backplatePaddingPixels", profile.BackplatePaddingPixels);
        AppendString(builder, "backplateColor", profile.BackplateColor);
        AppendNumber(builder, "exportLayer", profile.ExportLayer);
        AppendString(builder, "colorSpace", profile.ColorSpace);
        AppendString(builder, "encoder", profile.Encoder);
        AppendString(builder, "encoderVersion", profile.EncoderVersion);
        builder.Append('}');
    }

    private static void AppendString(
        StringBuilder builder,
        string propertyName,
        string? value,
        bool firstProperty = false
    )
    {
        if (!firstProperty)
            builder.Append(',');
        AppendQuoted(builder, propertyName);
        builder.Append(':');
        AppendQuoted(builder, value ?? string.Empty);
    }

    private static void AppendNumber(
        StringBuilder builder,
        string propertyName,
        int value,
        bool firstProperty = false
    )
    {
        if (!firstProperty)
            builder.Append(',');
        AppendQuoted(builder, propertyName);
        builder.Append(':');
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendPropertyName(StringBuilder builder, string propertyName)
    {
        builder.Append(',');
        AppendQuoted(builder, propertyName);
        builder.Append(':');
    }

    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(
                            ((int)character).ToString("x4", CultureInfo.InvariantCulture)
                        );
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        builder.Append('"');
    }
}

internal static class ReportAssetHash
{
    internal static string Sha256Utf8(string value)
    {
        using var hash = SHA256.Create();
        return ToLowerHex(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    internal static string Sha256File(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan
        );
        using var hash = SHA256.Create();
        return ToLowerHex(hash.ComputeHash(stream));
    }

    internal static bool IsLowerHexSha256(string? value)
    {
        if (value == null || value.Length != 64)
            return false;
        foreach (var character in value)
        {
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
                return false;
        }
        return true;
    }

    private static string ToLowerHex(byte[] bytes)
    {
        const string Hex = "0123456789abcdef";
        var result = new char[bytes.Length * 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            result[index * 2] = Hex[bytes[index] >> 4];
            result[index * 2 + 1] = Hex[bytes[index] & 0x0f];
        }
        return new string(result);
    }
}
