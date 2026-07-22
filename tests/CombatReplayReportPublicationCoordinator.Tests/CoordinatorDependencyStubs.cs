#nullable enable

namespace BazaarGameShared.Infra.Messages
{
    internal sealed class NetMessageCombatSim
    {
        internal int ReportSchemaVersion { get; init; } = 1;
        internal IReadOnlyList<string> EntityIds { get; init; } = new[] { "entity" };
        internal bool SeedStaleAssetReference { get; init; }
        internal string? EventIconSemanticKey { get; init; }
    }
}

namespace BazaarPlusPlus.Game.PvpBattles
{
    internal sealed class PvpBattleManifest
    {
        internal string BattleId { get; init; } = string.Empty;
    }
}

namespace BazaarPlusPlus.Game.CombatReplay.ReportData
{
    internal sealed class CombatReportProjector
    {
        internal CombatReportDocumentV1 Project(
            BazaarPlusPlus.Game.PvpBattles.PvpBattleManifest manifest,
            BazaarGameShared.Infra.Messages.NetMessageCombatSim combatMessage
        )
        {
            var document = new CombatReportDocumentV1
            {
                SchemaVersion = combatMessage.ReportSchemaVersion,
                DocumentId = "document-" + manifest.BattleId,
                BattleId = manifest.BattleId,
                Summary = new CombatReportSummaryV1
                {
                    PlayerName = "Player",
                    OpponentName = "Opponent",
                },
            };
            foreach (var entityId in combatMessage.EntityIds)
            {
                document.Entities.Add(
                    new CombatReportEntityV1
                    {
                        EntityId = entityId,
                        ContentKey = combatMessage.SeedStaleAssetReference
                            ? "sha256-" + new string('f', 64)
                            : null,
                        AssetRelativeUrl = combatMessage.SeedStaleAssetReference
                            ? "../report-assets/objects/ff/" + new string('f', 64) + ".png"
                            : null,
                    }
                );
            }
            if (!string.IsNullOrWhiteSpace(combatMessage.EventIconSemanticKey))
            {
                document.Events.Add(
                    new CombatReportEventV1
                    {
                        EventId = "semantic-event",
                        IconSemanticKey = combatMessage.EventIconSemanticKey,
                        IconContentKey = combatMessage.SeedStaleAssetReference
                            ? "sha256-" + new string('f', 64)
                            : null,
                        IconAssetRelativeUrl = combatMessage.SeedStaleAssetReference
                            ? "../report-assets/objects/ff/" + new string('f', 64) + ".png"
                            : null,
                    }
                );
            }
            return document;
        }
    }
}

namespace BazaarPlusPlus.Game.CombatReplay.Video
{
    internal sealed class CombatReplayVideoRecordingStarted
    {
        internal string RecordingId { get; init; } = string.Empty;
        internal string BattleId { get; init; } = string.Empty;
    }

    internal sealed class CombatReplayVideoRecordingCompleted
    {
        internal string RecordingId { get; init; } = string.Empty;
        internal string BattleId { get; init; } = string.Empty;
        internal string FinalFilePath { get; init; } = string.Empty;
        internal bool ArtifactUsable { get; init; }
        internal IReadOnlyList<ReplayVideoSyncAnchor> SyncAnchors { get; init; } =
            Array.Empty<ReplayVideoSyncAnchor>();
    }
}
