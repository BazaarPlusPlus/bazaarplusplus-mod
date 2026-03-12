#pragma warning disable CS0436
namespace BazaarPlusPlus;

internal interface IBoardAnchorStrategy
{
    bool TryResolve(out BoardPose pose);
}
