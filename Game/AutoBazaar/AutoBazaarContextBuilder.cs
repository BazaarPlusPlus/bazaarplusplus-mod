#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using BazaarGameClient.Domain.Cards;
using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;

namespace BazaarPlusPlus.Game.AutoBazaar;

/// <summary>
/// Reads live game state and produces an <see cref="AutoBazaarContext"/> snapshot.
/// Main thread only. Pure read — never mutates any state.
/// </summary>
internal static class AutoBazaarContextBuilder
{
    /// <summary>
    /// Build a context from live game state. Never throws — exceptions produce a
    /// degenerate context with <c>StateName=Unknown</c> and <c>AvailableActions=[Wait]</c>.
    /// </summary>
    public static AutoBazaarContext Build(IBppServices services, double actionCooldownRemainingSeconds)
    {
        bool isEnabled = services.Config.AutoBazaarEnabled?.Value == true;

        if (!isEnabled)
        {
            return MakeDegenerate(isEnabled: false, actionCooldownRemainingSeconds);
        }

        try
        {
            return BuildCore(services, isEnabled, actionCooldownRemainingSeconds);
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", "context build failed", ex);
            return MakeDegenerate(isEnabled: true, actionCooldownRemainingSeconds);
        }
    }

    // -------------------------------------------------------------------------
    // Core builder
    // -------------------------------------------------------------------------

    private static AutoBazaarContext BuildCore(IBppServices services, bool isEnabled, double actionCooldownRemainingSeconds)
    {
        var appState = AppState.CurrentState;
        var runState = Data.CurrentState;
        var run = Data.Run;

        // Determine state name
        var stateName = ResolveStateName(appState, runState);

        // If game isn't ready (no active AppState), the only meaningful action the
        // mod can offer is StartOrContinueRun — but only when actually at hero-select.
        if (appState == null || stateName == AutoBazaarRunStateName.Unknown)
        {
            bool canStartEarly = AutoBazaarSceneProbe.IsAtHeroSelectAndReadyForNewRun();
            var lobbyActions = canStartEarly
                ? new[] { WaitOption(), StartOrContinueRunOption() }
                : new[] { WaitOption() };
            return new AutoBazaarContext
            {
                SchemaVersion = "1.0.0",
                ServerTimeUtc = UtcNow(),
                IsEnabled = isEnabled,
                StateName = AutoBazaarRunStateName.Unknown,
                CanStartOrContinueRun = canStartEarly,
                ActionCooldownRemainingSeconds = actionCooldownRemainingSeconds,
                AvailableActions = lobbyActions,
            };
        }

        // AllowedOps — use the public accessor
        bool canHandleOp(StateOps op) => appState.CanHandleOperation(op);

        bool isInRun = run != null && appState is RunAppState;
        bool hasActiveRun = Data.HasActiveRun;
        // CanStartOrContinueRun: true on the hero-select scene with profile loaded
        // and no active AppState — see AutoBazaarSceneProbe for the conservative check.
        bool canStartOrContinueRun = AutoBazaarSceneProbe.IsAtHeroSelectAndReadyForNewRun();

        string? runId = null;
        if (run != null && run.GameModeId != default(Guid))
            runId = run.GameModeId.ToString("D");

        // Player gold via attribute system
        int playerGold = run?.Player?.GetAttributeValue(EPlayerAttributeType.Gold) ?? 0;

        bool selectionIsFree = runState?.SelectionContextRules?.SelectionIsFree ?? false;
        bool canExit = runState?.SelectionContextRules?.CanExit ?? false;

        int rerollCost = (int)(runState?.RerollCost ?? 0u);
        int rerollsRemaining = (int)(runState?.RerollsRemaining ?? 0u);

        bool canReroll = canHandleOp(StateOps.Reroll) && rerollsRemaining > 0 && playerGold >= rerollCost;

        string? currentEncounterId = runState?.CurrentEncounterId;

        // --- Card inventories ---
        bool canSell = canHandleOp(StateOps.SellItem);
        bool canMove = canHandleOp(StateOps.MoveItem);

        var boardItems = BuildBoardCards(run, playerGold, canSell, AutoBazaarCardLocation.Board);
        var chestItems = BuildBoardCards(run, playerGold, canSell, AutoBazaarCardLocation.Chest);
        var playerSkills = BuildSkillCards(run, canSell);

        var sellableItems = BuildSellableItems(boardItems, chestItems, playerSkills, canSell);

        // Selection set
        List<AutoBazaarCardSnapshot> selectionOptions = BuildSelectionOptions(
            runState, playerGold, selectionIsFree, run, canHandleOp(StateOps.SelectItem));

        // Target-selection mode (upgrade/enchant): when AppState._iteractionFilter
        // is non-empty, the game restricts SelectItem to owned cards whose
        // templateId is in the filter. Offer-based clicks silently no-op.
        var interactionFilterList = AutoBazaarInteractionFilterProbe.ReadCurrentFilter();
        ISet<string>? interactionFilter = interactionFilterList.Count > 0
            ? new HashSet<string>(interactionFilterList)
            : null;

        // Available actions
        var actions = BuildActions(
            stateName, isInRun, canHandleOp,
            canReroll, canStartOrContinueRun,
            runState, selectionOptions, boardItems, chestItems, playerSkills, canMove, canSell,
            run);

        if (interactionFilter is not null)
        {
            actions = ReplaceSelectItemWithTargetSelection(actions, interactionFilter,
                boardItems, chestItems, playerSkills, selectionOptions);
        }

        return new AutoBazaarContext
        {
            SchemaVersion = "1.0.0",
            TickId = 0,
            ServerTimeUtc = UtcNow(),
            IsEnabled = isEnabled,
            IsInRun = isInRun,
            HasActiveRun = hasActiveRun,
            CanStartOrContinueRun = canStartOrContinueRun,
            IsClientBusy = false, // TODO v2: track HttpGameClient busy state
            RunId = runId,
            StateName = stateName,
            PlayerGold = playerGold,
            SelectionIsFree = selectionIsFree,
            CanExit = canExit,
            CanReroll = canReroll,
            RerollCost = rerollCost,
            RerollsRemaining = rerollsRemaining,
            CurrentEncounterId = currentEncounterId,
            ActionCooldownRemainingSeconds = actionCooldownRemainingSeconds,
            InteractableTemplateIds = interactionFilter is not null ? interactionFilterList : null,
            BoardItems = boardItems,
            ChestItems = chestItems,
            PlayerSkills = playerSkills,
            SellableItems = sellableItems,
            SelectionOptions = selectionOptions,
            AvailableActions = actions,
        };
    }

    // -------------------------------------------------------------------------
    // State name resolution
    // -------------------------------------------------------------------------

    private static AutoBazaarRunStateName ResolveStateName(AppState? appState, RunState? runState)
    {
        if (appState is StartRunAppState) return AutoBazaarRunStateName.StartRun;
        if (appState is ReplayState) return AutoBazaarRunStateName.Replay;

        if (runState == null) return AutoBazaarRunStateName.Unknown;

        return runState.StateName switch
        {
            ERunState.Choice       => AutoBazaarRunStateName.Choice,
            ERunState.Encounter    => AutoBazaarRunStateName.Encounter,
            ERunState.Combat       => AutoBazaarRunStateName.Combat,
            ERunState.LevelUp      => AutoBazaarRunStateName.LevelUp,
            ERunState.Loot         => AutoBazaarRunStateName.Loot,
            ERunState.Pedestal     => AutoBazaarRunStateName.Pedestal,
            ERunState.PVPCombat    => AutoBazaarRunStateName.PvpCombat,
            ERunState.EndRunVictory => AutoBazaarRunStateName.EndRunVictory,
            ERunState.EndRunDefeat => AutoBazaarRunStateName.EndRunDefeat,
            _ => AutoBazaarRunStateName.Unknown,
        };
    }

    // -------------------------------------------------------------------------
    // Card snapshot builders
    // -------------------------------------------------------------------------

    private static IReadOnlyList<AutoBazaarCardSnapshot> BuildBoardCards(
        Run? run, int playerGold, bool canSell, AutoBazaarCardLocation location)
    {
        if (run?.Player == null) return Array.Empty<AutoBazaarCardSnapshot>();

        var inventory = location == AutoBazaarCardLocation.Board
            ? run.Player.Hand
            : run.Player.Stash;

        if (inventory == null) return Array.Empty<AutoBazaarCardSnapshot>();

        var result = new List<AutoBazaarCardSnapshot>();
        int order = 0;

        var container = (inventory as CardContainer)?.Container;
        if (container == null) return Array.Empty<AutoBazaarCardSnapshot>();

        foreach (var (socketable, socketId) in container.GetCardsAndSockets())
        {
            if (socketable is not ItemCard card) continue;

            int? sellPrice = card.GetAttributeValue(ECardAttributeType.SellPrice);
            result.Add(new AutoBazaarCardSnapshot
            {
                InstanceId = card.InstanceId.Value ?? "",
                Kind = AutoBazaarCardKind.Item,
                TemplateId = card.TemplateId.ToString("D"),
                DisplayName = card.Name,
                Tier = card.Tier.ToString(),
                Size = card.Size.ToString(),
                SocketId = socketId.ToString(),
                Location = location,
                Order = order++,
                SellPrice = sellPrice,
                CanSell = canSell && !card.HiddenTags.Contains(EHiddenTag.Unsellable),
            });
        }

        return result;
    }

    private static IReadOnlyList<AutoBazaarCardSnapshot> BuildSkillCards(Run? run, bool canSell)
    {
        if (run?.Player?.Skills == null) return Array.Empty<AutoBazaarCardSnapshot>();

        var result = new List<AutoBazaarCardSnapshot>();
        int order = 0;

        foreach (var skill in run.Player.Skills)
        {
            int? sellPrice = skill.GetAttributeValue(ECardAttributeType.SellPrice);
            result.Add(new AutoBazaarCardSnapshot
            {
                InstanceId = skill.InstanceId.Value ?? "",
                Kind = AutoBazaarCardKind.Skill,
                TemplateId = skill.TemplateId.ToString("D"),
                DisplayName = skill.Name,
                Tier = skill.Tier.ToString(),
                Size = skill.Size.ToString(),
                SocketId = null,
                Location = AutoBazaarCardLocation.Skill,
                Order = order++,
                SellPrice = sellPrice,
                CanSell = canSell && !skill.HiddenTags.Contains(EHiddenTag.Unsellable),
            });
        }

        return result;
    }

    private static IReadOnlyList<AutoBazaarCardSnapshot> BuildSellableItems(
        IReadOnlyList<AutoBazaarCardSnapshot> boardItems,
        IReadOnlyList<AutoBazaarCardSnapshot> chestItems,
        IReadOnlyList<AutoBazaarCardSnapshot> playerSkills,
        bool canSell)
    {
        if (!canSell) return Array.Empty<AutoBazaarCardSnapshot>();

        var result = new List<AutoBazaarCardSnapshot>();
        foreach (var c in boardItems) if (c.CanSell == true) result.Add(c);
        foreach (var c in chestItems) if (c.CanSell == true) result.Add(c);
        foreach (var c in playerSkills) if (c.CanSell == true) result.Add(c);
        return result;
    }

    private static List<AutoBazaarCardSnapshot> BuildSelectionOptions(
        RunState? runState, int playerGold, bool selectionIsFree, Run? run, bool canSelectItem)
    {
        var result = new List<AutoBazaarCardSnapshot>();
        if (runState?.SelectionSet == null) return result;

        var handContainer = (run?.Player?.Hand as CardContainer)?.Container;
        var stashContainer = (run?.Player?.Stash as CardContainer)?.Container;

        // Compute occupied sockets for placement hints
        var occupiedHand = GetOccupiedAndLockedSockets(handContainer);
        var occupiedStash = GetOccupiedAndLockedSockets(stashContainer);

        int handCapacity = SocketedContainer.SocketCount;
        int stashCapacity = SocketedContainer.SocketCount;

        int order = 0;
        foreach (var entry in runState.SelectionSet)
        {
            var instanceId = InstanceId.TryParse(entry);
            if (!Data.Entities.TryGetValue(instanceId, out var card)) continue;

            AutoBazaarCardKind kind;
            if (card is ItemCard)
                kind = AutoBazaarCardKind.Item;
            else if (card is SkillCard)
                kind = AutoBazaarCardKind.Skill;
            else
                kind = AutoBazaarCardKind.Encounter; // EncounterCard or similar

            int? buyPrice = card.GetAttributeValue(ECardAttributeType.BuyPrice);
            int? sellPrice = card.GetAttributeValue(ECardAttributeType.SellPrice);
            bool canAfford = selectionIsFree || playerGold >= (buyPrice ?? 0);

            // Determine first legal placement for informational hint
            AutoBazaarTargetSection? targetSection = null;
            string? targetSockets = null;
            bool canFit = false;

            if (kind == AutoBazaarCardKind.Item)
            {
                int size = (int)card.Size;
                var handPlacements = AutoBazaarMoveTargetPlanner.Enumerate(size, handCapacity, occupiedHand);
                var stashPlacements = AutoBazaarMoveTargetPlanner.Enumerate(size, stashCapacity, occupiedStash);

                if (handPlacements.Count > 0)
                {
                    canFit = true;
                    targetSection = AutoBazaarTargetSection.Hand;
                    targetSockets = string.Join(",", handPlacements[0]);
                }
                else if (stashPlacements.Count > 0)
                {
                    canFit = true;
                    targetSection = AutoBazaarTargetSection.Stash;
                    targetSockets = string.Join(",", stashPlacements[0]);
                }
            }
            else if (kind == AutoBazaarCardKind.Skill)
            {
                canFit = true; // skill sockets handled separately
                targetSection = AutoBazaarTargetSection.Skill;
            }
            else
            {
                canFit = true; // encounters don't need a placement
            }

            result.Add(new AutoBazaarCardSnapshot
            {
                InstanceId = card.InstanceId.Value ?? "",
                Kind = kind,
                TemplateId = card.TemplateId.ToString("D"),
                DisplayName = card.Name,
                Tier = card.Tier.ToString(),
                Size = card.Size.ToString(),
                SocketId = null,
                Location = AutoBazaarCardLocation.Selection,
                Order = order++,
                BuyPrice = buyPrice,
                SellPrice = sellPrice,
                CanAfford = canAfford,
                CanFit = canFit,
                CanSelect = true,
                IsFree = selectionIsFree,
                TargetSection = targetSection,
                TargetSockets = targetSockets,
            });
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Available actions builder
    // -------------------------------------------------------------------------

    private static IReadOnlyList<AutoBazaarDecisionOption> BuildActions(
        AutoBazaarRunStateName stateName,
        bool isInRun,
        Func<StateOps, bool> canHandleOp,
        bool canReroll,
        bool canStartOrContinueRun,
        RunState? runState,
        List<AutoBazaarCardSnapshot> selectionOptions,
        IReadOnlyList<AutoBazaarCardSnapshot> boardItems,
        IReadOnlyList<AutoBazaarCardSnapshot> chestItems,
        IReadOnlyList<AutoBazaarCardSnapshot> playerSkills,
        bool canMove,
        bool canSell,
        Run? run)
    {
        var actions = new List<AutoBazaarDecisionOption>();

        // 1. Wait (always first)
        actions.Add(WaitOption());

        // 2. StartOrContinueRun
        if (canStartOrContinueRun)
        {
            actions.Add(new AutoBazaarDecisionOption
            {
                ActionKind = AutoBazaarActionKind.StartOrContinueRun,
                Group = AutoBazaarActionGroup.Flow,
                DisplayKey = "StartOrContinueRun",
            });
        }

        // 3. AbandonRun
        static bool isEndOrReplay(AutoBazaarRunStateName s) =>
            s is AutoBazaarRunStateName.Combat
            or AutoBazaarRunStateName.PvpCombat
            or AutoBazaarRunStateName.Replay
            or AutoBazaarRunStateName.EndRunVictory
            or AutoBazaarRunStateName.EndRunDefeat;

        if (isInRun && !isEndOrReplay(stateName) && canHandleOp(StateOps.AbandonRun))
        {
            actions.Add(new AutoBazaarDecisionOption
            {
                ActionKind = AutoBazaarActionKind.AbandonRun,
                Group = AutoBazaarActionGroup.Flow,
                DisplayKey = "AbandonRun",
            });
        }

        // 4. Reroll
        if (canReroll)
        {
            actions.Add(new AutoBazaarDecisionOption
            {
                ActionKind = AutoBazaarActionKind.Reroll,
                Group = AutoBazaarActionGroup.Reroll,
                DisplayKey = "Reroll",
            });
        }

        // 5. ExitState
        if (canHandleOp(StateOps.ExitState) && runState?.SelectionContextRules?.CanExit != false)
        {
            actions.Add(new AutoBazaarDecisionOption
            {
                ActionKind = AutoBazaarActionKind.ExitState,
                Group = AutoBazaarActionGroup.Exit,
                DisplayKey = "ExitState",
            });
        }

        // 6. AdvanceEndRun
        if (stateName is AutoBazaarRunStateName.EndRunVictory or AutoBazaarRunStateName.EndRunDefeat)
        {
            actions.Add(new AutoBazaarDecisionOption
            {
                ActionKind = AutoBazaarActionKind.AdvanceEndRun,
                Group = AutoBazaarActionGroup.UiFlow,
                DisplayKey = "AdvanceEndRun",
            });
        }

        // 7. SellItem — per-card
        if (canSell && canHandleOp(StateOps.SellItem))
        {
            foreach (var card in SellableSnapshotsFrom(boardItems, chestItems, playerSkills))
            {
                if (card.CanSell != true) continue;
                actions.Add(new AutoBazaarDecisionOption
                {
                    ActionKind = AutoBazaarActionKind.SellItem,
                    Group = AutoBazaarActionGroup.Sell,
                    DisplayKey = $"SellItem:{card.InstanceId}",
                    CardInstanceId = card.InstanceId,
                    Card = card,
                });
            }
        }

        // 8. MoveItem — per-card per-placement (board + chest)
        if (canMove && canHandleOp(StateOps.MoveItem) && run?.Player != null)
        {
            var handContainer = (run.Player.Hand as CardContainer)?.Container;
            var stashContainer = (run.Player.Stash as CardContainer)?.Container;
            int cap = SocketedContainer.SocketCount;

            var occupiedHand = GetOccupiedAndLockedSockets(handContainer);
            var occupiedStash = GetOccupiedAndLockedSockets(stashContainer);

            EmitMoveActions(actions, boardItems, handContainer, stashContainer,
                occupiedHand, occupiedStash, cap, isOwnHand: true);
            EmitMoveActions(actions, chestItems, handContainer, stashContainer,
                occupiedHand, occupiedStash, cap, isOwnHand: false);
        }

        // 9. SelectItem / SelectSkill / SelectEncounter — per offer
        foreach (var offer in selectionOptions)
        {
            if (offer.CanSelect == false) continue;

            if (offer.Kind == AutoBazaarCardKind.Item && canHandleOp(StateOps.SelectItem))
            {
                // Enumerate legal placements (same logic as in BuildSelectionOptions but authoritative)
                int size = ParseSize(offer.Size);
                var handContainer = (run?.Player?.Hand as CardContainer)?.Container;
                var stashContainer = (run?.Player?.Stash as CardContainer)?.Container;
                int cap = SocketedContainer.SocketCount;
                var occupiedHand = GetOccupiedAndLockedSockets(handContainer);
                var occupiedStash = GetOccupiedAndLockedSockets(stashContainer);

                foreach (var placement in AutoBazaarMoveTargetPlanner.Enumerate(size, cap, occupiedHand))
                {
                    var sockets = new List<string>(placement);
                    actions.Add(new AutoBazaarDecisionOption
                    {
                        ActionKind = AutoBazaarActionKind.SelectItem,
                        Group = AutoBazaarActionGroup.Offer,
                        DisplayKey = $"SelectItem:{offer.InstanceId}:Hand:{string.Join(",", sockets)}",
                        CardInstanceId = offer.InstanceId,
                        TargetSection = AutoBazaarTargetSection.Hand,
                        TargetSockets = sockets,
                        Card = offer,
                    });
                }
                foreach (var placement in AutoBazaarMoveTargetPlanner.Enumerate(size, cap, occupiedStash))
                {
                    var sockets = new List<string>(placement);
                    actions.Add(new AutoBazaarDecisionOption
                    {
                        ActionKind = AutoBazaarActionKind.SelectItem,
                        Group = AutoBazaarActionGroup.Offer,
                        DisplayKey = $"SelectItem:{offer.InstanceId}:Stash:{string.Join(",", sockets)}",
                        CardInstanceId = offer.InstanceId,
                        TargetSection = AutoBazaarTargetSection.Stash,
                        TargetSockets = sockets,
                        Card = offer,
                    });
                }
            }
            else if (offer.Kind == AutoBazaarCardKind.Skill && canHandleOp(StateOps.SelectSkill))
            {
                actions.Add(new AutoBazaarDecisionOption
                {
                    ActionKind = AutoBazaarActionKind.SelectSkill,
                    Group = AutoBazaarActionGroup.Offer,
                    DisplayKey = $"SelectSkill:{offer.InstanceId}",
                    CardInstanceId = offer.InstanceId,
                    Card = offer,
                });
            }
            else if (offer.Kind == AutoBazaarCardKind.Encounter && canHandleOp(StateOps.SelectEncounter))
            {
                actions.Add(new AutoBazaarDecisionOption
                {
                    ActionKind = AutoBazaarActionKind.SelectEncounter,
                    Group = AutoBazaarActionGroup.Offer,
                    DisplayKey = $"SelectEncounter:{offer.InstanceId}",
                    CardInstanceId = offer.InstanceId,
                    Card = offer,
                });
            }
        }

        // 10. CommitToPedestal — per owned item card that the active pedestal template
        // marks as a valid upgrade target. PedestalState.CanBeUpgraded(card) runs the
        // template's SelectionCriteria predicate; we only surface cards it accepts so
        // the external client never POSTs CommitToPedestal on an ineligible card
        // (DoDrop short-circuits on the same predicate, so emitting ineligibles would
        // produce silent server-side rejections).
        if (stateName == AutoBazaarRunStateName.Pedestal && canHandleOp(StateOps.CommitToPedestal))
        {
            var pedestalState = AppState.CurrentState as PedestalState;
            foreach (var card in boardItems)
            {
                if (!IsPedestalEligible(pedestalState, card.InstanceId)) continue;
                actions.Add(new AutoBazaarDecisionOption
                {
                    ActionKind = AutoBazaarActionKind.CommitToPedestal,
                    Group = AutoBazaarActionGroup.Pedestal,
                    DisplayKey = $"CommitToPedestal:{card.InstanceId}",
                    CardInstanceId = card.InstanceId,
                    Card = card,
                });
            }
            foreach (var card in chestItems)
            {
                if (!IsPedestalEligible(pedestalState, card.InstanceId)) continue;
                actions.Add(new AutoBazaarDecisionOption
                {
                    ActionKind = AutoBazaarActionKind.CommitToPedestal,
                    Group = AutoBazaarActionGroup.Pedestal,
                    DisplayKey = $"CommitToPedestal:{card.InstanceId}",
                    CardInstanceId = card.InstanceId,
                    Card = card,
                });
            }
        }

        return actions;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void EmitMoveActions(
        List<AutoBazaarDecisionOption> actions,
        IReadOnlyList<AutoBazaarCardSnapshot> cards,
        SocketedContainer? handContainer,
        SocketedContainer? stashContainer,
        ISet<int> occupiedHand,
        ISet<int> occupiedStash,
        int cap,
        bool isOwnHand)
    {
        foreach (var card in cards)
        {
            int size = ParseSize(card.Size);

            // Hand placements
            var ownLeftSocket = isOwnHand ? ParseSocketIndex(card.SocketId) : -1;
            var handPlacements = AutoBazaarMoveTargetPlanner.Enumerate(
                size, cap, occupiedHand,
                excludeStartIndexInclusive: isOwnHand ? ownLeftSocket : -1,
                excludeCountInclusive: isOwnHand ? size : 0);

            foreach (var placement in handPlacements)
            {
                var sockets = new List<string>(placement);
                actions.Add(new AutoBazaarDecisionOption
                {
                    ActionKind = AutoBazaarActionKind.MoveItem,
                    Group = AutoBazaarActionGroup.Move,
                    DisplayKey = $"MoveItem:{card.InstanceId}:Hand:{string.Join(",", sockets)}",
                    CardInstanceId = card.InstanceId,
                    TargetSection = AutoBazaarTargetSection.Hand,
                    TargetSockets = sockets,
                    Card = card,
                });
            }

            // Stash placements
            var ownStashLeftSocket = !isOwnHand ? ParseSocketIndex(card.SocketId) : -1;
            var stashPlacements = AutoBazaarMoveTargetPlanner.Enumerate(
                size, cap, occupiedStash,
                excludeStartIndexInclusive: !isOwnHand ? ownStashLeftSocket : -1,
                excludeCountInclusive: !isOwnHand ? size : 0);

            foreach (var placement in stashPlacements)
            {
                var sockets = new List<string>(placement);
                actions.Add(new AutoBazaarDecisionOption
                {
                    ActionKind = AutoBazaarActionKind.MoveItem,
                    Group = AutoBazaarActionGroup.Move,
                    DisplayKey = $"MoveItem:{card.InstanceId}:Stash:{string.Join(",", sockets)}",
                    CardInstanceId = card.InstanceId,
                    TargetSection = AutoBazaarTargetSection.Stash,
                    TargetSockets = sockets,
                    Card = card,
                });
            }
        }
    }

    private static HashSet<int> GetOccupiedAndLockedSockets(SocketedContainer? container)
    {
        var result = new HashSet<int>();
        if (container == null) return result;

        for (int i = 0; i < container.Sockets.Length; i++)
        {
            if (container.Sockets[i] != null || container.IsSocketLocked(i))
                result.Add(i);
        }

        return result;
    }

    private static IEnumerable<AutoBazaarCardSnapshot> SellableSnapshotsFrom(
        IReadOnlyList<AutoBazaarCardSnapshot> board,
        IReadOnlyList<AutoBazaarCardSnapshot> chest,
        IReadOnlyList<AutoBazaarCardSnapshot> skills)
    {
        foreach (var c in board) yield return c;
        foreach (var c in chest) yield return c;
        foreach (var c in skills) yield return c;
    }

    private static int ParseSize(string? size)
    {
        return size switch
        {
            "Small" => 1,
            "Medium" => 2,
            "Large" => 3,
            _ => 0,
        };
    }

    private static int ParseSocketIndex(string? socketId)
    {
        // e.g. "Socket_3" → 3
        if (socketId == null) return -1;
        var idx = socketId.LastIndexOf('_');
        if (idx < 0 || idx + 1 >= socketId.Length) return -1;
        if (int.TryParse(socketId.AsSpan(idx + 1), out var n)) return n;
        return -1;
    }

    private static AutoBazaarDecisionOption WaitOption() => new()
    {
        ActionKind = AutoBazaarActionKind.Wait,
        Group = AutoBazaarActionGroup.Wait,
        DisplayKey = "Wait",
    };

    private static AutoBazaarDecisionOption StartOrContinueRunOption() => new()
    {
        ActionKind = AutoBazaarActionKind.StartOrContinueRun,
        Group = AutoBazaarActionGroup.Flow,
        DisplayKey = "StartOrContinueRun",
    };

    /// <summary>Target-selection mode (filter non-empty): keep only the SelectItem
    /// options whose underlying templateId is in the filter — these are the only
    /// clicks the game accepts (other clicks fail CanInteractWithCard silently).
    /// Add owned-card SelectItem options for any owned card whose templateId is in
    /// the filter (covers BuySpecificCardCondition's _canInteractWithOwnedCards=true
    /// variant). Card-bearing actions for other ActionKinds pass through unchanged.</summary>
    private static IReadOnlyList<AutoBazaarDecisionOption> ReplaceSelectItemWithTargetSelection(
        IReadOnlyList<AutoBazaarDecisionOption> actions,
        ISet<string> filter,
        IReadOnlyList<AutoBazaarCardSnapshot> boardItems,
        IReadOnlyList<AutoBazaarCardSnapshot> chestItems,
        IReadOnlyList<AutoBazaarCardSnapshot> playerSkills,
        IReadOnlyList<AutoBazaarCardSnapshot> selectionOptionsCards)
    {
        // Build cardInstanceId → templateId lookup across every snapshot (offers + owned).
        var templateByInstance = new Dictionary<string, string>();
        AddTemplates(templateByInstance, boardItems);
        AddTemplates(templateByInstance, chestItems);
        AddTemplates(templateByInstance, playerSkills);
        AddTemplates(templateByInstance, selectionOptionsCards);

        var kept = new List<AutoBazaarDecisionOption>(actions.Count);
        var keptInstanceIds = new HashSet<string>();
        foreach (var a in actions)
        {
            if (a.ActionKind != AutoBazaarActionKind.SelectItem)
            {
                kept.Add(a);
                continue;
            }
            if (a.CardInstanceId is null) continue;
            if (!templateByInstance.TryGetValue(a.CardInstanceId, out var tid)) continue;
            if (!filter.Contains(tid)) continue;
            kept.Add(a);
            keptInstanceIds.Add(a.CardInstanceId);
        }

        // Owned-card variant for when filter explicitly enumerates owned templates.
        var owned = new List<AutoBazaarTargetSelectionActions.OwnedCardRef>();
        AddOwnedRefs(owned, boardItems, AutoBazaarTargetSection.Hand);
        AddOwnedRefs(owned, chestItems, AutoBazaarTargetSection.Stash);
        AddOwnedRefs(owned, playerSkills, AutoBazaarTargetSection.Skill);

        var targetOpts = AutoBazaarTargetSelectionActions.Emit(filter, owned);
        foreach (var opt in targetOpts)
        {
            if (opt.CardInstanceId is null) continue;
            if (!keptInstanceIds.Add(opt.CardInstanceId)) continue;
            kept.Add(opt);
        }
        return kept;
    }

    private static void AddTemplates(
        Dictionary<string, string> sink,
        IReadOnlyList<AutoBazaarCardSnapshot> cards)
    {
        foreach (var c in cards)
        {
            if (string.IsNullOrEmpty(c.InstanceId) || string.IsNullOrEmpty(c.TemplateId)) continue;
            sink[c.InstanceId] = c.TemplateId!;
        }
    }

    private static void AddOwnedRefs(
        List<AutoBazaarTargetSelectionActions.OwnedCardRef> sink,
        IReadOnlyList<AutoBazaarCardSnapshot> cards,
        AutoBazaarTargetSection section)
    {
        foreach (var c in cards)
        {
            if (string.IsNullOrEmpty(c.TemplateId)) continue;
            int size = ParseCardSize(c.Size);
            sink.Add(new AutoBazaarTargetSelectionActions.OwnedCardRef(
                InstanceId: c.InstanceId,
                TemplateId: c.TemplateId!,
                Section: section,
                LeftSocketId: c.SocketId ?? "",
                Size: size));
        }
    }

    private static int ParseCardSize(string? size)
    {
        return size switch
        {
            "Small" => 1,
            "Medium" => 2,
            "Large" => 3,
            _ => 1,
        };
    }

    /// <summary>Resolves the live <c>Card</c> object from a snapshot instanceId and
    /// asks the active <see cref="PedestalState"/> whether it's a legal upgrade target.
    /// Returns false on any failure so the action list stays conservative.</summary>
    private static bool IsPedestalEligible(PedestalState? pedestalState, string? instanceId)
    {
        if (pedestalState is null || string.IsNullOrEmpty(instanceId)) return false;
        try
        {
            if (!Data.Entities.TryGetValue(new InstanceId(instanceId!), out var entity)) return false;
            if (entity is not Card card) return false;
            return pedestalState.CanBeUpgraded(card);
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", $"IsPedestalEligible threw for {instanceId}", ex);
            return false;
        }
    }

    private static AutoBazaarContext MakeDegenerate(bool isEnabled, double cooldown) => new()
    {
        SchemaVersion = "1.0.0",
        ServerTimeUtc = UtcNow(),
        IsEnabled = isEnabled,
        StateName = AutoBazaarRunStateName.Unknown,
        ActionCooldownRemainingSeconds = cooldown,
        AvailableActions = new[] { WaitOption() },
    };

    private static string UtcNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
