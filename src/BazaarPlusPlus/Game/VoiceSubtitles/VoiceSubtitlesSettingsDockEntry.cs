#nullable enable
using System;
using System.Globalization;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.VoiceSubtitles.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceSubtitlesSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.VoiceSubtitles;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "VoiceSubtitles",
            VoiceSubtitlesSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                () => ReadEnabled(config),
                enabled => WriteEnabled(config, enabled)
            )
        );

    internal static void RegisterAll(SettingsDockEntryRegistry registry)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));

        registry.Register(new VoiceSubtitlesSettingsDockEntry());
        registry.Register(new VoiceSubtitlesPositionSettingsDockEntry());
        registry.Register(new VoiceSubtitlesLanguageSettingsDockEntry());
        registry.Register(new VoiceSubtitlesEnglishFontScaleSettingsDockEntry());
        registry.Register(new VoiceSubtitlesChineseFontScaleSettingsDockEntry());
    }

    private static bool ReadEnabled(IBppConfig config) =>
        config.EnableVoiceSubtitlesConfig?.Value ?? false;

    private static void WriteEnabled(IBppConfig config, bool enabled)
    {
        var entry = config.EnableVoiceSubtitlesConfig;
        if (entry != null)
            entry.Value = enabled;
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
            ResolveStatus,
            () => VoiceLineSettings.Current.Position != SubtitlePosition.TopLeft,
            CyclePosition,
            collapseAfterActivate: false
        );

    private static string ResolveLabel(string languageCode) => Resolve(Label, languageCode);

    private static string ResolveStatus(string languageCode)
    {
        return Resolve(
            VoiceLineSettings.Current.Position switch
            {
                SubtitlePosition.TopRight => TopRight,
                SubtitlePosition.TopCenter => TopCenter,
                _ => TopLeft,
            },
            languageCode
        );
    }

    private static void CyclePosition()
    {
        VoiceLineSettings.SetPosition(
            VoiceLineSettings.Current.Position switch
            {
                SubtitlePosition.TopLeft => SubtitlePosition.TopRight,
                SubtitlePosition.TopRight => SubtitlePosition.TopCenter,
                _ => SubtitlePosition.TopLeft,
            }
        );
    }

    private static string Resolve(LocalizedTextSet text, string languageCode) =>
        text.Resolve(languageCode, L.CurrentMode);
}

internal sealed class VoiceSubtitlesLanguageSettingsDockEntry : ISettingsDockEntry
{
    private static readonly LocalizedTextSet Label = new(
        "Subtitle Language",
        "字幕语言",
        "字幕語言"
    );
    private static readonly LocalizedTextSet Both = new("Both", "双语", "雙語");
    private static readonly LocalizedTextSet Chinese = new("Chinese", "中文", "中文");
    private static readonly LocalizedTextSet English = new("English", "英文", "英文");

    public int Order => BppSettingsDockOrder.VoiceSubtitlesLanguage;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "VoiceSubtitlesLanguage",
            ResolveLabel,
            ResolveStatus,
            () => VoiceLineSettings.Current.LanguageMode != SubtitleLanguageMode.Both,
            CycleLanguageMode,
            collapseAfterActivate: false
        );

    private static string ResolveLabel(string languageCode) => Resolve(Label, languageCode);

    private static string ResolveStatus(string languageCode)
    {
        return Resolve(
            VoiceLineSettings.Current.LanguageMode switch
            {
                SubtitleLanguageMode.ChineseOnly => Chinese,
                SubtitleLanguageMode.EnglishOnly => English,
                _ => Both,
            },
            languageCode
        );
    }

    private static void CycleLanguageMode()
    {
        VoiceLineSettings.SetLanguageMode(
            VoiceLineSettings.Current.LanguageMode switch
            {
                SubtitleLanguageMode.Both => SubtitleLanguageMode.ChineseOnly,
                SubtitleLanguageMode.ChineseOnly => SubtitleLanguageMode.EnglishOnly,
                _ => SubtitleLanguageMode.Both,
            }
        );
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

    protected override float ReadScale() => VoiceLineSettings.Current.EnglishFontScale;

    protected override void WriteScale(float scale) => VoiceLineSettings.SetEnglishFontScale(scale);
}

internal sealed class VoiceSubtitlesChineseFontScaleSettingsDockEntry
    : VoiceSubtitlesFontScaleSettingsDockEntry
{
    public override int Order => BppSettingsDockOrder.VoiceSubtitlesChineseFontScale;

    protected override string Key => "VoiceSubtitlesChineseFontScale";

    protected override LocalizedTextSet Label => new("Chinese Size", "中文字幕号", "中文字幕號");

    protected override float DefaultScale => 1.1f;

    protected override float ReadScale() => VoiceLineSettings.Current.ChineseFontScale;

    protected override void WriteScale(float scale) => VoiceLineSettings.SetChineseFontScale(scale);
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
            _ => FormatStatus(ReadScale()),
            () => Math.Abs(ReadScale() - DefaultScale) > ComparisonTolerance,
            CycleScale,
            collapseAfterActivate: false
        );

    protected abstract float ReadScale();

    protected abstract void WriteScale(float scale);

    private string ResolveLabel(string languageCode) => Label.Resolve(languageCode, L.CurrentMode);

    private void CycleScale()
    {
        WriteScale(NextScale(ReadScale()));
    }

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
