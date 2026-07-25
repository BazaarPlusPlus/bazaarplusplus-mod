#nullable enable
using System.Reflection;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed class ViewerArtifactBundle
{
    internal const int CurrentReportSchemaVersion = 1;
    private const string ResourceNamePrefix = "BazaarPlusPlus.Resources.CombatReplayReport.";
    private const string EChartsResourceName = ResourceNamePrefix + "echarts.min.js";
    private const string ViewerResourceName = ResourceNamePrefix + "viewer.js";
    private const string StylesheetResourceName = ResourceNamePrefix + "viewer.css";
    private const int EChartsLength = 500315;
    private const string EChartsSha256 =
        "5eef51bee09fb9cc234c4179a58ae0150126f49f88c992efe2d6bca81d8dd03b";
    private static readonly byte[] ScriptSeparator = Encoding.UTF8.GetBytes("\n;\n");
    private static readonly Lazy<ViewerArtifactBundle> DefaultBundle = new(() =>
        CreateDefault(typeof(ViewerArtifactBundle).Assembly)
    );
    private readonly byte[] _scriptBytes;
    private readonly byte[] _stylesheetBytes;

    internal ViewerArtifactBundle(
        int reportSchemaVersion,
        byte[] scriptBytes,
        byte[] stylesheetBytes,
        int expectedScriptLength,
        string expectedScriptSha256,
        int expectedStylesheetLength,
        string expectedStylesheetSha256
    )
    {
        if (reportSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(reportSchemaVersion));

        ReportSchemaVersion = reportSchemaVersion;
        _scriptBytes = CloneAndVerify(
            scriptBytes,
            expectedScriptLength,
            expectedScriptSha256,
            "Viewer script"
        );
        _stylesheetBytes = CloneAndVerify(
            stylesheetBytes,
            expectedStylesheetLength,
            expectedStylesheetSha256,
            "Viewer stylesheet"
        );
        ScriptSha256 = expectedScriptSha256;
        StylesheetSha256 = expectedStylesheetSha256;
        BundleId = ComputeBundleId(
            reportSchemaVersion,
            expectedScriptSha256,
            expectedStylesheetSha256
        );
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

        var echarts = ReadPinnedResource(
            assembly,
            EChartsResourceName,
            EChartsLength,
            EChartsSha256,
            "ECharts"
        );
        var script = BuildScript(echarts, ReadRequiredResource(assembly, ViewerResourceName));
        var stylesheet = ReadRequiredResource(assembly, StylesheetResourceName);
        var scriptSha256 = StaticReportIntegrity.Sha256(script);
        var stylesheetSha256 = StaticReportIntegrity.Sha256(stylesheet);
        return new ViewerArtifactBundle(
            reportSchemaVersion: CurrentReportSchemaVersion,
            script,
            stylesheet,
            script.Length,
            scriptSha256,
            stylesheet.Length,
            stylesheetSha256
        );
    }

    private static byte[] CloneAndVerify(
        byte[] bytes,
        int expectedLength,
        string expectedSha256,
        string description
    )
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));
        StaticReportPaths.ParseSha256(expectedSha256, nameof(expectedSha256));
        if (
            bytes.Length != expectedLength
            || !string.Equals(
                StaticReportIntegrity.Sha256(bytes),
                expectedSha256,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidDataException(
                description + " bytes changed without updating the Viewer integrity pin."
            );
        }

        return (byte[])bytes.Clone();
    }

    private static byte[] BuildScript(byte[] echarts, byte[] viewer)
    {
        var script = new byte[echarts.Length + ScriptSeparator.Length + viewer.Length];
        Buffer.BlockCopy(echarts, 0, script, 0, echarts.Length);
        Buffer.BlockCopy(ScriptSeparator, 0, script, echarts.Length, ScriptSeparator.Length);
        Buffer.BlockCopy(viewer, 0, script, echarts.Length + ScriptSeparator.Length, viewer.Length);
        return script;
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

    private static byte[] ReadPinnedResource(
        Assembly assembly,
        string manifestResourceName,
        int expectedLength,
        string expectedSha256,
        string description
    )
    {
        var bytes = ReadRequiredResource(assembly, manifestResourceName);
        StaticReportPaths.ParseSha256(expectedSha256, nameof(expectedSha256));
        if (
            bytes.Length != expectedLength
            || !string.Equals(
                StaticReportIntegrity.Sha256(bytes),
                expectedSha256,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidDataException(
                description + " bytes changed without updating its content identity."
            );
        }

        return bytes;
    }
}
