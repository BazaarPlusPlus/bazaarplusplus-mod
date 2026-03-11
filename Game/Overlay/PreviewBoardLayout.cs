#pragma warning disable CS0436
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class PreviewBoardLayout
{
    public Vector3 LocalOffset = new Vector3(0f, 1.2f, 0f);
    public Vector3 CardSpacing = new Vector3(1.1f, 0f, 0f);
    public Vector3 CardScale = Vector3.one * 0.6f;
    public Vector2 BoardSize = new Vector2(7f, 4f);
    public float BoardThickness = 0.04f;
    public float BorderThickness = 0.08f;
    public float BorderHeight = 0.06f;
}
