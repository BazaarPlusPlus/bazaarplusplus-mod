using BazaarPlusPlus;

var controller = new MonsterLockShowcaseController();

Assert(
    !controller.ShouldInterceptLockToggle(
        isPreviewActive: false,
        hasCurrentCard: true,
        isShowcaseCard: false,
        isMonsterCard: false
    ),
    "Non-monster cards should not be intercepted when no preview is active."
);

Assert(
    controller.ShouldInterceptLockToggle(
        isPreviewActive: false,
        hasCurrentCard: true,
        isShowcaseCard: false,
        isMonsterCard: true
    ),
    "Monster cards should be intercepted so they can open the preview."
);

Assert(
    !controller.ShouldInterceptLockToggle(
        isPreviewActive: true,
        hasCurrentCard: true,
        isShowcaseCard: false,
        isMonsterCard: false
    ),
    "A non-monster card should not close an active preview."
);

Assert(
    controller.ShouldInterceptLockToggle(
        isPreviewActive: true,
        hasCurrentCard: true,
        isShowcaseCard: true,
        isMonsterCard: false
    ),
    "A showcase card should still be able to close the active preview."
);

Console.WriteLine("MonsterLockToggle gate checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
