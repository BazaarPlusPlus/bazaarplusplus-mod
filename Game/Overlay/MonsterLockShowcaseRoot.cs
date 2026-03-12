#pragma warning disable CS0436
namespace BazaarPlusPlus;

internal sealed class MonsterLockShowcaseRoot
{
    public bool Visible { get; private set; }

    public void SetVisible(bool visible)
    {
        Visible = visible;
    }
}
