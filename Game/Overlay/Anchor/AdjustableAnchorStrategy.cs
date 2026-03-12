#pragma warning disable CS0436
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class AdjustableAnchorStrategy : IBoardAnchorStrategy
{
    private readonly IBoardAnchorStrategy _inner;
    private readonly AnchorAdjustment _adjustment;

    public AdjustableAnchorStrategy(IBoardAnchorStrategy inner, AnchorAdjustment adjustment)
    {
        _inner = inner;
        _adjustment = adjustment ?? new AnchorAdjustment();
    }

    public bool TryResolve(out BoardPose pose)
    {
        if (_inner == null || !_inner.TryResolve(out var basePose) || basePose == null)
        {
            pose = null;
            return false;
        }

        pose = new BoardPose
        {
            Position = basePose.Position
                + _adjustment.WorldOffset
                + (basePose.Rotation * _adjustment.LocalOffset),
            Rotation = Quaternion.Euler(_adjustment.RotationOffsetEuler) * basePose.Rotation,
        };
        return true;
    }
}
