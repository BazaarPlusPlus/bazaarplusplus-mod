using System.Collections.Generic;
using BazaarPlusPlus.Game.AutoBazaar;
using Xunit;

public class AutoBazaarTargetSelectionActionsTests
{
    private static AutoBazaarTargetSelectionActions.OwnedCardRef Card(
        string id,
        string templateId,
        AutoBazaarTargetSection section,
        string leftSocket,
        int size
    ) => new(id, templateId, section, leftSocket, size);

    [Fact]
    public void Emit_EmptyFilter_ReturnsEmpty()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string>(),
            new[] { Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_2", 1) }
        );
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
            }
        );
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
            }
        );
        Assert.Equal(2, emit.Count);
        var ids = new HashSet<string>();
        foreach (var o in emit)
            ids.Add(o.CardInstanceId!);
        Assert.Contains("itm_a", ids);
        Assert.Contains("itm_b", ids);
        Assert.DoesNotContain("itm_c", ids);
    }

    [Fact]
    public void Emit_MultiCellItem_SocketsContiguousFromLeft()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "t1" },
            new[] { Card("itm_a", "t1", AutoBazaarTargetSection.Hand, "Socket_3", 3) }
        );
        Assert.Single(emit);
        Assert.Equal(new[] { "Socket_3", "Socket_4", "Socket_5" }, emit[0].TargetSockets);
    }

    [Fact]
    public void Emit_SkillCard_DoesNotEmitSelectItem()
    {
        var emit = AutoBazaarTargetSelectionActions.Emit(
            new HashSet<string> { "ts1" },
            new[] { Card("skl_a", "ts1", AutoBazaarTargetSection.Skill, "", 1) }
        );
        Assert.Empty(emit);
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
            }
        );
        Assert.Single(emit);
    }

    // -------------------------------------------------------------------------
    // ApplyTargetSelectionFilter
    // -------------------------------------------------------------------------

    private static AutoBazaarCardSnapshot Snap(
        string instanceId,
        string templateId,
        string size = "Small",
        string? socketId = null,
        AutoBazaarCardLocation location = AutoBazaarCardLocation.Selection
    ) =>
        new()
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            Size = size,
            SocketId = socketId,
            Location = location,
        };

    private static AutoBazaarDecisionOption SelectItemOption(
        string instanceId,
        AutoBazaarTargetSection section,
        params string[] sockets
    ) =>
        new()
        {
            ActionKind = AutoBazaarActionKind.SelectItem,
            Group = AutoBazaarActionGroup.Offer,
            DisplayKey = $"SelectItem:{instanceId}:{section}:{string.Join(",", sockets)}",
            CardInstanceId = instanceId,
            TargetSection = section,
            TargetSockets = sockets,
        };

    private static readonly AutoBazaarDecisionOption WaitOpt = new()
    {
        ActionKind = AutoBazaarActionKind.Wait,
        Group = AutoBazaarActionGroup.Wait,
        DisplayKey = "Wait",
    };

    [Fact]
    public void Apply_KeepsOfferSelectItem_WhenTemplateIsInFilter()
    {
        // Common upgrade-encounter shape: filter = [offer_template_only]
        var actions = new[]
        {
            WaitOpt,
            SelectItemOption("itm_offer", AutoBazaarTargetSection.Hand, "Socket_3", "Socket_4"),
            SelectItemOption("itm_offer", AutoBazaarTargetSection.Hand, "Socket_4", "Socket_5"),
        };
        var result = AutoBazaarTargetSelectionActions.ApplyTargetSelectionFilter(
            actions,
            new HashSet<string> { "tpl_X" },
            boardItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            chestItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            playerSkills: System.Array.Empty<AutoBazaarCardSnapshot>(),
            selectionOptionsCards: new[] { Snap("itm_offer", "tpl_X", size: "Medium") }
        );

        Assert.Contains(result, a => a.ActionKind == AutoBazaarActionKind.Wait);
        var selects = new List<AutoBazaarDecisionOption>(result);
        selects.RemoveAll(a => a.ActionKind != AutoBazaarActionKind.SelectItem);
        Assert.Equal(2, selects.Count);
        Assert.All(selects, s => Assert.Equal("itm_offer", s.CardInstanceId));
    }

    [Fact]
    public void Apply_DropsOfferSelectItem_WhenTemplateNotInFilter()
    {
        var actions = new[]
        {
            WaitOpt,
            SelectItemOption("itm_a", AutoBazaarTargetSection.Hand, "Socket_3"),
            SelectItemOption("itm_b", AutoBazaarTargetSection.Hand, "Socket_4"),
        };
        var result = AutoBazaarTargetSelectionActions.ApplyTargetSelectionFilter(
            actions,
            new HashSet<string> { "tpl_KEEP" },
            boardItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            chestItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            playerSkills: System.Array.Empty<AutoBazaarCardSnapshot>(),
            selectionOptionsCards: new[] { Snap("itm_a", "tpl_KEEP"), Snap("itm_b", "tpl_OTHER") }
        );

        var selects = result.Where(a => a.ActionKind == AutoBazaarActionKind.SelectItem).ToList();
        Assert.Single(selects);
        Assert.Equal("itm_a", selects[0].CardInstanceId);
    }

    [Fact]
    public void Apply_EmitsOwnedCardSelectItem_WhenFilterContainsOwnedTemplate()
    {
        // BuySpecificCardCondition._canInteractWithOwnedCards=true variant
        var actions = new[] { WaitOpt };
        var owned = new[]
        {
            Snap(
                "itm_owned",
                "tpl_OWNED",
                size: "Small",
                socketId: "Socket_3",
                location: AutoBazaarCardLocation.Board
            ),
        };
        var result = AutoBazaarTargetSelectionActions.ApplyTargetSelectionFilter(
            actions,
            new HashSet<string> { "tpl_OWNED" },
            boardItems: owned,
            chestItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            playerSkills: System.Array.Empty<AutoBazaarCardSnapshot>(),
            selectionOptionsCards: System.Array.Empty<AutoBazaarCardSnapshot>()
        );

        var selects = result.Where(a => a.ActionKind == AutoBazaarActionKind.SelectItem).ToList();
        Assert.Single(selects);
        Assert.Equal("itm_owned", selects[0].CardInstanceId);
        Assert.Equal(AutoBazaarTargetSection.Hand, selects[0].TargetSection);
    }

    [Fact]
    public void Apply_DoesNotEmitOwnedSkillAsSelectItem()
    {
        var actions = new[] { WaitOpt };
        var skill = new[]
        {
            Snap("skl_owned", "tpl_SKILL", size: "Small", location: AutoBazaarCardLocation.Skill),
        };
        var result = AutoBazaarTargetSelectionActions.ApplyTargetSelectionFilter(
            actions,
            new HashSet<string> { "tpl_SKILL" },
            boardItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            chestItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            playerSkills: skill,
            selectionOptionsCards: System.Array.Empty<AutoBazaarCardSnapshot>()
        );

        Assert.DoesNotContain(result, a => a.ActionKind == AutoBazaarActionKind.SelectItem);
    }

    [Fact]
    public void Apply_PreservesNonSelectItemActions()
    {
        var reroll = new AutoBazaarDecisionOption
        {
            ActionKind = AutoBazaarActionKind.Reroll,
            Group = AutoBazaarActionGroup.Reroll,
            DisplayKey = "Reroll",
        };
        var exit = new AutoBazaarDecisionOption
        {
            ActionKind = AutoBazaarActionKind.ExitState,
            Group = AutoBazaarActionGroup.Exit,
            DisplayKey = "ExitState",
        };
        var actions = new[] { WaitOpt, reroll, exit };
        var result = AutoBazaarTargetSelectionActions.ApplyTargetSelectionFilter(
            actions,
            new HashSet<string> { "tpl_X" },
            boardItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            chestItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            playerSkills: System.Array.Empty<AutoBazaarCardSnapshot>(),
            selectionOptionsCards: System.Array.Empty<AutoBazaarCardSnapshot>()
        );

        Assert.Contains(result, a => a.ActionKind == AutoBazaarActionKind.Wait);
        Assert.Contains(result, a => a.ActionKind == AutoBazaarActionKind.Reroll);
        Assert.Contains(result, a => a.ActionKind == AutoBazaarActionKind.ExitState);
        Assert.DoesNotContain(result, a => a.ActionKind == AutoBazaarActionKind.SelectItem);
    }

    [Fact]
    public void Apply_DropsOfferSelectItem_WhenInstanceIdNotInLookup()
    {
        // Defensive: SelectItem entry references an instanceId that's not in any
        // of the snapshot lists (e.g., card removed mid-build). Dropped.
        var actions = new[]
        {
            WaitOpt,
            SelectItemOption("itm_ghost", AutoBazaarTargetSection.Hand, "Socket_0"),
        };
        var result = AutoBazaarTargetSelectionActions.ApplyTargetSelectionFilter(
            actions,
            new HashSet<string> { "tpl_X" },
            boardItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            chestItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            playerSkills: System.Array.Empty<AutoBazaarCardSnapshot>(),
            selectionOptionsCards: System.Array.Empty<AutoBazaarCardSnapshot>()
        );

        Assert.DoesNotContain(result, a => a.ActionKind == AutoBazaarActionKind.SelectItem);
    }

    [Fact]
    public void Apply_DedupsOwnedEmit_WhenAlreadyKeptByTemplateMatch()
    {
        // If an owned card's templateId is in the filter AND the action list also
        // contained a SelectItem for that same owned instanceId (defensive only —
        // builder doesn't currently produce owned SelectItem in availableActions),
        // we should not emit a duplicate.
        var owned = Snap(
            "itm_o",
            "tpl_X",
            size: "Small",
            socketId: "Socket_3",
            location: AutoBazaarCardLocation.Board
        );
        var alreadyKept = SelectItemOption("itm_o", AutoBazaarTargetSection.Hand, "Socket_3");
        var actions = new[] { WaitOpt, alreadyKept };
        var result = AutoBazaarTargetSelectionActions.ApplyTargetSelectionFilter(
            actions,
            new HashSet<string> { "tpl_X" },
            boardItems: new[] { owned },
            chestItems: System.Array.Empty<AutoBazaarCardSnapshot>(),
            playerSkills: System.Array.Empty<AutoBazaarCardSnapshot>(),
            selectionOptionsCards: System.Array.Empty<AutoBazaarCardSnapshot>()
        );

        var selects = result.Where(a => a.ActionKind == AutoBazaarActionKind.SelectItem).ToList();
        Assert.Single(selects); // only the kept one; owned-emit skipped via dedup
    }
}
