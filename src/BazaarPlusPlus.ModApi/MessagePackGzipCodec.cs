#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using MessagePack;
using MessagePack.Resolvers;

namespace BazaarPlusPlus.ModApi;

/// <summary>
/// Shared MessagePack + gzip codec for every BPP payload that is persisted or uploaded as a
/// gzipped MessagePack blob. Holds the single resolver option, the gzip framing, and the gzip
/// magic-byte sniff in one place so the wire framing cannot silently diverge across callers.
/// </summary>
public static class MessagePackGzipCodec
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(
            ContractlessStandardResolverAllowPrivate.Instance
        );

    public static byte[] Serialize<T>(T payload)
        where T : class
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        var messagePackBytes = MessagePackSerializer.Serialize(payload, Options);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(messagePackBytes, 0, messagePackBytes.Length);
        }

        return output.ToArray();
    }

    public static T? Deserialize<T>(byte[]? payloadBytes)
        where T : class
    {
        if (payloadBytes == null || payloadBytes.Length == 0)
            return null;
        if (!LooksLikeGzip(payloadBytes))
            return null;

        try
        {
            using var input = new MemoryStream(payloadBytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gzip.CopyTo(decompressed);
            return MessagePackSerializer.Deserialize<T>(decompressed.ToArray(), Options);
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeGzip(byte[] bytes)
    {
        return bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;
    }
}
