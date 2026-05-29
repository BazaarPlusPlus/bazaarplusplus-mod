#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using UnityEngine;

namespace BazaarPlusPlus.Game.CardSetPreview;

// Fabricates a synthetic TMonster/TPlayer graph from item template specs so the
// game's MonsterBoardTooltip can render an arbitrary item set. Extracted verbatim
// from ItemBoardOverlay; construction order and clamps are unchanged.
internal static class SyntheticMonsterFactory
{
    public static TMonster BuildSyntheticMonster(IReadOnlyList<ItemBoardItemSpec> items)
    {
        var instances = new List<TCardInstanceItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var spec = items[i];
            instances.Add(
                new TCardInstanceItem
                {
                    TemplateId = spec.TemplateId,
                    TemplateVersion = string.Empty,
                    InstanceId = $"bpp-itemboard-{i}",
                    Tier = spec.Tier,
                    SocketId = spec.SocketId ?? ResolveSyntheticSocket(i),
                    EnchantmentType = spec.EnchantmentType,
                    Attributes =
                        spec.Attributes != null
                            ? new Dictionary<ECardAttributeType, int>(spec.Attributes)
                            : new Dictionary<ECardAttributeType, int>(),
                }
            );
        }

        return new TMonster
        {
            Id = Guid.Empty,
            Version = string.Empty,
            InternalName = "item_board_template_set",
            Player = new TPlayer
            {
                Attributes = new Dictionary<EPlayerAttributeType, int>
                {
                    [EPlayerAttributeType.HealthMax] = 0,
                },
                Hand = new TPlayerInventory
                {
                    UnlockedSlots = (ushort)Mathf.Max(10, instances.Count),
                    Items = instances,
                },
            },
        };
    }

    public static EContainerSocketId ResolveSyntheticSocket(int index)
    {
        var clamped = Mathf.Clamp(index, 0, 9);
        return (EContainerSocketId)clamped;
    }
}
