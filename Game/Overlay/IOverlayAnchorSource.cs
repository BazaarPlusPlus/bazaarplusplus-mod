#pragma warning disable CS0436
using UnityEngine;

namespace BazaarPlusPlus;

internal interface IOverlayAnchorSource
{
    bool TryGetAnchor(out Vector3 position, out Quaternion rotation);
}
