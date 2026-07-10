#nullable enable
using System;
using System.Text;
using System.Text.RegularExpressions;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewFormatting
{
    public const string PreviewHeaderText = "BazaarPlusPlus";

    private const string LogComponent = "ItemEnchantPreview";
    private const int PrefixSizePercent = 60;
    private const int EffectSizePercent = 55;
    private const string EnchantmentPrefix = "\u00A0\u00A0· ";
    private const string CjkLineHeight = "<line-height=1.15em>";

    private static readonly Regex SizeTagRegex = new Regex(
        "<size=(\\d+)%>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );
    private static readonly Regex NativeLineHeightRegex = new Regex(
        "<line-height=1\\.6em>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public static TooltipSegment CreateSegment(
        EEnchantmentType enchantmentType,
        string renderedText
    )
    {
        var enchantmentLabel = GetEnchantmentLabel(enchantmentType);
        var colorHex = GetEnchantmentColorHex(enchantmentType);
        var scaledText = ScaleInlineSizes(
            NormalizeNativeLineHeight(renderedText),
            EffectSizePercent / 100f
        );

        return new TooltipSegment(
            $"<size={PrefixSizePercent}%>{EnchantmentPrefix}<color=#{colorHex}>{enchantmentLabel}</color>: </size><size={EffectSizePercent}%>{scaledText}</size>",
            null,
            null,
            -1
        );
    }

    internal static string NormalizeNativeLineHeight(string text)
    {
        if (!BppTmpFontPolicy.ShouldUseEmbeddedCjkFont(text))
            return text;

        return NativeLineHeightRegex.Replace(text, CjkLineHeight);
    }

    public static void AppendTooltipText(StringBuilder builder, string text)
    {
        if (builder == null || string.IsNullOrWhiteSpace(text))
            return;

        var lineStart = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character != '\r' && character != '\n')
                continue;

            AppendLine(builder, text, lineStart, index - lineStart);
            if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                index++;
            lineStart = index + 1;
        }

        AppendLine(builder, text, lineStart, text.Length - lineStart);
    }

    private static void AppendLine(StringBuilder builder, string text, int startIndex, int length)
    {
        if (length <= 0)
            return;

        for (var index = startIndex; index < startIndex + length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
                continue;

            builder.Append(text, startIndex, length);
            builder.Append('\n');
            return;
        }
    }

    internal static string ScaleInlineSizes(string text, float scale)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return SizeTagRegex.Replace(
            text,
            match =>
            {
                if (!int.TryParse(match.Groups[1].Value, out var size))
                    return match.Value;

                var scaledSize = Math.Max(1, (int)Math.Round(size * scale));
                return $"<size={scaledSize}%>";
            }
        );
    }

    public static string GetEnchantmentLabel(EEnchantmentType enchantmentType)
    {
        try
        {
            return new LocalizableText(enchantmentType.ToString()).GetLocalizedText();
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                LogComponent,
                $"GetEnchantmentLabel: localization failed for enchant '{enchantmentType}', using raw name: {ex.Message}"
            );
            return enchantmentType.ToString();
        }
    }

    public static string GetEnchantmentColorHex(EEnchantmentType enchantmentType)
    {
        return enchantmentType switch
        {
            EEnchantmentType.Heavy => "CB9F6E",
            EEnchantmentType.Golden => "FFCD19",
            EEnchantmentType.Icy => "3FC8F7",
            EEnchantmentType.Turbo => "00ECC3",
            EEnchantmentType.Shielded => "F4CF20",
            EEnchantmentType.Restorative => "8EEA31",
            EEnchantmentType.Toxic => "0EBE4F",
            EEnchantmentType.Fiery => "FF9F45",
            EEnchantmentType.Shiny => "98A8FE",
            EEnchantmentType.Deadly => "F5503D",
            EEnchantmentType.Radiant => "98A8FE",
            EEnchantmentType.Obsidian => "9D4A6F",
            _ => "FFFFFF",
        };
    }
}
