#pragma warning disable CS0436
using System;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class TrackedTransformAnchorStrategy : IBoardAnchorStrategy
{
    private readonly Func<Transform> _resolver;

    public TrackedTransformAnchorStrategy(Func<Transform> resolver)
    {
        _resolver = resolver;
    }

    public bool TryResolve(out BoardPose pose)
    {
        var target = _resolver?.Invoke();
        if (target == null)
        {
            pose = null;
            return false;
        }

        pose = new BoardPose
        {
            Position = target.position,
            Rotation = target.rotation,
        };
        return true;
    }
}
