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

Assert(
    !controller.ShouldConsumeNextClickToClosePreview(
        isPreviewActive: false,
        closeOnNextClickArmed: false,
        isLeftClick: true,
        isRightClick: false
    ),
    "Without an active preview, left click should not be consumed."
);

Assert(
    controller.ShouldConsumeNextClickToClosePreview(
        isPreviewActive: true,
        closeOnNextClickArmed: true,
        isLeftClick: true,
        isRightClick: false
    ),
    "The next left click should close an active preview when the gate is armed."
);

Assert(
    controller.ShouldConsumeNextClickToClosePreview(
        isPreviewActive: true,
        closeOnNextClickArmed: true,
        isLeftClick: false,
        isRightClick: true
    ),
    "The next right click should also close an active preview when the gate is armed."
);

var armed = true;
var firstConsume = controller.ShouldConsumeNextClickToClosePreview(
    isPreviewActive: true,
    closeOnNextClickArmed: armed,
    isLeftClick: true,
    isRightClick: false
);
if (firstConsume)
    armed = false;

Assert(firstConsume, "The first armed click should be consumed.");
Assert(
    !controller.ShouldConsumeNextClickToClosePreview(
        isPreviewActive: false,
        closeOnNextClickArmed: armed,
        isLeftClick: true,
        isRightClick: false
    ),
    "After the preview closes, later clicks should no longer be consumed."
);

Console.WriteLine("MonsterLockToggle gate checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
