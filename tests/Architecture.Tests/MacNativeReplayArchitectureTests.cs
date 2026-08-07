using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

public sealed class MacNativeReplayArchitectureTests
{
    [Fact]
    public void Mac_payload_has_no_ffmpeg_while_windows_payload_keeps_it()
    {
        var projectPath = Path.Combine(
            RepoRoot(),
            "src",
            "BazaarPlusPlus",
            "BazaarPlusPlus.csproj"
        );
        var project = XDocument.Load(projectPath);
        var elements = project.Descendants().ToList();

        Assert.DoesNotContain(elements, element => element.Name.LocalName == "MacFfmpegZip");
        Assert.DoesNotContain(elements, element => element.Name.LocalName == "MacFfmpegLicense");
        Assert.Contains(elements, element => element.Name.LocalName == "WindowsFfmpegZip");
        Assert.Contains(elements, element => element.Name.LocalName == "WindowsFfmpegLicense");

        var unzips = elements.Where(element => element.Name.LocalName == "Unzip").ToList();
        Assert.DoesNotContain(
            unzips,
            element =>
                (element.Attribute("DestinationFolder")?.Value ?? string.Empty).Contains(
                    "SourceForBuild/macos",
                    StringComparison.Ordinal
                )
        );
        Assert.Contains(
            unzips,
            element =>
                (element.Attribute("DestinationFolder")?.Value ?? string.Empty).Contains(
                    "SourceForBuild/windows",
                    StringComparison.Ordinal
                )
        );

        var staleFiles = elements
            .Where(element => element.Name.LocalName == "StaleMacFfmpegFile")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(
            "$(BPPInstallerSourcePath)/SourceForBuild/macos/BepInEx/plugins/ffmpeg",
            staleFiles
        );
        Assert.Contains(
            "$(BPPInstallerSourcePath)/SourceForBuild/macos/BepInEx/plugins/ffmpeg-LICENSE.txt",
            staleFiles
        );
    }

    [Fact]
    public void Native_source_owns_video_and_audio_finalize_without_release_credentials()
    {
        var nativeRoot = Path.Combine(RepoRoot(), "native", "macos");
        var header = File.ReadAllText(Path.Combine(nativeRoot, "BppReplayVideoToolbox.h"));
        var build = File.ReadAllText(Path.Combine(nativeRoot, "build.sh"));

        Assert.Contains("BppVtPrepareRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppVtCommitRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppVtMuxAudio", header, StringComparison.Ordinal);
        Assert.Contains("-framework VideoToolbox", build, StringComparison.Ordinal);
        Assert.Contains("-framework AVFoundation", build, StringComparison.Ordinal);
        Assert.Contains("codesign --force --sign -", build, StringComparison.Ordinal);
        Assert.DoesNotContain("Developer ID", build, StringComparison.Ordinal);
        Assert.DoesNotContain("notary", build, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Native_plugin_availability_is_probed_before_background_work()
    {
        AssertNativeProbePrecedesTaskRun(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "Video",
                "CombatReplayVideoRecorder.cs"
            ),
            "_availabilityTask = Task.Run"
        );
        AssertNativeProbePrecedesTaskRun(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "Game",
                "HistoryPanel",
                "HistoryPanelReplayService.cs"
            ),
            "_ = Task.Run"
        );
        AssertNativeProbePrecedesTaskRun(
            Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "BazaarAgentReplayRecorderWiring.cs"),
            "_ = Task.Run"
        );
    }

    [Fact]
    public void Native_encoder_bounds_videotoolbox_delay_without_making_it_a_hard_requirement()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "native", "macos", "BppReplayVideoToolbox.mm")
        );

        Assert.Contains("int maxFrameDelayCount = 2;", source, StringComparison.Ordinal);
        Assert.Contains(
            "kVTCompressionPropertyKey_MaxFrameDelayCount,\n                maxFrameDelayCountNumber",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "SetCompressionProperty(\n                encoder,\n                kVTCompressionPropertyKey_MaxFrameDelayCount",
            source,
            StringComparison.Ordinal
        );
    }

    private static void AssertNativeProbePrecedesTaskRun(string sourcePath, string taskMarker)
    {
        var source = File.ReadAllText(sourcePath);
        var nativeProbe = source.IndexOf(
            "MacMetalVideoEncoder.TryGetAvailability",
            StringComparison.Ordinal
        );
        var backgroundTask = source.IndexOf(taskMarker, StringComparison.Ordinal);

        Assert.True(nativeProbe >= 0, $"Missing native availability probe in {sourcePath}.");
        Assert.True(backgroundTask >= 0, $"Missing background task boundary in {sourcePath}.");
        Assert.True(
            nativeProbe < backgroundTask,
            $"Native plugin availability must be probed before Task.Run in {sourcePath}."
        );
        Assert.DoesNotContain(
            "MacMetalVideoEncoder.TryGetAvailability",
            source[backgroundTask..],
            StringComparison.Ordinal
        );
    }

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
