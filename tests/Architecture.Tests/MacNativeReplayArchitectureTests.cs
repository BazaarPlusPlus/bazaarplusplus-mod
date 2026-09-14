using System.Runtime.CompilerServices;
using System.Text.Json;
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
            .Where(element => element.Name.LocalName == "StaleRecorderFile")
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

        Assert.Contains(
            staleFiles,
            path =>
                path.EndsWith("BepInEx/plugins/ffmpeg.exe", StringComparison.Ordinal)
                || path.EndsWith("BepInEx\\plugins\\ffmpeg.exe", StringComparison.Ordinal)
        );
        Assert.Contains(
            staleFiles,
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
    public void Mac_native_builds_target_the_game_macos_12_floor()
    {
        var replayBuild = File.ReadAllText(Path.Combine(RepoRoot(), "native", "macos", "build.sh"));
        var audioBuild = File.ReadAllText(
            Path.Combine(RepoRoot(), "native", "mac-audio-tap", "build.sh")
        );

        foreach (var build in new[] { replayBuild, audioBuild })
        {
            Assert.Contains("-mmacosx-version-min=12.0", build, StringComparison.Ordinal);
            Assert.DoesNotContain("-mmacosx-version-min=11.0", build, StringComparison.Ordinal);
            Assert.Contains("xcrun --sdk macosx", build, StringComparison.Ordinal);
            Assert.Contains("-isysroot", build, StringComparison.Ordinal);
            Assert.Contains("-Werror=unguarded-availability", build, StringComparison.Ordinal);
        }
        Assert.Contains(
            "-install_name @rpath/libBppMacAudio.dylib",
            audioBuild,
            StringComparison.Ordinal
        );

        var verifier = File.ReadAllText(Path.Combine(RepoRoot(), "native", "macos", "verify.sh"));
        Assert.Contains("vtool -show-build", verifier, StringComparison.Ordinal);
        Assert.Contains("lipo -archs", verifier, StringComparison.Ordinal);
        Assert.Contains("weak external", verifier, StringComparison.Ordinal);
        Assert.Contains("codesign --verify --strict", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_artifact_catalog_owns_content_freshness_and_abi_inputs()
    {
        using var catalog = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "native", "artifacts.json"))
        );
        var platforms = catalog.RootElement.GetProperty("platforms");

        var macos = platforms.GetProperty("macos");
        Assert.Equal("arm64", macos.GetProperty("architecture").GetString());
        Assert.Equal("12.0", macos.GetProperty("deploymentTarget").GetString());
        Assert.Equal("adhoc", macos.GetProperty("signing").GetString());
        var macInputs = macos
            .GetProperty("inputs")
            .EnumerateArray()
            .Select(input => input.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("native/mac-audio-tap/build.sh", macInputs);
        Assert.Contains("native/macos/build.sh", macInputs);
        Assert.Contains("native/macos/verify.sh", macInputs);
        Assert.Contains(
            "src/BazaarPlusPlus/Game/CombatReplay/Video/MacMetalVideoEncoder.cs",
            macInputs
        );
        Assert.Contains(
            "src/BazaarPlusPlus/Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs",
            macInputs
        );
        var macExports = macos
            .GetProperty("artifacts")[1]
            .GetProperty("requiredExports")
            .EnumerateArray()
            .Select(symbol => symbol.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BppVtDiscardRenderEvent", macExports);
        Assert.Contains("UnityPluginLoad", macExports);
        Assert.Contains("UnityPluginUnload", macExports);

        var windows = platforms.GetProperty("windows");
        Assert.Equal("x64", windows.GetProperty("architecture").GetString());
        Assert.Equal("unsigned", windows.GetProperty("signing").GetString());
        var windowsInputs = windows
            .GetProperty("inputs")
            .EnumerateArray()
            .Select(input => input.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("native/windows/build.ps1", windowsInputs);
        Assert.Contains("native/windows/test.ps1", windowsInputs);
        Assert.Contains(
            "src/BazaarPlusPlus/Game/CombatReplay/Video/WindowsMediaFoundationVideoEncoder.cs",
            windowsInputs
        );
        var windowsExports = windows
            .GetProperty("artifacts")[0]
            .GetProperty("requiredExports")
            .EnumerateArray()
            .Select(symbol => symbol.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BppMfDiscardRenderEvent", windowsExports);
        Assert.Contains("UnityPluginLoad", windowsExports);
        Assert.Contains("UnityPluginUnload", windowsExports);
    }

    [Fact]
    public void Publish_ensures_native_inputs_before_managed_packaging()
    {
        var runScript = File.ReadAllText(Path.Combine(RepoRoot(), "run.sh"));
        var publishStart = runScript.IndexOf("publish() {", StringComparison.Ordinal);
        var ensure = runScript.IndexOf(
            "ensure_native_release_inputs \"$installer_source\" \"$release_platform\"",
            publishStart,
            StringComparison.Ordinal
        );
        var remoteData = runScript.IndexOf(
            "fetch_remote_data \"${common_args[@]}\"",
            publishStart,
            StringComparison.Ordinal
        );
        Assert.True(ensure > publishStart);
        Assert.True(remoteData > ensure);
        var managedBuild = runScript.IndexOf(
            "dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj",
            remoteData,
            StringComparison.Ordinal
        );
        var prepareArchives = runScript.IndexOf(
            "prepare_installer_resource_archives \"$installer_source\" \"$release_platform\"",
            managedBuild,
            StringComparison.Ordinal
        );
        Assert.True(managedBuild > remoteData);
        Assert.True(prepareArchives > managedBuild);
        Assert.Contains(
            "\"-p:BppReleasePlatform=$release_platform\"",
            runScript,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "run prepare:resources -- --platform \"$release_platform\"",
            runScript,
            StringComparison.Ordinal
        );

        var project = XDocument.Load(
            Path.Combine(RepoRoot(), "src", "BazaarPlusPlus", "BazaarPlusPlus.csproj")
        );
        var releaseCopy = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value == "CopyToInstallerSource"
            );
        Assert.DoesNotContain("LocalMacReplayPluginBundle", releaseCopy.ToString());
        Assert.DoesNotContain("LocalWindowsReplayPlugin", releaseCopy.ToString());
        Assert.Contains("$(BppReleasePlatform)", releaseCopy.ToString());

        var releasePackage = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value == "PackageInstallerSource"
            );
        var zip = Assert.Single(
            releasePackage.Descendants(),
            element => element.Name.LocalName == "ZipDirectory"
        );
        Assert.Contains("$(BppReleasePlatform)", zip.ToString());

        var releaseValidation = project
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Target"
                && element.Attribute("Name")?.Value == "ValidateProductionReleasePlatform"
            );
        Assert.Contains("macos", releaseValidation.ToString(), StringComparison.Ordinal);
        Assert.Contains("windows", releaseValidation.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_native_source_requires_d3d11_hardware_mft_and_tracked_surfaces()
    {
        var nativeRoot = Path.Combine(RepoRoot(), "native", "windows");
        var header = File.ReadAllText(Path.Combine(nativeRoot, "BppReplayMediaFoundation.h"));
        var source = File.ReadAllText(Path.Combine(nativeRoot, "BppReplayMediaFoundation.cpp"));
        var normalizedSource = source.ReplaceLineEndings("\n");
        var build = File.ReadAllText(Path.Combine(nativeRoot, "build.ps1"));
        var smokeTest = File.ReadAllText(Path.Combine(nativeRoot, "test.ps1"));

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
        Assert.Contains("/utf-8", build, StringComparison.Ordinal);
        Assert.Contains("/utf-8", smokeTest, StringComparison.Ordinal);
        Assert.Contains("/Brepro", build, StringComparison.Ordinal);
        Assert.Contains("dumpbin /headers", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dumpbin /exports", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dumpbin /dependents", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Join-Path $PSHOME 'Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1'",
            build,
            StringComparison.Ordinal
        );
        Assert.Contains("Import-Module -Name $securityModulePath -ErrorAction Stop", build);
        Assert.Contains("Get-AuthenticodeSignature", build, StringComparison.Ordinal);
        Assert.Contains("test.ps1", build, StringComparison.Ordinal);
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

    [Fact]
    public void Render_event_packet_ownership_is_documented_and_asymmetric_on_both_backends()
    {
        // The compiler cannot tell Cancel from Discard: both take the same opaque pointer and
        // differ only in how many references they consume. Discard on a queued event is a
        // use-after-free, Cancel on an unqueued one leaks, so the contract is pinned here.
        // Both headers carry the same contract; neither may drift away from it alone.
        foreach (
            var headerPath in new[]
            {
                Path.Combine(RepoRoot(), "native", "macos", "BppReplayVideoToolbox.h"),
                Path.Combine(RepoRoot(), "native", "windows", "BppReplayMediaFoundation.h"),
            }
        )
        {
            var header = File.ReadAllText(headerPath);
            Assert.Contains("releases the caller's reference", header, StringComparison.Ordinal);
            Assert.Contains("releases BOTH references", header, StringComparison.Ordinal);
            Assert.Contains("event that WAS queued", header, StringComparison.Ordinal);
        }

        var macSource = File.ReadAllText(
                Path.Combine(RepoRoot(), "native", "macos", "BppReplayVideoToolbox.mm")
            )
            .ReplaceLineEndings("\n");
        Assert.Contains(
            "        CompleteRenderEvent(packet->encoder);\n    }\n    ReleaseRenderEventPacket(packet);",
            macSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "        CompleteRenderEvent(packet->encoder);\n        ReleaseRenderEventPacket(packet);\n    }\n    ReleaseRenderEventPacket(packet);",
            macSource,
            StringComparison.Ordinal
        );

        var windowsSource = File.ReadAllText(
                Path.Combine(RepoRoot(), "native", "windows", "BppReplayMediaFoundation.cpp")
            )
            .ReplaceLineEndings("\n");
        Assert.Contains(
            "        CompleteRenderEvent(packet->encoder);\n    }\n    ReleasePacket(packet);",
            windowsSource,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "        CompleteRenderEvent(packet->encoder);\n        ReleasePacket(packet);\n    }\n    ReleasePacket(packet);",
            windowsSource,
            StringComparison.Ordinal
        );

        var session = File.ReadAllText(
                Path.Combine(
                    RepoRoot(),
                    "src",
                    "BazaarPlusPlus",
                    "Game",
                    "CombatReplay",
                    "Video",
                    "ReplayVideoCaptureSession.cs"
                )
            )
            .ReplaceLineEndings("\n");
        // Count rather than Contains: the session has more than one submission path, and a
        // single correct branch must not vouch for an inverted sibling.
        var queuedBranches = session.Split("if (eventQueued)").Length - 1;
        var correctBranches =
            session
                .Split(
                    "if (eventQueued)\n                encoder.CancelRenderEvent(eventData);\n            else\n                encoder.DiscardRenderEvent(eventData);"
                )
                .Length - 1;
        Assert.True(queuedBranches > 0, "No queued-event abandon branch found in the session.");
        Assert.Equal(queuedBranches, correctBranches);
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
        if (Path.GetFileName(sourcePath) == "HistoryPanelReplayService.cs")
        {
            var method = source[
                source.IndexOf(
                    "public void PrewarmRecordingAvailability()",
                    StringComparison.Ordinal
                )..source.IndexOf("public bool CanRecordReplay(", StringComparison.Ordinal)
            ];
            Assert.DoesNotContain("Task.Run", method, StringComparison.Ordinal);
        }
        else
            Assert.DoesNotContain("Task.Run", source, StringComparison.Ordinal);
    }

    private static string RepoRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
