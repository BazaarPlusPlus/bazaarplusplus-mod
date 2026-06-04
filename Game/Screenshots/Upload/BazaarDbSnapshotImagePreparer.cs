#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbSnapshotImagePreparer
{
    private static readonly int[] PngLongestEdgeTargets = [1920, 1600, 1280, 1024, 768];
    private static readonly int[] JpegQualityTargets = [90, 82, 74, 66, 58, 50];

    private readonly string _uploadCacheDirectoryPath;

    public BazaarDbSnapshotImagePreparer(string uploadCacheDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(uploadCacheDirectoryPath))
            throw new ArgumentException(
                "Upload cache directory is required.",
                nameof(uploadCacheDirectoryPath)
            );

        _uploadCacheDirectoryPath = uploadCacheDirectoryPath;
    }

    public BazaarDbSnapshotUploadImage? Prepare(string snapshotId, string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(snapshotId) || string.IsNullOrWhiteSpace(absolutePath))
            return null;

        var sourceBytes = File.ReadAllBytes(absolutePath);
        if (sourceBytes.Length == 0)
            return null;
        if (sourceBytes.Length <= BazaarDbSnapshotUploadLimits.MaxUploadImageBytes)
            return new BazaarDbSnapshotUploadImage
            {
                Bytes = sourceBytes,
                ContentType = "image/png",
                SourcePath = absolutePath,
            };

        var cachedPngPath = Path.Combine(_uploadCacheDirectoryPath, $"{snapshotId}.png");
        var cachedPng = TryReadCached(cachedPngPath, "image/png");
        if (cachedPng != null)
            return cachedPng;

        var cachedJpegPath = Path.Combine(_uploadCacheDirectoryPath, $"{snapshotId}.jpg");
        var cachedJpeg = TryReadCached(cachedJpegPath, "image/jpeg");
        if (cachedJpeg != null)
            return cachedJpeg;

        return PrepareResized(sourceBytes, cachedPngPath, cachedJpegPath);
    }

    private BazaarDbSnapshotUploadImage? TryReadCached(string path, string contentType)
    {
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length > BazaarDbSnapshotUploadLimits.MaxUploadImageBytes)
            return null;

        return new BazaarDbSnapshotUploadImage
        {
            Bytes = bytes,
            ContentType = contentType,
            SourcePath = path,
        };
    }

    private static BazaarDbSnapshotUploadImage? PrepareResized(
        byte[] sourceBytes,
        string cachedPngPath,
        string cachedJpegPath
    )
    {
        Texture2D? sourceTexture = null;
        try
        {
            sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!sourceTexture.LoadImage(sourceBytes, markNonReadable: false))
                return null;

            foreach (var target in PngLongestEdgeTargets)
            {
                using var candidate = ResizedTextureLease.Create(sourceTexture, target);
                var bytes = candidate.Texture.EncodeToPNG();
                if (
                    bytes is { Length: > 0 }
                    && bytes.Length <= BazaarDbSnapshotUploadLimits.MaxUploadImageBytes
                )
                {
                    WriteAllBytesAtomically(cachedPngPath, bytes);
                    return new BazaarDbSnapshotUploadImage
                    {
                        Bytes = bytes,
                        ContentType = "image/png",
                        SourcePath = cachedPngPath,
                    };
                }
            }

            for (
                var targetIndex = PngLongestEdgeTargets.Length - 1;
                targetIndex >= 0;
                targetIndex--
            )
            {
                using var candidate = ResizedTextureLease.Create(
                    sourceTexture,
                    PngLongestEdgeTargets[targetIndex]
                );
                foreach (var quality in JpegQualityTargets)
                {
                    var bytes = candidate.Texture.EncodeToJPG(quality);
                    if (
                        bytes is not { Length: > 0 }
                        || bytes.Length > BazaarDbSnapshotUploadLimits.MaxUploadImageBytes
                    )
                    {
                        continue;
                    }

                    WriteAllBytesAtomically(cachedJpegPath, bytes);
                    return new BazaarDbSnapshotUploadImage
                    {
                        Bytes = bytes,
                        ContentType = "image/jpeg",
                        SourcePath = cachedJpegPath,
                    };
                }
            }
        }
        finally
        {
            if (sourceTexture != null)
                UnityEngine.Object.Destroy(sourceTexture);
        }

        return null;
    }

    private static void WriteAllBytesAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private sealed class ResizedTextureLease : IDisposable
    {
        private ResizedTextureLease(Texture2D texture)
        {
            Texture = texture;
        }

        public Texture2D Texture { get; }

        public static ResizedTextureLease Create(Texture2D source, int longestEdge)
        {
            var sourceLongestEdge = Math.Max(source.width, source.height);
            var scale =
                sourceLongestEdge > 0 ? Math.Min(1f, longestEdge / (float)sourceLongestEdge) : 1f;
            var width = Math.Max(1, Mathf.RoundToInt(source.width * scale));
            var height = Math.Max(1, Mathf.RoundToInt(source.height * scale));
            if (width == source.width && height == source.height)
                return new ResizedTextureLease(UnityEngine.Object.Instantiate(source));

            var sourcePixels = source.GetPixels32();
            var resizedPixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                var sourceY = Math.Min(
                    source.height - 1,
                    Mathf.RoundToInt(y * (source.height - 1) / Math.Max(1f, height - 1f))
                );
                for (var x = 0; x < width; x++)
                {
                    var sourceX = Math.Min(
                        source.width - 1,
                        Mathf.RoundToInt(x * (source.width - 1) / Math.Max(1f, width - 1f))
                    );
                    resizedPixels[(y * width) + x] = sourcePixels[
                        (sourceY * source.width) + sourceX
                    ];
                }
            }

            var resized = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
            resized.SetPixels32(resizedPixels);
            resized.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return new ResizedTextureLease(resized);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(Texture);
        }
    }
}
