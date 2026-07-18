#nullable enable
using BazaarPlusPlus.ModApi;

internal static class ErrorFormatterTests
{
    public static void Run()
    {
        IncludesParseFailureForMalformedJson();
        Console.WriteLine("ErrorFormatterTests passed.");
    }

    private static void IncludesParseFailureForMalformedJson()
    {
        var formatted = ModApiErrorFormatter.FormatHttpFailure(503, "{broken");
        if (!formatted.StartsWith("http_503:unparseable_error_payload(", StringComparison.Ordinal))
            throw new Exception($"Malformed JSON should include parse diagnostics: {formatted}");
        if (!formatted.EndsWith(":{broken", StringComparison.Ordinal))
            throw new Exception(
                $"Malformed JSON should retain the truncated response body: {formatted}"
            );
    }
}
