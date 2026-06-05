#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UnityEngine;
using UnityEngine.Rendering;

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

    public Task<ScreenshotCaptureResult?> CaptureCurrentFrameAsync(ScreenshotCaptureRequest request)
    {
        if (string.IsNullOrWhiteSpace(_directoryPath))
            return Task.FromResult<ScreenshotCaptureResult?>(null);
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var capturedAtLocal = _nowProvider();
            var capturedAtUtc = capturedAtLocal.ToUniversalTime();
            var screenshotId = Guid.NewGuid().ToString("N");
            var relativePath = ScreenshotPathBuilder.BuildRelativePath(
                request.RunId,
                capturedAtLocal
            );
            var filePath = Path.Combine(_directoryPath, relativePath);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
                Directory.CreateDirectory(directoryPath);

            var result = new ScreenshotCaptureResult
            {
                ScreenshotId = screenshotId,
                RunId = request.RunId,
                HeroName = request.HeroName,
                BattleId = request.BattleId,
                CaptureSource = request.CaptureSource,
                RelativePath = relativePath,
                FilePath = filePath,
                CapturedAtLocal = capturedAtLocal,
                CapturedAtUtc = capturedAtUtc,
            };
            return CaptureAndWriteCurrentFrameAsync(filePath)
                .ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                        {
                            BppLog.Error(
                                "ScreenshotService",
                                "Failed to capture screenshot.",
                                task.Exception!.GetBaseException()
                            );
                            return null;
                        }

                        if (task.IsCanceled || !task.Result)
                            return null;

                        BppLog.Info("ScreenshotService", $"Saved screenshot: {filePath}");
                        return result;
                    },
                    TaskScheduler.Default
                );
        }
        catch (Exception ex)
        {
            BppLog.Error("ScreenshotService", "Failed to capture screenshot.", ex);
            return Task.FromResult<ScreenshotCaptureResult?>(null);
        }
    }

    private static Task<bool> CaptureAndWriteCurrentFrameAsync(string filePath)
    {
        var width = Screen.width;
        var height = Screen.height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                $"Cannot capture screenshot with invalid size {width}x{height}."
            );

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        RenderTexture? renderTexture = null;
        try
        {
            renderTexture = new RenderTexture(
                width,
                height,
                depth: 0,
                format: RenderTextureFormat.ARGB32
            )
            {
                name = "BPP_EndOfRunScreenshot",
                useMipMap = false,
                autoGenerateMips = false,
            };
            if (!renderTexture.Create())
                throw new InvalidOperationException(
                    $"Failed to create RenderTexture {width}x{height} for screenshot capture."
                );

            ScreenCapture.CaptureScreenshotIntoRenderTexture(renderTexture);
            var capturedTexture = renderTexture;
            AsyncGPUReadback.Request(
                capturedTexture,
                0,
                TextureFormat.RGBA32,
                request =>
                    OnReadbackComplete(
                        request,
                        capturedTexture,
                        width,
                        height,
                        filePath,
                        completion
                    )
            );
        }
        catch
        {
            ReleaseRenderTexture(renderTexture);
            throw;
        }

        return completion.Task;
    }

    private static void OnReadbackComplete(
        AsyncGPUReadbackRequest request,
        RenderTexture renderTexture,
        int width,
        int height,
        string filePath,
        TaskCompletionSource<bool> completion
    )
    {
        try
        {
            if (request.hasError)
            {
                completion.TrySetException(
                    new InvalidOperationException("AsyncGPUReadback returned an error.")
                );
                return;
            }

            var pixels = new byte[width * height * 4];
            request.GetData<byte>().CopyTo(pixels);
            if (!SystemInfo.graphicsUVStartsAtTop)
                FlipVerticalRgba32(pixels, width, height);

            _ = Task.Run(() =>
            {
                try
                {
                    WriteRgba32PngAtomically(filePath, pixels, width, height);
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            ReleaseRenderTexture(renderTexture);
        }
    }

    private static void WriteRgba32PngAtomically(
        string filePath,
        byte[] pixels,
        int width,
        int height
    )
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var image = Image.LoadPixelData<Rgba32>(pixels, width, height))
            {
                image.SaveAsPng(tempPath);
            }

            if (File.Exists(filePath))
                File.Replace(tempPath, filePath, null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void FlipVerticalRgba32(byte[] buffer, int width, int height)
    {
        var stride = width * 4;
        var rowBuffer = new byte[stride];
        for (var row = 0; row < height / 2; row++)
        {
            var topOffset = row * stride;
            var bottomOffset = (height - 1 - row) * stride;

            Buffer.BlockCopy(buffer, topOffset, rowBuffer, 0, stride);
            Buffer.BlockCopy(buffer, bottomOffset, buffer, topOffset, stride);
            Buffer.BlockCopy(rowBuffer, 0, buffer, bottomOffset, stride);
        }
    }

    private static void ReleaseRenderTexture(RenderTexture? renderTexture)
    {
        if (renderTexture == null)
            return;

        try
        {
            if (renderTexture.IsCreated())
                renderTexture.Release();
            UnityEngine.Object.Destroy(renderTexture);
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "ScreenshotService",
                $"Failed to release screenshot RenderTexture: {ex.Message}"
            );
        }
    }
}
