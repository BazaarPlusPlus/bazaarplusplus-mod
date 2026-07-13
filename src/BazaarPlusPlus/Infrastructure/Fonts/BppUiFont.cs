#nullable enable
using System;
using System.IO;
using System.Reflection;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Infrastructure;
using BepInEx;
using UnityEngine;

namespace BazaarPlusPlus.Infrastructure.Fonts;

internal static class BppUiFont
{
    private const string Component = "UiFont";
    private const string FontFileName = "LXGWWenKai-Regular.ttf";
    private const string ResourceName = "BazaarPlusPlus.Resources.Fonts.LXGWWenKai-Regular.ttf";
    private const string SansSerifResourceName = "LegacyRuntime.ttf";

    private static Func<BppUiFontKind>? _kindProvider;
    private static Font? _lxgw;
    private static Font? _sans;

    public static Font Default => ResolveDefault();

    public static Font LxgwWenKai => _lxgw ??= LoadLxgwWenKai();

    public static void Install(Func<BppUiFontKind> kindProvider)
    {
        _kindProvider = kindProvider ?? throw new ArgumentNullException(nameof(kindProvider));
    }

    public static void Reset()
    {
        _kindProvider = null;
    }

    public static void RequestCharactersInTexture(string characters, int size, FontStyle style)
    {
        if (string.IsNullOrEmpty(characters))
            return;

        Default.RequestCharactersInTexture(characters, size, style);
    }

    private static Font ResolveDefault()
    {
        var kind = _kindProvider?.Invoke() ?? BppUiFontKind.SansSerif;
        return kind switch
        {
            BppUiFontKind.SansSerif => SansSerif,
            _ => LxgwWenKai,
        };
    }

    private static Font SansSerif => _sans ??= LoadSansSerif();

    private static Font LoadLxgwWenKai()
    {
        var cacheRoot = Path.Combine(GetCacheRoot(), "BazaarPlusPlus", "Fonts");
        var fontPath = EmbeddedFontFile.Extract(
            Assembly.GetExecutingAssembly(),
            ResourceName,
            FontFileName,
            cacheRoot
        );
        var font = new Font(fontPath);
        BppLog.Info(Component, $"Loaded UI font '{FontFileName}' from '{fontPath}'.");
        return font;
    }

    private static Font LoadSansSerif()
    {
        var font = Resources.GetBuiltinResource<Font>(SansSerifResourceName);
        if (font == null)
        {
            BppLog.Warn(
                Component,
                $"Failed to load built-in UI font '{SansSerifResourceName}'; falling back to '{FontFileName}'."
            );
            return LxgwWenKai;
        }

        BppLog.Info(Component, $"Loaded built-in UI font '{SansSerifResourceName}'.");
        return font;
    }

    private static string GetCacheRoot()
    {
        if (!string.IsNullOrWhiteSpace(Paths.CachePath))
            return Paths.CachePath;

        return Path.Combine(Path.GetTempPath(), "BepInEx", "cache");
    }
}
