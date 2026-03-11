#pragma warning disable CS0436
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class FixedWorldAnchorSource : IOverlayAnchorSource
{
    public Vector3 Position { get; set; }

    public Quaternion Rotation { get; set; } = Quaternion.identity;

    public bool TryGetAnchor(out Vector3 position, out Quaternion rotation)
    {
        position = Position;
        rotation = Rotation;
        return true;
    }
}
