using System.Text;
using System.Text.Json;
using BazaarPlusPlus.Game.CombatReplay.Reports;
using BazaarPlusPlus.Infrastructure.Files;

var failures = new List<string>();
var sandbox = Path.Combine(
    Path.GetTempPath(),
    "bpp-static-report-tests-" + Guid.NewGuid().ToString("N")
);

try
{
    Directory.CreateDirectory(sandbox);
    VerifyStaticPaths();
    VerifyTypedSiblingUrls();
    VerifyHtmlEmitter();
    VerifyViewerReleasePinAndInstall();
    VerifyViewerFunctionalContract();
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
    Check(
        paths.GetReportHtmlFilePath(recordingId)
            == Path.Combine(dataRoot, "reports", recordingId + ".html"),
        "Reports must be keyed by the typed recording ID."
    );
    Check(
        StaticReportPaths.BuildViewerScriptRelativeUrl("1") == "../report-viewer/v1/viewer.js"
            && StaticReportPaths.BuildViewerStylesheetRelativeUrl("1")
                == "../report-viewer/v1/viewer.css"
            && StaticReportPaths.BuildViewerScriptRelativeUrl("2")
                == "../report-viewer/v2/viewer.js"
            && StaticReportPaths.BuildViewerStylesheetRelativeUrl("2")
                == "../report-viewer/v2/viewer.css"
            && StaticReportPaths.BuildViewerScriptRelativeUrl("3")
                == "../report-viewer/v3/viewer.js"
            && StaticReportPaths.BuildViewerStylesheetRelativeUrl("3")
                == "../report-viewer/v3/viewer.css"
            && StaticReportPaths.BuildViewerScriptRelativeUrl("4")
                == "../report-viewer/v4/viewer.js"
            && StaticReportPaths.BuildViewerStylesheetRelativeUrl("4")
                == "../report-viewer/v4/viewer.css"
            && StaticReportPaths.BuildViewerScriptRelativeUrl("5")
                == "../report-viewer/v5/viewer.js"
            && StaticReportPaths.BuildViewerStylesheetRelativeUrl("5")
                == "../report-viewer/v5/viewer.css",
        "Every shared Viewer release must use its stable versioned sibling URLs."
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
    CheckThrows<ArgumentException>(
        () => StaticReportPaths.BuildViewerScriptRelativeUrl("01"),
        "Viewer versions with leading zeroes must be rejected."
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
    const string envelope =
        "{\"schemaVersion\":1,\"label\":\"</script><script id='bpp-report-data'>x</script><>&\u2028\u2029\",\"asset\":\"../report-assets/objects/ab/abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd.png\"}";
    var bytes = new ReportHtmlEmitter().Emit(
        "战斗 <报告> & review",
        "zh_TW",
        envelope,
        ViewerReleaseRegistry.CurrentVersion
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
            "<link rel=\"stylesheet\" href=\"../report-viewer/v5/viewer.css\">",
            StringComparison.Ordinal
        )
            && html.Contains(
                "<script defer src=\"../report-viewer/v5/viewer.js\"></script>",
                StringComparison.Ordinal
            ),
        "HTML must reference exactly one immutable shared Viewer version."
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

void VerifyViewerReleasePinAndInstall()
{
    var registry = ViewerReleaseRegistry.CreateDefault();
    var legacy = registry.GetRequired("1");
    var previous = registry.GetRequired("2");
    var frozenV3 = registry.GetRequired("3");
    var frozenV4 = registry.GetRequired("4");
    var release = registry.GetRequired(ViewerReleaseRegistry.CurrentVersion);
    Check(
        legacy.ScriptBytes.Length > 500_000
            && legacy.ScriptSha256 == StaticReportIntegrity.Sha256(legacy.ScriptBytes)
            && legacy.StylesheetSha256 == StaticReportIntegrity.Sha256(legacy.StylesheetBytes)
            && previous.ScriptBytes.Length > legacy.ScriptBytes.Length
            && previous.ScriptSha256 == StaticReportIntegrity.Sha256(previous.ScriptBytes)
            && previous.StylesheetSha256 == StaticReportIntegrity.Sha256(previous.StylesheetBytes)
            && frozenV3.ScriptSha256 == StaticReportIntegrity.Sha256(frozenV3.ScriptBytes)
            && frozenV3.StylesheetSha256 == StaticReportIntegrity.Sha256(frozenV3.StylesheetBytes)
            && frozenV4.ScriptSha256 == StaticReportIntegrity.Sha256(frozenV4.ScriptBytes)
            && frozenV4.StylesheetSha256 == StaticReportIntegrity.Sha256(frozenV4.StylesheetBytes)
            && release.ScriptSha256 == StaticReportIntegrity.Sha256(release.ScriptBytes)
            && release.StylesheetSha256 == StaticReportIntegrity.Sha256(release.StylesheetBytes)
            && frozenV3.ScriptSha256 == frozenV4.ScriptSha256
            && frozenV3.StylesheetSha256 != frozenV4.StylesheetSha256
            && frozenV4.ScriptSha256 != release.ScriptSha256
            && frozenV4.StylesheetSha256 == release.StylesheetSha256,
        "Every immutable Viewer release must match its source-controlled length and SHA pins."
    );

    var paths = new StaticReportPaths(Path.Combine(sandbox, "viewer-install"));
    var installer = new ViewerInstaller(paths, registry, new ImmutableArtifactCommitter());
    var first = installer.EnsureInstalled(ViewerReleaseRegistry.CurrentVersion);
    var second = installer.EnsureInstalled(ViewerReleaseRegistry.CurrentVersion);
    Check(
        first.ScriptCommit == ImmutableArtifactCommitResult.Created
            && first.StylesheetCommit == ImmutableArtifactCommitResult.Created
            && second.ScriptCommit == ImmutableArtifactCommitResult.Reused
            && second.StylesheetCommit == ImmutableArtifactCommitResult.Reused,
        "Viewer install must create once and then reuse byte-identical immutable files."
    );
    Check(
        File.ReadAllBytes(paths.GetViewerScriptFilePath(ViewerReleaseRegistry.CurrentVersion))
            .SequenceEqual(release.ScriptBytes)
            && File.ReadAllBytes(
                    paths.GetViewerStylesheetFilePath(ViewerReleaseRegistry.CurrentVersion)
                )
                .SequenceEqual(release.StylesheetBytes),
        "Installed Viewer files must equal the registered release bytes."
    );

    var changed = (byte[])release.ScriptBytes.Clone();
    changed[0] ^= 0xff;
    CheckThrows<InvalidDataException>(
        () =>
            new ViewerReleaseDefinition(
                ViewerReleaseRegistry.CurrentVersion,
                1,
                changed,
                release.StylesheetBytes,
                release.ScriptBytes.Length,
                release.ScriptSha256,
                release.StylesheetBytes.Length,
                release.StylesheetSha256
            ),
        "Changing Viewer bytes without changing the version pin must fail closed."
    );

    File.WriteAllBytes(
        paths.GetViewerScriptFilePath(ViewerReleaseRegistry.CurrentVersion),
        Encoding.UTF8.GetBytes("mutated")
    );
    CheckThrows<IOException>(
        () => installer.EnsureInstalled(ViewerReleaseRegistry.CurrentVersion),
        "An installed immutable Viewer version must never be overwritten."
    );
}

void VerifyViewerFunctionalContract()
{
    var assembly = typeof(ViewerReleaseRegistry).Assembly;
    var script = ReadEmbeddedText(
        assembly,
        "BazaarPlusPlus.Resources.CombatReplayReport.viewer.js"
    );
    var stylesheet = ReadEmbeddedText(
        assembly,
        "BazaarPlusPlus.Resources.CombatReplayReport.viewer.css"
    );

    Check(
        script.Contains(
            "const METRIC_ORDER = [\"health\", \"rage\", \"healthRegen\", \"shield\"]",
            StringComparison.Ordinal
        )
            && script.Contains("function frameZeroMetricSamples(battle)", StringComparison.Ordinal)
            && script.Contains("pick(battle, [\"frameZeroState\"]", StringComparison.Ordinal)
            && !script.Contains("const firstChange = events.find", StringComparison.Ordinal)
            && script.Contains("function signedOrder(value)", StringComparison.Ordinal)
            && script.Contains("data-time-zoom", StringComparison.Ordinal)
            && script.Contains("data-lane-zoom", StringComparison.Ordinal),
        "Viewer must seed the shared four-metric chart from producer-owned frame-zero state, without guessing from a late first change, and retain independent time/lane zoom."
    );
    Check(
        script.Contains("report-tab-timeline", StringComparison.Ordinal)
            && script.Contains("report-tab-statistics", StringComparison.Ordinal)
            && script.Contains("statistics-output-chart", StringComparison.Ordinal)
            && script.Contains("statistics-effects-chart", StringComparison.Ordinal)
            && !script.Contains("type: \"pie\"", StringComparison.Ordinal),
        "Viewer must expose Timeline/Statistics tabs and comparison bars without a meaningless single-slice donut."
    );
    Check(
        script.Contains("iconAssetRelativeUrl", StringComparison.Ordinal)
            && script.Contains("safeAssetUrl", StringComparison.Ordinal)
            && script.Contains("cachedIconImage", StringComparison.Ordinal)
            && script.Contains("bpp-event-native-icon", StringComparison.Ordinal),
        "Event native icons must pass through the typed asset allowlist and retain Canvas/inspector fallbacks."
    );
    Check(
        script.Contains("triggerSourceEntityId", StringComparison.Ordinal)
            && script.Contains("removedTargetEntityIds", StringComparison.Ordinal)
            && script.Contains("attributionConfidence", StringComparison.Ordinal)
            && script.Contains(
                "function relatedLaneRoles(cluster, entities)",
                StringComparison.Ordinal
            )
            && script.Contains("is-related-trigger", StringComparison.Ordinal)
            && script.Contains("is-related-removed", StringComparison.Ordinal)
            && script.Contains("appendRelation(\"attribution\"", StringComparison.Ordinal),
        "Viewer details and hover/selection highlighting must retain source, trigger, target, removed-target, and attribution semantics."
    );
    Check(
        script.Contains(
            "model.events.filter((event) => isVisibleTimelineEvent(event, entityById))",
            StringComparison.Ordinal
        )
            && script.Contains(
                "function buildStatusRanges(model, entities",
                StringComparison.Ordinal
            )
            && script.Contains("drawStatusRanges(context, statusRanges", StringComparison.Ordinal)
            && script.Contains("ROUTINE_CARD_ACTIONS", StringComparison.Ordinal)
            && script.Contains("source.type.toLowerCase() === \"skill\"", StringComparison.Ordinal)
            && script.Contains("cluster.events.push(event)", StringComparison.Ordinal)
            && script.Contains("function eventsAtFrame(events, frame)", StringComparison.Ordinal)
            && script.Contains("event.frame === frame", StringComparison.Ordinal)
            && script.Contains("frame-event-total", StringComparison.Ordinal)
            && script.Contains("const visible = events.slice(0, limit)", StringComparison.Ordinal),
        "Metric/routine countdown records must stay out of lane markers, sustained statuses must remain flat interactive ranges, skill Rage triggers remain visible, and the paged inspector must expand the entire selected frame."
    );
    Check(
        script.Contains("function scheduleHoverSeek(cluster)", StringComparison.Ordinal)
            && script.Contains("!video.paused", StringComparison.Ordinal)
            && script.Contains("model.sync.status !== \"ReadyExact\"", StringComparison.Ordinal)
            && script.Contains("window.requestAnimationFrame(function ()", StringComparison.Ordinal)
            && script.Contains(
                "window.cancelAnimationFrame(hoverSeekAnimationFrame)",
                StringComparison.Ordinal
            ),
        "Paused exact-sync recordings must preview hovered frames through one coalesced animation-frame seek, while click cancels pending preview work."
    );
    Check(
        script.Contains("event.attributionConfidence === \"exact\"", StringComparison.Ordinal)
            && script.Contains("result.output[targetSide][1] += damage", StringComparison.Ordinal)
            && script.Contains("result.output[targetSide][2] += damage", StringComparison.Ordinal)
            && script.Contains(
                "changedAttribute === \"Health\" && amount < 0",
                StringComparison.Ordinal
            )
            && script.Contains(
                "changedAttribute === \"Shield\" && amount > 0",
                StringComparison.Ordinal
            )
            && script.Contains(
                "changedAttribute === \"Shield\" && amount < 0",
                StringComparison.Ordinal
            )
            && !script.Contains("changedAttribute === \"Joy\"", StringComparison.Ordinal)
            && !script.Contains("opposite(targetSide)", StringComparison.Ordinal),
        "Statistics must count only negative Health as damage, apply signed Shield gain/loss policy, exclude Joy, and never fabricate the opposite side as source."
    );
    Check(
        script.Contains(
            "function renderDamageComposition(parent, statistics, copy)",
            StringComparison.Ordinal
        )
            && script.Contains("activeKeys.length === 1", StringComparison.Ordinal)
            && script.Contains("stack: \"damage-composition\"", StringComparison.Ordinal)
            && !script.Contains("type: \"pie\"", StringComparison.Ordinal),
        "Damage composition must degrade to a direct summary for one type and use a reversible stacked comparison only for multiple types."
    );
    Check(
        script.Contains("const key = event.frame + \":\"", StringComparison.Ordinal)
            && !script.Contains(
                "const key = endpoint.lane + \":\" + pixel",
                StringComparison.Ordinal
            ),
        "Dense adjacent frames that quantize to the same pixel must retain separate frame cluster identities."
    );
    Check(
        script.Contains("recording-video-toggle", StringComparison.Ordinal)
            && script.Contains("(max-height: 900px)", StringComparison.Ordinal)
            && script.Contains("frame.hidden = !expanded", StringComparison.Ordinal),
        "Short viewports must default to a collapsible recording while preserving an explicit full-width expansion control."
    );
    Check(
        script.Contains("function translatedEntityType(copy, rawType)", StringComparison.Ordinal)
            && script.Contains("translate(copy, \"entityHero\")", StringComparison.Ordinal)
            && script.Contains("translate(copy, \"entityItem\")", StringComparison.Ordinal)
            && script.Contains("translate(copy, \"entitySkill\")", StringComparison.Ordinal),
        "Lane type labels are Viewer UI and must be localized instead of leaking raw English entity types."
    );
    Check(
        !script.Contains("fetch(", StringComparison.Ordinal)
            && !script.Contains("new Worker", StringComparison.Ordinal)
            && !script.Contains("import(", StringComparison.Ordinal),
        "The file Viewer must remain a classic, self-contained script with no local fetch, dynamic import, or Worker dependency."
    );
    Check(
        stylesheet.Contains(".bpp-recording-video", StringComparison.Ordinal)
            && stylesheet.Contains("width: 100%;", StringComparison.Ordinal)
            && stylesheet.Contains(".bpp-video-frame[hidden]", StringComparison.Ordinal)
            && stylesheet.Contains(".bpp-lane-label.is-event-related", StringComparison.Ordinal)
            && stylesheet.Contains(".bpp-stats-charts", StringComparison.Ordinal)
            && stylesheet.Contains(".bpp-timeline-toolbar", StringComparison.Ordinal)
            && stylesheet.Contains(
                ".bpp-lane-art.bpp-art-item.bpp-span-1",
                StringComparison.Ordinal
            )
            && stylesheet.Contains(
                "calc(18.6px * var(--bpp-lane-scale, 1))",
                StringComparison.Ordinal
            )
            && stylesheet.Contains(
                "calc(36.1px * var(--bpp-lane-scale, 1))",
                StringComparison.Ordinal
            )
            && stylesheet.Contains(
                "calc(71.2px * var(--bpp-lane-scale, 1))",
                StringComparison.Ordinal
            )
            && stylesheet.Contains(
                "grid-template-rows: auto auto auto minmax(0, 1fr)",
                StringComparison.Ordinal
            )
            && stylesheet.Contains("height: 100dvh", StringComparison.Ordinal)
            && stylesheet.Contains("height: 120px", StringComparison.Ordinal)
            && stylesheet.Contains("height: 100%;", StringComparison.Ordinal),
        "Viewer CSS must retain a full-width recording, a viewport-bound timeline workspace, compact low-height metrics, responsive zoom controls, and natural Small/Medium/Large item ratios."
    );
}

string ReadEmbeddedText(System.Reflection.Assembly assembly, string resourceName)
{
    using var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream == null)
    {
        failures.Add("Missing embedded resource: " + resourceName);
        return string.Empty;
    }
    using var reader = new StreamReader(
        stream,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: true
    );
    return reader.ReadToEnd();
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

    CheckThrows<IOException>(
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

    CheckThrows<IOException>(
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
    CheckThrows<IOException>(
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

    CheckThrows<InvalidOperationException>(
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

    CheckThrows<IOException>(
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
