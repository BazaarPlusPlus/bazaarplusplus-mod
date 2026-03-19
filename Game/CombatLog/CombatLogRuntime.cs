#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatLog;

internal sealed class CombatLogRuntime
{
    private readonly Func<string, CombatLogCardDisplayInfo?>? _displayInfoResolver;

    public CombatLogTimeline? CurrentTimeline { get; private set; }

    public CombatLogRuntime(Func<string, CombatLogCardDisplayInfo?>? displayInfoResolver = null)
    {
        _displayInfoResolver = displayInfoResolver;
    }

    public void ReplaceCombat(CombatSim combatSim, CombatLogPlaybackPass playbackPass)
    {
        CurrentTimeline = BuildTimeline(combatSim, playbackPass);
    }

    public void Clear()
    {
        CurrentTimeline = null;
    }

    private CombatLogTimeline BuildTimeline(
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

    private CombatLogFrame BuildFrame(CombatSimFrame simFrame, int frameIndex, int totalFrames)
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

    private IReadOnlyList<CombatLogEventEntry> BuildEvents(
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
                    var executedSourceId = FormatInstanceId(executed.Source);
                    var executedSourceDisplay = ResolveCardDisplayName(executedSourceId);
                    var executedTargetId = FormatTargetId(executed.Target);
                    var executedTargetDisplay = FormatTargetDisplay(executed.Target);
                    results.Add(
                        new CombatLogEventEntry(
                            "EffectExecuted",
                            executed.ExecutionContextId,
                            executedSourceId,
                            executedTargetId,
                            executedSourceDisplay,
                            executedTargetDisplay,
                            BuildEffectExecutedText(executed, executedSourceDisplay, executedTargetDisplay)
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
                            null,
                            null,
                            $"Monster xp +{xp.HealthAmount:0.##}"
                        )
                    );
                    break;
                case CombatSimEventEffectTriggered triggered:
                    var triggeredSourceId = FormatInstanceId(triggered.Source);
                    var triggeredSourceDisplay = ResolveCardDisplayName(triggeredSourceId);
                    var triggeredTargets = JoinTargets(triggered.Targets);
                    results.Add(
                        new CombatLogEventEntry(
                            "EffectTriggered",
                            triggered.ExecutionContextId,
                            triggeredSourceId,
                            triggeredTargets.raw,
                            triggeredSourceDisplay,
                            triggeredTargets.display,
                            BuildEffectTriggeredText(triggered, triggeredSourceDisplay, triggeredTargets.display)
                        )
                    );
                    break;
                case CombatSimEventEffectAuraExecuted auraExecuted:
                    var auraSourceId = FormatInstanceId(auraExecuted.Source);
                    var auraSourceDisplay = ResolveCardDisplayName(auraSourceId);
                    results.Add(
                        new CombatLogEventEntry(
                            "EffectAuraExecuted",
                            auraExecuted.ExecutionContextId,
                            auraSourceId,
                            null,
                            auraSourceDisplay,
                            null,
                            BuildEffectAuraExecutedText(auraExecuted, auraSourceDisplay)
                        )
                    );
                    break;
                case CombatSimEventCardEnchanted enchanted:
                    var enchantedDisplay = ResolveCardDisplayName(enchanted.InstanceId);
                    results.Add(
                        new CombatLogEventEntry(
                            "CardEnchanted",
                            null,
                            enchanted.InstanceId,
                            null,
                            enchantedDisplay,
                            null,
                            BuildCardEnchantedText(enchanted, enchantedDisplay)
                        )
                    );
                    break;
                case CombatSimEventCardTransformed transformed:
                    var transformedDisplay = ResolveCardDisplayName(transformed.OriginalInstanceId);
                    results.Add(
                        new CombatLogEventEntry(
                            "CardTransformed",
                            transformed.ExecutionContextId,
                            transformed.OriginalInstanceId,
                            null,
                            transformedDisplay,
                            null,
                            BuildCardTransformedText(transformed, transformedDisplay)
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
                            null,
                            null,
                            BuildCardTransformRevertedText(reverted)
                        )
                    );
                    break;
                case CombatSimEventCardQuestCompleted questCompleted:
                    var questCompletedDisplay = ResolveCardDisplayName(questCompleted.InstanceId);
                    results.Add(
                        new CombatLogEventEntry(
                            "CardQuestCompleted",
                            null,
                            questCompleted.InstanceId,
                            null,
                            questCompletedDisplay,
                            null,
                            $"Quest completed {questCompletedDisplay ?? questCompleted.InstanceId} group={questCompleted.QuestGroupIndex} entry={questCompleted.QuestEntryIndex}"
                        )
                    );
                    break;
                case CombatSimEventCardQuestUpdated questUpdated:
                    var questUpdatedDisplay = ResolveCardDisplayName(questUpdated.InstanceId);
                    results.Add(
                        new CombatLogEventEntry(
                            "CardQuestUpdated",
                            null,
                            questUpdated.InstanceId,
                            null,
                            questUpdatedDisplay,
                            null,
                            $"Quest updated {questUpdatedDisplay ?? questUpdated.InstanceId} group={questUpdated.QuestGroupIndex} entry={questUpdated.QuestEntryIndex} {questUpdated.OldProgress} -> {questUpdated.NewProgress}"
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

    private IReadOnlyList<CombatLogCardUpdateEntry> BuildCardUpdates(
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
                        ResolveCardDisplayInfo(item.Key.ToString()),
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

    private string BuildEffectExecutedText(
        CombatSimEventEffectExecuted executed,
        string? sourceDisplayName,
        string? targetDisplayName
    )
    {
        var sourceText = sourceDisplayName ?? FormatInstanceId(executed.Source) ?? "unknown-source";
        var targetText = targetDisplayName ?? FormatTargetId(executed.Target) ?? "unknown-target";
        return $"{executed.ActionType} {executed.EffectId} {sourceText} -> {targetText}";
    }

    private string BuildEffectTriggeredText(
        CombatSimEventEffectTriggered triggered,
        string? sourceDisplayName,
        string? targetDisplayName
    )
    {
        var sourceText = sourceDisplayName ?? FormatInstanceId(triggered.Source) ?? "unknown-source";
        var targetText = targetDisplayName ?? JoinTargets(triggered.Targets).raw ?? "unknown-target";
        return $"Triggered {triggered.EffectId} {sourceText} -> {targetText}";
    }

    private string BuildEffectAuraExecutedText(
        CombatSimEventEffectAuraExecuted auraExecuted,
        string? sourceDisplayName
    )
    {
        var sourceText = sourceDisplayName ?? FormatInstanceId(auraExecuted.Source) ?? "unknown-source";
        var applied = JoinTargets(auraExecuted.AppliedTo).display;
        var removed = JoinTargets(auraExecuted.RemovedFrom).display;
        if (!string.IsNullOrEmpty(applied) && !string.IsNullOrEmpty(removed))
            return $"Aura {auraExecuted.EffectId} {sourceText} applied [{applied}] removed [{removed}]";
        if (!string.IsNullOrEmpty(applied))
            return $"Aura {auraExecuted.EffectId} {sourceText} applied [{applied}]";
        if (!string.IsNullOrEmpty(removed))
            return $"Aura {auraExecuted.EffectId} {sourceText} removed [{removed}]";
        return $"Aura {auraExecuted.EffectId} {sourceText}";
    }

    private static string BuildCardEnchantedText(
        CombatSimEventCardEnchanted enchanted,
        string? displayName
    )
    {
        var state = enchanted.IsReverted ? "reverted" : "applied";
        return $"Enchant {state} {displayName ?? enchanted.InstanceId} -> {enchanted.EnchantmentType?.ToString() ?? "none"}";
    }

    private string BuildCardTransformedText(
        CombatSimEventCardTransformed transformed,
        string? displayName
    )
    {
        var transformedCards = transformed.TransformedCards?.Count > 0
            ? string.Join(", ", transformed.TransformedCards.Select(FormatTransformation))
            : "none";
        return $"Transform {displayName ?? transformed.OriginalInstanceId} -> [{transformedCards}]";
    }

    private string BuildCardTransformRevertedText(CombatSimEventCardTransformReverted reverted)
    {
        var original = FormatTransformation(reverted.OriginalCard);
        var revertedIds = reverted.TransformedCardInstanceIds?.Count > 0
            ? string.Join(", ", reverted.TransformedCardInstanceIds)
            : "none";
        return $"Transform reverted {original} from [{revertedIds}]";
    }

    private string FormatTransformation(SimEventCardTransformation transformation)
    {
        var section = transformation.Section?.ToString() ?? "?";
        var socket = transformation.Socket?.ToString() ?? "?";
        var displayName = ResolveCardDisplayName(transformation.InstanceId);
        return $"{displayName ?? transformation.InstanceId}/{transformation.TemplateId} {transformation.Type} {transformation.CombatantId} {section}:{socket}";
    }

    private static string? FormatInstanceId(BazaarGameShared.Domain.Core.InstanceId? instanceId)
    {
        return instanceId?.ToString();
    }

    private static string? FormatTargetId(IEffectTarget? target)
    {
        return target switch
        {
            EffectTargetCard card => card.Target.ToString(),
            EffectTargetPlayer player => player.Target.ToString(),
            null => null,
            _ => target.GetType().Name,
        };
    }

    private string? FormatTargetDisplay(IEffectTarget? target)
    {
        return target switch
        {
            EffectTargetCard card => ResolveCardDisplayName(card.Target.ToString()) ?? BuildShortIdentifier(card.Target.ToString()),
            EffectTargetPlayer player => player.Target.ToString(),
            null => null,
            _ => target.GetType().Name,
        };
    }

    private (string? raw, string? display) JoinTargets(IEnumerable<IEffectTarget>? targets)
    {
        if (targets == null)
            return (null, null);

        var rawValues = targets
            .Select(FormatTargetId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var displayValues = targets
            .Select(FormatTargetDisplay)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        return (
            rawValues.Count == 0 ? null : string.Join(", ", rawValues),
            displayValues.Count == 0 ? null : string.Join(", ", displayValues)
        );
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
            return FormatTargetId(effectTarget) ?? effectTarget.GetType().Name;

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

    private CombatLogCardDisplayInfo ResolveCardDisplayInfo(string instanceId)
    {
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            var resolved = _displayInfoResolver?.Invoke(instanceId) ?? ResolveLiveCardDisplayInfo(instanceId);
            if (resolved != null)
                return resolved;
        }

        return CreateFallbackCardDisplayInfo(instanceId);
    }

    private string? ResolveCardDisplayName(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return null;

        return ResolveCardDisplayInfo(instanceId).DisplayName;
    }

    private static CombatLogCardDisplayInfo? ResolveLiveCardDisplayInfo(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return null;

        foreach (var card in EnumerateCombatCards())
        {
            var cardInstanceId = card.GetInstanceId().ToString();
            if (!string.Equals(cardInstanceId, instanceId, StringComparison.Ordinal))
                continue;

            return new CombatLogCardDisplayInfo(
                cardInstanceId,
                card.TemplateId.ToString(),
                ResolveDisplayName(
                    card.Template?.Localization?.Title?.Text,
                    card.Template?.InternalName,
                    card.TemplateId.ToString(),
                    cardInstanceId
                ),
                card.Owner == Data.Run?.Player ? "Player" : card.Owner == Data.Run?.Opponent ? "Opponent" : null,
                card.Type.ToString()
            );
        }

        return null;
    }

    private static IEnumerable<Card> EnumerateCombatCards()
    {
        if (Data.Run?.Player?.Hand != null)
        {
            foreach (var card in GameDataReader.GetItemsAsCards(Data.Run.Player.Hand))
                yield return card;
        }

        if (Data.Run?.Player?.Skills != null)
        {
            foreach (var skill in Data.Run.Player.Skills)
                yield return skill;
        }

        if (Data.Run?.Opponent?.Hand != null)
        {
            foreach (var card in GameDataReader.GetItemsAsCards(Data.Run.Opponent.Hand))
                yield return card;
        }

        if (Data.Run?.Opponent?.Skills != null)
        {
            foreach (var skill in Data.Run.Opponent.Skills)
                yield return skill;
        }
    }

    private static CombatLogCardDisplayInfo CreateFallbackCardDisplayInfo(string instanceId)
    {
        return new CombatLogCardDisplayInfo(
            instanceId,
            null,
            BuildShortIdentifier(instanceId)
        );
    }

    private static string ResolveDisplayName(
        string? localizedTitle,
        string? internalName,
        string? templateId,
        string instanceId
    )
    {
        if (!string.IsNullOrWhiteSpace(localizedTitle))
            return localizedTitle;
        if (!string.IsNullOrWhiteSpace(internalName))
            return internalName;
        if (!string.IsNullOrWhiteSpace(templateId))
            return BuildShortIdentifier(templateId);

        return BuildShortIdentifier(instanceId);
    }

    private static string BuildShortIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        return value.Length <= 8 ? value : value[..8];
    }
}
