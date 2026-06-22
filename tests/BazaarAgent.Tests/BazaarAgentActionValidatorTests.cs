#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.BazaarAgent;
using Xunit;

public class BazaarAgentActionValidatorTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static BazaarAgentContextSnapshot MakeSnap(
        ulong tickId = 1,
        params BazaarAgentDecisionOption[] available
    )
    {
        var ctx = new BazaarAgentContext { TickId = tickId, AvailableActions = available };
        return new BazaarAgentContextSnapshot(ctx);
    }

    private static BazaarAgentDecisionOption CardOption(
        BazaarAgentActionKind kind,
        string cardId = "c1",
        BazaarAgentTargetSection? section = null,
        IReadOnlyList<string>? sockets = null,
        bool? canSelect = null,
        bool? canSell = null,
        bool? canAfford = null,
        bool? canFit = null
    ) =>
        new()
        {
            ActionKind = kind,
            CardInstanceId = cardId,
            TargetSection = section,
            TargetSockets = sockets,
            Card = new BazaarAgentCardSnapshot
            {
                InstanceId = cardId,
                CanSelect = canSelect,
                CanSell = canSell,
                CanAfford = canAfford,
                CanFit = canFit,
            },
        };

    private static BazaarAgentDecisionOption SimpleOption(BazaarAgentActionKind kind) =>
        new() { ActionKind = kind };

    // ── Rule 1: known actionKind ──────────────────────────────────────────────

    [Fact]
    public void Rule1_KnownKind_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Wait));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Wait };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule1_UnknownKind_RejectsInvalid()
    {
        var snap = MakeSnap();
        var action = new BazaarAgentAction { ActionKind = (BazaarAgentActionKind)999 };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Invalid, result.Code);
        Assert.Equal(400, result.HttpStatus);
        Assert.Equal("unknown actionKind", result.Details);
    }

    // ── Rule 2: actionKind in availableActions (Wait exempt) ─────────────────

    [Fact]
    public void Rule2_Wait_ExemptFromAvailableActionsCheck()
    {
        // snapshot has NO AvailableActions at all, but Wait should still pass rule 2
        var snap = MakeSnap(1);
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Wait };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule2_ActionKindNotInAvailableActions_RejectsStaleOrUnavailable()
    {
        var snap = MakeSnap(5, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.ExitState };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.Equal("actionKind not in availableActions", result.Details);
        Assert.NotNull(result.Extra);
        Assert.Equal(5UL, result.Extra["currentTickId"]);
    }

    [Fact]
    public void Rule2_ActionKindInAvailableActions_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Reroll };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    // ── Rule 3: card-bearing exact match ─────────────────────────────────────

    [Fact]
    public void Rule3_CardBearing_ExactMatch_Passes()
    {
        var opt = CardOption(
            BazaarAgentActionKind.SelectItem,
            "c1",
            BazaarAgentTargetSection.Hand,
            new[] { "Socket_0" }
        );
        var snap = MakeSnap(1, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.SelectItem,
            CardInstanceId = "c1",
            TargetSection = BazaarAgentTargetSection.Hand,
            TargetSockets = new[] { "Socket_0" },
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule3_CardBearing_WrongCardId_RejectsStaleOrUnavailable()
    {
        var opt = CardOption(BazaarAgentActionKind.SelectItem, "c1");
        var snap = MakeSnap(3, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.SelectItem,
            CardInstanceId = "c99",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.Equal("no matching option for card-bearing action", result.Details);
        Assert.NotNull(result.Extra);
        Assert.Equal(3UL, result.Extra["currentTickId"]);
    }

    [Fact]
    public void Rule3_CardBearing_SocketOrderDiffers_RejectsStaleOrUnavailable()
    {
        var opt = CardOption(
            BazaarAgentActionKind.MoveItem,
            "c1",
            BazaarAgentTargetSection.Hand,
            new[] { "Socket_0", "Socket_1" }
        );
        var snap = MakeSnap(2, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.MoveItem,
            CardInstanceId = "c1",
            TargetSection = BazaarAgentTargetSection.Hand,
            TargetSockets = new[] { "Socket_1", "Socket_0" }, // reversed
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
    }

    [Fact]
    public void Rule3_NonCardBearing_Reroll_SkipsCardMatch()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Reroll };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    // ── Rule 4: Hero / PlayMode for StartOrContinueRun ───────────────────────

    [Fact]
    public void Rule4_StartOrContinueRun_ValidHeroAndPlayMode_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.StartOrContinueRun));
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.StartOrContinueRun,
            Hero = "Pygmalien",
            PlayMode = "Ranked",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule4_StartOrContinueRun_HeroCaseInsensitive_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.StartOrContinueRun));
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.StartOrContinueRun,
            Hero = "karnok",
            PlayMode = "unranked",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule4_StartOrContinueRun_UnknownHero_RejectsInvalid()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.StartOrContinueRun));
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.StartOrContinueRun,
            Hero = "Gandalf",
            PlayMode = "Unranked",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Invalid, result.Code);
        Assert.Equal(400, result.HttpStatus);
        Assert.Equal("unknown hero", result.Details);
    }

    [Fact]
    public void Rule4_StartOrContinueRun_UnknownPlayMode_RejectsInvalid()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.StartOrContinueRun));
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.StartOrContinueRun,
            Hero = "Vanessa",
            PlayMode = "Tournament",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Invalid, result.Code);
        Assert.Equal(400, result.HttpStatus);
        Assert.Equal("unknown playMode", result.Details);
    }

    [Fact]
    public void Rule4_OtherKind_NoHeroPlayModeCheck()
    {
        // Reroll with garbage hero/playMode — rule 4 only fires for StartOrContinueRun
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.Reroll,
            Hero = "Gandalf",
            PlayMode = "Tournament",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    // ── Rule 5: CanSelect != false ────────────────────────────────────────────

    [Fact]
    public void Rule5_SelectItem_CanSelectNull_Passes()
    {
        var opt = CardOption(BazaarAgentActionKind.SelectItem, "c1", canSelect: null);
        var snap = MakeSnap(1, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.SelectItem,
            CardInstanceId = "c1",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule5_SelectItem_CanSelectFalse_RejectsStaleOrUnavailable()
    {
        var opt = CardOption(BazaarAgentActionKind.SelectItem, "c1", canSelect: false);
        var snap = MakeSnap(1, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.SelectItem,
            CardInstanceId = "c1",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.Equal("option marked CanSelect=false", result.Details);
    }

    [Fact]
    public void Rule5_SelectItem_CanAffordFalse_RejectsStaleOrUnavailable()
    {
        var opt = CardOption(BazaarAgentActionKind.SelectItem, "c1", canAfford: false);
        var snap = MakeSnap(1, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.SelectItem,
            CardInstanceId = "c1",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.Equal("option not affordable", result.Details);
    }

    [Fact]
    public void Rule5_SelectItem_CanFitFalse_RejectsStaleOrUnavailable()
    {
        var opt = CardOption(BazaarAgentActionKind.SelectItem, "c1", canFit: false);
        var snap = MakeSnap(1, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.SelectItem,
            CardInstanceId = "c1",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.Equal("option does not fit", result.Details);
    }

    // ── Rule 6: CanSell == true ───────────────────────────────────────────────

    [Fact]
    public void Rule6_SellItem_CanSellTrue_Passes()
    {
        var opt = CardOption(BazaarAgentActionKind.SellItem, "c1", canSell: true);
        var snap = MakeSnap(1, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.SellItem,
            CardInstanceId = "c1",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule6_SellItem_CanSellNull_RejectsStaleOrUnavailable()
    {
        var opt = CardOption(BazaarAgentActionKind.SellItem, "c1", canSell: null);
        var snap = MakeSnap(1, opt);
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.SellItem,
            CardInstanceId = "c1",
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.Equal("option not sellable", result.Details);
    }

    // ── Rule 7: forTickId match ───────────────────────────────────────────────

    [Fact]
    public void Rule7_ForTickId_Matches_Passes()
    {
        var snap = MakeSnap(7, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.Reroll,
            ForTickId = 7UL,
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule7_ForTickId_Mismatch_RejectsStaleOrUnavailable()
    {
        var snap = MakeSnap(9, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.Reroll,
            ForTickId = 5UL,
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.Equal("stale tickId", result.Details);
        Assert.NotNull(result.Extra);
        Assert.Equal(9UL, result.Extra["currentTickId"]);
    }

    [Fact]
    public void Rule7_ForTickId_Null_Passes()
    {
        var snap = MakeSnap(9, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction
        {
            ActionKind = BazaarAgentActionKind.Reroll,
            ForTickId = null,
        };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    // ── Rule 8: cooldown (Wait exempt) ───────────────────────────────────────

    [Fact]
    public void Rule8_Cooldown_Zero_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Reroll };
        var result = BazaarAgentActionValidator.Validate(snap, action, cooldownRemainingSeconds: 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Rule8_Cooldown_Positive_RejectsCooldown()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Reroll));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Reroll };
        var result = BazaarAgentActionValidator.Validate(
            snap,
            action,
            cooldownRemainingSeconds: 1.5
        );
        Assert.Equal(BazaarAgentValidationCode.Cooldown, result.Code);
        Assert.Equal(429, result.HttpStatus);
        Assert.Equal("action min-delay not yet elapsed", result.Details);
        Assert.NotNull(result.Extra);
        Assert.Equal(1.5, result.Extra["retryAfterSeconds"]);
    }

    [Fact]
    public void Rule8_Wait_ExemptFromCooldown()
    {
        var snap = MakeSnap(1);
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Wait };
        var result = BazaarAgentActionValidator.Validate(
            snap,
            action,
            cooldownRemainingSeconds: 99.9
        );
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    // ── Rule ordering: first failure short-circuits ───────────────────────────

    [Fact]
    public void RuleOrder_Rule1BeforeRule8_UnknownKindBeforeCooldown()
    {
        // Unknown kind + active cooldown — must be Rule1 (Invalid/400) not Rule8
        var snap = MakeSnap();
        var action = new BazaarAgentAction { ActionKind = (BazaarAgentActionKind)999 };
        var result = BazaarAgentActionValidator.Validate(snap, action, cooldownRemainingSeconds: 5);
        Assert.Equal(BazaarAgentValidationCode.Invalid, result.Code);
        Assert.Equal(400, result.HttpStatus);
    }

    // ── ReturnToMenu (end-of-run advance) ─────────────────────────────────────

    [Fact]
    public void ReturnToMenu_InAvailableActions_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.ReturnToMenu));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.ReturnToMenu };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void ReturnToMenu_NotInAvailableActions_RejectsStaleOrUnavailable()
    {
        var snap = MakeSnap(4, SimpleOption(BazaarAgentActionKind.Wait));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.ReturnToMenu };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
        Assert.NotNull(result.Extra);
        Assert.Equal(4UL, result.Extra["currentTickId"]);
    }

    [Fact]
    public void ReturnToMenu_NonWait_SubjectToCooldown()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.ReturnToMenu));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.ReturnToMenu };
        var result = BazaarAgentActionValidator.Validate(
            snap,
            action,
            cooldownRemainingSeconds: 0.5
        );
        Assert.Equal(BazaarAgentValidationCode.Cooldown, result.Code);
        Assert.Equal(429, result.HttpStatus);
    }

    // ── Continue (replay advance) ─────────────────────────────────────────────

    [Fact]
    public void Continue_InAvailableActions_Passes()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Continue));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Continue };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.Ok, result.Code);
    }

    [Fact]
    public void Continue_NotInAvailableActions_RejectsStale()
    {
        var snap = MakeSnap(1, SimpleOption(BazaarAgentActionKind.Wait));
        var action = new BazaarAgentAction { ActionKind = BazaarAgentActionKind.Continue };
        var result = BazaarAgentActionValidator.Validate(snap, action, 0);
        Assert.Equal(BazaarAgentValidationCode.StaleOrUnavailable, result.Code);
        Assert.Equal(409, result.HttpStatus);
    }
}
