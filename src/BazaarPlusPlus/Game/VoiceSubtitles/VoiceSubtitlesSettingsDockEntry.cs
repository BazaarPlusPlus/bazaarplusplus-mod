#nullable enable
using System;
using System.Globalization;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceSubtitlesSettingsDockEntry : ISettingsDockEntry
{
    private static readonly LocalizedTextSet Off = new("OFF", "关闭", "關閉");
    private static readonly LocalizedTextSet Both = new("BOTH", "双语", "雙語");
    private static readonly LocalizedTextSet Chinese = new("ZH", "中文", "中文");
    private static readonly LocalizedTextSet English = new("EN", "英文", "英文");

    public int Order => BppSettingsDockOrder.VoiceSubtitles;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "VoiceSubtitles",
            VoiceSubtitlesSettingsMenuLabel.Resolve,
            languageCode => ResolveStatus(config, languageCode),
            () => ReadMode(config) != SubtitleMode.Off,
            () => CycleMode(config),
            collapseAfterActivate: false
        );

    internal static void RegisterAll(SettingsDockEntryRegistry registry)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));

        registry.Register(new VoiceSubtitlesSettingsDockEntry());
        registry.Register(new VoiceSubtitlesPositionSettingsDockEntry());
        registry.Register(new VoiceSubtitlesEnglishFontScaleSettingsDockEntry());
        registry.Register(new VoiceSubtitlesChineseFontScaleSettingsDockEntry());
    }

    private static SubtitleMode ReadMode(IBppConfig config)
    {
        if (config.EnableVoiceSubtitlesConfig?.Value != true)
            return SubtitleMode.Off;

        return (config.VoiceSubtitlesLanguageModeConfig?.Value ?? SubtitleLanguageMode.Both) switch
        {
            SubtitleLanguageMode.ChineseOnly => SubtitleMode.Chinese,
            SubtitleLanguageMode.EnglishOnly => SubtitleMode.English,
            _ => SubtitleMode.Both,
        };
    }

    private static string ResolveStatus(IBppConfig config, string languageCode)
    {
        var text = ReadMode(config) switch
        {
            SubtitleMode.Both => Both,
            SubtitleMode.Chinese => Chinese,
            SubtitleMode.English => English,
            _ => Off,
        };
        return text.Resolve(languageCode, L.CurrentMode);
    }

    private static void CycleMode(IBppConfig config)
    {
        var next = ReadMode(config) switch
        {
            SubtitleMode.Off => SubtitleMode.Both,
            SubtitleMode.Both => SubtitleMode.Chinese,
            SubtitleMode.Chinese => SubtitleMode.English,
            _ => SubtitleMode.Off,
        };
        WriteMode(config, next);
    }

    private static void WriteMode(IBppConfig config, SubtitleMode mode)
    {
        var enabledEntry = config.EnableVoiceSubtitlesConfig;
        if (enabledEntry != null)
            enabledEntry.Value = mode != SubtitleMode.Off;

        var languageEntry = config.VoiceSubtitlesLanguageModeConfig;
        if (languageEntry != null)
        {
            languageEntry.Value = mode switch
            {
                SubtitleMode.Chinese => SubtitleLanguageMode.ChineseOnly,
                SubtitleMode.English => SubtitleLanguageMode.EnglishOnly,
                _ => SubtitleLanguageMode.Both,
            };
        }
    }

    private enum SubtitleMode
    {
        Off,
        Both,
        Chinese,
        English,
    }
}

internal sealed class VoiceSubtitlesPositionSettingsDockEntry : ISettingsDockEntry
{
    private static readonly LocalizedTextSet Label = new(
        "Subtitle Position",
        "字幕位置",
        "字幕位置"
    );
    private static readonly LocalizedTextSet TopLeft = new("Top Left", "左上", "左上");
    private static readonly LocalizedTextSet TopRight = new("Top Right", "右上", "右上");
    private static readonly LocalizedTextSet TopCenter = new("Top Center", "顶部居中", "頂部置中");

    public int Order => BppSettingsDockOrder.VoiceSubtitlesPosition;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "VoiceSubtitlesPosition",
            ResolveLabel,
            languageCode => ResolveStatus(config, languageCode),
            () => ReadPosition(config) != SubtitlePosition.TopLeft,
            () => CyclePosition(config),
            collapseAfterActivate: false
        );

    private static string ResolveLabel(string languageCode) => Resolve(Label, languageCode);

    private static SubtitlePosition ReadPosition(IBppConfig config) =>
        config.VoiceSubtitlesPositionConfig?.Value ?? SubtitlePosition.TopLeft;

    private static string ResolveStatus(IBppConfig config, string languageCode)
    {
        return Resolve(
            ReadPosition(config) switch
            {
                SubtitlePosition.TopRight => TopRight,
                SubtitlePosition.TopCenter => TopCenter,
                _ => TopLeft,
            },
            languageCode
        );
    }

    private static void CyclePosition(IBppConfig config)
    {
        var entry = config.VoiceSubtitlesPositionConfig;
        if (entry == null)
            return;

        entry.Value = entry.Value switch
        {
            SubtitlePosition.TopLeft => SubtitlePosition.TopRight,
            SubtitlePosition.TopRight => SubtitlePosition.TopCenter,
            _ => SubtitlePosition.TopLeft,
        };
    }

    private static string Resolve(LocalizedTextSet text, string languageCode) =>
        text.Resolve(languageCode, L.CurrentMode);
}

internal sealed class VoiceSubtitlesEnglishFontScaleSettingsDockEntry
    : VoiceSubtitlesFontScaleSettingsDockEntry
{
    public override int Order => BppSettingsDockOrder.VoiceSubtitlesEnglishFontScale;

    protected override string Key => "VoiceSubtitlesEnglishFontScale";

    protected override LocalizedTextSet Label => new("English Size", "英文字号", "英文字號");

    protected override float DefaultScale => 1f;

    protected override float ReadScale(IBppConfig config) =>
        config.VoiceSubtitlesEnglishFontScaleConfig?.Value ?? DefaultScale;

    protected override void WriteScale(IBppConfig config, float scale)
    {
        var entry = config.VoiceSubtitlesEnglishFontScaleConfig;
        if (entry != null)
            entry.Value = scale;
    }
}

internal sealed class VoiceSubtitlesChineseFontScaleSettingsDockEntry
    : VoiceSubtitlesFontScaleSettingsDockEntry
{
    public override int Order => BppSettingsDockOrder.VoiceSubtitlesChineseFontScale;

    protected override string Key => "VoiceSubtitlesChineseFontScale";

    protected override LocalizedTextSet Label => new("Chinese Size", "中文字号", "中文字號");

    protected override float DefaultScale => 1f;

    protected override float ReadScale(IBppConfig config) =>
        config.VoiceSubtitlesChineseFontScaleConfig?.Value ?? DefaultScale;

    protected override void WriteScale(IBppConfig config, float scale)
    {
        var entry = config.VoiceSubtitlesChineseFontScaleConfig;
        if (entry != null)
            entry.Value = scale;
    }
}

internal abstract class VoiceSubtitlesFontScaleSettingsDockEntry : ISettingsDockEntry
{
    private const float ComparisonTolerance = 0.0001f;
    private static readonly float[] ScaleLadder = { 1f, 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f };

    public abstract int Order { get; }

    protected abstract string Key { get; }

    protected abstract LocalizedTextSet Label { get; }

    protected abstract float DefaultScale { get; }

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            Key,
            ResolveLabel,
            _ => FormatStatus(ReadScale(config)),
            () => Math.Abs(ReadScale(config) - DefaultScale) > ComparisonTolerance,
            () => WriteScale(config, NextScale(ReadScale(config))),
            collapseAfterActivate: false
        );

    protected abstract float ReadScale(IBppConfig config);

    protected abstract void WriteScale(IBppConfig config, float scale);

    private string ResolveLabel(string languageCode) => Label.Resolve(languageCode, L.CurrentMode);

    private static float NextScale(float current)
    {
        foreach (var candidate in ScaleLadder)
        {
            if (candidate > current + ComparisonTolerance)
                return candidate;
        }

        return ScaleLadder[0];
    }

    private static string FormatStatus(float scale)
    {
        return scale.ToString("0.##", CultureInfo.InvariantCulture) + "x";
    }
}
