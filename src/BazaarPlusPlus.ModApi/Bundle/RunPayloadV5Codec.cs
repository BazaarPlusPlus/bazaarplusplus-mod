#nullable enable
using System.IO.Compression;
using MessagePack;
using MessagePack.Resolvers;

namespace BazaarPlusPlus.ModApi.Bundle;

public static class RunPayloadV5Codec
{
    public const int MaxDecompressedBytes = 64 * 1024 * 1024;

    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions
        .Standard.WithResolver(ContractlessStandardResolverAllowPrivate.Instance)
        .WithSecurity(MessagePackSecurity.UntrustedData);

    public static byte[] Encode(RunPayloadV5 payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.PayloadFormatVersion != BundleLimitsV5.RunFormatVersion)
            throw new ArgumentException("Run payload format version must be 5.", nameof(payload));

        var packed = MessagePackSerializer.Serialize(payload, Options);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(packed, 0, packed.Length);
        return output.ToArray();
    }

    public static RunPayloadV5 Decode(ReadOnlyMemory<byte> bytes)
    {
        if (!TryDecode(bytes, out var payload, out var reason))
            throw new InvalidDataException($"Run payload is invalid ({reason}).");
        return payload!;
    }

    public static bool TryDecode(
        ReadOnlyMemory<byte> bytes,
        out RunPayloadV5? payload,
        out string? reason
    )
    {
        payload = null;
        reason = null;
        if (bytes.Length < 2)
        {
            reason = "run_payload_empty";
            return false;
        }
        var source = bytes.Span;
        if (source[0] != 0x1F || source[1] != 0x8B)
        {
            reason = "run_payload_not_gzip";
            return false;
        }

        try
        {
            using var input = new MemoryStream(bytes.ToArray(), writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = gzip.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                if (decompressed.Length + read > MaxDecompressedBytes)
                {
                    reason = "run_payload_decompressed_too_large";
                    return false;
                }
                decompressed.Write(buffer, 0, read);
            }

            payload = MessagePackSerializer.Deserialize<RunPayloadV5>(
                decompressed.ToArray(),
                Options
            );
            if (payload == null)
            {
                reason = "run_payload_deserialized_null";
                return false;
            }
            if (payload.PayloadFormatVersion != BundleLimitsV5.RunFormatVersion)
            {
                payload = null;
                reason = "unsupported_run_payload_version";
                return false;
            }
            return true;
        }
        catch (Exception)
        {
            payload = null;
            reason = "run_payload_decode_failed";
            return false;
        }
    }
}
