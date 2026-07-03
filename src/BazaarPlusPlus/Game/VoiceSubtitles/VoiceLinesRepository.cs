#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLinesRepository
{
    private const string EmbeddedResourceName =
        "BazaarPlusPlus.Data.VoiceSubtitles.voice-lines.json";

    public void BeginLoad()
    {
        var lines = LoadEmbeddedSeed();
        VoiceLineCatalog.ReplaceCatalog(lines, "embedded");
    }

    internal static VoiceLine[] LoadEmbeddedSeed()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream =
            assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new FileNotFoundException(
                $"Embedded voice subtitle seed '{EmbeddedResourceName}' was not found."
            );
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false
        );
        var json = reader.ReadToEnd();
        var lines = VoiceLinesDocument.Parse(json, EmbeddedResourceName);
        VoiceSubtitlesLog.Info($"Loaded {lines.Length} embedded voice subtitle lines.");
        return lines;
    }
}
