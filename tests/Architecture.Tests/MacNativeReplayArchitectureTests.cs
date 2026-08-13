using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

public sealed class MacNativeReplayArchitectureTests
{
    [Fact]
    public void Desktop_payloads_have_no_ffmpeg_and_ship_native_render_plugins()
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
        Assert.DoesNotContain(elements, element => element.Name.LocalName == "WindowsFfmpegZip");
        Assert.DoesNotContain(
            elements,
            element => element.Name.LocalName == "WindowsFfmpegLicense"
        );
        Assert.Contains(elements, element => element.Name.LocalName == "WindowsReplayPlugin");
        Assert.Contains(elements, element => element.Name.LocalName == "IsWindowsHost");
        Assert.Contains(
            elements,
            element =>
                element.Name.LocalName == "Copy"
                && (element.Attribute("DestinationFiles")?.Value ?? string.Empty).Contains(
                    "TheBazaar_Data\\Plugins\\x86_64\\GfxPluginBppReplayMediaFoundation.dll",
                    StringComparison.Ordinal
                )
        );

        var unzips = elements.Where(element => element.Name.LocalName == "Unzip").ToList();
        Assert.DoesNotContain(
            unzips,
            element =>
                (element.Attribute("DestinationFolder")?.Value ?? string.Empty).Contains(
                    "SourceForBuild/macos",
                    StringComparison.Ordinal
                )
        );
        Assert.DoesNotContain(
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

        var staleWindowsFiles = elements
            .Where(element => element.Name.LocalName == "StaleWindowsFfmpegFile")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToList();
        Assert.Contains(
            staleWindowsFiles,
            path =>
                path.EndsWith("BepInEx/plugins/ffmpeg.exe", StringComparison.Ordinal)
                || path.EndsWith("BepInEx\\plugins\\ffmpeg.exe", StringComparison.Ordinal)
        );
        Assert.Contains(
            staleWindowsFiles,
            path => path.EndsWith("ffmpeg-LICENSE.txt", StringComparison.Ordinal)
        );

        var packageErrors = elements
            .Where(element => element.Name.LocalName == "Error")
            .Select(element => element.Attribute("Condition")?.Value ?? string.Empty)
            .ToList();
        Assert.Contains(
            packageErrors,
            condition =>
                condition.Contains("InstallerMacReplayPluginBundle", StringComparison.Ordinal)
        );
        Assert.Contains(
            packageErrors,
            condition =>
                condition.Contains("InstallerWindowsReplayPlugin", StringComparison.Ordinal)
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
        Assert.Contains("BppVtDiscardRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppVtMuxAudio", header, StringComparison.Ordinal);
        Assert.Contains("-framework VideoToolbox", build, StringComparison.Ordinal);
        Assert.Contains("-framework AVFoundation", build, StringComparison.Ordinal);
        Assert.Contains("codesign --force --sign -", build, StringComparison.Ordinal);
        Assert.DoesNotContain("Developer ID", build, StringComparison.Ordinal);
        Assert.DoesNotContain("notary", build, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Windows_native_source_requires_d3d11_hardware_mft_and_tracked_surfaces()
    {
        var nativeRoot = Path.Combine(RepoRoot(), "native", "windows");
        var header = File.ReadAllText(Path.Combine(nativeRoot, "BppReplayMediaFoundation.h"));
        var source = File.ReadAllText(Path.Combine(nativeRoot, "BppReplayMediaFoundation.cpp"));
        var normalizedSource = source.ReplaceLineEndings("\n");
        var build = File.ReadAllText(Path.Combine(nativeRoot, "build.ps1"));

        Assert.Contains("BppMfPrepareRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppMfDiscardRenderEvent", header, StringComparison.Ordinal);
        Assert.Contains("BppMfMuxAudio", header, StringComparison.Ordinal);
        Assert.Contains("MFCreateDXGISurfaceBuffer", source, StringComparison.Ordinal);
        Assert.Contains("IMFTrackedSample", source, StringComparison.Ordinal);
        Assert.Contains("MFT_ENUM_FLAG_HARDWARE", source, StringComparison.Ordinal);
        Assert.Contains("MFT_ENUM_HARDWARE_URL_Attribute", source, StringComparison.Ordinal);
        Assert.Contains("category != MFT_CATEGORY_VIDEO_ENCODER", source, StringComparison.Ordinal);
        Assert.Contains("TransformOutputsH264", source, StringComparison.Ordinal);
        Assert.Contains("firstFrameIndex + index", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nextFrameIndex", source, StringComparison.Ordinal);
        Assert.Contains("previousMultithreadProtection", source, StringComparison.Ordinal);
        Assert.Contains("destroyRequested", source, StringComparison.Ordinal);
        Assert.Contains("MF_READWRITE_D3D_OPTIONAL, FALSE", source, StringComparison.Ordinal);
        Assert.Contains(
            "outputColor.Nominal_Range = D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235;",
            source,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("outputColor.RGB_Range", source, StringComparison.Ordinal);
        Assert.Contains(
            "case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:\n        processorSourceFormat = DXGI_FORMAT_B8G8R8A8_UNORM;",
            normalizedSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:\n        processorSourceFormat = DXGI_FORMAT_R8G8B8A8_UNORM;",
            normalizedSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("ffmpeg", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/W4 /WX", build, StringComparison.Ordinal);
    }

    [Fact]
    public void Mac_native_writer_shutdown_and_metal_pipeline_are_serialized()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "native", "macos", "BppReplayVideoToolbox.mm")
        );

        Assert.Contains("CancelWriterOnQueue", source, StringComparison.Ordinal);
        Assert.Contains("writerClosed", source, StringComparison.Ordinal);
        Assert.Contains("gConversionPipelines", source, StringComparison.Ordinal);
        Assert.Contains("objectForKey:device", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_plugin_availability_is_probed_synchronously()
    {
        AssertNativeProbeIsSynchronous(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "Game",
                "CombatReplay",
                "Video",
                "CombatReplayVideoRecorder.cs"
            )
        );
        AssertNativeProbeIsSynchronous(
            Path.Combine(
                RepoRoot(),
                "src",
                "BazaarPlusPlus",
                "Game",
                "HistoryPanel",
                "HistoryPanelReplayService.cs"
            )
        );
        AssertNativeProbeIsSynchronous(
            Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "BazaarAgentReplayRecorderWiring.cs")
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

    private static void AssertNativeProbeIsSynchronous(string sourcePath)
    {
        var source = File.ReadAllText(sourcePath);
        var nativeProbe = source.IndexOf(
            "MacMetalVideoEncoder.TryGetAvailability",
            StringComparison.Ordinal
        );
        var windowsNativeProbe = source.IndexOf(
            "WindowsMediaFoundationVideoEncoder.TryGetAvailability",
            StringComparison.Ordinal
        );
        Assert.True(nativeProbe >= 0, $"Missing native availability probe in {sourcePath}.");
        Assert.True(
            windowsNativeProbe >= 0,
            $"Missing Windows native availability probe in {sourcePath}."
        );
        Assert.DoesNotContain("Task.Run", source, StringComparison.Ordinal);
    }

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
