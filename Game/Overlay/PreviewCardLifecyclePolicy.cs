namespace BazaarPlusPlus;

internal enum PreviewCardKind
{
    Item,
    Skill,
}

internal static class PreviewCardLifecyclePolicy
{
    public static bool ShouldReturnToPool(PreviewCardKind kind)
    {
        return true;
    }

    public static bool ShouldRefreshAfterInstantiate(PreviewCardKind kind)
    {
        return kind == PreviewCardKind.Skill;
    }
}
