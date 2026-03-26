#nullable enable
namespace BazaarPlusPlus.Game.RunLogging.Upload;

internal static class RunUploadErrorFormatter
{
    public static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "empty_response";

        return value.Length <= 256 ? value : value[..256];
    }
}
