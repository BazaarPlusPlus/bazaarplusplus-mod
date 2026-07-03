#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
    private const string SettingsFileName = "BazaarLine.cfg";
    private const string LegacySettingsFileName = "settings.cfg";
    private const float DefaultEnglishFontScale = 1f;
    private const float DefaultChineseFontScale = 1.1f;
    private const float MinimumFontScale = 1f;
    private const float MaximumFontScale = 2.5f;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Sync = new();
    private static VoiceLineSettings _current = new();
    private static bool _loadedOnce;
    private static string? _settingsPathOverride;
    private static string? _legacySettingsPathOverride;
    private static bool _hasPathOverrides;

    private VoiceLineSettings(
        SubtitlePosition position = SubtitlePosition.TopLeft,
        SubtitleLanguageMode languageMode = SubtitleLanguageMode.Both,
        float englishFontScale = DefaultEnglishFontScale,
        float chineseFontScale = DefaultChineseFontScale
    )
    {
        Position = position;
        LanguageMode = languageMode;
        EnglishFontScale = NormalizeScale(englishFontScale);
        ChineseFontScale = NormalizeScale(chineseFontScale);
    }

    public SubtitlePosition Position { get; }
    public SubtitleLanguageMode LanguageMode { get; }
    public float EnglishFontScale { get; }
    public float ChineseFontScale { get; }

    public static VoiceLineSettings Current
    {
        get
        {
            lock (Sync)
            {
                EnsureLoadedLocked();
                return _current;
            }
        }
    }

    internal static void SetPosition(SubtitlePosition position)
    {
        Save(current => current.WithPosition(position));
    }

    internal static void SetLanguageMode(SubtitleLanguageMode mode)
    {
        Save(current => current.WithLanguageMode(mode));
    }

    internal static void SetEnglishFontScale(float scale)
    {
        Save(current => current.WithEnglishFontScale(scale));
    }

    internal static void SetChineseFontScale(float scale)
    {
        Save(current => current.WithChineseFontScale(scale));
    }

    internal static void Save()
    {
        lock (Sync)
        {
            EnsureLoadedLocked();
            WriteSettingsBestEffort(SettingsPath, _current);
        }
    }

    internal static void ConfigureForTests(string settingsPath, string? legacySettingsPath)
    {
        lock (Sync)
        {
            _settingsPathOverride = settingsPath;
            _legacySettingsPathOverride = legacySettingsPath;
            _hasPathOverrides = true;
            _current = new VoiceLineSettings();
            _loadedOnce = false;
        }
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            _settingsPathOverride = null;
            _legacySettingsPathOverride = null;
            _hasPathOverrides = false;
            _current = new VoiceLineSettings();
            _loadedOnce = false;
        }
    }

    private static string SettingsPath =>
        _settingsPathOverride ?? Path.Combine(Paths.ConfigPath, SettingsFileName);

    private static string? LegacySettingsPath =>
        _hasPathOverrides ? _legacySettingsPathOverride
        : string.IsNullOrWhiteSpace(Paths.PluginPath) ? null
        : Path.Combine(Paths.PluginPath, "BazaarLine", LegacySettingsFileName);

    private static void EnsureLoadedLocked()
    {
        if (_loadedOnce)
            return;

        var path = SettingsPath;
        MigrateLegacySettingsBestEffort(path, LegacySettingsPath);
        _current = Load(path);
        _loadedOnce = true;
    }

    private static void Save(Func<VoiceLineSettings, VoiceLineSettings> update)
    {
        lock (Sync)
        {
            EnsureLoadedLocked();
            _current = update(_current);
            WriteSettingsBestEffort(SettingsPath, _current);
        }
    }

    private VoiceLineSettings WithPosition(SubtitlePosition position) =>
        new(position, LanguageMode, EnglishFontScale, ChineseFontScale);

    private VoiceLineSettings WithLanguageMode(SubtitleLanguageMode mode) =>
        new(Position, mode, EnglishFontScale, ChineseFontScale);

    private VoiceLineSettings WithEnglishFontScale(float scale) =>
        new(Position, LanguageMode, scale, ChineseFontScale);

    private VoiceLineSettings WithChineseFontScale(float scale) =>
        new(Position, LanguageMode, EnglishFontScale, scale);

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

            return new VoiceLineSettings(
                position: ParsePosition(
                    values.TryGetValue("position", out var position) ? position : null
                ),
                languageMode: ParseLanguageMode(
                    values.TryGetValue("language", out var language) ? language : null
                ),
                englishFontScale: ParseScale(
                    values.TryGetValue("englishFontScale", out var englishScale)
                        ? englishScale
                        : null,
                    DefaultEnglishFontScale
                ),
                chineseFontScale: ParseScale(
                    values.TryGetValue("chineseFontScale", out var chineseScale)
                        ? chineseScale
                        : null,
                    DefaultChineseFontScale
                )
            );
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to load subtitle settings: {ex.Message}");
            return new VoiceLineSettings();
        }
    }

    private static void MigrateLegacySettingsBestEffort(string settingsPath, string? legacyPath)
    {
        if (
            string.IsNullOrWhiteSpace(legacyPath)
            || File.Exists(settingsPath)
            || !File.Exists(legacyPath)
        )
            return;

        try
        {
            var settingsDirectory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(settingsDirectory))
                Directory.CreateDirectory(settingsDirectory);

            File.Copy(legacyPath, settingsPath, overwrite: false);
            VoiceSubtitlesLog.Info($"Migrated legacy subtitle settings to {settingsPath}.");
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to migrate legacy subtitle settings: {ex.Message}");
        }
    }

    private static void WriteSettingsBestEffort(string path, VoiceLineSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllLines(
                path,
                new[]
                {
                    "position=" + FormatPosition(settings.Position),
                    "language=" + FormatLanguageMode(settings.LanguageMode),
                    "englishFontScale=" + FormatScale(settings.EnglishFontScale),
                    "chineseFontScale=" + FormatScale(settings.ChineseFontScale),
                },
                Utf8NoBom
            );
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to save subtitle settings: {ex.Message}");
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

    private static string FormatPosition(SubtitlePosition position)
    {
        return position switch
        {
            SubtitlePosition.TopRight => "top-right",
            SubtitlePosition.TopCenter => "top-center",
            _ => "top-left",
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

    private static string FormatLanguageMode(SubtitleLanguageMode mode)
    {
        return mode switch
        {
            SubtitleLanguageMode.ChineseOnly => "chinese",
            SubtitleLanguageMode.EnglishOnly => "english",
            _ => "both",
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
            return NormalizeScale(parsed);
        }

        return fallback;
    }

    private static float NormalizeScale(float value)
    {
        return Math.Min(MaximumFontScale, Math.Max(MinimumFontScale, value));
    }

    private static string FormatScale(float value)
    {
        return NormalizeScale(value).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
