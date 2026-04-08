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

    public ScreenshotCaptureResult? CaptureCurrentFrame(ScreenshotCaptureRequest request)
    {
        if (string.IsNullOrWhiteSpace(_directoryPath))
            return null;
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var capturedAtLocal = _nowProvider();
            var capturedAtUtc = capturedAtLocal.ToUniversalTime();
            var screenshotId = Guid.NewGuid().ToString("N");
            var relativePath = ScreenshotPathBuilder.BuildRelativePath(
                request.RunId,
                capturedAtLocal,
                request.CaptureSource,
                screenshotId,
                request.BattleId
            );
            var filePath = Path.Combine(_directoryPath, relativePath);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
                Directory.CreateDirectory(directoryPath);

            ScreenCapture.CaptureScreenshot(filePath);
            BppLog.Info("ScreenshotService", $"Queued screenshot save: {filePath}");
            return new ScreenshotCaptureResult
            {
                ScreenshotId = screenshotId,
                RunId = request.RunId,
                BattleId = request.BattleId,
                CaptureSource = request.CaptureSource,
                RelativePath = relativePath,
                FilePath = filePath,
                CapturedAtLocal = capturedAtLocal,
                CapturedAtUtc = capturedAtUtc,
            };
        }
        catch (Exception ex)
        {
            BppLog.Error("ScreenshotService", "Failed to queue screenshot capture.", ex);
            return null;
        }
    }
}
