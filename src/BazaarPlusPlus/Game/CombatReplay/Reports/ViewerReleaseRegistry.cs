#nullable enable
using System.Reflection;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed class ViewerReleaseDefinition
{
    internal ViewerReleaseDefinition(
        string version,
        int reportSchemaVersion,
        byte[] scriptBytes,
        byte[] stylesheetBytes,
        int expectedScriptLength,
        string expectedScriptSha256,
        int expectedStylesheetLength,
        string expectedStylesheetSha256
    )
    {
        Version = StaticReportPaths.ParseViewerVersion(version);
        if (reportSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(reportSchemaVersion));
        ReportSchemaVersion = reportSchemaVersion;
        ScriptBytes = CloneAndVerify(
            scriptBytes,
            expectedScriptLength,
            expectedScriptSha256,
            "Viewer script"
        );
        StylesheetBytes = CloneAndVerify(
            stylesheetBytes,
            expectedStylesheetLength,
            expectedStylesheetSha256,
            "Viewer stylesheet"
        );
        ScriptSha256 = expectedScriptSha256;
        StylesheetSha256 = expectedStylesheetSha256;
    }

    internal string Version { get; }

    internal int ReportSchemaVersion { get; }

    internal byte[] ScriptBytes { get; }

    internal byte[] StylesheetBytes { get; }

    internal string ScriptSha256 { get; }

    internal string StylesheetSha256 { get; }

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
                description
                    + " bytes changed without a matching immutable Viewer version declaration."
            );
        }

        return (byte[])bytes.Clone();
    }
}

internal sealed class ViewerReleaseRegistry
{
    internal const string CurrentVersion = "7";
    private const string ResourceNamePrefix = "BazaarPlusPlus.Resources.CombatReplayReport.";
    private const string EChartsResourceName = ResourceNamePrefix + "echarts.min.js";
    private const string V1ViewerResourceName = ResourceNamePrefix + "v1.viewer.js";
    private const string V1StylesheetResourceName = ResourceNamePrefix + "v1.viewer.css";
    private const string V2ViewerResourceName = ResourceNamePrefix + "v2.viewer.js";
    private const string V2StylesheetResourceName = ResourceNamePrefix + "v2.viewer.css";
    private const string V3ViewerResourceName = ResourceNamePrefix + "v3.viewer.js";
    private const string V3StylesheetResourceName = ResourceNamePrefix + "v3.viewer.css";
    private const string V4ViewerResourceName = ResourceNamePrefix + "v4.viewer.js";
    private const string V4StylesheetResourceName = ResourceNamePrefix + "v4.viewer.css";
    private const string V5ViewerResourceName = ResourceNamePrefix + "v5.viewer.js";
    private const string V5StylesheetResourceName = ResourceNamePrefix + "v5.viewer.css";
    private const string V6ViewerResourceName = ResourceNamePrefix + "v6.viewer.js";
    private const string V6StylesheetResourceName = ResourceNamePrefix + "v6.viewer.css";
    private const string ViewerResourceName = ResourceNamePrefix + "viewer.js";
    private const string StylesheetResourceName = ResourceNamePrefix + "viewer.css";
    private static readonly byte[] ScriptSeparator = Encoding.UTF8.GetBytes("\n;\n");

    // These pins are deliberately source-controlled. If the classic Viewer bundle changes,
    // release engineering must add a new immutable version instead of mutating an installed release.
    private const int V1ScriptLength = 681695;
    private const string V1ScriptSha256 =
        "f0f7d55b4b39cb23684100375f9a2dcd1fc7aa7d647baaa46fef992a99079bbe";
    private const int V1StylesheetLength = 18271;
    private const string V1StylesheetSha256 =
        "6c4d071d959bff1e0bf948a611434f351e994040fdfd0e1b03f7ffe8d1659f33";
    private const int V2ScriptLength = 695696;
    private const string V2ScriptSha256 =
        "8870dd8f4d405a0302deaebeb5cde239d8911ddfb49543a2db882dda7edf5446";
    private const int V2StylesheetLength = 18848;
    private const string V2StylesheetSha256 =
        "620bd30d3dda0a45a43a58a9cd0a15798878d2e50c309ac3156474673ef9246b";
    private const int V3ScriptLength = 695898;
    private const string V3ScriptSha256 =
        "12ace1b651f97885f1e9d6ffb8c58d706e63aa9c728ab738580687a32676ddd3";
    private const int V3StylesheetLength = 22177;
    private const string V3StylesheetSha256 =
        "839dd079d662fac460b1c0358735775dd56105d685436b373c2a5d457f0463fc";
    private const int V4ScriptLength = 695898;
    private const string V4ScriptSha256 =
        "12ace1b651f97885f1e9d6ffb8c58d706e63aa9c728ab738580687a32676ddd3";
    private const int V4StylesheetLength = 21994;
    private const string V4StylesheetSha256 =
        "98fce05201d2920abafda97ea179c4579606c1cc94478fb1b8429ae513dfe4b9";
    private const int V5ScriptLength = 696544;
    private const string V5ScriptSha256 =
        "9e9dbe3109855442d64fcc12c1363a3f9f9a84f42bd4281c218a25b91b6cbe06";
    private const int V5StylesheetLength = 21994;
    private const string V5StylesheetSha256 =
        "98fce05201d2920abafda97ea179c4579606c1cc94478fb1b8429ae513dfe4b9";
    private const int V6ScriptLength = 716166;
    private const string V6ScriptSha256 =
        "ced58eb71afaabf81d8a904b28c56073488b163893072489c33572887e41be8d";
    private const int V6StylesheetLength = 25253;
    private const string V6StylesheetSha256 =
        "f52db5018f652893b0c867550e1ea6155d121c619eaacdd2c7f05b8383f8c3f0";
    private const int V7ScriptLength = 717495;
    private const string V7ScriptSha256 =
        "8541bd9bf65904ba178b2c2bdec0ada86d95cf3389b7517d209305461b0d5c60";
    private const int V7StylesheetLength = 25643;
    private const string V7StylesheetSha256 =
        "c865002fb089626d85d31cace8940add3547cc651be8a56547f9a8680701bd73";

    private readonly IReadOnlyDictionary<string, ViewerReleaseDefinition> _releases;

    internal ViewerReleaseRegistry(IReadOnlyList<ViewerReleaseDefinition> releases)
    {
        if (releases == null)
            throw new ArgumentNullException(nameof(releases));

        var byVersion = new Dictionary<string, ViewerReleaseDefinition>(StringComparer.Ordinal);
        for (var index = 0; index < releases.Count; index++)
        {
            var release =
                releases[index]
                ?? throw new ArgumentException(
                    "A Viewer release cannot be null.",
                    nameof(releases)
                );
            if (!byVersion.TryAdd(release.Version, release))
                throw new ArgumentException(
                    "Viewer release versions must be unique.",
                    nameof(releases)
                );
        }
        _releases = byVersion;
    }

    internal static ViewerReleaseRegistry CreateDefault() =>
        CreateDefault(typeof(ViewerReleaseRegistry).Assembly);

    internal static ViewerReleaseRegistry CreateDefault(Assembly assembly)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        var echarts = ReadRequiredResource(assembly, EChartsResourceName);
        var v1Script = BuildScript(echarts, ReadRequiredResource(assembly, V1ViewerResourceName));
        var v1Stylesheet = ReadRequiredResource(assembly, V1StylesheetResourceName);
        var v2Script = BuildScript(echarts, ReadRequiredResource(assembly, V2ViewerResourceName));
        var v2Stylesheet = ReadRequiredResource(assembly, V2StylesheetResourceName);
        var v3Script = BuildScript(echarts, ReadRequiredResource(assembly, V3ViewerResourceName));
        var v3Stylesheet = ReadRequiredResource(assembly, V3StylesheetResourceName);
        var v4Script = BuildScript(echarts, ReadRequiredResource(assembly, V4ViewerResourceName));
        var v4Stylesheet = ReadRequiredResource(assembly, V4StylesheetResourceName);
        var v5Script = BuildScript(echarts, ReadRequiredResource(assembly, V5ViewerResourceName));
        var v5Stylesheet = ReadRequiredResource(assembly, V5StylesheetResourceName);
        var v6Script = BuildScript(echarts, ReadRequiredResource(assembly, V6ViewerResourceName));
        var v6Stylesheet = ReadRequiredResource(assembly, V6StylesheetResourceName);
        var currentScript = BuildScript(
            echarts,
            ReadRequiredResource(assembly, ViewerResourceName)
        );
        var currentStylesheet = ReadRequiredResource(assembly, StylesheetResourceName);
        return new ViewerReleaseRegistry(
            new[]
            {
                new ViewerReleaseDefinition(
                    "1",
                    reportSchemaVersion: 1,
                    v1Script,
                    v1Stylesheet,
                    V1ScriptLength,
                    V1ScriptSha256,
                    V1StylesheetLength,
                    V1StylesheetSha256
                ),
                new ViewerReleaseDefinition(
                    "2",
                    reportSchemaVersion: 1,
                    v2Script,
                    v2Stylesheet,
                    V2ScriptLength,
                    V2ScriptSha256,
                    V2StylesheetLength,
                    V2StylesheetSha256
                ),
                new ViewerReleaseDefinition(
                    "3",
                    reportSchemaVersion: 1,
                    v3Script,
                    v3Stylesheet,
                    V3ScriptLength,
                    V3ScriptSha256,
                    V3StylesheetLength,
                    V3StylesheetSha256
                ),
                new ViewerReleaseDefinition(
                    "4",
                    reportSchemaVersion: 1,
                    v4Script,
                    v4Stylesheet,
                    V4ScriptLength,
                    V4ScriptSha256,
                    V4StylesheetLength,
                    V4StylesheetSha256
                ),
                new ViewerReleaseDefinition(
                    "5",
                    reportSchemaVersion: 1,
                    v5Script,
                    v5Stylesheet,
                    V5ScriptLength,
                    V5ScriptSha256,
                    V5StylesheetLength,
                    V5StylesheetSha256
                ),
                new ViewerReleaseDefinition(
                    "6",
                    reportSchemaVersion: 1,
                    v6Script,
                    v6Stylesheet,
                    V6ScriptLength,
                    V6ScriptSha256,
                    V6StylesheetLength,
                    V6StylesheetSha256
                ),
                new ViewerReleaseDefinition(
                    CurrentVersion,
                    reportSchemaVersion: 1,
                    currentScript,
                    currentStylesheet,
                    V7ScriptLength,
                    V7ScriptSha256,
                    V7StylesheetLength,
                    V7StylesheetSha256
                ),
            }
        );
    }

    private static byte[] BuildScript(byte[] echarts, byte[] viewer)
    {
        var script = new byte[echarts.Length + ScriptSeparator.Length + viewer.Length];
        Buffer.BlockCopy(echarts, 0, script, 0, echarts.Length);
        Buffer.BlockCopy(ScriptSeparator, 0, script, echarts.Length, ScriptSeparator.Length);
        Buffer.BlockCopy(viewer, 0, script, echarts.Length + ScriptSeparator.Length, viewer.Length);
        return script;
    }

    internal ViewerReleaseDefinition GetRequired(string version)
    {
        var normalized = StaticReportPaths.ParseViewerVersion(version);
        if (!_releases.TryGetValue(normalized, out var release))
            throw new KeyNotFoundException(
                "Unknown immutable Viewer version '" + normalized + "'."
            );
        return release;
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
