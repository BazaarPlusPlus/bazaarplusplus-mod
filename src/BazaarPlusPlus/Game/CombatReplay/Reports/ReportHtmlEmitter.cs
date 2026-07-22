#nullable enable
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed class ReportHtmlEmitter
{
    public const string EmbeddedReportElementId = "bpp-report-data";

    public byte[] Emit(
        string title,
        string htmlLanguage,
        string embeddedReportEnvelopeJson,
        string viewerVersion
    )
    {
        if (title == null)
            throw new ArgumentNullException(nameof(title));
        if (htmlLanguage == null)
            throw new ArgumentNullException(nameof(htmlLanguage));
        if (embeddedReportEnvelopeJson == null)
            throw new ArgumentNullException(nameof(embeddedReportEnvelopeJson));

        var normalizedLanguage = NormalizeHtmlLanguage(htmlLanguage);
        var normalizedViewerVersion = StaticReportPaths.ParseViewerVersion(viewerVersion);
        var scriptUrl = StaticReportPaths.BuildViewerScriptRelativeUrl(normalizedViewerVersion);
        var stylesheetUrl = StaticReportPaths.BuildViewerStylesheetRelativeUrl(
            normalizedViewerVersion
        );

        var builder = new StringBuilder(4096 + embeddedReportEnvelopeJson.Length);
        builder
            .Append("<!doctype html>\n<html lang=\"")
            .Append(EscapeHtmlAttribute(normalizedLanguage))
            .Append("\">\n<head>\n");
        builder.Append(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; media-src 'self'; font-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'\">\n"
        );
        builder.Append("<meta charset=\"utf-8\">\n");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n");
        builder
            .Append("<meta name=\"bpp-report-viewer-version\" content=\"")
            .Append(EscapeHtmlAttribute(normalizedViewerVersion))
            .Append("\">\n");
        builder.Append("<title>").Append(EscapeHtmlText(title)).Append("</title>\n");
        builder
            .Append("<link rel=\"stylesheet\" href=\"")
            .Append(EscapeHtmlAttribute(stylesheetUrl))
            .Append("\">\n");

        builder.Append("</head>\n<body>\n");
        builder.Append("<main data-bpp-test-id=\"report-root\"></main>\n");
        builder
            .Append("<script type=\"application/json\" id=\"")
            .Append(EmbeddedReportElementId)
            .Append("\">")
            .Append(EscapeJsonForInertScript(embeddedReportEnvelopeJson))
            .Append("</script>\n");

        builder
            .Append("<script defer src=\"")
            .Append(EscapeHtmlAttribute(scriptUrl))
            .Append("\"></script>\n");

        builder.Append("</body>\n</html>\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
            builder.ToString()
        );
    }

    internal static string EscapeJsonForInertScript(string json)
    {
        if (json == null)
            throw new ArgumentNullException(nameof(json));

        StringBuilder? builder = null;
        for (var index = 0; index < json.Length; index++)
        {
            var replacement = ReplacementForJsonScriptCharacter(json[index]);
            if (replacement == null)
            {
                if (builder != null)
                    builder.Append(json[index]);
                continue;
            }

            if (builder == null)
            {
                builder = new StringBuilder(json.Length + 32);
                builder.Append(json, 0, index);
            }

            builder.Append(replacement);
        }

        return builder?.ToString() ?? json;
    }

    private static string? ReplacementForJsonScriptCharacter(char character)
    {
        switch (character)
        {
            case '<':
                return "\\u003c";
            case '>':
                return "\\u003e";
            case '&':
                return "\\u0026";
            case '\u2028':
                return "\\u2028";
            case '\u2029':
                return "\\u2029";
            default:
                return null;
        }
    }

    private static string EscapeHtmlText(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string EscapeHtmlAttribute(string value)
    {
        return EscapeHtmlText(value).Replace("\"", "&quot;").Replace("'", "&#39;");
    }

    private static string NormalizeHtmlLanguage(string value)
    {
        var normalized = value.Replace('_', '-');
        if (
            normalized.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "zh-Hant";
        }
        if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";
        return "en";
    }
}
