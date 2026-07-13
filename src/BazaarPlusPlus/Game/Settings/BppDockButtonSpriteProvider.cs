#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppDockButtonSpriteProvider
{
    private const string IconResourceSuffix = "Resources.DockButtons.collection-panel-icon.png";

    private static Sprite? _cachedSprite;
    private static bool _cacheResolved;

    internal static Sprite? Get()
    {
        if (_cacheResolved)
            return _cachedSprite;

        _cachedSprite = LoadSprite(IconResourceSuffix, "CollectionPanel");
        _cacheResolved = true;
        return _cachedSprite;
    }

    private static Sprite? LoadSprite(string resourceSuffix, string spriteName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase)
            );
        if (resourceName == null)
        {
            ReportDegraded(SettingsLogReasonCode.ResourceMissing);
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            ReportDegraded(SettingsLogReasonCode.ResourceStreamUnavailable);
            return null;
        }

        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);

        var texture = new Texture2D(2, 2, TextureFormat.ARGB32, mipChain: false)
        {
            name = $"BPP_{spriteName}_Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        if (!texture.LoadImage(bytes.ToArray(), markNonReadable: false))
        {
            UnityEngine.Object.Destroy(texture);
            ReportDegraded(SettingsLogReasonCode.ResourceDecodeFailed);
            return null;
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            extrude: 0u,
            SpriteMeshType.FullRect
        );
        sprite.name = $"BPP_{spriteName}_Sprite";
        return sprite;
    }

    private static void ReportDegraded(SettingsLogReasonCode reasonCode) =>
        BppLog.WarnEvent(
            SettingsLogEvents.DockSpriteDegraded,
            SettingsLogEvents.DockSpriteDegradedReasonCode.Bind(reasonCode),
            SettingsLogEvents.DockSpriteDegradedResourceId.Bind(
                SettingsDockSpriteResourceId.CollectionPanelIcon
            )
        );
}
