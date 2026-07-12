#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal readonly struct NativeCardPreviewLease
{
    public NativeCardPreviewLease(Component card, NativeCardPreviewKind kind, bool alreadySetUp)
    {
        Card = card;
        Kind = kind;
        AlreadySetUp = alreadySetUp;
    }

    public Component Card { get; }
    public NativeCardPreviewKind Kind { get; }
    public bool AlreadySetUp { get; }
}
