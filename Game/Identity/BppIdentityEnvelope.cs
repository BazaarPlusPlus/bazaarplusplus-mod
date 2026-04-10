#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Identity;

internal static class BppIdentityEnvelope
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("BPP1");
    private const ushort SchemaVersion = 1;
    private const ushort Flags = 0;
    private const int HeaderLength = 12;
    private const int ChecksumLength = 32;

    public static byte[] EncodePayload<T>(T payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        var payloadJson = JsonConvert.SerializeObject(
            payload,
            BazaarPlusPlus.Game.Online.V3Serialization.SerializerSettings
        );
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        using var sha256 = SHA256.Create();
        var checksum = sha256.ComputeHash(payloadBytes);
        var buffer = new byte[HeaderLength + payloadBytes.Length + ChecksumLength];

        Buffer.BlockCopy(Magic, 0, buffer, 0, Magic.Length);
        WriteUInt16LittleEndian(buffer, 4, SchemaVersion);
        WriteUInt16LittleEndian(buffer, 6, Flags);
        WriteUInt32LittleEndian(buffer, 8, (uint)payloadBytes.Length);
        Buffer.BlockCopy(payloadBytes, 0, buffer, HeaderLength, payloadBytes.Length);
        Buffer.BlockCopy(checksum, 0, buffer, HeaderLength + payloadBytes.Length, checksum.Length);
        return buffer;
    }

    public static bool TryDecodePayload<T>(byte[] envelopeBytes, out T? payload)
        where T : class
    {
        payload = null;
        if (envelopeBytes == null || envelopeBytes.Length < HeaderLength + ChecksumLength)
            return false;

        for (var index = 0; index < Magic.Length; index++)
        {
            if (envelopeBytes[index] != Magic[index])
                return false;
        }

        if (ReadUInt16LittleEndian(envelopeBytes, 4) != SchemaVersion)
            return false;

        var payloadLength = (int)ReadUInt32LittleEndian(envelopeBytes, 8);
        if (payloadLength < 0)
            return false;

        var expectedLength = HeaderLength + payloadLength + ChecksumLength;
        if (envelopeBytes.Length != expectedLength)
            return false;

        var payloadBytes = new byte[payloadLength];
        Buffer.BlockCopy(envelopeBytes, HeaderLength, payloadBytes, 0, payloadBytes.Length);
        using var sha256 = SHA256.Create();
        var expectedChecksum = sha256.ComputeHash(payloadBytes);
        for (var index = 0; index < ChecksumLength; index++)
        {
            if (envelopeBytes[HeaderLength + payloadLength + index] != expectedChecksum[index])
                return false;
        }

        payload = JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(payloadBytes));
        return payload != null;
    }

    public static bool TryDecodePayload<T>(string path, out T? payload)
        where T : class
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        return TryDecodePayload(File.ReadAllBytes(path), out payload);
    }

    private static ushort ReadUInt16LittleEndian(byte[] buffer, int offset)
    {
        return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    private static uint ReadUInt32LittleEndian(byte[] buffer, int offset)
    {
        return (uint)(
            buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24)
        );
    }

    private static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value & 0xff);
        buffer[offset + 1] = (byte)((value >> 8) & 0xff);
    }

    private static void WriteUInt32LittleEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xff);
        buffer[offset + 1] = (byte)((value >> 8) & 0xff);
        buffer[offset + 2] = (byte)((value >> 16) & 0xff);
        buffer[offset + 3] = (byte)((value >> 24) & 0xff);
    }
}
