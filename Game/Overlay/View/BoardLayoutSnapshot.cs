#pragma warning disable CS0436
using System.Collections.Generic;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class BoardLayoutSnapshot
{
    private const int BoardSlotCount = 10;
    private const int SkillSlotCount = 5;
    private const float BoardWidth = 8.25f;
    private const float SkillRegionWidth = BoardWidth / 2f;

    internal sealed class SlotPlacement
    {
        public string TemplateId { get; set; } = string.Empty;

        public int Span { get; set; } = 1;

        public Vector3 Center { get; set; } = Vector3.zero;
    }

    public IReadOnlyList<SlotPlacement> ItemSlots { get; private set; } = new List<SlotPlacement>();

    public IReadOnlyList<SlotPlacement> SkillSlots { get; private set; } = new List<SlotPlacement>();

    public static BoardLayoutSnapshot Build(PreviewBoardModel model)
    {
        model ??= new PreviewBoardModel();

        var snapshot = new BoardLayoutSnapshot
        {
            ItemSlots = BuildItemSlots(model.ItemCards),
            SkillSlots = BuildSkillSlots(model.SkillCards),
        };
        return snapshot;
    }

    private static IReadOnlyList<SlotPlacement> BuildItemSlots(IReadOnlyList<PreviewCardSpec> cards)
    {
        var result = new List<SlotPlacement>();
        if (cards == null || cards.Count == 0)
            return result;

        var occupied = 0;
        foreach (var card in cards)
            occupied += ClampSpan(card?.Size ?? 1);

        var slot = (BoardSlotCount - occupied) / 2;
        foreach (var card in cards)
        {
            var span = ClampSpan(card?.Size ?? 1);
            var start = slot;
            var end = start + span - 1;
            result.Add(
                new SlotPlacement
                {
                    TemplateId = card?.TemplateId ?? string.Empty,
                    Span = span,
                    Center = new Vector3(
                        (GetBoardSlotCenterX(start) + GetBoardSlotCenterX(end)) * 0.5f,
                        0f,
                        0f
                    ),
                }
            );
            slot += span;
        }

        return result;
    }

    private static IReadOnlyList<SlotPlacement> BuildSkillSlots(IReadOnlyList<PreviewCardSpec> cards)
    {
        var result = new List<SlotPlacement>();
        if (cards == null || cards.Count == 0)
            return result;

        var count = cards.Count > SkillSlotCount ? SkillSlotCount : cards.Count;
        var leadingEmpty = (SkillSlotCount - count) / 2;
        for (var index = 0; index < count; index++)
        {
            var slotIndex = leadingEmpty + index;
            result.Add(
                new SlotPlacement
                {
                    TemplateId = cards[index]?.TemplateId ?? string.Empty,
                    Span = 1,
                    Center = new Vector3(GetSkillSlotCenterX(slotIndex), 0f, 0f),
                }
            );
        }

        return result;
    }

    private static int ClampSpan(int span)
    {
        if (span < 1)
            return 1;
        if (span > 3)
            return 3;
        return span;
    }

    private static float GetBoardSlotCenterX(int slotIndex)
    {
        var slotWidth = BoardWidth / BoardSlotCount;
        return -BoardWidth * 0.5f + slotWidth * (slotIndex + 0.5f);
    }

    private static float GetSkillSlotCenterX(int slotIndex)
    {
        var slotWidth = SkillRegionWidth / SkillSlotCount;
        return -SkillRegionWidth * 0.5f + slotWidth * (slotIndex + 0.5f);
    }
}
