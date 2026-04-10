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

            WriteCurrentFrameToFile(filePath, request.CaptureSource);
            BppLog.Info("ScreenshotService", $"Saved screenshot: {filePath}");
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
            BppLog.Error("ScreenshotService", "Failed to capture screenshot.", ex);
            return null;
        }
    }

    private static void WriteCurrentFrameToFile(
        string filePath,
        RunScreenshotCaptureSource captureSource
    )
    {
        if (captureSource == RunScreenshotCaptureSource.PvpBattleStart)
        {
            if (TryCaptureMainCameraRenderToFile(filePath))
                return;
        }

        var width = Screen.width;
        var height = Screen.height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                $"Cannot capture screenshot with invalid size {width}x{height}."
            );

        Texture2D? texture = null;
        var previousActive = RenderTexture.active;
        try
        {
            texture = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            var pngBytes = texture.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0)
                throw new InvalidOperationException("Screenshot PNG encoding returned no data.");

            File.WriteAllBytes(filePath, pngBytes);
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
    }

    private static bool TryCaptureMainCameraRenderToFile(string filePath)
    {
        var mainCamera = Camera.main;
        if (mainCamera == null)
            return false;

        var width = mainCamera.scaledPixelWidth > 0 ? mainCamera.scaledPixelWidth : Screen.width;
        var height = mainCamera.scaledPixelHeight > 0 ? mainCamera.scaledPixelHeight : Screen.height;
        if (width <= 0 || height <= 0)
            return false;

        Texture2D? texture = null;
        RenderTexture? renderTexture = null;
        var previousActive = RenderTexture.active;
        var previousTargetTexture = mainCamera.targetTexture;
        var previousRect = mainCamera.rect;
        try
        {
            renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            mainCamera.targetTexture = renderTexture;
            mainCamera.rect = new Rect(0f, 0f, 1f, 1f);
            RenderTexture.active = renderTexture;
            mainCamera.Render();

            texture = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            var pngBytes = texture.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0)
                throw new InvalidOperationException("Screenshot PNG encoding returned no data.");

            File.WriteAllBytes(filePath, pngBytes);
            return true;
        }
        finally
        {
            mainCamera.targetTexture = previousTargetTexture;
            mainCamera.rect = previousRect;
            RenderTexture.active = previousActive;
            if (renderTexture != null)
                RenderTexture.ReleaseTemporary(renderTexture);
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
    }
}
