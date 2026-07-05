#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewHoverTracker
{
    private static Component? _current;

    public static Component? Current
    {
        get
        {
            if (_current == null)
            {
                _current = null;
                return null;
            }

            return _current;
        }
    }

    public static void NotifyHover(Component card)
    {
        if (card == null)
            return;

        _current = card;
    }

    public static void NotifyHoverOut(Component card)
    {
        var current = Current;
        if (current != null && card != null && ReferenceEquals(current, card))
            _current = null;
    }
}
