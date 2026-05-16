using System.Collections.Generic;
using Xunit;
using BazaarPlusPlus.Game.AutoBazaar;

public class AutoBazaarTargetSelectionActionsTests
{
    private static AutoBazaarTargetSelectionActions.OwnedCardRef Card(
        string id, string templateId, AutoBazaarTargetSection section,
        string leftSocket, int size)
        => new(id, templateId, section, leftSocket, size);

    [Fact]
    public void Emit_EmptyFilter_ReturnsEmpty()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string>(),
            new[] { Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1) });
        Assert.Empty(emit);
    }

    [Fact]
    public void Emit_FilterMatchesOneCard_SingleOption()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1" },
            new[]
            {
                Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1),
                Card("itm_b", "t2", AutoBazaarTargetSection.Hand, "Socket_3", 1),
            });
        Assert.Single(emit);
        var o = emit[0];
        Assert.Equal(AutoBazaarActionKind.SelectItem, o.ActionKind);
        Assert.Equal(AutoBazaarActionGroup.Offer, o.Group);
        Assert.Equal("itm_a", o.CardInstanceId);
        Assert.Equal(AutoBazaarTargetSection.Hand, o.TargetSection);
        Assert.NotNull(o.TargetSockets);
        Assert.Single(o.TargetSockets!);
        Assert.Equal("Socket_2", o.TargetSockets![0]);
        Assert.StartsWith("SelectItem:itm_a", o.DisplayKey);
    }

    [Fact]
    public void Emit_FilterMatchesMultipleCards_OnePerCard()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1", "t2" },
            new[]
            {
                Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1),
                Card("itm_b", "t2", AutoBazaarTargetSection.Stash, "Socket_5", 1),
                Card("itm_c", "t3", AutoBazaarTargetSection.Hand, "Socket_7", 1),
            });
        Assert.Equal(2, emit.Count);
        var ids = new HashSet<string>();
        foreach (var o in emit) ids.Add(o.CardInstanceId!);
        Assert.Contains("itm_a", ids);
        Assert.Contains("itm_b", ids);
        Assert.DoesNotContain("itm_c", ids);
    }

    [Fact]
    public void Emit_MultiCellItem_SocketsContiguousFromLeft()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1" },
            new[] { Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_3", 3) });
        Assert.Single(emit);
        Assert.Equal(new[] { "Socket_3", "Socket_4", "Socket_5" }, emit[0].TargetSockets);
    }

    [Fact]
    public void Emit_SkillCard_NoSocketsField()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "ts1" },
            new[] { Card("skl_a", "ts1", AutoBazaarTargetSection.Skill, "", 1) });
        Assert.Single(emit);
        Assert.Equal(AutoBazaarTargetSection.Skill, emit[0].TargetSection);
        Assert.True(emit[0].TargetSockets is null || emit[0].TargetSockets!.Count == 0);
    }

    [Fact]
    public void Emit_DuplicateInstanceId_DeduplicatedToOneEmit()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1" },
            new[]
            {
                Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1),
                Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1),
            });
        Assert.Single(emit);
    }
}
