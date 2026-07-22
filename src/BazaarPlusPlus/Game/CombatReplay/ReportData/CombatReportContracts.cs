#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.ReportData;

public sealed class EmbeddedReportEnvelopeV1
{
    public int SchemaVersion { get; set; } = 1;

    public string Locale { get; set; } = "en-US";

    public CombatReportDocumentV1 BattleDocument { get; set; } = new();

    public RecordingReportManifestV1 RecordingManifest { get; set; } = new();
}

public sealed class CombatReportDocumentV1
{
    public int SchemaVersion { get; set; } = 1;

    public string DocumentId { get; set; } = string.Empty;

    public string BattleId { get; set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; set; }

    public int? Day { get; set; }

    public string? Result { get; set; }

    public CombatReportSummaryV1 Summary { get; set; } = new();

    public CombatReportParticipantV1 Player { get; set; } = new();

    public CombatReportParticipantV1 Opponent { get; set; } = new();

    public int FrameDurationMs { get; set; } = 50;

    public int FrameCount { get; set; }

    public int DurationMs { get; set; }

    public string Winner { get; set; } = string.Empty;

    public string Loser { get; set; } = string.Empty;

    public int RawRecordCount { get; set; }

    public List<CombatReportEntityV1> Entities { get; set; } = new();

    public List<CombatReportEventV1> Events { get; set; } = new();

    /// <summary>
    /// State immediately before the first recorded combat transition. These values are projected
    /// from the raw simulation on the producer side so a Viewer never has to guess a late first
    /// sample's starting value.
    /// </summary>
    public CombatReportFrameZeroStateV1 FrameZeroState { get; set; } = new();

    public List<CombatReportMetricSampleV1> Metrics { get; set; } = new();
}

public sealed class CombatReportFrameZeroStateV1
{
    public CombatReportCombatantStateV1 Player { get; set; } = new();

    public CombatReportCombatantStateV1 Opponent { get; set; } = new();
}

public sealed class CombatReportCombatantStateV1
{
    public long? Health { get; set; }

    public long? Rage { get; set; }

    public long? HealthRegen { get; set; }

    public long? Shield { get; set; }
}

public sealed class CombatReportParticipantV1
{
    public string Name { get; set; } = string.Empty;

    public string Hero { get; set; } = string.Empty;
}

public sealed class CombatReportSummaryV1
{
    public string PlayerName { get; set; } = string.Empty;

    public string OpponentName { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;
}

public sealed class CombatReportEntityV1
{
    public string EntityId { get; set; } = string.Empty;

    public string? TemplateId { get; set; }

    public string Owner { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Size { get; set; }

    public int? Slot { get; set; }

    public int? Span { get; set; }

    public string? Tier { get; set; }

    public string? Enchant { get; set; }

    public string? ContentKey { get; set; }

    public string? AssetRelativeUrl { get; set; }

    public int Order { get; set; }
}

public sealed class CombatReportEventV1
{
    public string EventId { get; set; } = string.Empty;

    public int Frame { get; set; }

    public int FrameSequence { get; set; }

    public int CombatTimeMs { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? EffectId { get; set; }

    public string? ExecutionContextId { get; set; }

    public string? SourceEntityId { get; set; }

    public string? TriggerSourceEntityId { get; set; }

    public List<string> TargetEntityIds { get; set; } = new();

    public List<string> RemovedTargetEntityIds { get; set; } = new();

    public long? Value { get; set; }

    public long? PreviousValue { get; set; }

    public long? CurrentValue { get; set; }

    public string? Unit { get; set; }

    public bool? IsCritical { get; set; }

    public string Role { get; set; } = string.Empty;

    public string AttributionConfidence { get; set; } = string.Empty;

    /// <summary>
    /// Stable, locale-independent semantic binding for an optional native status icon. The
    /// content key and URL are populated only after the shared asset cache resolves that binding.
    /// </summary>
    public string? IconSemanticKey { get; set; }

    public string? IconContentKey { get; set; }

    public string? IconAssetRelativeUrl { get; set; }

    public CombatReportRawReferenceV1 RawReference { get; set; } = new();
}

public sealed class CombatReportRawReferenceV1
{
    public string Category { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int Index { get; set; }
}

public sealed class CombatReportMetricSampleV1
{
    public int Frame { get; set; }

    public int CombatTimeMs { get; set; }

    public string Combatant { get; set; } = string.Empty;

    public string Metric { get; set; } = string.Empty;

    public long Value { get; set; }

    public string Unit { get; set; } = "points";
}

public sealed class RecordingReportManifestV1
{
    public int SchemaVersion { get; set; } = 1;

    public string ArtifactId { get; set; } = string.Empty;

    public string RecordingId { get; set; } = string.Empty;

    public string BattleId { get; set; } = string.Empty;

    public string ViewerVersion { get; set; } = string.Empty;

    public string VideoRelativeUrl { get; set; } = string.Empty;

    public string SyncMetadataStatus { get; set; } = "ReadyUnsynced";

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? FramesPerSecond { get; set; }

    public long? DurationMs { get; set; }

    public List<RecordingReportSyncAnchorV1> SyncAnchors { get; set; } = new();

    public List<RecordingReportAssetV1> Assets { get; set; } = new();
}

public sealed class RecordingReportSyncAnchorV1
{
    public int CombatFrame { get; set; }

    public int CombatMs { get; set; }

    public long MediaPtsMs { get; set; }

    public long OutputOrdinal { get; set; }
}

public sealed class RecordingReportAssetV1
{
    public string ContentKey { get; set; } = string.Empty;

    public string RelativeUrl { get; set; } = string.Empty;

    public string SemanticRole { get; set; } = string.Empty;

    public int NaturalWidth { get; set; }

    public int NaturalHeight { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}
