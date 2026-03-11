#pragma warning disable CS0436
using System;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class TrackedObjectAnchorSource : IOverlayAnchorSource
{
    private readonly Func<Transform> _resolver;

    public TrackedObjectAnchorSource(Func<Transform> resolver)
    {
        _resolver = resolver;
    }

    public bool TryGetAnchor(out Vector3 position, out Quaternion rotation)
    {
        var target = _resolver?.Invoke();
        if (target == null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        position = target.position;
        rotation = target.rotation;
        return true;
    }
}
