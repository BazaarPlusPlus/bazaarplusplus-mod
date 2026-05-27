#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.HistoryPanel.Data;

internal sealed class HistoryItemSpec
{
    public Guid TemplateId { get; set; }

    public ETier Tier { get; set; } = ETier.Bronze;

    public EEnchantmentType? EnchantmentType { get; set; }

    public EContainerSocketId? SocketId { get; set; }

    public IReadOnlyDictionary<ECardAttributeType, int> Attributes { get; set; } =
        new Dictionary<ECardAttributeType, int>();
}
