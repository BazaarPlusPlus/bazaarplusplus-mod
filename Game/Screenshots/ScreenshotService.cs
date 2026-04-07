#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class ScreenshotService
{
    private readonly string _directoryPath;
    private readonly Func<DateTimeOffset> _nowProvider;

    public ScreenshotService(string directoryPath, Func<DateTimeOffset>? nowProvider = null)
    {
        _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
        _nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
    }

    public string? CaptureCurrentFrame(string? runId)
    {
        if (string.IsNullOrWhiteSpace(_directoryPath))
            return null;

        try
        {
            var capturedAtLocal = _nowProvider();
            var relativePath = ScreenshotPathBuilder.BuildRelativePath(runId, capturedAtLocal);
            var filePath = Path.Combine(_directoryPath, relativePath);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
                Directory.CreateDirectory(directoryPath);

            ScreenCapture.CaptureScreenshot(filePath);
            BppLog.Info("ScreenshotService", $"Queued screenshot save: {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            BppLog.Error("ScreenshotService", "Failed to queue screenshot capture.", ex);
            return null;
        }
    }
}
