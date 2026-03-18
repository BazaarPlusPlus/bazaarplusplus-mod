#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogRuntime
{
    public CombatLogTimeline? CurrentTimeline { get; private set; }

    public void ReplaceCombat(CombatSim combatSim, CombatLogPlaybackPass playbackPass)
    {
        CurrentTimeline = BuildTimeline(combatSim, playbackPass);
    }

    public void Clear()
    {
        CurrentTimeline = null;
    }

    private static CombatLogTimeline BuildTimeline(
        CombatSim combatSim,
        CombatLogPlaybackPass playbackPass
    )
    {
        var rawFrames = combatSim?.Frames ?? new List<CombatSimFrame>();
        var frames = new List<CombatLogFrame>(rawFrames.Count);
        for (var frameIndex = 0; frameIndex < rawFrames.Count; frameIndex++)
            frames.Add(BuildFrame(rawFrames[frameIndex], frameIndex, rawFrames.Count));

        var rows = CombatLogFormatter.BuildRows(frames);
        return new CombatLogTimeline(playbackPass, frames, rows);
    }

    private static CombatLogFrame BuildFrame(CombatSimFrame simFrame, int frameIndex, int totalFrames)
    {
        return new CombatLogFrame(
            frameIndex,
            Math.Max(totalFrames - 1 - frameIndex, 0),
            GetLogicalTime(frameIndex),
            BuildEvents(simFrame.Events),
            BuildSide("Player", simFrame.PlayerUpdates),
            BuildSide("Opponent", simFrame.OpponentUpdates),
            BuildCardUpdates(simFrame.CardUpdates)
        );
    }

    private static TimeSpan GetLogicalTime(int frameIndex)
    {
        return TimeSpan.FromMilliseconds(frameIndex * CombatLogTiming.MillisecondsPerFrame);
    }

    private static IReadOnlyList<CombatLogEventEntry> BuildEvents(
        IReadOnlyList<ICombatSimEvent> simEvents
    )
    {
        var results = new List<CombatLogEventEntry>(simEvents?.Count ?? 0);
        if (simEvents == null)
            return results;

        foreach (var simEvent in simEvents)
        {
            switch (simEvent)
            {
                case CombatSimEventEffectExecuted executed:
                    results.Add(
                        new CombatLogEventEntry(
                            "EffectExecuted",
                            executed.ExecutionContextId,
                            FormatInstanceId(executed.Source),
                            FormatTarget(executed.Target),
                            BuildEffectExecutedText(executed)
                        )
                    );
                    break;
                case CombatSimEventCombatantDied died:
                    results.Add(
                        new CombatLogEventEntry(
                            "CombatantDied",
                            null,
                            null,
                            died.CombatantId.ToString(),
                            $"{died.CombatantId} died"
                        )
                    );
                    break;
                case CombatSimEventMonsterGoldReceived gold:
                    results.Add(
                        new CombatLogEventEntry(
                            "MonsterGoldReceived",
                            null,
                            null,
                            null,
                            $"Monster gold +{gold.HealthAmount:0.##}"
                        )
                    );
                    break;
                case CombatSimEventMonsterXpReceived xp:
                    results.Add(
                        new CombatLogEventEntry(
                            "MonsterXpReceived",
                            null,
                            null,
                            null,
                            $"Monster xp +{xp.HealthAmount:0.##}"
                        )
                    );
                    break;
                case CombatSimEventEffectTriggered triggered:
                    results.Add(
                        new CombatLogEventEntry(
                            "EffectTriggered",
                            triggered.ExecutionContextId,
                            FormatInstanceId(triggered.Source),
                            JoinTargets(triggered.Targets),
                            BuildEffectTriggeredText(triggered)
                        )
                    );
                    break;
                case CombatSimEventEffectAuraExecuted auraExecuted:
                    results.Add(
                        new CombatLogEventEntry(
                            "EffectAuraExecuted",
                            auraExecuted.ExecutionContextId,
                            FormatInstanceId(auraExecuted.Source),
                            null,
                            BuildEffectAuraExecutedText(auraExecuted)
                        )
                    );
                    break;
                case CombatSimEventCardEnchanted enchanted:
                    results.Add(
                        new CombatLogEventEntry(
                            "CardEnchanted",
                            null,
                            enchanted.InstanceId,
                            null,
                            BuildCardEnchantedText(enchanted)
                        )
                    );
                    break;
                case CombatSimEventCardTransformed transformed:
                    results.Add(
                        new CombatLogEventEntry(
                            "CardTransformed",
                            transformed.ExecutionContextId,
                            transformed.OriginalInstanceId,
                            null,
                            BuildCardTransformedText(transformed)
                        )
                    );
                    break;
                case CombatSimEventCardTransformReverted reverted:
                    results.Add(
                        new CombatLogEventEntry(
                            "CardTransformReverted",
                            null,
                            null,
                            null,
                            BuildCardTransformRevertedText(reverted)
                        )
                    );
                    break;
                case CombatSimEventCardQuestCompleted questCompleted:
                    results.Add(
                        new CombatLogEventEntry(
                            "CardQuestCompleted",
                            null,
                            questCompleted.InstanceId,
                            null,
                            $"Quest completed {questCompleted.InstanceId} group={questCompleted.QuestGroupIndex} entry={questCompleted.QuestEntryIndex}"
                        )
                    );
                    break;
                case CombatSimEventCardQuestUpdated questUpdated:
                    results.Add(
                        new CombatLogEventEntry(
                            "CardQuestUpdated",
                            null,
                            questUpdated.InstanceId,
                            null,
                            $"Quest updated {questUpdated.InstanceId} group={questUpdated.QuestGroupIndex} entry={questUpdated.QuestEntryIndex} {questUpdated.OldProgress} -> {questUpdated.NewProgress}"
                        )
                    );
                    break;
                case CombatSimEventSandstormCountdownStarted:
                    results.Add(
                        new CombatLogEventEntry(
                            "SandstormCountdownStarted",
                            null,
                            null,
                            null,
                            "Sandstorm countdown started"
                        )
                    );
                    break;
                case CombatSimEventSandstormStarted:
                    results.Add(
                        new CombatLogEventEntry(
                            "SandstormStarted",
                            null,
                            null,
                            null,
                            "Sandstorm started"
                        )
                    );
                    break;
                default:
                    results.Add(
                        new CombatLogEventEntry(
                            simEvent.GetType().Name.Replace("CombatSimEvent", string.Empty),
                            null,
                            null,
                            null,
                            BuildFallbackEventText(simEvent)
                        )
                    );
                    break;
            }
        }

        return results;
    }

    private static CombatLogSideUpdate? BuildSide(string side, CombatSimPlayerUpdate? update)
    {
        if (update == null)
            return null;

        var healthAdjustments = (IReadOnlyList<CombatLogHealthAdjustment>)
            update
                .HealthAdjustments
                .Select(
                    adjustment =>
                        new CombatLogHealthAdjustment(
                            adjustment.AttributeChanged.ToString(),
                            adjustment.Amount,
                            adjustment.IsCrit,
                            adjustment.IsDamageReduced
                        )
                )
                .ToList();

        var attributes = (IReadOnlyList<CombatLogAttributeChange>)
            update
                .Attributes
                .OrderBy(item => item.Key.ToString())
                .Select(
                    item =>
                        new CombatLogAttributeChange(
                            item.Key.ToString(),
                            item.Value.PreviousValue,
                            item.Value.CurrentValue
                        )
                )
                .ToList();

        var details = new List<string>();
        if (update.Portrait?.Index.HasValue == true)
            details.Add($"PortraitIndex -> {update.Portrait.Index.Value}");
        if (update.IsPlayerDead)
            details.Add("Marked dead");

        return new CombatLogSideUpdate(side, update.IsPlayerDead, healthAdjustments, attributes, details);
    }

    private static IReadOnlyList<CombatLogCardUpdateEntry> BuildCardUpdates(
        IReadOnlyDictionary<BazaarGameShared.Domain.Core.InstanceId, CombatSimCardUpdate> updates
    )
    {
        if (updates == null || updates.Count == 0)
            return Array.Empty<CombatLogCardUpdateEntry>();

        return updates
            .OrderBy(item => item.Key.ToString())
            .Select(
                item =>
                {
                    var details = new List<string>();
                    if (item.Value.Enchantment.HasValue)
                        details.Add($"Enchantment -> {item.Value.Enchantment.Value}");
                    if (item.Value.Size.HasValue)
                        details.Add($"Size -> {item.Value.Size.Value}");
                    if (item.Value.Tier.HasValue)
                        details.Add($"Tier -> {item.Value.Tier.Value}");
                    if (item.Value.State != null)
                    {
                        details.Add(
                            $"State {item.Value.State.PreviousValue} -> {item.Value.State.CurrentValue}"
                        );
                    }
                    if (item.Value.Placement != null)
                        details.Add($"Placement -> {FormatObject(item.Value.Placement)}");
                    if (item.Value.Tags?.Count > 0)
                        details.Add($"Tags -> {string.Join(", ", item.Value.Tags.OrderBy(tag => tag.ToString()))}");
                    if (item.Value.HiddenTags?.Count > 0)
                    {
                        details.Add(
                            $"HiddenTags -> {string.Join(", ", item.Value.HiddenTags.OrderBy(tag => tag.ToString()))}"
                        );
                    }
                    if (item.Value.Heroes?.Count > 0)
                        details.Add($"Heroes -> {string.Join(", ", item.Value.Heroes.OrderBy(hero => hero.ToString()))}");

                    return new CombatLogCardUpdateEntry(
                        item.Key.ToString(),
                        item.Value.Attributes
                            .OrderBy(attribute => attribute.Key.ToString())
                            .Select(
                                attribute =>
                                    new CombatLogAttributeChange(
                                        attribute.Key.ToString(),
                                        attribute.Value.PreviousValue,
                                        attribute.Value.CurrentValue
                                    )
                            )
                            .ToList(),
                        details
                    );
                }
            )
            .ToList();
    }

    private static string BuildEffectExecutedText(CombatSimEventEffectExecuted executed)
    {
        var sourceText = FormatInstanceId(executed.Source) ?? "unknown-source";
        var targetText = FormatTarget(executed.Target) ?? "unknown-target";
        return $"{executed.ActionType} {executed.EffectId} {sourceText} -> {targetText}";
    }

    private static string BuildEffectTriggeredText(CombatSimEventEffectTriggered triggered)
    {
        var sourceText = FormatInstanceId(triggered.Source) ?? "unknown-source";
        var targetText = JoinTargets(triggered.Targets) ?? "unknown-target";
        return $"Triggered {triggered.EffectId} {sourceText} -> {targetText}";
    }

    private static string BuildEffectAuraExecutedText(CombatSimEventEffectAuraExecuted auraExecuted)
    {
        var sourceText = FormatInstanceId(auraExecuted.Source) ?? "unknown-source";
        var applied = JoinTargets(auraExecuted.AppliedTo);
        var removed = JoinTargets(auraExecuted.RemovedFrom);
        if (!string.IsNullOrEmpty(applied) && !string.IsNullOrEmpty(removed))
            return $"Aura {auraExecuted.EffectId} {sourceText} applied [{applied}] removed [{removed}]";
        if (!string.IsNullOrEmpty(applied))
            return $"Aura {auraExecuted.EffectId} {sourceText} applied [{applied}]";
        if (!string.IsNullOrEmpty(removed))
            return $"Aura {auraExecuted.EffectId} {sourceText} removed [{removed}]";
        return $"Aura {auraExecuted.EffectId} {sourceText}";
    }

    private static string BuildCardEnchantedText(CombatSimEventCardEnchanted enchanted)
    {
        var state = enchanted.IsReverted ? "reverted" : "applied";
        return $"Enchant {state} {enchanted.InstanceId} -> {enchanted.EnchantmentType?.ToString() ?? "none"}";
    }

    private static string BuildCardTransformedText(CombatSimEventCardTransformed transformed)
    {
        var transformedCards = transformed.TransformedCards?.Count > 0
            ? string.Join(", ", transformed.TransformedCards.Select(FormatTransformation))
            : "none";
        return $"Transform {transformed.OriginalInstanceId} -> [{transformedCards}]";
    }

    private static string BuildCardTransformRevertedText(CombatSimEventCardTransformReverted reverted)
    {
        var original = FormatTransformation(reverted.OriginalCard);
        var revertedIds = reverted.TransformedCardInstanceIds?.Count > 0
            ? string.Join(", ", reverted.TransformedCardInstanceIds)
            : "none";
        return $"Transform reverted {original} from [{revertedIds}]";
    }

    private static string FormatTransformation(SimEventCardTransformation transformation)
    {
        var section = transformation.Section?.ToString() ?? "?";
        var socket = transformation.Socket?.ToString() ?? "?";
        return $"{transformation.InstanceId}/{transformation.TemplateId} {transformation.Type} {transformation.CombatantId} {section}:{socket}";
    }

    private static string? FormatInstanceId(BazaarGameShared.Domain.Core.InstanceId? instanceId)
    {
        return instanceId?.ToString();
    }

    private static string? FormatTarget(IEffectTarget? target)
    {
        return target switch
        {
            EffectTargetCard card => card.Target.ToString(),
            EffectTargetPlayer player => player.Target.ToString(),
            null => null,
            _ => target.GetType().Name,
        };
    }

    private static string? JoinTargets(IEnumerable<IEffectTarget>? targets)
    {
        if (targets == null)
            return null;

        var values = targets
            .Select(FormatTarget)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static string BuildFallbackEventText(ICombatSimEvent simEvent)
    {
        var details = FormatObject(simEvent);
        var typeName = simEvent.GetType().Name.Replace("CombatSimEvent", string.Empty);
        return string.IsNullOrEmpty(details) ? typeName : $"{typeName} {details}";
    }

    private static string FormatObject(object? value)
    {
        if (value == null)
            return "null";

        if (value is string text)
            return text;

        if (value is IEffectTarget effectTarget)
            return FormatTarget(effectTarget) ?? effectTarget.GetType().Name;

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var parts = new List<string>();
            foreach (var item in enumerable)
                parts.Add(FormatObject(item));
            return $"[{string.Join(", ", parts)}]";
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is decimal)
            return value.ToString() ?? string.Empty;

        var properties = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToArray();
        if (properties.Length == 0)
            return value.ToString() ?? type.Name;

        var partsFromProperties = new List<string>();
        foreach (var property in properties)
        {
            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            partsFromProperties.Add($"{property.Name}={FormatObject(propertyValue)}");
        }

        return string.Join(", ", partsFromProperties);
    }
}
