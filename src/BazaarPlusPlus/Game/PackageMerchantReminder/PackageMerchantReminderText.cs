#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.PackageMerchantReminder;

internal static class PackageMerchantReminderText
{
    private static readonly LocalizedTextSet VisiblePackage = new(
        "Package to deliver!",
        "有快递可以交付！",
        "有快遞可以交付！"
    );

    private static readonly LocalizedTextSet ClosedStash = new(
        "Package in Stash!",
        "仓库里有快递！",
        "倉庫裡有快遞！"
    );

    internal static string Resolve(PackageMerchantReminderAnchor anchor) =>
        LocalizedTextHelpers.Resolve(
            anchor == PackageMerchantReminderAnchor.ClosedStash ? ClosedStash : VisiblePackage
        );
}
