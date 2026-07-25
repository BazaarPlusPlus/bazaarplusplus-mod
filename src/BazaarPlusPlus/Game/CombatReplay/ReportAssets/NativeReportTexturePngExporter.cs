#nullable enable
using BazaarPlusPlus.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

/// <summary>
/// Copies native textures through a private RenderTexture and performs an asynchronous GPU
/// readback. It never reads the display backbuffer and never makes the source texture readable.
/// </summary>
internal static class NativeReportTexturePngExporter
{
    internal static Task<ReportAssetProducedFile> ExportTextureAsync(
        Texture source,
        Rect sourceRectPixels,
        int outputWidth,
        int outputHeight,
        string outputPath,
        CancellationToken cancellationToken,
        ReportAssetReadbackSource readbackSource
    )
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (outputWidth <= 0 || outputHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputWidth));
        if (
            source.width <= 0
            || source.height <= 0
            || sourceRectPixels.width <= 0
            || sourceRectPixels.height <= 0
            || sourceRectPixels.xMin < 0
            || sourceRectPixels.yMin < 0
            || sourceRectPixels.xMax > source.width
            || sourceRectPixels.yMax > source.height
        )
        {
            throw new InvalidDataException("Native report texture has invalid source geometry.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var target = CreateTarget(outputWidth, outputHeight, "BPP_ReportTextureCopy");
        try
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.Clear(clearDepth: true, clearColor: true, UnityEngine.Color.clear);
                Graphics.Blit(
                    source,
                    target,
                    new Vector2(
                        sourceRectPixels.width / source.width,
                        sourceRectPixels.height / source.height
                    ),
                    new Vector2(
                        sourceRectPixels.x / source.width,
                        sourceRectPixels.y / source.height
                    )
                );
            }
            finally
            {
                RenderTexture.active = previous;
            }

            return ReadbackAndWriteAsync(target, outputPath, cancellationToken, readbackSource);
        }
        catch
        {
            Release(target);
            throw;
        }
    }

    internal static Task<ReportAssetProducedFile> ReadbackAndWriteAsync(
        RenderTexture renderTexture,
        string outputPath,
        CancellationToken cancellationToken,
        ReportAssetReadbackSource readbackSource,
        bool trimTransparentBounds = false,
        int transparentPaddingPixels = 0
    )
    {
        if (renderTexture == null)
            throw new ArgumentNullException(nameof(renderTexture));
        if (!renderTexture.IsCreated())
            throw new InvalidDataException("Native report render target is not created.");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException(
                "Report texture output path is required.",
                nameof(outputPath)
            );
        if (transparentPaddingPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(transparentPaddingPixels));

        cancellationToken.ThrowIfCancellationRequested();
        var width = renderTexture.width;
        var height = renderTexture.height;
        // Raw material textures and offscreen camera targets have the opposite authored vertical
        // convention from Unity Sprite geometry. Keep the source contract explicit so adding a new
        // asset path cannot silently inherit the wrong normalization.
        var flipVertically = ReportAssetPixelOrientation.RequiresVerticalFlip(
            SystemInfo.graphicsUVStartsAtTop,
            readbackSource
        );
        var completion = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        try
        {
            AsyncGPUReadback.Request(
                renderTexture,
                0,
                TextureFormat.RGBA32,
                request =>
                {
                    try
                    {
                        if (request.hasError)
                            throw new InvalidOperationException(
                                "Native report texture GPU readback failed."
                            );

                        var pixels = new byte[checked(width * height * 4)];
                        request.GetData<byte>().CopyTo(pixels);
                        if (flipVertically)
                            ReportAssetPixelOrientation.FlipVertical(pixels, width, height);
                        completion.TrySetResult(pixels);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        Release(renderTexture);
                    }
                }
            );
        }
        catch
        {
            Release(renderTexture);
            throw;
        }

        return CompleteWriteAsync(
            completion.Task,
            width,
            height,
            outputPath,
            cancellationToken,
            trimTransparentBounds,
            transparentPaddingPixels
        );
    }

    internal static RenderTexture CreateTarget(int width, int height, string name)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
        {
            throw new InvalidDataException(
                $"Native report target {width}x{height} exceeds max texture size {SystemInfo.maxTextureSize}."
            );
        }

        var target = new RenderTexture(
            width,
            height,
            depth: 24,
            format: RenderTextureFormat.ARGB32,
            readWrite: RenderTextureReadWrite.Default
        )
        {
            name = name,
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1,
        };
        if (!target.Create())
        {
            Object.Destroy(target);
            throw new InvalidOperationException(
                $"Failed to create native report target {width}x{height}."
            );
        }
        return target;
    }

    private static async Task<ReportAssetProducedFile> CompleteWriteAsync(
        Task<byte[]> readback,
        int width,
        int height,
        string outputPath,
        CancellationToken cancellationToken,
        bool trimTransparentBounds,
        int transparentPaddingPixels
    )
    {
        var pixels = await readback.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(
                () =>
                    WriteValidatedPng(
                        pixels,
                        width,
                        height,
                        outputPath,
                        trimTransparentBounds,
                        transparentPaddingPixels
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static ReportAssetProducedFile WriteValidatedPng(
        byte[] pixels,
        int width,
        int height,
        string outputPath,
        bool trimTransparentBounds,
        int transparentPaddingPixels
    )
    {
        if (pixels.Length != checked(width * height * 4))
            throw new InvalidDataException("Native report readback byte length is invalid.");

        var visiblePixels = 0;
        var coloredPixels = 0;
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] >= 8)
                visiblePixels++;
            if (pixels[offset - 3] >= 8 || pixels[offset - 2] >= 8 || pixels[offset - 1] >= 8)
                coloredPixels++;
        }
        var visibleRatio = visiblePixels / (double)(width * height);
        if (visibleRatio < 0.005d)
        {
            var coloredRatio = coloredPixels / (double)(width * height);
            throw new InvalidDataException(
                $"Native report texture is effectively transparent ({visibleRatio:P2}); "
                    + $"non-black RGB ratio is {coloredRatio:P2}."
            );
        }

        var outputPixels = pixels;
        var outputWidth = width;
        var outputHeight = height;
        if (trimTransparentBounds)
        {
            if (
                !ReportAssetAlphaCrop.TryResolveBounds(
                    pixels,
                    width,
                    height,
                    transparentPaddingPixels,
                    out var cropBounds
                )
            )
            {
                throw new InvalidDataException(
                    "Native report texture has no visible alpha bounds."
                );
            }

            outputPixels = ReportAssetAlphaCrop.Crop(pixels, width, height, cropBounds);
            outputWidth = cropBounds.Width;
            outputHeight = cropBounds.Height;
        }

        AtomicFileWriter.Write(
            outputPath,
            temporaryPath =>
            {
                using var image = Image.LoadPixelData<Rgba32>(
                    outputPixels,
                    outputWidth,
                    outputHeight
                );
                image.SaveAsPng(temporaryPath);
            }
        );
        return new ReportAssetProducedFile(outputWidth, outputHeight);
    }

    private static void Release(RenderTexture? renderTexture)
    {
        if (renderTexture == null)
            return;
        if (renderTexture.IsCreated())
            renderTexture.Release();
        Object.Destroy(renderTexture);
    }
}
