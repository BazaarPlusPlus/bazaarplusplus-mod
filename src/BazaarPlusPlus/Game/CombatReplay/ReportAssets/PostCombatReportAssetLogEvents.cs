#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

internal enum PostCombatReportCardPreviewReasonCode
{
    Completed,
    SnapshotUnavailable,
    InvalidTemplateId,
    PreviewCreateFailed,
    PreviewShowFailed,
    PreviewResizeFailed,
    InvalidGeometry,
    CaptureFailed,
    ValidationFailed,
    Canceled,
    Exception,
}

[BppLogEventSource]
internal static class PostCombatReportAssetLogEvents
{
    internal static readonly BppLogFieldDefinition BattleId = PublicField(
        0,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition TemplateId = PublicField(
        1,
        "template_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition ReasonCode = PublicField(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FilePath = new(
        3,
        "file_path",
        BppLogFieldPrivacy.LocalPath,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CacheHitCount = PublicField(
        1,
        "cache_hit_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CacheFollowerCount = PublicField(
        2,
        "cache_follower_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CacheMissCount = PublicField(
        3,
        "cache_miss_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition UnityMaterializerInvocationCount = PublicField(
        4,
        "unity_materializer_invocation_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition PreviewMaterialized = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.report_card_preview.materialized",
        [BattleId, TemplateId, ReasonCode, FilePath]
    );

    internal static readonly BppLogEventDefinition PreviewMaterializationFailed = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.report_card_preview.materialization_failed",
        [BattleId, TemplateId, ReasonCode, FilePath]
    );

    internal static readonly BppLogEventDefinition MaterializationBatchCompleted = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.report_asset_materialization.batch_completed",
        [
            BattleId,
            CacheHitCount,
            CacheFollowerCount,
            CacheMissCount,
            UnityMaterializerInvocationCount,
        ]
    );

    private static BppLogFieldDefinition PublicField(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.Public, correlation, cardinality);
}

internal static class PostCombatReportAssetDiagnostics
{
    internal static void ReportBatchCompleted(
        string? battleId,
        int cacheHitCount,
        int cacheFollowerCount,
        int cacheMissCount,
        int unityMaterializerInvocationCount
    )
    {
        BppLog.InfoEvent(
            PostCombatReportAssetLogEvents.MaterializationBatchCompleted,
            [
                PostCombatReportAssetLogEvents.BattleId.Bind(battleId),
                PostCombatReportAssetLogEvents.CacheHitCount.Bind(cacheHitCount),
                PostCombatReportAssetLogEvents.CacheFollowerCount.Bind(cacheFollowerCount),
                PostCombatReportAssetLogEvents.CacheMissCount.Bind(cacheMissCount),
                PostCombatReportAssetLogEvents.UnityMaterializerInvocationCount.Bind(
                    unityMaterializerInvocationCount
                ),
            ]
        );
    }

    internal static void ReportFailure(
        string? battleId,
        Guid? templateId,
        PostCombatReportCardPreviewReasonCode reasonCode,
        string? filePath,
        Exception? exception = null
    )
    {
        var fields = BuildFields(battleId, templateId, reasonCode, filePath);
        if (exception == null)
            BppLog.WarnEvent(PostCombatReportAssetLogEvents.PreviewMaterializationFailed, fields);
        else
        {
            BppLog.WarnEvent(
                PostCombatReportAssetLogEvents.PreviewMaterializationFailed,
                exception,
                fields
            );
        }
    }

    private static BppLogFieldValue[] BuildFields(
        string? battleId,
        Guid? templateId,
        PostCombatReportCardPreviewReasonCode reasonCode,
        string? filePath
    ) =>
        [
            PostCombatReportAssetLogEvents.BattleId.Bind(battleId),
            PostCombatReportAssetLogEvents.TemplateId.Bind(templateId),
            PostCombatReportAssetLogEvents.ReasonCode.Bind(reasonCode),
            PostCombatReportAssetLogEvents.FilePath.Bind(filePath),
        ];
}
