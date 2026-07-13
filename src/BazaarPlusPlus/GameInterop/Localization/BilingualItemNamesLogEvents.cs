#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.GameInterop.Localization;

internal enum BilingualLogReasonCode
{
    DatabaseUnavailable,
    QueryException,
    ConfigurationUnavailable,
    FontReferencesUnavailable,
    FontLoadFailed,
    TooltipPatchException,
}

internal enum BilingualFontStage
{
    ResolveConfiguration,
    LoadFonts,
    RestoreBinding,
    ReleaseHandle,
}

[BppLogEventSource]
internal static class BilingualItemNamesLogEvents
{
    internal static readonly BppLogFieldDefinition CatalogDegradedLocale = PublicLow(0, "locale");
    internal static readonly BppLogFieldDefinition CatalogDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition CatalogDegraded = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.catalog.degraded",
        [CatalogDegradedLocale, CatalogDegradedReasonCode],
        new BppLogStormPolicy([CatalogDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition CatalogRecoveredLocale = PublicLow(0, "locale");
    internal static readonly BppLogEventDefinition CatalogRecovered = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.catalog.recovered",
        [CatalogRecoveredLocale]
    );
    internal static readonly BppLogFieldDefinition CatalogLoadedLocale = PublicLow(0, "locale");
    internal static readonly BppLogEventDefinition CatalogLoaded = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.catalog.loaded",
        [CatalogLoadedLocale]
    );

    internal static readonly BppLogFieldDefinition FontFallbackDegradedStage = PublicLow(
        0,
        "stage"
    );
    internal static readonly BppLogFieldDefinition FontFallbackDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition FontFallbackDegraded = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.font_fallback.degraded",
        [FontFallbackDegradedStage, FontFallbackDegradedReasonCode],
        new BppLogStormPolicy([FontFallbackDegradedStage, FontFallbackDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition FontFallbackRecoveredFontCount = PublicHigh(
        0,
        "font_count"
    );
    internal static readonly BppLogFieldDefinition FontFallbackRecoveredFontNames = Untrusted(
        1,
        "font_names"
    );
    internal static readonly BppLogEventDefinition FontFallbackRecovered = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.font_fallback.recovered",
        [FontFallbackRecoveredFontCount, FontFallbackRecoveredFontNames]
    );
    internal static readonly BppLogFieldDefinition FontFallbackLoadedFontCount = PublicHigh(
        0,
        "font_count"
    );
    internal static readonly BppLogFieldDefinition FontFallbackLoadedFontNames = Untrusted(
        1,
        "font_names"
    );
    internal static readonly BppLogEventDefinition FontFallbackLoaded = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.font_fallback.loaded",
        [FontFallbackLoadedFontCount, FontFallbackLoadedFontNames]
    );
    internal static readonly BppLogFieldDefinition FontFallbackCleanupFailedStage = PublicLow(
        0,
        "stage"
    );
    internal static readonly BppLogEventDefinition FontFallbackCleanupFailed = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.font_fallback.cleanup_failed",
        [FontFallbackCleanupFailedStage]
    );

    internal static readonly BppLogFieldDefinition TooltipDegradedReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition TooltipDegraded = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.tooltip.degraded",
        [TooltipDegradedReasonCode],
        new BppLogStormPolicy([TooltipDegradedReasonCode])
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

    private static BppLogFieldDefinition Untrusted(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.UntrustedText,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );
}
