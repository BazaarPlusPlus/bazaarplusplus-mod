#nullable enable
using System;
using System.Linq;
using BazaarPlusPlus.ModApi;

internal static class CodecTests
{
    // Plain POCO: ContractlessStandardResolverAllowPrivate needs no MessagePack attributes.
    private sealed class Sample
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public int[] Values { get; set; } = Array.Empty<int>();
    }

    public static void Run()
    {
        RoundTripsPayload();
        ProducesGzipFraming();
        ReturnsNullForEmptyOrNonGzip();
        Console.WriteLine("CodecTests passed.");
    }

    private static void RoundTripsPayload()
    {
        var sample = new Sample
        {
            Name = "abc",
            Count = 7,
            Values = new[] { 1, 2, 3 },
        };
        var bytes = MessagePackGzipCodec.Serialize(sample);
        var restored = MessagePackGzipCodec.Deserialize<Sample>(bytes);
        if (
            restored == null
            || restored.Name != "abc"
            || restored.Count != 7
            || !restored.Values.SequenceEqual(new[] { 1, 2, 3 })
        )
            throw new Exception("MessagePackGzipCodec round-trip mismatch.");
    }

    private static void ProducesGzipFraming()
    {
        var bytes = MessagePackGzipCodec.Serialize(new Sample { Name = "x" });
        if (bytes.Length < 2 || bytes[0] != 0x1F || bytes[1] != 0x8B)
            throw new Exception("MessagePackGzipCodec did not emit gzip framing.");
    }

    private static void ReturnsNullForEmptyOrNonGzip()
    {
        if (MessagePackGzipCodec.Deserialize<Sample>(null) != null)
            throw new Exception("Expected null for null input.");
        if (MessagePackGzipCodec.Deserialize<Sample>(Array.Empty<byte>()) != null)
            throw new Exception("Expected null for empty input.");
        if (MessagePackGzipCodec.Deserialize<Sample>(new byte[] { 1, 2, 3, 4 }) != null)
            throw new Exception("Expected null for non-gzip input.");
    }
}
