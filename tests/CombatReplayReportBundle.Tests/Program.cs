using System.Text;
using System.Text.Json;
using BazaarPlusPlus.Game.CombatReplay.Reports;
using BazaarPlusPlus.Infrastructure.Files;

var failures = new List<string>();
var sandbox = Path.Combine(
    Path.GetTempPath(),
    "bpp-static-report-tests-" + Guid.NewGuid().ToString("N")
);
const string TestViewerScriptSha256 =
    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const string TestViewerStylesheetSha256 =
    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

try
{
    Directory.CreateDirectory(sandbox);
    VerifyStaticPaths();
    VerifyTypedSiblingUrls();
    VerifyHtmlEmitter();
    VerifyCurrentViewerReportIntegrityScan();
    VerifyViewerArtifactPinAndInstall();
    VerifyViewerThirdPartyNoticesAreEmbedded();
    VerifyViewerOpenGateVerifiesPinnedArtifacts();
    VerifyViewerStaticRuntimeBoundary();
    VerifyImmutableCommitConcurrency();
    VerifyImmutableCommitRejectsLinks();
    VerifyPhysicalFileChain();
}
finally
{
    if (Directory.Exists(sandbox))
        Directory.Delete(sandbox, recursive: true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine("CombatReplayReportBundle tests passed.");
}

void VerifyStaticPaths()
{
    var dataRoot = Path.Combine(sandbox, "BazaarPlusPlusV4");
    var paths = new StaticReportPaths(dataRoot);
    const string recordingId = "0123456789abcdef0123456789abcdef";
    const string viewerBundleId =
        "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
    Check(
        paths.GetReportHtmlFilePath(recordingId)
            == Path.Combine(dataRoot, "reports", recordingId + ".html"),
        "Reports must be keyed by the typed recording ID."
    );
    Check(
        StaticReportPaths.BuildViewerScriptRelativeUrl(viewerBundleId)
            == "../report-viewer/objects/" + viewerBundleId + "/viewer.js"
            && StaticReportPaths.BuildViewerStylesheetRelativeUrl(viewerBundleId)
                == "../report-viewer/objects/" + viewerBundleId + "/viewer.css",
        "The Viewer must use one immutable content-addressed sibling generation."
    );

    const string digest = "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
    Check(
        StaticReportPaths.BuildAssetRelativeUrl(digest)
            == "../report-assets/objects/ab/" + digest + ".png",
        "Content assets must use the SHA prefix fanout."
    );
    CheckThrows<ArgumentException>(
        () => paths.GetReportHtmlFilePath("recording-name"),
        "Arbitrary recording names must not become report paths."
    );
    var video = Path.Combine(dataRoot, "CombatReplayVideos", "2026-07-22", "战斗 录像.mp4");
    var url = paths.BuildVideoRelativeUrl(video);
    Check(
        url == "../CombatReplayVideos/2026-07-22/%E6%88%98%E6%96%97%20%E5%BD%95%E5%83%8F.mp4",
        "Video URLs must escape each sibling path segment."
    );
}

void VerifyTypedSiblingUrls()
{
    const string digest = "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd";
    var validAsset = "../report-assets/objects/ab/" + digest + ".png";
    var validVideo = "../CombatReplayVideos/2026-07-22/%E6%88%98%E6%96%97%20%E5%BD%95%E5%83%8F.mp4";
    Check(
        TypedReportSiblingUrl.TryParse(validAsset, out var asset)
            && asset?.Kind == TypedReportSiblingUrlKind.Asset,
        "A canonical content-addressed asset URL must be accepted."
    );
    Check(
        TypedReportSiblingUrl.TryParse(validVideo, out var video)
            && video?.Kind == TypedReportSiblingUrlKind.Video,
        "A canonical escaped MP4 URL must be accepted."
    );
    const string validDirectVideo = "../CombatReplayVideos/battle.mp4";
    Check(
        TypedReportSiblingUrl.TryParse(validDirectVideo, out var directVideo)
            && directVideo?.Kind == TypedReportSiblingUrlKind.Video,
        "A direct child MP4 URL must be accepted."
    );

    var hostile = new[]
    {
        "../../report-assets/objects/ab/" + digest + ".png",
        "../report-assets/objects/cd/" + digest + ".png",
        "../report-assets/objects/ab/" + digest + ".jpg",
        "../report-assets/objects/ab/%2e%2e.png",
        "../CombatReplayVideos/2026-07-22/%2e%2e/evil.mp4",
        "../CombatReplayVideos/2026-07-22/%252e%252e/evil.mp4",
        "../CombatReplayVideos/2026-07-22/a%2fb.mp4",
        "../CombatReplayVideos/2026-07-22/a%5cb.mp4",
        "../CombatReplayVideos/2026-07-22/100%25.mp4",
        "../CombatReplayVideos/2026-07-22/control%0A.mp4",
        "../CombatReplayVideos/2026-07-22/战斗.mp4",
        "../CombatReplayVideos/2026-07-22/bad name.mp4",
        "../CombatReplayVideos/2026-07-22/evil.mp4?x=1",
        "../CombatReplayVideos/2026-07-22/evil.mp4#x",
        "file:///tmp/evil.mp4",
        "https://example.test/evil.mp4",
    };
    foreach (var value in hostile)
    {
        Check(
            !TypedReportSiblingUrl.TryParse(value, out _),
            "Hostile sibling URL must be rejected: " + value
        );
    }
}

void VerifyHtmlEmitter()
{
    const string viewerBundleId =
        "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
    const string envelope =
        "{\"schemaVersion\":1,\"label\":\"</script><script id='bpp-report-data'>x</script><>&\u2028\u2029\",\"asset\":\"../report-assets/objects/ab/abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd.png\"}";
    var bytes = new ReportHtmlEmitter().Emit(
        "战斗 <报告> & review",
        "zh_TW",
        envelope,
        viewerBundleId,
        TestViewerScriptSha256,
        TestViewerStylesheetSha256
    );
    var html = Encoding.UTF8.GetString(bytes);
    Check(
        html.Contains("<html lang=\"zh-Hant\">", StringComparison.Ordinal),
        "HTML language must normalize to zh-Hant."
    );
    Check(
        html.Contains("<title>战斗 &lt;报告&gt; &amp; review</title>", StringComparison.Ordinal),
        "HTML title must be escaped."
    );
    Check(
        Count(html, "id=\"bpp-report-data\"") == 1,
        "Hostile report JSON must not create a duplicate data node."
    );
    Check(
        html.Contains(
            "\\u003c/script\\u003e\\u003cscript id='bpp-report-data'\\u003ex\\u003c/script\\u003e\\u003c\\u003e\\u0026\\u2028\\u2029",
            StringComparison.Ordinal
        ),
        "The inert JSON payload must escape every HTML breakout character."
    );
    Check(
        html.Contains(
            "<link rel=\"stylesheet\" href=\"../report-viewer/objects/"
                + viewerBundleId
                + "/viewer.css\">",
            StringComparison.Ordinal
        )
            && html.Contains(
                "<script defer src=\"../report-viewer/objects/"
                    + viewerBundleId
                    + "/viewer.js\"></script>",
                StringComparison.Ordinal
            ),
        "HTML must reference exactly one immutable shared Viewer generation."
    );
    Check(
        html.Contains(
            "<meta name=\"bpp-viewer-bundle\" content=\"" + viewerBundleId + "\">",
            StringComparison.Ordinal
        )
            && html.Contains(
                "<meta name=\"bpp-viewer-script-sha256\" content=\""
                    + TestViewerScriptSha256
                    + "\">",
                StringComparison.Ordinal
            )
            && html.Contains(
                "<meta name=\"bpp-viewer-stylesheet-sha256\" content=\""
                    + TestViewerStylesheetSha256
                    + "\">",
                StringComparison.Ordinal
            ),
        "HTML must pin the immutable Viewer bundle and both artifact digests."
    );
    Check(
        !html.Contains("http://", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("https://", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("fetch(", StringComparison.Ordinal)
            && Count(html, "<script") == 2,
        "HTML must contain only the inert data node and one external classic script."
    );

    var start = html.IndexOf(
        "<script type=\"application/json\" id=\"bpp-report-data\">",
        StringComparison.Ordinal
    );
    start += "<script type=\"application/json\" id=\"bpp-report-data\">".Length;
    var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
    using var parsed = JsonDocument.Parse(html.Substring(start, end - start));
    Check(
        parsed.RootElement.GetProperty("schemaVersion").GetInt32() == 1,
        "Escaped inert JSON must remain valid JSON."
    );
}

void VerifyCurrentViewerReportIntegrityScan()
{
    const string viewerBundleId =
        "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
    var reportDirectory = Path.Combine(sandbox, "viewer-integrity-scan");
    Directory.CreateDirectory(reportDirectory);
    var emitted = Encoding.UTF8.GetString(
        new ReportHtmlEmitter().Emit(
            "Current",
            "en",
            "{\"events\":[1,2,3]}",
            viewerBundleId,
            TestViewerScriptSha256,
            TestViewerStylesheetSha256
        )
    );

    var completeReportPath = Path.Combine(reportDirectory, "complete.html");
    File.WriteAllText(completeReportPath, emitted);
    Check(
        IsValidViewerReport(completeReportPath, out var completeReason)
            && string.IsNullOrEmpty(completeReason),
        "Viewer validation must accept a complete current report."
    );

    var largePayloadReportPath = Path.Combine(reportDirectory, "large-payload.html");
    var largeJson = "{\"events\":[" + string.Join(",", Enumerable.Repeat("0", 160_000)) + "]}";
    File.WriteAllText(
        largePayloadReportPath,
        Encoding.UTF8.GetString(
            new ReportHtmlEmitter().Emit(
                "Large",
                "en",
                largeJson,
                viewerBundleId,
                TestViewerScriptSha256,
                TestViewerStylesheetSha256
            )
        )
    );
    Check(
        IsValidViewerReport(largePayloadReportPath, out var largePayloadReason)
            && string.IsNullOrEmpty(largePayloadReason),
        "Viewer validation must stream a large valid embedded payload without a whole-file read."
    );

    var delayedHeadReportPath = Path.Combine(reportDirectory, "delayed-head.html");
    File.WriteAllText(delayedHeadReportPath, new string(' ', 65 * 1024) + emitted);
    Check(
        !IsValidViewerReport(delayedHeadReportPath, out _),
        "Viewer validation must only trust bundle references found in the bounded HTML head scan."
    );

    var truncatedPayloads = new[]
    {
        ("truncated-object.html", "{\"events\":1"),
        ("truncated-array.html", "{\"events\":[1,2"),
        ("truncated-string.html", "{\"name\":\"unfinished"),
        ("truncated-nested-object.html", "{\"nested\":{}"),
        ("truncated-nested-array.html", "{\"nested\":[]"),
    };
    foreach (var (fileName, payload) in truncatedPayloads)
    {
        var truncatedJsonReportPath = Path.Combine(reportDirectory, fileName);
        File.WriteAllText(truncatedJsonReportPath, ReplaceEmbeddedReportJson(emitted, payload));
        Check(
            !IsValidViewerReport(truncatedJsonReportPath, out _),
            "A current Viewer marker must not admit a report with truncated embedded JSON."
        );
    }

    var missingDataReportPath = Path.Combine(reportDirectory, "missing-data.html");
    File.WriteAllText(
        missingDataReportPath,
        emitted.Replace(
            "<script type=\"application/json\" id=\"bpp-report-data\">"
                + "{\"events\":[1,2,3]}"
                + "</script>\n",
            string.Empty,
            StringComparison.Ordinal
        )
    );
    Check(
        !IsValidViewerReport(missingDataReportPath, out _),
        "A current Viewer marker must not admit a report without embedded report data."
    );

    var missingScriptCloseReportPath = Path.Combine(reportDirectory, "missing-script-close.html");
    File.WriteAllText(
        missingScriptCloseReportPath,
        emitted.Replace("</script>\n<script defer", "\n<script defer", StringComparison.Ordinal)
    );
    Check(
        !IsValidViewerReport(missingScriptCloseReportPath, out _),
        "A current Viewer marker must not admit a report without the data script close."
    );

    var missingBodyCloseReportPath = Path.Combine(reportDirectory, "missing-body-close.html");
    File.WriteAllText(
        missingBodyCloseReportPath,
        emitted.Replace("</body>\n", string.Empty, StringComparison.Ordinal)
    );
    Check(
        !IsValidViewerReport(missingBodyCloseReportPath, out _),
        "A current Viewer marker must not admit a report without the body close."
    );

    var missingHtmlCloseReportPath = Path.Combine(reportDirectory, "missing-html-close.html");
    File.WriteAllText(
        missingHtmlCloseReportPath,
        emitted.Replace("</html>\n", string.Empty, StringComparison.Ordinal)
    );
    Check(
        !IsValidViewerReport(missingHtmlCloseReportPath, out _),
        "A current Viewer marker must not admit a report without the document close."
    );

    bool IsValidViewerReport(string reportPath, out string reason)
    {
        var valid = StaticReportPaths.TryValidateViewerReport(
            reportPath,
            out var parsedBundleId,
            out var parsedScriptSha256,
            out var parsedStylesheetSha256,
            out reason
        );
        return valid
            && parsedBundleId == viewerBundleId
            && parsedScriptSha256 == TestViewerScriptSha256
            && parsedStylesheetSha256 == TestViewerStylesheetSha256;
    }
}

static string ReplaceEmbeddedReportJson(string html, string replacement)
{
    const string startToken = "<script type=\"application/json\" id=\"bpp-report-data\">";
    var payloadStart = html.IndexOf(startToken, StringComparison.Ordinal) + startToken.Length;
    var payloadEnd = html.IndexOf("</script>", payloadStart, StringComparison.Ordinal);
    return html.Substring(0, payloadStart) + replacement + html.Substring(payloadEnd);
}

void VerifyViewerArtifactPinAndInstall()
{
    var artifacts = ViewerArtifactBundle.CreateDefault();
    Check(
        ReferenceEquals(artifacts, ViewerArtifactBundle.CreateDefault()),
        "The embedded Viewer bundle must be materialized once and reused for the process lifetime."
    );
    Check(
        artifacts.ScriptBytes.Length > 100_000
            && artifacts.ScriptSha256 == StaticReportIntegrity.Sha256(artifacts.ScriptBytes)
            && artifacts.StylesheetSha256 == StaticReportIntegrity.Sha256(artifacts.StylesheetBytes)
            && artifacts.BundleId.Length == 64,
        "The Viewer must derive a canonical content identity from its embedded bytes."
    );
    var callerOwnedScript = artifacts.ScriptBytes;
    callerOwnedScript[0] ^= 0xff;
    Check(
        artifacts.ScriptSha256 == StaticReportIntegrity.Sha256(artifacts.ScriptBytes),
        "Viewer artifact bytes exposed to callers must be defensive copies."
    );

    var paths = new StaticReportPaths(Path.Combine(sandbox, "viewer-install"));
    var installer = new ViewerInstaller(paths, artifacts, new ImmutableArtifactCommitter());
    var first = installer.EnsureInstalled();
    var second = installer.EnsureInstalled();
    Check(
        first.ScriptPublish == ImmutableArtifactCommitResult.Created
            && first.StylesheetPublish == ImmutableArtifactCommitResult.Created
            && second.ScriptPublish == ImmutableArtifactCommitResult.Reused
            && second.StylesheetPublish == ImmutableArtifactCommitResult.Reused,
        "Viewer install must create once and then reuse one byte-identical generation."
    );
    Check(
        File.ReadAllBytes(paths.GetViewerScriptFilePath(artifacts.BundleId))
            .SequenceEqual(artifacts.ScriptBytes)
            && File.ReadAllBytes(paths.GetViewerStylesheetFilePath(artifacts.BundleId))
                .SequenceEqual(artifacts.StylesheetBytes),
        "Installed Viewer files must equal the embedded artifact bytes."
    );

    File.WriteAllBytes(
        paths.GetViewerScriptFilePath(artifacts.BundleId),
        Encoding.UTF8.GetBytes("mutated")
    );
    CheckThrows<ArtifactPublicationException>(
        () => installer.EnsureInstalled(),
        "A changed immutable Viewer generation must fail closed instead of being overwritten."
    );
    File.WriteAllBytes(paths.GetViewerScriptFilePath(artifacts.BundleId), artifacts.ScriptBytes);

    var alternateScript = (byte[])artifacts.ScriptBytes.Clone();
    alternateScript[alternateScript.Length - 1] ^= 0x01;
    var alternate = new ViewerArtifactBundle(1, alternateScript, artifacts.StylesheetBytes);
    Check(
        alternate.BundleId != artifacts.BundleId,
        "Changing either Viewer artifact must create a different bundle generation."
    );

    var committer = new ImmutableArtifactCommitter();
    _ = committer.CommitBelowRoot(
        paths.DataRootDirectoryPath,
        paths.GetViewerStylesheetFilePath(alternate.BundleId),
        alternate.StylesheetBytes
    );
    var completedPartialGeneration = new ViewerInstaller(
        paths,
        alternate,
        committer
    ).EnsureInstalled();
    Check(
        completedPartialGeneration.StylesheetPublish == ImmutableArtifactCommitResult.Reused
            && completedPartialGeneration.ScriptPublish == ImmutableArtifactCommitResult.Created
            && File.ReadAllBytes(paths.GetViewerScriptFilePath(artifacts.BundleId))
                .SequenceEqual(artifacts.ScriptBytes),
        "A partial unreferenced generation must be completed without changing an existing generation."
    );
}

void VerifyViewerThirdPartyNoticesAreEmbedded()
{
    var assembly = typeof(ViewerArtifactBundle).Assembly;
    var requiredResources = new[]
    {
        "BazaarPlusPlus.Resources.CombatReplayReport.echarts-license.txt",
        "BazaarPlusPlus.Resources.CombatReplayReport.echarts-notice.txt",
    };
    foreach (var resourceName in requiredResources)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Check(
            stream != null && stream.Length > 0,
            "The Viewer bundle fixture must expose the required ECharts legal resource: "
                + resourceName
        );
    }
}

void VerifyViewerStaticRuntimeBoundary()
{
    var artifacts = ViewerArtifactBundle.CreateDefault();
    var script = Encoding.UTF8.GetString(artifacts.ScriptBytes);
    var stylesheet = Encoding.UTF8.GetString(artifacts.StylesheetBytes);

    Check(
        script.Length > 100_000
            && stylesheet.Contains(".bpp-report-root", StringComparison.Ordinal)
            && !script.Contains("fetch(", StringComparison.Ordinal)
            && !script.Contains("new Worker(", StringComparison.Ordinal)
            && !script.Contains("new SharedWorker(", StringComparison.Ordinal)
            && !script.Contains("import(", StringComparison.Ordinal)
            && !stylesheet.Contains("@import", StringComparison.Ordinal),
        "The committed Viewer artifacts must preserve the static file:// runtime boundary."
    );
}

void VerifyViewerOpenGateVerifiesPinnedArtifacts()
{
    var dataRoot = Path.Combine(sandbox, "viewer-open-gate");
    var reportRoot = Path.Combine(dataRoot, "reports");
    Directory.CreateDirectory(reportRoot);

    var install = CombatReplayReportViewerGate.EnsureInstalledForDataRoot(dataRoot);

    var artifacts = ViewerArtifactBundle.CreateDefault();
    var paths = new StaticReportPaths(dataRoot);
    var currentReportPath = Path.Combine(reportRoot, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.html");
    File.WriteAllBytes(
        currentReportPath,
        new ReportHtmlEmitter().Emit(
            "Current",
            "en",
            "{}",
            artifacts.BundleId,
            artifacts.ScriptSha256,
            artifacts.StylesheetSha256
        )
    );
    Check(
        install.Artifacts.BundleId == artifacts.BundleId
            && File.ReadAllBytes(paths.GetViewerScriptFilePath(artifacts.BundleId))
                .SequenceEqual(artifacts.ScriptBytes)
            && File.ReadAllBytes(paths.GetViewerStylesheetFilePath(artifacts.BundleId))
                .SequenceEqual(artifacts.StylesheetBytes),
        "The Viewer open gate must publish the complete current generation."
    );
    Check(
        CombatReplayReportViewerGate.TryEnsureInstalledForReport(
            reportRoot,
            currentReportPath,
            out _
        ),
        "The Viewer open gate must accept a report that references the complete current bundle."
    );
    var incompleteReportPath = Path.Combine(reportRoot, "cccccccccccccccccccccccccccccccc.html");
    File.WriteAllText(
        incompleteReportPath,
        ReplaceEmbeddedReportJson(
            Encoding.UTF8.GetString(
                new ReportHtmlEmitter().Emit(
                    "Incomplete",
                    "en",
                    "{}",
                    artifacts.BundleId,
                    artifacts.ScriptSha256,
                    artifacts.StylesheetSha256
                )
            ),
            "{\"events\":[1,2"
        )
    );
    Check(
        !CombatReplayReportViewerGate.TryEnsureInstalledForReport(
            reportRoot,
            incompleteReportPath,
            out _
        ),
        "The Viewer open gate must reject a current-bundle report whose embedded data is incomplete."
    );
    var staleReportPath = Path.Combine(reportRoot, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.html");
    File.WriteAllText(staleReportPath, "<!doctype html><html><body>stale</body></html>");
    Check(
        !CombatReplayReportViewerGate.TryEnsureInstalledForReport(
            reportRoot,
            staleReportPath,
            out _
        ),
        "The Viewer open gate must reject an existing report from an unsupported Viewer bundle."
    );

    var alternateScript = artifacts.ScriptBytes;
    alternateScript[alternateScript.Length - 1] ^= 0x01;
    var alternate = new ViewerArtifactBundle(
        ViewerArtifactBundle.CurrentReportSchemaVersion,
        alternateScript,
        artifacts.StylesheetBytes
    );
    _ = new ViewerInstaller(paths, alternate, new ImmutableArtifactCommitter()).EnsureInstalled();
    var pinnedReportPath = Path.Combine(reportRoot, "dddddddddddddddddddddddddddddddd.html");
    File.WriteAllBytes(
        pinnedReportPath,
        new ReportHtmlEmitter().Emit(
            "Pinned",
            "en",
            "{}",
            alternate.BundleId,
            alternate.ScriptSha256,
            alternate.StylesheetSha256
        )
    );
    File.WriteAllText(paths.GetViewerScriptFilePath(artifacts.BundleId), "corrupt current bundle");
    Check(
        CombatReplayReportViewerGate.TryEnsureInstalledForReport(
            reportRoot,
            pinnedReportPath,
            out _
        ),
        "A report pinned to another intact generation must open without installing or validating the unrelated current generation."
    );

    var forgedReportPath = Path.Combine(reportRoot, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee.html");
    File.WriteAllBytes(
        forgedReportPath,
        new ReportHtmlEmitter().Emit(
            "Forged",
            "en",
            "{}",
            alternate.BundleId,
            artifacts.ScriptSha256,
            artifacts.StylesheetSha256
        )
    );
    Check(
        !CombatReplayReportViewerGate.TryEnsureInstalledForReport(
            reportRoot,
            forgedReportPath,
            out _
        ),
        "Viewer artifact digests must be cryptographically bound to the pinned bundle identity."
    );
}

void VerifyImmutableCommitConcurrency()
{
    var root = Path.Combine(sandbox, "commit-concurrency");
    var destination = Path.Combine(root, "objects", "same.bin");
    var bytes = Encoding.UTF8.GetBytes("same immutable bytes");
    var results = Enumerable
        .Range(0, 12)
        .Select(_ =>
            Task.Run(() =>
                new ImmutableArtifactCommitter().CommitBelowRoot(root, destination, bytes)
            )
        )
        .ToArray();
    Task.WaitAll(results);
    var created = results.Count(task => task.Result == ImmutableArtifactCommitResult.Created);
    var reused = results.Count(task => task.Result == ImmutableArtifactCommitResult.Reused);
    Check(
        created == 1 && reused == 11,
        $"Concurrent identical commits must create once and reuse all losers (created={created}, reused={reused})."
    );

    CheckThrows<ArtifactPublicationException>(
        () =>
            new ImmutableArtifactCommitter().CommitBelowRoot(
                root,
                destination,
                Encoding.UTF8.GetBytes("different")
            ),
        "The same immutable identity with different bytes must fail closed."
    );
}

void VerifyImmutableCommitRejectsLinks()
{
    var root = Path.Combine(sandbox, "commit-links");
    Directory.CreateDirectory(root);
    var target = Path.Combine(root, "target.bin");
    File.WriteAllText(target, "target");
    var link = Path.Combine(root, "linked.bin");
    try
    {
        File.CreateSymbolicLink(link, target);
    }
    catch (Exception exception)
        when (exception is UnauthorizedAccessException
            || exception is PlatformNotSupportedException
            || exception is IOException
        )
    {
        Console.WriteLine("Symlink test skipped: " + exception.GetType().Name);
        return;
    }

    CheckThrows<ArtifactPublicationException>(
        () =>
            new ImmutableArtifactCommitter().CommitBelowRoot(
                root,
                link,
                Encoding.UTF8.GetBytes("target")
            ),
        "Immutable commit must reject a symlink destination."
    );

    var external = Path.Combine(sandbox, "external-directory");
    Directory.CreateDirectory(external);
    var linkedDirectory = Path.Combine(root, "linked-directory");
    Directory.CreateSymbolicLink(linkedDirectory, external);
    CheckThrows<ArtifactPublicationException>(
        () =>
            new ImmutableArtifactCommitter().CommitBelowRoot(
                root,
                Path.Combine(linkedDirectory, "escape.bin"),
                Encoding.UTF8.GetBytes("escape")
            ),
        "Immutable commit must reject a symlink in its parent directory chain."
    );
}

void VerifyPhysicalFileChain()
{
    var root = Path.Combine(sandbox, "physical-files");
    var nested = Path.Combine(root, "date");
    Directory.CreateDirectory(nested);
    var physical = Path.Combine(nested, "battle.mp4");
    File.WriteAllBytes(physical, new byte[] { 0, 1, 2 });
    ReportPhysicalFile.RequireBelowRoot(root, physical, "Recorded combat video");

    CheckThrows<ArtifactPublicationException>(
        () =>
            ReportPhysicalFile.RequireBelowRoot(
                root,
                Path.Combine(sandbox, "outside.mp4"),
                "Recorded combat video"
            ),
        "Physical file validation must reject paths outside the typed root."
    );

    var external = Path.Combine(sandbox, "physical-file-external");
    Directory.CreateDirectory(external);
    File.WriteAllBytes(Path.Combine(external, "linked.mp4"), new byte[] { 3, 4, 5 });
    var linkedDirectory = Path.Combine(root, "linked-date");
    try
    {
        Directory.CreateSymbolicLink(linkedDirectory, external);
    }
    catch (Exception exception)
        when (exception is UnauthorizedAccessException
            || exception is PlatformNotSupportedException
            || exception is IOException
        )
    {
        Console.WriteLine("Physical chain symlink test skipped: " + exception.GetType().Name);
        return;
    }

    CheckThrows<ArtifactPublicationException>(
        () =>
            ReportPhysicalFile.RequireBelowRoot(
                root,
                Path.Combine(linkedDirectory, "linked.mp4"),
                "Recorded combat video"
            ),
        "Physical file validation must reject a symlink in the directory chain."
    );
}

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void CheckThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
        failures.Add(message + " (did not throw)");
    }
    catch (TException)
    {
        // Expected.
    }
}

static int Count(string value, string needle)
{
    var count = 0;
    var offset = 0;
    while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += needle.Length;
    }
    return count;
}
