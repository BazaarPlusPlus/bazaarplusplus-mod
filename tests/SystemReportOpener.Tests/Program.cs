using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.Files;

const string RecordingIdText = "0123456789abcdef0123456789abcdef";
const string LinkedFileRecordingIdText = "11111111111111111111111111111111";
const string LinkedParentRecordingIdText = "22222222222222222222222222222222";

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-system-report-opener-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);

try
{
    Assert(
        CombatReportRecordingId.TryParse(RecordingIdText, out var recordingId),
        "A lowercase 32-hex recording ID should parse."
    );
    foreach (
        var invalid in new[]
        {
            "",
            "0123456789abcdef0123456789abcde",
            "0123456789abcdef0123456789abcdef0",
            "0123456789ABCDEF0123456789ABCDEF",
            "01234567-89ab-cdef-0123-456789abcdef",
            "g123456789abcdef0123456789abcdef",
            "../0123456789abcdef0123456789ab",
            RecordingIdText + ".html",
        }
    )
    {
        Assert(
            !CombatReportRecordingId.TryParse(invalid, out _),
            "A noncanonical recording ID must be rejected: " + invalid
        );
    }

    var reportRoot = Path.Combine(tempRoot, "reports");
    var rootPrefixSibling = Path.Combine(tempRoot, "reports2");
    var outsideRoot = Path.Combine(tempRoot, "outside");
    Directory.CreateDirectory(reportRoot);
    Directory.CreateDirectory(rootPrefixSibling);
    Directory.CreateDirectory(outsideRoot);

    var reportPath = Path.Combine(reportRoot, RecordingIdText + ".html");
    var rootPrefixPath = Path.Combine(rootPrefixSibling, RecordingIdText + ".html");
    var outsidePath = Path.Combine(outsideRoot, RecordingIdText + ".html");
    File.WriteAllText(rootPrefixPath, "<!doctype html><title>root prefix sibling</title>");
    File.WriteAllText(outsidePath, "<!doctype html><title>outside</title>");

    Assert(
        !SystemReportOpener.TryResolve(reportRoot, recordingId, out _, out _),
        "A report in an outside or root-prefix sibling directory must not satisfy the trusted root."
    );
    File.WriteAllText(
        Path.Combine(reportRoot, RecordingIdText + ".htm"),
        "<!doctype html><title>wrong extension</title>"
    );
    Assert(
        !SystemReportOpener.TryResolve(reportRoot, recordingId, out _, out _),
        "A noncanonical extension must not satisfy the report lookup."
    );

    File.WriteAllText(reportPath, string.Empty);
    Assert(
        !SystemReportOpener.TryResolve(reportRoot, recordingId, out _, out _),
        "An empty HTML file is not a committed report."
    );
    File.WriteAllText(reportPath, "<!doctype html><title>static report</title>");
    Assert(
        SystemReportOpener.TryResolve(
            reportRoot,
            recordingId,
            out var resolvedReport,
            out var resolveReason
        )
            && resolvedReport != null
            && resolvedReport.RecordingId.Equals(recordingId)
            && string.Equals(
                resolvedReport.FullPath,
                Path.GetFullPath(reportPath),
                StringComparison.Ordinal
            ),
        "The typed lookup should resolve only <report-root>/<recording-id>.html: " + resolveReason
    );
    var newestMissingId = "ffffffffffffffffffffffffffffffff";
    var candidates = new[]
    {
        new HistoryBattleReportCandidate(newestMissingId),
        new HistoryBattleReportCandidate("not-canonical"),
        new HistoryBattleReportCandidate(RecordingIdText),
    };
    Assert(
        HistoryBattleReportLocator.TryResolveLatestReport(
            candidates,
            reportRoot,
            out var latestReport
        )
            && latestReport != null
            && latestReport.RecordingId.Equals(recordingId)
            && latestReport.FullPath == Path.GetFullPath(reportPath),
        "History lookup should skip newer missing/noncanonical rows and select the first usable recording ID."
    );

    var directoryRecordingIdText = "33333333333333333333333333333333";
    Assert(
        CombatReportRecordingId.TryParse(directoryRecordingIdText, out var directoryRecordingId),
        "Directory fixture ID should parse."
    );
    Directory.CreateDirectory(Path.Combine(reportRoot, directoryRecordingIdText + ".html"));
    Assert(
        !SystemReportOpener.TryResolve(reportRoot, directoryRecordingId, out _, out _),
        "A directory named like a report must be rejected."
    );

    TestFinalSymlink(reportRoot, outsideRoot);
    TestParentSymlink(tempRoot);
    TestStartInfo(Path.GetFullPath(Path.Combine(reportRoot, RecordingIdText + " open me.html")));
}
finally
{
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("SystemReportOpener checks passed.");

static void TestFinalSymlink(string reportRoot, string outsideRoot)
{
    if (!CombatReportRecordingId.TryParse(LinkedFileRecordingIdText, out var recordingId))
        throw new InvalidOperationException("Final symlink fixture ID should parse.");

    var target = Path.Combine(outsideRoot, "physical-report.html");
    var link = Path.Combine(reportRoot, LinkedFileRecordingIdText + ".html");
    File.WriteAllText(target, "<!doctype html><title>physical target</title>");
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
        Console.WriteLine("Final symlink check skipped: " + exception.GetType().Name);
        return;
    }

    Assert(
        !SystemReportOpener.TryResolve(reportRoot, recordingId, out _, out _),
        "A final-file symlink/reparse point must be rejected."
    );
}

static void TestParentSymlink(string tempRoot)
{
    if (!CombatReportRecordingId.TryParse(LinkedParentRecordingIdText, out var recordingId))
        throw new InvalidOperationException("Parent symlink fixture ID should parse.");

    var physicalRoot = Path.Combine(tempRoot, "physical-reports");
    var linkedRoot = Path.Combine(tempRoot, "linked-reports");
    Directory.CreateDirectory(physicalRoot);
    File.WriteAllText(
        Path.Combine(physicalRoot, LinkedParentRecordingIdText + ".html"),
        "<!doctype html><title>physical target</title>"
    );
    try
    {
        Directory.CreateSymbolicLink(linkedRoot, physicalRoot);
    }
    catch (Exception exception)
        when (exception is UnauthorizedAccessException
            || exception is PlatformNotSupportedException
            || exception is IOException
        )
    {
        Console.WriteLine("Parent symlink check skipped: " + exception.GetType().Name);
        return;
    }

    Assert(
        !SystemReportOpener.TryResolve(linkedRoot, recordingId, out _, out _),
        "A symlinked/reparse report root must be rejected."
    );
}

static void TestStartInfo(string fullPath)
{
    var mac = SystemReportOpener.BuildStartInfo(SystemReportOpenPlatform.MacOS, fullPath);
    Assert(
        mac.FileName == "/usr/bin/open"
            && !mac.UseShellExecute
            && mac.ArgumentList.Count == 1
            && mac.ArgumentList[0] == fullPath,
        "macOS should pass the exact full path as one /usr/bin/open argument."
    );

    var windows = SystemReportOpener.BuildStartInfo(SystemReportOpenPlatform.Windows, fullPath);
    Assert(
        windows.FileName == fullPath && windows.UseShellExecute && windows.ArgumentList.Count == 0,
        "Windows should use the default shell file association without extra arguments."
    );

    var linux = SystemReportOpener.BuildStartInfo(SystemReportOpenPlatform.Linux, fullPath);
    Assert(
        linux.FileName == "xdg-open"
            && !linux.UseShellExecute
            && linux.ArgumentList.Count == 1
            && linux.ArgumentList[0] == fullPath,
        "Linux should pass the exact full path as one xdg-open argument."
    );

    AssertThrows<ArgumentException>(
        () => SystemReportOpener.BuildStartInfo(SystemReportOpenPlatform.Linux, "relative.html"),
        "Start info must reject a relative report path."
    );
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
