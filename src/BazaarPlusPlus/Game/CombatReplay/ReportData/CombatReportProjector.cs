#nullable enable
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.CombatReplay.ReportData;

internal sealed class CombatReportProjector
{
    internal const int FrameDurationMs = 50;

    internal CombatReportDocumentV1 Project(
        PvpBattleManifest manifest,
        NetMessageCombatSim combatMessage
    )
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (combatMessage == null)
            throw new ArgumentNullException(nameof(combatMessage));
        if (string.IsNullOrWhiteSpace(manifest.BattleId))
            throw new ArgumentException("Battle id is required.", nameof(manifest));

        var frames = combatMessage.Data?.Frames ?? new List<CombatSimFrame>();
        var events = new List<CombatReportEventV1>();
        var metrics = new List<CombatReportMetricSampleV1>();
        var globalSequence = 0;

        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var frame = frames[frameIndex];
            var frameSequence = 0;
            var combatTimeMs = checked(frameIndex * FrameDurationMs);
            var effectValueAttributions = CombatReportEffectValueAttributor.Resolve(frame);

            for (var rawIndex = 0; rawIndex < frame.Events.Count; rawIndex++)
            {
                effectValueAttributions.TryGetValue(rawIndex, out var valueAttribution);
                var projected = ProjectSimEvent(
                    frame.Events[rawIndex],
                    frameIndex,
                    frameSequence++,
                    combatTimeMs,
                    rawIndex,
                    valueAttribution
                );
                projected.EventId = "e" + globalSequence++;
                events.Add(projected);
            }

            ProjectPlayerUpdates(
                "Player",
                frame.PlayerUpdates,
                frameIndex,
                combatTimeMs,
                ref frameSequence,
                ref globalSequence,
                events,
                metrics
            );
            ProjectPlayerUpdates(
                "Opponent",
                frame.OpponentUpdates,
                frameIndex,
                combatTimeMs,
                ref frameSequence,
                ref globalSequence,
                events,
                metrics
            );

            foreach (
                var cardPair in frame.CardUpdates.OrderBy(
                    pair => pair.Key.Value,
                    StringComparer.Ordinal
                )
            )
            {
                var rawIndex = 0;
                foreach (
                    var attributePair in cardPair.Value.Attributes.OrderBy(pair => (int)pair.Key)
                )
                {
                    var update = attributePair.Value;
                    if (update.Delta == 0)
                    {
                        rawIndex++;
                        continue;
                    }

                    events.Add(
                        NewEvent(
                            "e" + globalSequence++,
                            frameIndex,
                            frameSequence++,
                            combatTimeMs,
                            "card-attribute",
                            update.AttributeType.ToString(),
                            source: null,
                            targets: new[] { cardPair.Key.Value },
                            value: update.Delta,
                            previousValue: update.PreviousValue,
                            currentValue: update.CurrentValue,
                            unit: UnitForCardAttribute(update.AttributeType),
                            role: "received",
                            attribution: "target-exact-source-unknown",
                            rawCategory: "card-update",
                            rawType: nameof(CombatSimCardAttributeUpdate),
                            rawIndex: rawIndex++
                        )
                    );
                }
            }
        }

        var document = new CombatReportDocumentV1
        {
            BattleId = manifest.BattleId,
            RecordedAtUtc = manifest.RecordedAtUtc,
            Day = manifest.Day,
            Result = manifest.Outcome?.Result,
            Summary = new CombatReportSummaryV1
            {
                PlayerName = manifest.Participants?.PlayerName ?? string.Empty,
                OpponentName = manifest.Participants?.OpponentName ?? string.Empty,
                Outcome = manifest.Outcome?.Result ?? string.Empty,
            },
            Player = new CombatReportParticipantV1
            {
                Name = manifest.Participants?.PlayerName ?? string.Empty,
                Hero = manifest.Participants?.PlayerHero ?? string.Empty,
            },
            Opponent = new CombatReportParticipantV1
            {
                Name = manifest.Participants?.OpponentName ?? string.Empty,
                Hero = manifest.Participants?.OpponentHero ?? string.Empty,
            },
            FrameDurationMs = FrameDurationMs,
            FrameCount = frames.Count,
            DurationMs = Math.Max(0, (frames.Count - 1) * FrameDurationMs),
            Winner = combatMessage.Data?.Winner.ToString() ?? string.Empty,
            Loser = combatMessage.Data?.Loser.ToString() ?? string.Empty,
            RawRecordCount = CountRawRecords(frames),
            Entities = BuildEntities(manifest),
            Events = events,
            FrameZeroState = BuildFrameZeroState(frames),
            Metrics = metrics,
        };
        foreach (var reportEvent in document.Events)
        {
            if (ReportStatusIconSemanticResolver.TryResolve(reportEvent, out var iconSemantic))
                reportEvent.IconSemanticKey = iconSemantic.StableKey;
        }
        CombatReportJson.RefreshDocumentId(document);
        return document;
    }

    private static CombatReportEventV1 ProjectSimEvent(
        ICombatSimEvent combatEvent,
        int frame,
        int frameSequence,
        int combatTimeMs,
        int rawIndex,
        CombatReportEffectValueAttribution? valueAttribution
    )
    {
        switch (combatEvent)
        {
            case CombatSimEventEffectExecuted executed:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "effect-executed",
                    executed.ActionType.ToString(),
                    Value(executed.Source),
                    TargetValues(executed.Target),
                    value: valueAttribution?.Value,
                    unit: valueAttribution?.Unit,
                    role: "applied",
                    attribution: "exact",
                    effectId: executed.EffectId,
                    executionContextId: executed.ExecutionContextId,
                    triggerSource: Value(executed.TriggerSource),
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventEffectExecuted),
                    rawIndex: rawIndex
                );
            case CombatSimEventEffectTriggered triggered:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "effect-triggered",
                    "Triggered",
                    Value(triggered.Source),
                    TargetValues(triggered.Targets),
                    role: "triggered",
                    attribution: "exact",
                    effectId: triggered.EffectId,
                    executionContextId: triggered.ExecutionContextId,
                    triggerSource: Value(triggered.TriggerSource),
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventEffectTriggered),
                    rawIndex: rawIndex
                );
            case CombatSimEventEffectAuraExecuted aura:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "aura",
                    "Aura",
                    Value(aura.Source),
                    TargetValues(aura.AppliedTo),
                    role: "applied",
                    attribution: "exact",
                    effectId: aura.EffectId,
                    executionContextId: aura.ExecutionContextId,
                    triggerSource: Value(aura.TriggerSource),
                    removedTargets: TargetValues(aura.RemovedFrom),
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventEffectAuraExecuted),
                    rawIndex: rawIndex
                );
            case CombatSimEventCombatantDied died:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "combatant-died",
                    "Died",
                    source: null,
                    targets: new[] { PlayerEntityId(died.CombatantId.ToString()) },
                    role: "received",
                    attribution: "target-exact-source-unknown",
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventCombatantDied),
                    rawIndex: rawIndex
                );
            case CombatSimEventSandstormCountdownStarted:
                return GenericFrameEvent(
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "sandstorm-countdown",
                    "SandstormCountdownStarted",
                    nameof(CombatSimEventSandstormCountdownStarted),
                    rawIndex
                );
            case CombatSimEventSandstormStarted:
                return GenericFrameEvent(
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "sandstorm-started",
                    "SandstormStarted",
                    nameof(CombatSimEventSandstormStarted),
                    rawIndex
                );
            case CombatSimEventCardEnchanted enchanted:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "card-enchanted",
                    enchanted.IsReverted ? "EnchantReverted" : "Enchanted",
                    source: null,
                    targets: new[] { enchanted.InstanceId },
                    role: "received",
                    attribution: "target-exact-source-unknown",
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventCardEnchanted),
                    rawIndex: rawIndex
                );
            case CombatSimEventCardTransformed transformed:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "card-transformed",
                    "Transformed",
                    transformed.OriginalInstanceId,
                    transformed.TransformedCards.Select(card => card.InstanceId),
                    role: "applied",
                    attribution: "exact",
                    executionContextId: transformed.ExecutionContextId,
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventCardTransformed),
                    rawIndex: rawIndex
                );
            case CombatSimEventCardTransformReverted reverted:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "card-transform-reverted",
                    "TransformReverted",
                    reverted.OriginalCard.InstanceId,
                    reverted.TransformedCardInstanceIds,
                    role: "applied",
                    attribution: "exact",
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventCardTransformReverted),
                    rawIndex: rawIndex
                );
            case CombatSimEventCardQuestCompleted completed:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "card-quest-completed",
                    "QuestCompleted",
                    source: completed.InstanceId,
                    targets: Array.Empty<string>(),
                    role: "triggered",
                    attribution: "exact",
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventCardQuestCompleted),
                    rawIndex: rawIndex
                );
            case CombatSimEventCardQuestUpdated updated:
                return NewEvent(
                    string.Empty,
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "card-quest-updated",
                    "QuestUpdated",
                    source: updated.InstanceId,
                    targets: Array.Empty<string>(),
                    value: updated.NewProgress - updated.OldProgress,
                    previousValue: updated.OldProgress,
                    currentValue: updated.NewProgress,
                    unit: "points",
                    role: "triggered",
                    attribution: "exact",
                    rawCategory: "combat-event",
                    rawType: nameof(CombatSimEventCardQuestUpdated),
                    rawIndex: rawIndex
                );
            case CombatSimEventMonsterGoldReceived gold:
                return GenericNumericFrameEvent(
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "monster-gold-received",
                    "MonsterGoldReceived",
                    (long)Math.Round(gold.HealthAmount),
                    nameof(CombatSimEventMonsterGoldReceived),
                    rawIndex
                );
            case CombatSimEventMonsterXpReceived xp:
                return GenericNumericFrameEvent(
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "monster-xp-received",
                    "MonsterXpReceived",
                    (long)Math.Round(xp.HealthAmount),
                    nameof(CombatSimEventMonsterXpReceived),
                    rawIndex
                );
            default:
                return GenericFrameEvent(
                    frame,
                    frameSequence,
                    combatTimeMs,
                    "unknown",
                    combatEvent.GetType().Name,
                    combatEvent.GetType().Name,
                    rawIndex
                );
        }
    }

    private static void ProjectPlayerUpdates(
        string combatant,
        CombatSimPlayerUpdate? update,
        int frame,
        int combatTimeMs,
        ref int frameSequence,
        ref int globalSequence,
        ICollection<CombatReportEventV1> events,
        ICollection<CombatReportMetricSampleV1> metrics
    )
    {
        if (update == null)
            return;

        var target = PlayerEntityId(combatant);
        for (var rawIndex = 0; rawIndex < update.HealthAdjustments.Count; rawIndex++)
        {
            var adjustment = update.HealthAdjustments[rawIndex];
            events.Add(
                NewEvent(
                    "e" + globalSequence++,
                    frame,
                    frameSequence++,
                    combatTimeMs,
                    "health",
                    adjustment.AttributeChanged + ":" + adjustment.DamageType,
                    source: null,
                    targets: new[] { target },
                    value: adjustment.Amount,
                    unit: "points",
                    role: "received",
                    attribution: "target-exact-source-unknown",
                    isCritical: adjustment.IsCrit,
                    rawCategory: "player-update",
                    rawType: nameof(CombatSimPlayerHealthAdjustment),
                    rawIndex: rawIndex
                )
            );
        }

        var attributeIndex = 0;
        foreach (var attributePair in update.Attributes.OrderBy(pair => (int)pair.Key))
        {
            var attribute = attributePair.Value;
            if (attribute.Delta == 0)
            {
                attributeIndex++;
                continue;
            }

            var action = attribute.AttributeType.ToString();
            events.Add(
                NewEvent(
                    "e" + globalSequence++,
                    frame,
                    frameSequence++,
                    combatTimeMs,
                    "player-attribute",
                    action,
                    source: null,
                    targets: new[] { target },
                    value: attribute.Delta,
                    previousValue: attribute.PreviousValue,
                    currentValue: attribute.CurrentValue,
                    unit: "points",
                    role: "received",
                    attribution: "target-exact-source-unknown",
                    rawCategory: "player-update",
                    rawType: nameof(CombatSimPlayerAttributeUpdate),
                    rawIndex: attributeIndex++
                )
            );
            metrics.Add(
                new CombatReportMetricSampleV1
                {
                    Frame = frame,
                    CombatTimeMs = combatTimeMs,
                    Combatant = combatant.ToLowerInvariant(),
                    Metric = action,
                    Value = attribute.CurrentValue,
                }
            );
        }
    }

    private static CombatReportFrameZeroStateV1 BuildFrameZeroState(
        IReadOnlyList<CombatSimFrame> frames
    ) =>
        new()
        {
            Player = BuildCombatantFrameZeroState(frames, frame => frame.PlayerUpdates),
            Opponent = BuildCombatantFrameZeroState(frames, frame => frame.OpponentUpdates),
        };

    private static CombatReportCombatantStateV1 BuildCombatantFrameZeroState(
        IReadOnlyList<CombatSimFrame> frames,
        Func<CombatSimFrame, CombatSimPlayerUpdate?> selectUpdate
    )
    {
        var baseline = new CombatReportCombatantStateV1();
        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var attributes = selectUpdate(frames[frameIndex])?.Attributes;
            if (attributes == null)
                continue;

            CaptureInitialAttribute(
                attributes,
                EPlayerAttributeType.Health,
                baseline.Health,
                value => baseline.Health = value
            );
            CaptureInitialAttribute(
                attributes,
                EPlayerAttributeType.Rage,
                baseline.Rage,
                value => baseline.Rage = value
            );
            CaptureInitialAttribute(
                attributes,
                EPlayerAttributeType.HealthRegen,
                baseline.HealthRegen,
                value => baseline.HealthRegen = value
            );
            CaptureInitialAttribute(
                attributes,
                EPlayerAttributeType.Shield,
                baseline.Shield,
                value => baseline.Shield = value
            );
            CaptureInitialAttribute(
                attributes,
                EPlayerAttributeType.Burn,
                baseline.Burn,
                value => baseline.Burn = value
            );
            CaptureInitialAttribute(
                attributes,
                EPlayerAttributeType.Poison,
                baseline.Poison,
                value => baseline.Poison = value
            );

            if (
                baseline.Health.HasValue
                && baseline.Rage.HasValue
                && baseline.HealthRegen.HasValue
                && baseline.Shield.HasValue
                && baseline.Burn.HasValue
                && baseline.Poison.HasValue
            )
            {
                break;
            }
        }

        return baseline;
    }

    private static void CaptureInitialAttribute(
        IReadOnlyDictionary<EPlayerAttributeType, CombatSimPlayerAttributeUpdate> attributes,
        EPlayerAttributeType attributeType,
        long? existingValue,
        Action<long> assign
    )
    {
        if (
            existingValue.HasValue
            || !attributes.TryGetValue(attributeType, out var firstTransition)
        )
        {
            return;
        }

        // PreviousValue is the state before this metric's first raw transition. This remains the
        // deterministic t=0 baseline even when the first transition occurs late in the battle.
        assign(firstTransition.PreviousValue);
    }

    private static int CountRawRecords(IReadOnlyList<CombatSimFrame> frames)
    {
        var count = 0;
        for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
        {
            var frame = frames[frameIndex];
            count = checked(count + frame.Events.Count);
            count = checked(count + CountPlayerUpdateRecords(frame.PlayerUpdates));
            count = checked(count + CountPlayerUpdateRecords(frame.OpponentUpdates));
            foreach (var cardUpdate in frame.CardUpdates.Values)
                count = checked(count + CountCardUpdateRecords(cardUpdate));
        }
        return count;
    }

    private static int CountPlayerUpdateRecords(CombatSimPlayerUpdate? update)
    {
        if (update == null)
            return 0;
        return checked(
            update.HealthAdjustments.Count
            + update.Attributes.Count
            + (update.Portrait == null ? 0 : 1)
        );
    }

    private static int CountCardUpdateRecords(CombatSimCardUpdate update) =>
        checked(
            update.Attributes.Count
            + (update.Enchantment.HasValue ? 1 : 0)
            + (update.Heroes == null ? 0 : 1)
            + (update.HiddenTags == null ? 0 : 1)
            + (update.Placement == null ? 0 : 1)
            + (update.Size.HasValue ? 1 : 0)
            + (update.Tags == null ? 0 : 1)
            + (update.Tier.HasValue ? 1 : 0)
            + (update.State == null ? 0 : 1)
        );

    private static List<CombatReportEntityV1> BuildEntities(PvpBattleManifest manifest)
    {
        var result = new List<CombatReportEntityV1>
        {
            new()
            {
                EntityId = PlayerEntityId("Player"),
                Owner = "player",
                Type = "hero",
                Name = manifest.Participants?.PlayerName ?? string.Empty,
                ContentKey = HeroContentKey(manifest.Participants?.PlayerHero),
                Order = 0,
            },
        };

        AppendCards(result, manifest.Snapshots?.PlayerHand?.Items, "player", "item", 10);
        AppendCards(result, manifest.Snapshots?.PlayerSkills?.Items, "player", "skill", 100);
        result.Add(
            new CombatReportEntityV1
            {
                EntityId = PlayerEntityId("Opponent"),
                Owner = "opponent",
                Type = "hero",
                Name = manifest.Participants?.OpponentName ?? string.Empty,
                ContentKey = HeroContentKey(manifest.Participants?.OpponentHero),
                Order = 1000,
            }
        );
        AppendCards(result, manifest.Snapshots?.OpponentHand?.Items, "opponent", "item", 1010);
        AppendCards(result, manifest.Snapshots?.OpponentSkills?.Items, "opponent", "skill", 1100);
        return result.OrderBy(entity => entity.Order).ToList();
    }

    private static void AppendCards(
        ICollection<CombatReportEntityV1> destination,
        IList<PvpBattleCardSnapshot>? cards,
        string owner,
        string type,
        int baseOrder
    )
    {
        if (cards == null)
            return;

        var ordered = cards
            .OrderBy(card => card.Socket.HasValue ? (int)card.Socket.Value : int.MaxValue)
            .ThenBy(card => card.InstanceId, StringComparer.Ordinal)
            .ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var card = ordered[index];
            destination.Add(
                new CombatReportEntityV1
                {
                    EntityId = card.InstanceId,
                    TemplateId = card.TemplateId,
                    Owner = owner,
                    Type = ResolveReportEntityType(card.Type, type),
                    Name = card.Name ?? string.Empty,
                    Size = card.Size.ToString(),
                    Slot = card.Socket.HasValue ? (int)card.Socket.Value : null,
                    Span = Math.Max(1, (int)card.Size),
                    Tier = card.Tier,
                    Enchant = card.Enchant,
                    Order = baseOrder + index,
                }
            );
        }
    }

    private static string ResolveReportEntityType(ECardType cardType, string collectionType) =>
        cardType switch
        {
            ECardType.SocketEffect => "effect",
            ECardType.Item => "item",
            ECardType.Skill => "skill",
            _ => collectionType,
        };

    private static CombatReportEventV1 NewEvent(
        string id,
        int frame,
        int frameSequence,
        int combatTimeMs,
        string kind,
        string action,
        string? source,
        IEnumerable<string> targets,
        long? value = null,
        long? previousValue = null,
        long? currentValue = null,
        string? unit = null,
        string role = "",
        string attribution = "unknown",
        string? effectId = null,
        string? executionContextId = null,
        string? triggerSource = null,
        IEnumerable<string>? removedTargets = null,
        bool? isCritical = null,
        string rawCategory = "",
        string rawType = "",
        int rawIndex = 0
    ) =>
        new()
        {
            EventId = id,
            Frame = frame,
            FrameSequence = frameSequence,
            CombatTimeMs = combatTimeMs,
            Kind = kind,
            Action = action,
            EffectId = effectId,
            ExecutionContextId = executionContextId,
            SourceEntityId = source,
            TriggerSourceEntityId = triggerSource,
            TargetEntityIds = targets
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList(),
            RemovedTargetEntityIds = (removedTargets ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList(),
            Value = value,
            PreviousValue = previousValue,
            CurrentValue = currentValue,
            Unit = unit,
            IsCritical = isCritical,
            Role = role,
            AttributionConfidence = attribution,
            RawReference = new CombatReportRawReferenceV1
            {
                Category = rawCategory,
                Type = rawType,
                Index = rawIndex,
            },
        };

    private static CombatReportEventV1 GenericFrameEvent(
        int frame,
        int frameSequence,
        int combatTimeMs,
        string kind,
        string action,
        string rawType,
        int rawIndex
    ) =>
        NewEvent(
            string.Empty,
            frame,
            frameSequence,
            combatTimeMs,
            kind,
            action,
            source: null,
            targets: Array.Empty<string>(),
            role: "system",
            attribution: "exact",
            rawCategory: "combat-event",
            rawType: rawType,
            rawIndex: rawIndex
        );

    private static CombatReportEventV1 GenericNumericFrameEvent(
        int frame,
        int frameSequence,
        int combatTimeMs,
        string kind,
        string action,
        long value,
        string rawType,
        int rawIndex
    )
    {
        var projected = GenericFrameEvent(
            frame,
            frameSequence,
            combatTimeMs,
            kind,
            action,
            rawType,
            rawIndex
        );
        projected.Value = value;
        projected.Unit = "points";
        return projected;
    }

    private static IEnumerable<string> TargetValues(IEnumerable<IEffectTarget> targets) =>
        targets.SelectMany(TargetValues).OrderBy(value => value, StringComparer.Ordinal);

    private static IEnumerable<string> TargetValues(IEffectTarget? target)
    {
        switch (target)
        {
            case EffectTargetCard card:
                return new[] { card.Target.Value };
            case EffectTargetPlayer player:
                return new[] { PlayerEntityId(player.Target.ToString()) };
            default:
                return Array.Empty<string>();
        }
    }

    private static string? Value(InstanceId? instanceId) => instanceId?.Value;

    private static string PlayerEntityId(string combatant) => "player:" + combatant;

    private static string UnitForCardAttribute(ECardAttributeType type)
    {
        switch (type)
        {
            case ECardAttributeType.Cooldown:
            case ECardAttributeType.CooldownMax:
            case ECardAttributeType.ChargeAmount:
            case ECardAttributeType.Haste:
            case ECardAttributeType.HasteAmount:
            case ECardAttributeType.Slow:
            case ECardAttributeType.SlowAmount:
            case ECardAttributeType.Freeze:
            case ECardAttributeType.FreezeAmount:
            case ECardAttributeType.FlatCooldownReduction:
                return "ms";
            default:
                return "points";
        }
    }

    private static string? HeroContentKey(string? hero)
    {
        if (string.IsNullOrWhiteSpace(hero))
            return null;
        return "hero-" + hero.Trim().ToLowerInvariant().Replace(' ', '-');
    }
}
