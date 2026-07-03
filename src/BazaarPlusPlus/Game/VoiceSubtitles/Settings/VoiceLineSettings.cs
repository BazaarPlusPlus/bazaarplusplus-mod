#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BazaarPlusPlus.Game.VoiceSubtitles;
using BepInEx;

namespace BazaarPlusPlus.Game.VoiceSubtitles.Settings;

internal enum SubtitlePosition
{
    TopLeft,
    TopRight,
    TopCenter,
}

internal enum SubtitleLanguageMode
{
    Both,
    ChineseOnly,
    EnglishOnly,
}

internal sealed class VoiceLineSettings
{
    private const string SettingsFileName = "settings.cfg";
    private static readonly object Sync = new();
    private static VoiceLineSettings _current = new();
    private static DateTime _lastWriteTimeUtc = DateTime.MinValue;
    private static bool _loadedOnce;

    public SubtitlePosition Position { get; private set; } = SubtitlePosition.TopLeft;
    public SubtitleLanguageMode LanguageMode { get; private set; } = SubtitleLanguageMode.Both;
    public float EnglishFontScale { get; private set; } = 1f;
    public float ChineseFontScale { get; private set; } = 1.1f;

    public static VoiceLineSettings Current
    {
        get
        {
            ReloadIfChanged();
            return _current;
        }
    }

    public static void ReloadIfChanged()
    {
        lock (Sync)
        {
            var path = SettingsPath;
            var writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

            if (_loadedOnce && writeTime == _lastWriteTimeUtc)
                return;

            _current = Load(path);
            _lastWriteTimeUtc = writeTime;
            _loadedOnce = true;
        }
    }

    private static string SettingsPath
    {
        get
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var pluginDir = Path.GetDirectoryName(assemblyPath) ?? Paths.PluginPath;
            return Path.Combine(pluginDir, "BazaarLine", SettingsFileName);
        }
    }

    private static VoiceLineSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new VoiceLineSettings();

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            return new VoiceLineSettings
            {
                Position = ParsePosition(
                    values.TryGetValue("position", out var position) ? position : null
                ),
                LanguageMode = ParseLanguageMode(
                    values.TryGetValue("language", out var language) ? language : null
                ),
                EnglishFontScale = ParseScale(
                    values.TryGetValue("englishFontScale", out var englishScale)
                        ? englishScale
                        : null,
                    1f
                ),
                ChineseFontScale = ParseScale(
                    values.TryGetValue("chineseFontScale", out var chineseScale)
                        ? chineseScale
                        : null,
                    1.1f
                ),
            };
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to load subtitle settings: {ex.Message}");
            return new VoiceLineSettings();
        }
    }

    private static SubtitlePosition ParsePosition(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "top-right" => SubtitlePosition.TopRight,
            "top-center" => SubtitlePosition.TopCenter,
            _ => SubtitlePosition.TopLeft,
        };
    }

    private static SubtitleLanguageMode ParseLanguageMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "chinese" => SubtitleLanguageMode.ChineseOnly,
            "english" => SubtitleLanguageMode.EnglishOnly,
            _ => SubtitleLanguageMode.Both,
        };
    }

    private static float ParseScale(string? value, float fallback)
    {
        if (
            value != null
            && float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            return Math.Min(2.5f, Math.Max(1f, parsed));
        }

        return fallback;
    }
}
