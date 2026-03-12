#pragma warning disable CS0436
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class AnchorAdjustment
{
    public Vector3 WorldOffset { get; set; } = Vector3.zero;

    public Vector3 LocalOffset { get; set; } = Vector3.zero;

    public Vector3 RotationOffsetEuler { get; set; } = Vector3.zero;
}
