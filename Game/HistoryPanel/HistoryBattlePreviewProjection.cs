#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.PreviewSurface;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryBattlePreviewProjection
{
    private static readonly object SocketEffectTemplateLock = new();
    private static readonly Dictionary<
        (Guid TemplateId, int Tier),
        ECardAttributeType?
    > SocketEffectAttributeTypeCache = new();
    private static object? _staticGameData;

    public static HistoryBattlePreviewData BuildEmpty()
    {
        return new HistoryBattlePreviewData(
            new PreviewBoardModel
            {
                ItemCards = new List<PreviewCardSpec>(),
                SkillCards = new List<PreviewCardSpec>(),
                Metadata = new Dictionary<string, string>(),
                Signature = string.Empty,
            },
            new PreviewBoardModel
            {
                ItemCards = new List<PreviewCardSpec>(),
                SkillCards = new List<PreviewCardSpec>(),
                Metadata = new Dictionary<string, string>(),
                Signature = string.Empty,
            }
        );
    }

    public static HistoryBattlePreviewData Build(PvpBattleSnapshots? snapshots)
    {
        if (snapshots == null)
            return BuildEmpty();

        return Build(
            snapshots.PlayerHand,
            snapshots.PlayerSkills,
            snapshots.OpponentHand,
            snapshots.OpponentSkills
        );
    }

    public static HistoryBattlePreviewData Build(
        PvpBattleCardSetCapture playerHand,
        PvpBattleCardSetCapture playerSkills,
        PvpBattleCardSetCapture opponentHand,
        PvpBattleCardSetCapture opponentSkills
    )
    {
        var playerBoard = BuildBoard(playerHand, playerSkills);
        var opponentBoard = BuildBoard(opponentHand, opponentSkills);
        return new HistoryBattlePreviewData(playerBoard, opponentBoard);
    }

    public static HistoryBattleSnapshotCounts CountSnapshots(
        PvpBattleCardSetCapture? playerHand,
        PvpBattleCardSetCapture? playerSkills,
        PvpBattleCardSetCapture? opponentHand,
        PvpBattleCardSetCapture? opponentSkills
    )
    {
        // Counts match what Build() would render so row-chip totals stay aligned with the
        // preview board: drop snapshots with empty/unparseable TemplateId, type mismatch
        // (e.g. SocketEffect appearing in an item capture), or missing static template.
        var staticData = TryGetStaticGameData();
        return new HistoryBattleSnapshotCounts(
            CountRenderable(playerHand?.Items, isSkill: false, staticData),
            CountRenderable(playerSkills?.Items, isSkill: true, staticData),
            CountRenderable(opponentHand?.Items, isSkill: false, staticData),
            CountRenderable(opponentSkills?.Items, isSkill: true, staticData)
        );
    }

    private static int CountRenderable(
        IList<CombatReplayCardSnapshot>? snapshots,
        bool isSkill,
        object? staticData
    )
    {
        if (snapshots == null)
            return 0;

        var count = 0;
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
                continue;
            if (isSkill ? snapshot.Type != ECardType.Skill : snapshot.Type != ECardType.Item)
                continue;
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;
            if (!HasStaticCardTemplate(staticData, templateId))
                continue;
            count++;
        }
        return count;
    }

    private static PreviewBoardModel BuildBoard(
        PvpBattleCardSetCapture itemCapture,
        PvpBattleCardSetCapture skillCapture
    )
    {
        var itemSnapshots = itemCapture?.Items;
        var socketEffectsBySocket = BuildSocketEffectMap(itemSnapshots);
        var staticData = TryGetStaticGameData();
        var model = new PreviewBoardModel
        {
            ItemCards = PreviewCardSpecFilter.Filter(
                BuildPreviewCardSpecs(itemSnapshots, isSkill: false, socketEffectsBySocket),
                templateId => HasStaticCardTemplate(staticData, templateId)
            ),
            SkillCards = PreviewCardSpecFilter.Filter(
                BuildPreviewCardSpecs(skillCapture?.Items, isSkill: true, null),
                templateId => HasStaticCardTemplate(staticData, templateId)
            ),
            Metadata = new Dictionary<string, string>(),
        };
        model.Signature = PreviewBoardSignature.Build(model);
        return model;
    }

    private static List<PreviewCardSpec> BuildPreviewCardSpecs(
        IEnumerable<CombatReplayCardSnapshot>? snapshots,
        bool isSkill,
        IReadOnlyDictionary<EContainerSocketId, HashSet<ECardAttributeType>>? socketEffectsBySocket
    )
    {
        var specs = new List<PreviewCardSpec>();
        if (snapshots == null)
            return specs;

        foreach (
            var snapshot in snapshots
                .Select((snapshot, index) => new { snapshot, index })
                .OrderBy(entry => entry.snapshot?.Socket.HasValue == true ? 0 : 1)
                .ThenBy(entry =>
                    entry.snapshot?.Socket.HasValue == true
                        ? (int)entry.snapshot.Socket!.Value
                        : int.MaxValue
                )
                .ThenBy(entry => entry.index)
                .Select(entry => entry.snapshot)
        )
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
                continue;

            var spec = BuildPreviewCardSpec(snapshot, isSkill, socketEffectsBySocket);
            if (spec != null)
                specs.Add(spec);
        }

        return specs;
    }

    private static PreviewCardSpec? BuildPreviewCardSpec(
        CombatReplayCardSnapshot snapshot,
        bool isSkill,
        IReadOnlyDictionary<EContainerSocketId, HashSet<ECardAttributeType>>? socketEffectsBySocket
    )
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
            return null;

        if (isSkill)
        {
            if (snapshot.Type != ECardType.Skill)
                return null;
        }
        else if (snapshot.Type != ECardType.Item)
            return null;

        var attributes = new Dictionary<int, int>();
        if (snapshot.Attributes != null)
        {
            foreach (var pair in snapshot.Attributes)
            {
                if (
                    Enum.TryParse<ECardAttributeType>(
                        pair.Key,
                        ignoreCase: false,
                        out var attributeType
                    )
                )
                {
                    attributes[(int)attributeType] = pair.Value;
                }
            }
        }

        if (!isSkill)
            ApplySocketEffectAttributes(snapshot, attributes, socketEffectsBySocket);

        return new PreviewCardSpec
        {
            TemplateId = snapshot.TemplateId,
            SourceName = snapshot.Name ?? string.Empty,
            Tier = ParseTier(snapshot.Tier),
            Size = isSkill ? 1 : ParseSize(snapshot.Size),
            Enchant = string.IsNullOrWhiteSpace(snapshot.Enchant) ? "None" : snapshot.Enchant!,
            Attributes = attributes,
        };
    }

    private static IReadOnlyDictionary<
        EContainerSocketId,
        HashSet<ECardAttributeType>
    > BuildSocketEffectMap(IEnumerable<CombatReplayCardSnapshot>? snapshots)
    {
        var result = new Dictionary<EContainerSocketId, HashSet<ECardAttributeType>>();
        if (snapshots == null)
            return result;

        foreach (var snapshot in snapshots)
        {
            if (
                snapshot == null
                || snapshot.Type != ECardType.SocketEffect
                || !snapshot.Socket.HasValue
                || string.IsNullOrWhiteSpace(snapshot.TemplateId)
            )
                continue;

            var effectType = ResolveSocketEffectAttributeType(snapshot);
            if (!effectType.HasValue)
                continue;

            if (!result.TryGetValue(snapshot.Socket.Value, out var effects))
            {
                effects = new HashSet<ECardAttributeType>();
                result[snapshot.Socket.Value] = effects;
            }

            effects.Add(effectType.Value);
        }

        return result;
    }

    private static void ApplySocketEffectAttributes(
        CombatReplayCardSnapshot snapshot,
        IDictionary<int, int> attributes,
        IReadOnlyDictionary<EContainerSocketId, HashSet<ECardAttributeType>>? socketEffectsBySocket
    )
    {
        if (
            snapshot == null
            || attributes == null
            || socketEffectsBySocket == null
            || !snapshot.Socket.HasValue
        )
            return;

        foreach (var socket in EnumerateOccupiedSockets(snapshot.Socket.Value, snapshot.Size))
        {
            if (!socketEffectsBySocket.TryGetValue(socket, out var effectTypes))
                continue;

            foreach (var effectType in effectTypes)
            {
                var key = (int)effectType;
                if (!attributes.TryGetValue(key, out var currentValue) || currentValue <= 0)
                    attributes[key] = 1;
            }
        }
    }

    private static IEnumerable<EContainerSocketId> EnumerateOccupiedSockets(
        EContainerSocketId startSocket,
        ECardSize size
    )
    {
        var span = ParseSize(size);
        var start = Math.Max(0, (int)startSocket);
        var end = Math.Min(9, start + Math.Max(1, span) - 1);
        for (var value = start; value <= end; value++)
            yield return (EContainerSocketId)value;
    }

    private static ECardAttributeType? ResolveSocketEffectAttributeType(
        CombatReplayCardSnapshot snapshot
    )
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TemplateId))
            return null;

        if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
            return null;

        var tier = ParseTier(snapshot.Tier);
        var cacheKey = (templateId, tier);
        lock (SocketEffectTemplateLock)
        {
            if (SocketEffectAttributeTypeCache.TryGetValue(cacheKey, out var cachedType))
                return cachedType;
        }

        ECardAttributeType? resolvedType = null;
        try
        {
            var staticData = GetStaticGameData();
            var template = GetTemplate(staticData, templateId) as TCardSocketEffect;
            if (template != null)
            {
                var auras = template.GetAuraTemplatesByTier((ETier)Math.Max(0, tier));
                foreach (var aura in auras)
                {
                    if (
                        aura?.Action is TAuraActionCardModifyAttribute action
                        && (
                            action.AttributeType == ECardAttributeType.Heated
                            || action.AttributeType == ECardAttributeType.Chilled
                        )
                    )
                    {
                        resolvedType = action.AttributeType;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "HistoryPanel",
                $"Failed to resolve socket-effect attribute type for {snapshot.TemplateId}: {ex.Message}"
            );
        }

        lock (SocketEffectTemplateLock)
            SocketEffectAttributeTypeCache[cacheKey] = resolvedType;

        return resolvedType;
    }

    private static bool HasStaticCardTemplate(object? staticData, Guid templateId)
    {
        return templateId != Guid.Empty && GetTemplate(staticData, templateId) != null;
    }

    private static object? TryGetStaticGameData()
    {
        try
        {
            return GetStaticGameData();
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "HistoryBattlePreviewProjection",
                "Failed to load static game data for battle preview filtering",
                ex
            );
            return null;
        }
    }

    private static object? GetStaticGameData()
    {
        lock (SocketEffectTemplateLock)
        {
            if (_staticGameData != null)
                return _staticGameData;
        }

        var staticData = BppStaticDataAccess.TryGet();
        if (staticData == null)
            return null;

        lock (SocketEffectTemplateLock)
        {
            _staticGameData ??= staticData;
            return _staticGameData;
        }
    }

    private static object? GetTemplate(object? staticData, Guid templateId)
    {
        if (staticData == null)
            return null;

        var method = staticData
            .GetType()
            .GetMethod(
                "GetCardById",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Guid) },
                null
            );
        return method?.Invoke(staticData, new object[] { templateId });
    }

    private static int ParseTier(string? value)
    {
        return
            !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<ETier>(value, ignoreCase: false, out var tier)
            ? (int)tier
            : 0;
    }

    private static int ParseSize(ECardSize size)
    {
        return size switch
        {
            ECardSize.Small => 1,
            ECardSize.Medium => 2,
            ECardSize.Large => 3,
            _ => 1,
        };
    }
}
