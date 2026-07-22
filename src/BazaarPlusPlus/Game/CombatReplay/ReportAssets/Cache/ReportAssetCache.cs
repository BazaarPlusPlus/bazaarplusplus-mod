#nullable enable
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

internal sealed record ReportAssetResolvedAsset(
    string RenderKeyHash,
    string ContentHash,
    string FilePath,
    int PixelWidth,
    int PixelHeight
);

internal sealed class ReportAssetCacheConflictException : InvalidOperationException
{
    internal ReportAssetCacheConflictException(string message)
        : base(message) { }
}

internal sealed class ReportAssetCache
{
    private const int MappingSchemaVersion = 1;
    private const int MaximumMappingBytes = 1024 * 1024;
    private const int LockAttemptCount = 200;
    private const int LockRetryMilliseconds = 10;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly HashSet<string> MappingPropertyNames = new(
        [
            "schemaVersion",
            "renderKeyHash",
            "canonicalRenderKey",
            "contentHash",
            "pixelWidth",
            "pixelHeight",
        ],
        StringComparer.Ordinal
    );

    private readonly string _rootPath;

    internal ReportAssetCache(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Report asset cache root is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
    }

    internal string RootPath => _rootPath;

    internal bool TryResolve(ReportAssetRenderKey key, out ReportAssetResolvedAsset resolvedAsset)
    {
        var request = RenderRequest.Create(key);
        var validation = Validate(request);
        if (validation.ResolvedAsset != null)
        {
            resolvedAsset = validation.ResolvedAsset;
            return true;
        }

        if (!validation.MappingExists)
        {
            resolvedAsset = null!;
            return false;
        }

        using var repairLock = AcquireExclusiveLock("render", request.RenderKeyHash);
        validation = Validate(request);
        if (validation.ResolvedAsset != null)
        {
            resolvedAsset = validation.ResolvedAsset;
            return true;
        }

        if (validation.MappingExists)
            QuarantineInvalid(validation, request.RenderKeyHash);

        resolvedAsset = null!;
        return false;
    }

    internal ReportAssetResolvedAsset Publish(
        ReportAssetRenderKey key,
        string sourcePngPath,
        int expectedPixelWidth = 0,
        int expectedPixelHeight = 0
    )
    {
        var request = RenderRequest.Create(key);
        var sourcePath = ValidateRegularFile(sourcePngPath, "Captured report asset");
        if (!TryDecodePng(sourcePath, out var pixelWidth, out var pixelHeight))
            throw new InvalidDataException("Captured report asset is not a fully decodable PNG.");
        if (
            (expectedPixelWidth > 0 && expectedPixelWidth != pixelWidth)
            || (expectedPixelHeight > 0 && expectedPixelHeight != pixelHeight)
        )
        {
            throw new InvalidDataException(
                $"Captured report asset geometry {pixelWidth}x{pixelHeight} does not match expected {expectedPixelWidth}x{expectedPixelHeight}."
            );
        }

        var contentHash = ReportAssetHash.Sha256File(sourcePath);
        var objectPath = BuildObjectPath(contentHash);
        using var renderLock = AcquireExclusiveLock("render", request.RenderKeyHash);

        var existing = Validate(request);
        if (existing.ResolvedAsset != null)
        {
            if (
                !string.Equals(
                    existing.ResolvedAsset.ContentHash,
                    contentHash,
                    StringComparison.Ordinal
                )
                || existing.ResolvedAsset.PixelWidth != pixelWidth
                || existing.ResolvedAsset.PixelHeight != pixelHeight
            )
            {
                throw new ReportAssetCacheConflictException(
                    $"Render key {request.RenderKeyHash} is already mapped to different content."
                );
            }

            return existing.ResolvedAsset;
        }

        if (existing.MappingExists)
            QuarantineInvalid(existing, request.RenderKeyHash);

        using (AcquireExclusiveLock("object", contentHash))
        {
            if (PathExists(objectPath))
            {
                if (!ValidateObject(objectPath, contentHash, pixelWidth, pixelHeight))
                    QuarantinePath(objectPath, "object", contentHash);
            }

            if (!PathExists(objectPath))
                CopyImmutable(sourcePath, objectPath, contentHash);

            if (!ValidateObject(objectPath, contentHash, pixelWidth, pixelHeight))
                throw new InvalidDataException("Published report asset object failed validation.");
        }

        // The content object must be durable and validated before its render-key mapping exists.
        var mapping = new CacheMapping(
            MappingSchemaVersion,
            request.RenderKeyHash,
            request.CanonicalJson,
            contentHash,
            pixelWidth,
            pixelHeight
        );
        var mappingPath = BuildMappingPath(request.RenderKeyHash);
        WriteMappingImmutable(mappingPath, mapping);

        var terminal = Validate(request);
        if (terminal.ResolvedAsset == null)
            throw new InvalidDataException("Published report asset mapping failed validation.");
        return terminal.ResolvedAsset;
    }

    internal string CreateStagingFilePath(string renderKeyHash)
    {
        if (!ReportAssetHash.IsLowerHexSha256(renderKeyHash))
            throw new ArgumentException("Render-key hash is invalid.", nameof(renderKeyHash));

        var directory = EnsureCacheDirectory("staging", renderKeyHash[..2]);
        return Path.Combine(directory, $"{renderKeyHash}.{Guid.NewGuid():N}.png");
    }

    private CacheValidation Validate(RenderRequest request)
    {
        var mappingPath = BuildMappingPath(request.RenderKeyHash);
        if (!PathExists(mappingPath))
            return CacheValidation.Missing(mappingPath);

        if (!TryReadMapping(mappingPath, out var mapping))
            return CacheValidation.InvalidMapping(mappingPath);
        if (
            mapping.SchemaVersion != MappingSchemaVersion
            || !string.Equals(
                mapping.RenderKeyHash,
                request.RenderKeyHash,
                StringComparison.Ordinal
            )
            || !string.Equals(
                mapping.CanonicalRenderKey,
                request.CanonicalJson,
                StringComparison.Ordinal
            )
            || !ReportAssetHash.IsLowerHexSha256(mapping.ContentHash)
            || mapping.PixelWidth <= 0
            || mapping.PixelHeight <= 0
        )
        {
            return CacheValidation.InvalidMapping(mappingPath);
        }

        var objectPath = BuildObjectPath(mapping.ContentHash);
        if (!PathExists(objectPath))
            return CacheValidation.MissingObject(mappingPath, objectPath);
        if (!IsRegularFile(objectPath))
            return CacheValidation.InvalidObject(mappingPath, objectPath);

        string actualHash;
        try
        {
            actualHash = ReportAssetHash.Sha256File(objectPath);
        }
        catch (Exception ex) when (IsExpectedFileException(ex))
        {
            return CacheValidation.InvalidObject(mappingPath, objectPath);
        }

        if (!string.Equals(actualHash, mapping.ContentHash, StringComparison.Ordinal))
            return CacheValidation.InvalidObject(mappingPath, objectPath);
        if (!TryDecodePng(objectPath, out var width, out var height))
            return CacheValidation.InvalidObject(mappingPath, objectPath);
        if (width != mapping.PixelWidth || height != mapping.PixelHeight)
            return CacheValidation.InvalidMapping(mappingPath, objectPath);

        return CacheValidation.Valid(
            mappingPath,
            objectPath,
            new ReportAssetResolvedAsset(
                request.RenderKeyHash,
                mapping.ContentHash,
                Path.GetFullPath(objectPath),
                width,
                height
            )
        );
    }

    private void QuarantineInvalid(CacheValidation validation, string renderKeyHash)
    {
        if (validation.QuarantineObject && !string.IsNullOrWhiteSpace(validation.ObjectPath))
        {
            var objectHash = Path.GetFileNameWithoutExtension(validation.ObjectPath);
            if (ReportAssetHash.IsLowerHexSha256(objectHash))
            {
                using var objectLock = AcquireExclusiveLock("object", objectHash);
                QuarantinePath(validation.ObjectPath, "object", objectHash);
            }
        }

        QuarantinePath(validation.MappingPath, "mapping", renderKeyHash);
    }

    private void WriteMappingImmutable(string mappingPath, CacheMapping mapping)
    {
        var json = SerializeMapping(mapping);
        var bytes = Utf8NoBom.GetBytes(json);
        var directory = EnsureCacheDirectory("keys", mapping.RenderKeyHash[..2]);
        var expectedMappingPath = Path.Combine(directory, mapping.RenderKeyHash + ".json");
        if (!string.Equals(mappingPath, expectedMappingPath, StringComparison.Ordinal))
            throw new InvalidDataException("Render-key mapping escaped its cache directory.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(mappingPath)}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            EnsureCacheDirectory("keys", mapping.RenderKeyHash[..2]);
            WriteNewFile(temporaryPath, bytes);
            try
            {
                EnsureCacheDirectory("keys", mapping.RenderKeyHash[..2]);
                File.Move(temporaryPath, mappingPath);
            }
            catch (IOException) when (File.Exists(mappingPath))
            {
                if (!TryReadMapping(mappingPath, out var existing) || !Equals(existing, mapping))
                {
                    throw new ReportAssetCacheConflictException(
                        $"Render-key mapping {mapping.RenderKeyHash} already exists with different content."
                    );
                }
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private void CopyImmutable(string sourcePath, string objectPath, string contentHash)
    {
        var directory = EnsureCacheDirectory("objects", contentHash[..2]);
        var expectedObjectPath = Path.Combine(directory, contentHash + ".png");
        if (!string.Equals(objectPath, expectedObjectPath, StringComparison.Ordinal))
            throw new InvalidDataException("Content object escaped its cache directory.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(objectPath)}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            EnsureCacheDirectory("objects", contentHash[..2]);
            using (
                var source = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                )
            )
            using (
                var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            try
            {
                EnsureCacheDirectory("objects", contentHash[..2]);
                File.Move(temporaryPath, objectPath);
            }
            catch (IOException) when (File.Exists(objectPath))
            {
                // A second render key may publish the same content concurrently. The caller
                // validates the terminal object before creating its mapping.
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private FileStream AcquireExclusiveLock(string category, string hash)
    {
        var directory = EnsureCacheDirectory("locks", category, hash[..2]);
        var lockPath = Path.Combine(directory, hash + ".lock");
        IOException? lastException = null;
        for (var attempt = 0; attempt < LockAttemptCount; attempt++)
        {
            try
            {
                EnsureCacheDirectory("locks", category, hash[..2]);
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException ex)
            {
                lastException = ex;
                Thread.Sleep(LockRetryMilliseconds);
            }
        }

        throw new IOException(
            $"Could not acquire report asset {category} lock {hash}.",
            lastException
        );
    }

    private void QuarantinePath(string path, string kind, string identity)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        EnsureCacheDirectory(
            string.Equals(kind, "mapping", StringComparison.Ordinal) ? "keys" : "objects",
            identity[..2]
        );
        var directory = EnsureCacheDirectory("quarantine", kind, identity[..2]);
        var stamp = DateTime.UtcNow.ToString(
            "yyyyMMddTHHmmssfffffffZ",
            CultureInfo.InvariantCulture
        );
        var destination = Path.Combine(
            directory,
            $"{Path.GetFileName(path)}.{stamp}.{Guid.NewGuid():N}.bad"
        );
        EnsureCacheDirectory("quarantine", kind, identity[..2]);
        if (Directory.Exists(path))
            Directory.Move(path, destination);
        else
            File.Move(path, destination);
    }

    private string BuildObjectPath(string contentHash) =>
        Path.Combine(EnsureCacheDirectory("objects", contentHash[..2]), contentHash + ".png");

    private string BuildMappingPath(string renderKeyHash) =>
        Path.Combine(EnsureCacheDirectory("keys", renderKeyHash[..2]), renderKeyHash + ".json");

    private static string ValidateRegularFile(string filePath, string description)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException(description + " path is required.", nameof(filePath));
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath) || !IsRegularFile(fullPath))
            throw new InvalidDataException(description + " must be a regular file.");
        return fullPath;
    }

    private static bool ValidateObject(
        string objectPath,
        string contentHash,
        int pixelWidth,
        int pixelHeight
    )
    {
        if (!File.Exists(objectPath) || !IsRegularFile(objectPath))
            return false;
        try
        {
            return string.Equals(
                    ReportAssetHash.Sha256File(objectPath),
                    contentHash,
                    StringComparison.Ordinal
                )
                && TryDecodePng(objectPath, out var width, out var height)
                && width == pixelWidth
                && height == pixelHeight;
        }
        catch (Exception ex) when (IsExpectedFileException(ex))
        {
            return false;
        }
    }

    private static bool TryReadMapping(string mappingPath, out CacheMapping mapping)
    {
        mapping = null!;
        if (!IsRegularFile(mappingPath))
            return false;
        try
        {
            var info = new FileInfo(mappingPath);
            if (info.Length <= 0 || info.Length > MaximumMappingBytes)
                return false;
            var document = JObject.Parse(
                File.ReadAllText(mappingPath, Utf8NoBom),
                new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Ignore,
                }
            );
            var names = document.Properties().Select(property => property.Name).ToArray();
            if (
                names.Length != MappingPropertyNames.Count
                || names.Any(name => !MappingPropertyNames.Contains(name))
            )
                return false;

            var schemaVersion = document["schemaVersion"]?.Value<int?>();
            var renderKeyHash = document["renderKeyHash"]?.Value<string>();
            var canonicalRenderKey = document["canonicalRenderKey"]?.Value<string>();
            var contentHash = document["contentHash"]?.Value<string>();
            var pixelWidth = document["pixelWidth"]?.Value<int?>();
            var pixelHeight = document["pixelHeight"]?.Value<int?>();
            if (
                !schemaVersion.HasValue
                || renderKeyHash == null
                || canonicalRenderKey == null
                || contentHash == null
                || !pixelWidth.HasValue
                || !pixelHeight.HasValue
            )
            {
                return false;
            }

            mapping = new CacheMapping(
                schemaVersion.Value,
                renderKeyHash,
                canonicalRenderKey,
                contentHash,
                pixelWidth.Value,
                pixelHeight.Value
            );
            return true;
        }
        catch (Exception ex) when (IsExpectedMappingException(ex))
        {
            return false;
        }
    }

    private static string SerializeMapping(CacheMapping mapping) =>
        "{"
        + $"\"schemaVersion\":{mapping.SchemaVersion.ToString(CultureInfo.InvariantCulture)},"
        + $"\"renderKeyHash\":{JsonConvert.ToString(mapping.RenderKeyHash)},"
        + $"\"canonicalRenderKey\":{JsonConvert.ToString(mapping.CanonicalRenderKey)},"
        + $"\"contentHash\":{JsonConvert.ToString(mapping.ContentHash)},"
        + $"\"pixelWidth\":{mapping.PixelWidth.ToString(CultureInfo.InvariantCulture)},"
        + $"\"pixelHeight\":{mapping.PixelHeight.ToString(CultureInfo.InvariantCulture)}"
        + "}";

    private static bool TryDecodePng(string filePath, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan
            );
            using var image = Image.Load<Rgba32>(stream, out var format);
            if (
                format == null
                || !format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase)
            )
                return false;

            width = image.Width;
            height = image.Height;
            return width > 0 && height > 0;
        }
        catch (Exception ex) when (IsExpectedImageException(ex))
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    private string EnsureCacheDirectory(params string[] relativeSegments)
    {
        EnsureSafeDirectory(_rootPath, "cache root");
        var current = _rootPath;
        foreach (var segment in relativeSegments)
        {
            if (
                string.IsNullOrWhiteSpace(segment)
                || segment == "."
                || segment == ".."
                || segment.IndexOf(Path.DirectorySeparatorChar) >= 0
                || segment.IndexOf(Path.AltDirectorySeparatorChar) >= 0
            )
            {
                throw new InvalidDataException("Cache directory segment is invalid.");
            }

            current = Path.Combine(current, segment);
            EnsureSafeDirectory(current, "cache directory");
        }

        return current;
    }

    private static void EnsureSafeDirectory(string path, string description)
    {
        if (!TryGetAttributes(path, out var attributes))
        {
            Directory.CreateDirectory(path);
            if (!TryGetAttributes(path, out attributes))
                throw new IOException($"Could not create report asset {description}.");
        }

        if (
            (attributes & FileAttributes.Directory) == 0
            || (attributes & FileAttributes.ReparsePoint) != 0
        )
        {
            throw new InvalidDataException(
                $"Report asset {description} must be a physical directory, not a symlink or reparse point: {path}"
            );
        }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static bool IsRegularFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception ex) when (IsExpectedFileException(ex))
        {
            return false;
        }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void WriteNewFile(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None
        );
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A staging artifact is never authoritative; leave cleanup to a later maintenance pass.
        }
    }

    private static bool IsExpectedMappingException(Exception exception) =>
        IsExpectedFileException(exception)
        || exception is JsonException
        || exception is FormatException
        || exception is InvalidCastException
        || exception is OverflowException;

    private static bool IsExpectedFileException(Exception exception) =>
        exception is IOException
        || exception is UnauthorizedAccessException
        || exception is NotSupportedException
        || exception is ArgumentException;

    private static bool IsExpectedImageException(Exception exception) =>
        IsExpectedFileException(exception)
        || exception is UnknownImageFormatException
        || exception is InvalidImageContentException;

    private sealed record RenderRequest(string CanonicalJson, string RenderKeyHash)
    {
        internal static RenderRequest Create(ReportAssetRenderKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            var canonicalJson = key.CanonicalJson;
            return new RenderRequest(canonicalJson, ReportAssetHash.Sha256Utf8(canonicalJson));
        }
    }

    private sealed record CacheMapping(
        int SchemaVersion,
        string RenderKeyHash,
        string CanonicalRenderKey,
        string ContentHash,
        int PixelWidth,
        int PixelHeight
    );

    private sealed record CacheValidation(
        string MappingPath,
        bool MappingExists,
        string? ObjectPath,
        bool QuarantineObject,
        ReportAssetResolvedAsset? ResolvedAsset
    )
    {
        internal static CacheValidation Missing(string mappingPath) =>
            new(mappingPath, false, null, false, null);

        internal static CacheValidation InvalidMapping(
            string mappingPath,
            string? objectPath = null
        ) => new(mappingPath, true, objectPath, false, null);

        internal static CacheValidation MissingObject(string mappingPath, string objectPath) =>
            new(mappingPath, true, objectPath, false, null);

        internal static CacheValidation InvalidObject(string mappingPath, string objectPath) =>
            new(mappingPath, true, objectPath, true, null);

        internal static CacheValidation Valid(
            string mappingPath,
            string objectPath,
            ReportAssetResolvedAsset resolvedAsset
        ) => new(mappingPath, true, objectPath, false, resolvedAsset);
    }
}
