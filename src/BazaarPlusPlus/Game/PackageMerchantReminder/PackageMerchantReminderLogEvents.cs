#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PackageMerchantReminder;

internal enum PackageMerchantReminderFailurePhase
{
    EventHandler,
    InventoryRead,
    PresentationReadiness,
    NativeTooltip,
}

[BppLogEventSource]
internal static class PackageMerchantReminderLogEvents
{
    internal static readonly BppLogFieldDefinition ShownMerchantTemplateId = PublicHigh(
        0,
        "merchant_template_id"
    );
    internal static readonly BppLogFieldDefinition ShownAnchor = PublicLow(1, "anchor");
    internal static readonly BppLogEventDefinition Shown = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.package_merchant_reminder.shown",
        [ShownMerchantTemplateId, ShownAnchor]
    );

    internal static readonly BppLogFieldDefinition DegradedPhase = PublicLow(0, "phase");
    internal static readonly BppLogFieldDefinition DegradedReasonCode = PublicLow(1, "reason_code");
    internal static readonly BppLogEventDefinition Degraded = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.package_merchant_reminder.degraded",
        [DegradedPhase, DegradedReasonCode],
        new BppLogStormPolicy([DegradedPhase, DegradedReasonCode])
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.Low
        );

    private static BppLogFieldDefinition PublicHigh(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );
}
