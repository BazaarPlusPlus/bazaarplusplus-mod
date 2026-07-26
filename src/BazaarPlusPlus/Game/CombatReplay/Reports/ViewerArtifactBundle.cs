#nullable enable
using System.Reflection;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed class ViewerArtifactBundle
{
    internal const int CurrentReportSchemaVersion = 1;
    private const string ResourceNamePrefix = "BazaarPlusPlus.Resources.CombatReplayReport.";
    private const string ViewerResourceName = ResourceNamePrefix + "viewer.js";
    private const string StylesheetResourceName = ResourceNamePrefix + "viewer.css";
    private static readonly Lazy<ViewerArtifactBundle> DefaultBundle = new(() =>
        CreateDefault(typeof(ViewerArtifactBundle).Assembly)
    );
    private readonly byte[] _scriptBytes;
    private readonly byte[] _stylesheetBytes;

    internal ViewerArtifactBundle(
        int reportSchemaVersion,
        byte[] scriptBytes,
        byte[] stylesheetBytes
    )
    {
        if (reportSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(reportSchemaVersion));
        if (scriptBytes == null)
            throw new ArgumentNullException(nameof(scriptBytes));
        if (stylesheetBytes == null)
            throw new ArgumentNullException(nameof(stylesheetBytes));
        if (scriptBytes.Length == 0)
            throw new InvalidDataException("Viewer script bytes are empty.");
        if (stylesheetBytes.Length == 0)
            throw new InvalidDataException("Viewer stylesheet bytes are empty.");

        ReportSchemaVersion = reportSchemaVersion;
        _scriptBytes = (byte[])scriptBytes.Clone();
        _stylesheetBytes = (byte[])stylesheetBytes.Clone();
        ScriptSha256 = StaticReportIntegrity.Sha256(_scriptBytes);
        StylesheetSha256 = StaticReportIntegrity.Sha256(_stylesheetBytes);
        BundleId = ComputeBundleId(reportSchemaVersion, ScriptSha256, StylesheetSha256);
    }

    internal static string ComputeBundleId(
        int reportSchemaVersion,
        string scriptSha256,
        string stylesheetSha256
    )
    {
        if (reportSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(reportSchemaVersion));
        StaticReportPaths.ParseSha256(scriptSha256, nameof(scriptSha256));
        StaticReportPaths.ParseSha256(stylesheetSha256, nameof(stylesheetSha256));
        return StaticReportIntegrity.Sha256(
            Encoding.ASCII.GetBytes(
                reportSchemaVersion + "\n" + scriptSha256 + "\n" + stylesheetSha256 + "\n"
            )
        );
    }

    internal int ReportSchemaVersion { get; }

    internal byte[] ScriptBytes => (byte[])_scriptBytes.Clone();

    internal byte[] StylesheetBytes => (byte[])_stylesheetBytes.Clone();

    internal string ScriptSha256 { get; }

    internal string StylesheetSha256 { get; }

    /// <summary>
    /// Content identity for the complete Viewer generation. Reports reference this immutable
    /// generation only after both artifacts have been published successfully.
    /// </summary>
    internal string BundleId { get; }

    internal static ViewerArtifactBundle CreateDefault() => DefaultBundle.Value;

    internal static ViewerArtifactBundle CreateDefault(Assembly assembly)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        return new ViewerArtifactBundle(
            reportSchemaVersion: CurrentReportSchemaVersion,
            ReadRequiredResource(assembly, ViewerResourceName),
            ReadRequiredResource(assembly, StylesheetResourceName)
        );
    }

    private static byte[] ReadRequiredResource(Assembly assembly, string manifestResourceName)
    {
        using var stream = assembly.GetManifestResourceStream(manifestResourceName);
        if (stream == null)
            throw new InvalidOperationException(
                "Required embedded Viewer resource '" + manifestResourceName + "' is missing."
            );

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        if (buffer.Length == 0)
            throw new InvalidDataException(
                "Required embedded Viewer resource '" + manifestResourceName + "' is empty."
            );
        return buffer.ToArray();
    }
}
