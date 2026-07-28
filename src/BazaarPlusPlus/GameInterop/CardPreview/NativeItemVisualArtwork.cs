#nullable enable
using System.Reflection;
using HarmonyLib;
using TheBazaar.Game.CardFrames;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeItemVisualArtwork
{
    private static readonly FieldInfo? CardIllustrationRendererField = AccessTools.Field(
        typeof(ItemVisualsController),
        "cardIllustrationRenderer"
    );

    internal static bool TryGetIllustrationRenderer(
        ItemVisualsController controller,
        out Renderer renderer
    )
    {
        renderer = null!;
        if (controller == null || CardIllustrationRendererField == null)
            return false;

        try
        {
            if (CardIllustrationRendererField.GetValue(controller) is not Renderer value)
                return false;
            renderer = value;
            return true;
        }
        catch
        {
            renderer = null!;
            return false;
        }
    }
}
