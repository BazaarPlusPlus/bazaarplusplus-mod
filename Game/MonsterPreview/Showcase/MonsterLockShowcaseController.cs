#pragma warning disable CS0436
using System;

namespace BazaarPlusPlus;

internal sealed class MonsterLockShowcaseController
{
    public bool ShouldShowForLock(Guid? lockedCardId, bool isShowcaseCard)
    {
        return lockedCardId.HasValue && !isShowcaseCard;
    }

    public bool ShouldHideForUnlock(Guid? currentCardId, bool isShowcaseCard)
    {
        return !isShowcaseCard;
    }
}
