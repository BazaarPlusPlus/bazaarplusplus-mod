#nullable enable
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.CombatReplay.Reports;

internal sealed class StaticReportPaths
{
    private const int ViewerReportHeadByteLimit = 64 * 1024;
    private const int ViewerReportScanBufferSize = 8 * 1024;
    private const int ViewerReportJsonBufferSize = 64 * 1024;

    internal const string ViewerBundleMetaName = "bpp-viewer-bundle";
    internal const string ViewerScriptSha256MetaName = "bpp-viewer-script-sha256";
    internal const string ViewerStylesheetSha256MetaName = "bpp-viewer-stylesheet-sha256";
    internal const string EmbeddedReportElementId = "bpp-report-data";

    internal StaticReportPaths(string dataRootDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(dataRootDirectoryPath))
            throw new ArgumentException(
                "A report data root is required.",
                nameof(dataRootDirectoryPath)
            );

        DataRootDirectoryPath = TrimEndingSeparators(Path.GetFullPath(dataRootDirectoryPath));
        ReportsDirectoryPath = Path.Combine(DataRootDirectoryPath, "reports");
        ViewerRootDirectoryPath = Path.Combine(DataRootDirectoryPath, "report-viewer");
        AssetRootDirectoryPath = Path.Combine(DataRootDirectoryPath, "report-assets");
        VideoRootDirectoryPath = Path.Combine(DataRootDirectoryPath, "CombatReplayVideos");
    }

    internal string DataRootDirectoryPath { get; }

    internal string ReportsDirectoryPath { get; }

    internal string ViewerRootDirectoryPath { get; }

    internal string AssetRootDirectoryPath { get; }

    internal string VideoRootDirectoryPath { get; }

    internal string GetReportHtmlFilePath(string recordingId) =>
        Path.Combine(ReportsDirectoryPath, ParseRecordingId(recordingId) + ".html");

    internal string GetViewerBundleDirectoryPath(string bundleId) =>
        Path.Combine(ViewerRootDirectoryPath, "objects", ParseSha256(bundleId, nameof(bundleId)));

    internal string GetViewerScriptFilePath(string bundleId) =>
        Path.Combine(GetViewerBundleDirectoryPath(bundleId), "viewer.js");

    internal string GetViewerStylesheetFilePath(string bundleId) =>
        Path.Combine(GetViewerBundleDirectoryPath(bundleId), "viewer.css");

    internal static string BuildViewerScriptRelativeUrl(string bundleId) =>
        "../report-viewer/objects/" + ParseSha256(bundleId, nameof(bundleId)) + "/viewer.js";

    internal static string BuildViewerStylesheetRelativeUrl(string bundleId) =>
        "../report-viewer/objects/" + ParseSha256(bundleId, nameof(bundleId)) + "/viewer.css";

    internal static bool TryValidateViewerReport(
        string reportFilePath,
        out string viewerBundleId,
        out string viewerScriptSha256,
        out string viewerStylesheetSha256,
        out string reason
    )
    {
        viewerBundleId = string.Empty;
        viewerScriptSha256 = string.Empty;
        viewerStylesheetSha256 = string.Empty;
        reason = "Battle report Viewer references are incomplete or invalid.";
        if (string.IsNullOrWhiteSpace(reportFilePath))
            return false;

        try
        {
            var reportHead = ReadReportHead(reportFilePath);
            if (
                !TryReadMetaSha256(reportHead, ViewerBundleMetaName, out viewerBundleId)
                || !TryReadMetaSha256(
                    reportHead,
                    ViewerScriptSha256MetaName,
                    out viewerScriptSha256
                )
                || !TryReadMetaSha256(
                    reportHead,
                    ViewerStylesheetSha256MetaName,
                    out viewerStylesheetSha256
                )
            )
            {
                return false;
            }

            var bundleId = viewerBundleId;
            var expectedMarker =
                "<meta name=\"" + ViewerBundleMetaName + "\" content=\"" + bundleId + "\">";
            var expectedScriptDigest =
                "<meta name=\""
                + ViewerScriptSha256MetaName
                + "\" content=\""
                + viewerScriptSha256
                + "\">";
            var expectedStylesheetDigest =
                "<meta name=\""
                + ViewerStylesheetSha256MetaName
                + "\" content=\""
                + viewerStylesheetSha256
                + "\">";
            var expectedScript =
                "<script defer src=\"" + BuildViewerScriptRelativeUrl(bundleId) + "\"></script>";
            var expectedStylesheet =
                "<link rel=\"stylesheet\" href=\""
                + BuildViewerStylesheetRelativeUrl(bundleId)
                + "\">";
            var embeddedReportStart =
                "<script type=\"application/json\" id=\"" + EmbeddedReportElementId + "\">";
            using var reader = new BufferedReportReader(Path.GetFullPath(reportFilePath));
            if (!TryFindToken(reader, expectedMarker, ViewerReportHeadByteLimit, out _, out _))
                return false;
            if (
                !TryFindToken(reader, expectedScriptDigest, ViewerReportHeadByteLimit, out _, out _)
            )
                return false;
            if (
                !TryFindToken(
                    reader,
                    expectedStylesheetDigest,
                    ViewerReportHeadByteLimit,
                    out _,
                    out _
                )
            )
                return false;
            if (!TryFindToken(reader, expectedStylesheet, ViewerReportHeadByteLimit, out _, out _))
                return false;
            if (
                !TryFindToken(reader, "</head>", ViewerReportHeadByteLimit, out _, out var headEnd)
                || headEnd > ViewerReportHeadByteLimit
            )
            {
                return false;
            }
            if (!TryFindToken(reader, "<body>", out _, out _))
                return false;
            if (
                !TryFindToken(reader, embeddedReportStart, out _, out var payloadStart)
                || !TryFindToken(reader, "</script>", out var payloadEnd, out _)
            )
            {
                return false;
            }
            if (
                !TryValidateEmbeddedReportJson(reportFilePath, payloadStart, payloadEnd, out reason)
            )
                return false;
            if (!TryFindToken(reader, expectedScript, out _, out _))
                return false;
            if (!TryFindToken(reader, "</body>", out _, out _))
                return false;
            if (!TryFindToken(reader, "</html>", out _, out _))
                return false;

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.GetBaseException().Message;
            return false;
        }
    }

    private static string ReadReportHead(string reportFilePath)
    {
        using var stream = new FileStream(
            Path.GetFullPath(reportFilePath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        var length = (int)Math.Min(stream.Length, ViewerReportHeadByteLimit);
        var bytes = new byte[length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read <= 0)
                break;
            offset += read;
        }

        return new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true
        ).GetString(bytes, 0, offset);
    }

    private static bool TryReadMetaSha256(string reportHead, string metaName, out string digest)
    {
        digest = string.Empty;
        var prefix = "<meta name=\"" + metaName + "\" content=\"";
        var start = reportHead.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return false;
        start += prefix.Length;
        if (start + 66 > reportHead.Length)
            return false;

        var candidate = reportHead.Substring(start, 64);
        if (
            !IsLowerHex(candidate, 64)
            || reportHead[start + 64] != '"'
            || reportHead[start + 65] != '>'
        )
        {
            return false;
        }

        digest = candidate;
        return true;
    }

    internal static string BuildAssetRelativeUrl(string contentSha256)
    {
        var digest = ParseSha256(contentSha256, nameof(contentSha256));
        return "../report-assets/objects/" + digest.Substring(0, 2) + "/" + digest + ".png";
    }

    internal string BuildVideoRelativeUrl(string videoFilePath)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath))
            throw new ArgumentException("A report video path is required.", nameof(videoFilePath));

        var video = Path.GetFullPath(videoFilePath);
        if (!IsBelowRoot(VideoRootDirectoryPath, video))
            throw new InvalidOperationException("The report video escaped CombatReplayVideos.");
        if (!string.Equals(Path.GetExtension(video), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The report video must be an MP4.", nameof(videoFilePath));

        var relative = video
            .Substring(VideoRootDirectoryPath.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries
        );
        if (segments.Length == 0)
            throw new InvalidOperationException("The report video has no relative path.");

        var builder = new StringBuilder("../CombatReplayVideos");
        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index] == "." || segments[index] == "..")
                throw new InvalidOperationException("The report video path contains traversal.");
            builder.Append('/').Append(Uri.EscapeDataString(segments[index]));
        }

        var url = builder.ToString();
        _ = TypedReportSiblingUrl.Parse(url);
        return url;
    }

    internal static string ParseRecordingId(string value)
    {
        if (!IsLowerHex(value, 32))
            throw new ArgumentException(
                "A recording ID must be 32 lowercase hexadecimal characters.",
                nameof(value)
            );
        return value;
    }

    internal static string ParseSha256(string value, string parameterName)
    {
        if (!IsLowerHex(value, 64))
            throw new ArgumentException(
                "A content SHA-256 must be 64 lowercase hexadecimal characters.",
                parameterName
            );
        return value;
    }

    internal static bool IsLowerHex(string? value, int length)
    {
        if (value == null || value.Length != length)
            return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
                return false;
        }
        return true;
    }

    private static bool TryValidateEmbeddedReportJson(
        string reportFilePath,
        long payloadStart,
        long payloadEnd,
        out string reason
    )
    {
        reason = "Battle report data is incomplete or invalid.";
        var payloadLength = payloadEnd - payloadStart;
        if (payloadLength <= 0)
            return false;

        using var stream = new FileStream(
            reportFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        stream.Position = payloadStart;
        using var boundedStream = new BoundedReadStream(stream, payloadLength);
        using var textReader = new StreamReader(
            boundedStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: ViewerReportJsonBufferSize,
            leaveOpen: true
        );
        using var jsonReader = new JsonTextReader(textReader)
        {
            CloseInput = false,
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 256,
            SupportMultipleContent = false,
        };
        JsonToken? firstToken = null;
        JsonToken? lastToken = null;
        var lastTokenDepth = -1;

        try
        {
            while (jsonReader.Read())
            {
                if (jsonReader.TokenType == JsonToken.Comment)
                    return false;
                firstToken ??= jsonReader.TokenType;
                lastToken = jsonReader.TokenType;
                lastTokenDepth = jsonReader.Depth;
            }

            return firstToken == JsonToken.StartObject
                && lastToken == JsonToken.EndObject
                && lastTokenDepth == 0
                && boundedStream.Position == payloadLength;
        }
        catch (JsonReaderException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryFindToken(
        BufferedReportReader reader,
        string token,
        out long startOffset,
        out long endOffset
    ) =>
        TryFindToken(
            reader,
            token,
            maximumEndOffset: long.MaxValue,
            out startOffset,
            out endOffset
        );

    private static bool TryFindToken(
        BufferedReportReader reader,
        string token,
        long maximumEndOffset,
        out long startOffset,
        out long endOffset
    )
    {
        var pattern = Encoding.UTF8.GetBytes(token);
        var prefix = BuildPrefixTable(pattern);
        var matched = 0;
        int value;
        while (reader.Position < maximumEndOffset && (value = reader.ReadByte()) >= 0)
        {
            var current = (byte)value;
            while (matched > 0 && current != pattern[matched])
                matched = prefix[matched - 1];
            if (current == pattern[matched])
                matched++;
            if (matched != pattern.Length)
                continue;

            endOffset = reader.Position;
            startOffset = endOffset - pattern.Length;
            return true;
        }

        startOffset = -1;
        endOffset = -1;
        return false;
    }

    private static int[] BuildPrefixTable(byte[] pattern)
    {
        var prefix = new int[pattern.Length];
        var matched = 0;
        for (var index = 1; index < pattern.Length; index++)
        {
            while (matched > 0 && pattern[index] != pattern[matched])
                matched = prefix[matched - 1];
            if (pattern[index] == pattern[matched])
                matched++;
            prefix[index] = matched;
        }
        return prefix;
    }

    private static bool IsBelowRoot(string root, string candidatePath)
    {
        var normalizedRoot = TrimEndingSeparators(Path.GetFullPath(root));
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(prefix, PathComparison);
    }

    private static string TrimEndingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        while (
            path.Length > (root?.Length ?? 0)
            && (
                path[path.Length - 1] == Path.DirectorySeparatorChar
                || path[path.Length - 1] == Path.AltDirectorySeparatorChar
            )
        )
        {
            path = path.Substring(0, path.Length - 1);
        }
        return path;
    }

    private static StringComparison PathComparison =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed class BufferedReportReader : IDisposable
    {
        private readonly FileStream _stream;
        private readonly byte[] _buffer = new byte[ViewerReportScanBufferSize];
        private int _bufferIndex;
        private int _bufferLength;

        internal BufferedReportReader(string reportFilePath)
        {
            _stream = new FileStream(
                reportFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
        }

        internal long Position { get; private set; }

        internal int ReadByte()
        {
            if (_bufferIndex >= _bufferLength)
            {
                _bufferLength = _stream.Read(_buffer, 0, _buffer.Length);
                _bufferIndex = 0;
                if (_bufferLength <= 0)
                    return -1;
            }

            Position++;
            return _buffer[_bufferIndex++];
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }

    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream _stream;
        private readonly long _length;
        private long _remaining;

        internal BoundedReadStream(Stream stream, long length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _length = length;
            _remaining = length;
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _length - _remaining;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining == 0)
                return 0;

            var read = _stream.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }

        public override int ReadByte()
        {
            if (_remaining == 0)
                return -1;

            var value = _stream.ReadByte();
            if (value >= 0)
                _remaining--;
            return value;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

internal enum TypedReportSiblingUrlKind
{
    Asset,
    Video,
}

internal sealed record TypedReportSiblingUrl(TypedReportSiblingUrlKind Kind, string Value)
{
    internal static TypedReportSiblingUrl Parse(string value)
    {
        if (TryParse(value, out var parsed))
            return parsed!;
        throw new ArgumentException(
            "The report URL is not an allowed sibling resource URL.",
            nameof(value)
        );
    }

    internal static bool TryParse(string? value, out TypedReportSiblingUrl? parsed)
    {
        parsed = null;
        if (
            string.IsNullOrEmpty(value)
            || value.Length > 2048
            || value.IndexOf('?') >= 0
            || value.IndexOf('#') >= 0
            || value.IndexOf('\\') >= 0
            || value.IndexOf(':') >= 0
        )
        {
            return false;
        }

        var segments = value.Split('/');
        if (segments.Length < 3 || segments[0] != "..")
            return false;
        for (var index = 1; index < segments.Length; index++)
        {
            if (!TryValidateEscapedSegment(segments[index]))
                return false;
        }

        if (
            segments.Length == 5
            && segments[1] == "report-assets"
            && segments[2] == "objects"
            && StaticReportPaths.IsLowerHex(segments[3], 2)
            && segments[4].EndsWith(".png", StringComparison.Ordinal)
        )
        {
            var digest = segments[4].Substring(0, segments[4].Length - 4);
            if (
                StaticReportPaths.IsLowerHex(digest, 64)
                && digest.StartsWith(segments[3], StringComparison.Ordinal)
            )
            {
                parsed = new TypedReportSiblingUrl(TypedReportSiblingUrlKind.Asset, value);
                return true;
            }
            return false;
        }

        if (segments[1] == "CombatReplayVideos" && segments.Length >= 3)
        {
            string finalSegment;
            try
            {
                finalSegment = Uri.UnescapeDataString(segments[segments.Length - 1]);
            }
            catch
            {
                return false;
            }
            if (finalSegment.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                parsed = new TypedReportSiblingUrl(TypedReportSiblingUrlKind.Video, value);
                return true;
            }
        }

        return false;
    }

    private static bool TryValidateEscapedSegment(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return false;

        string decoded;
        string decodedTwice;
        try
        {
            decoded = Uri.UnescapeDataString(raw);
            decodedTwice = Uri.UnescapeDataString(decoded);
        }
        catch
        {
            return false;
        }

        if (!string.Equals(Uri.EscapeDataString(decoded), raw, StringComparison.Ordinal))
            return false;
        return IsSafeDecodedSegment(decoded) && IsSafeDecodedSegment(decodedTwice);
    }

    private static bool IsSafeDecodedSegment(string value)
    {
        if (
            string.IsNullOrEmpty(value)
            || value == "."
            || value == ".."
            || value.IndexOf('/') >= 0
            || value.IndexOf('\\') >= 0
            || value.IndexOf(':') >= 0
            || value.IndexOf('?') >= 0
            || value.IndexOf('#') >= 0
            || value.IndexOf('%') >= 0
            || value.IndexOf('\0') >= 0
        )
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] <= '\u001f')
                return false;
        }

        return true;
    }
}
