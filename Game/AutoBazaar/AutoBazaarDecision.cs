#nullable enable
using System.Collections.Generic;

// Polyfill for netstandard2.1 (init support)
#pragma warning disable CS8356
namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit { }
}
#pragma warning restore CS8356

namespace BazaarPlusPlus.Game.AutoBazaar
{
    internal enum AutoBazaarActionKind
    {
        Wait,
        StartOrContinueRun,
        AbandonRun,
        SelectItem,
        SelectSkill,
        SelectEncounter,
        CommitToPedestal,
        MoveItem,
        SellItem,
        Reroll,
        ExitState,
        AdvanceEndRun,
    }

    internal enum AutoBazaarActionGroup
    {
        Wait,
        Flow,
        Offer,
        Route,
        Pedestal,
        Move,
        Sell,
        Reroll,
        Exit,
        UiFlow,
    }

    internal enum AutoBazaarRunStateName
    {
        Unknown,
        StartRun,
        Choice,
        Encounter,
        Combat,
        PvpCombat,
        Replay,
        LevelUp,
        Loot,
        Pedestal,
        EndRunVictory,
        EndRunDefeat,
    }

    internal enum AutoBazaarCardKind
    {
        Item,
        Skill,
        Encounter,
        Unknown,
    }

    internal enum AutoBazaarCardLocation
    {
        Selection,
        Board,
        Chest,
        Skill,
        Unknown,
    }

    internal enum AutoBazaarTargetSection
    {
        Hand,
        Stash,
        Skill,
        Fuse,
    }

    internal sealed class AutoBazaarCardSnapshot
    {
        public string InstanceId { get; init; } = "";
        public AutoBazaarCardKind Kind { get; init; }
        public string? TemplateId { get; init; }
        public string? DisplayName { get; init; }
        public string? Tier { get; init; }
        public string? Size { get; init; }
        public string? SocketId { get; init; }
        public AutoBazaarCardLocation Location { get; init; }
        public int Order { get; init; }

        // Selection-only fields
        public int? BuyPrice { get; init; }
        public int? SellPrice { get; init; }
        public bool? CanAfford { get; init; }
        public bool? CanFit { get; init; }
        public bool? CanSelect { get; init; }
        public bool? IsFree { get; init; }
        public AutoBazaarTargetSection? TargetSection { get; init; }
        public string? TargetSockets { get; init; }
        public string? UnavailableReason { get; init; }

        // Owned (Board/Chest/Skill)
        public bool? CanSell { get; init; }
    }

    internal sealed class AutoBazaarDecisionOption
    {
        public AutoBazaarActionKind ActionKind { get; init; }
        public AutoBazaarActionGroup Group { get; init; }
        public string DisplayKey { get; init; } = "";
        public string? CardInstanceId { get; init; }
        public AutoBazaarTargetSection? TargetSection { get; init; }
        public IReadOnlyList<string>? TargetSockets { get; init; }
        public AutoBazaarCardSnapshot? Card { get; init; }
    }

    internal sealed class AutoBazaarContext
    {
        public string SchemaVersion { get; init; } = "1.0.0";
        public ulong TickId { get; init; }
        public string ServerTimeUtc { get; init; } = "";

        public bool IsEnabled { get; init; }
        public bool IsInRun { get; init; }
        public bool HasActiveRun { get; init; }
        public bool CanStartOrContinueRun { get; init; }
        public bool IsClientBusy { get; init; }

        public string? RunId { get; init; }
        public AutoBazaarRunStateName StateName { get; init; }
        public int PlayerGold { get; init; }
        public bool SelectionIsFree { get; init; }
        public bool CanExit { get; init; }
        public bool CanReroll { get; init; }
        public int RerollCost { get; init; }
        public int RerollsRemaining { get; init; }
        public string? CurrentEncounterId { get; init; }
        public double ActionCooldownRemainingSeconds { get; init; }

        public IReadOnlyList<AutoBazaarCardSnapshot> BoardItems { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
        public IReadOnlyList<AutoBazaarCardSnapshot> ChestItems { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
        public IReadOnlyList<AutoBazaarCardSnapshot> PlayerSkills { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
        public IReadOnlyList<AutoBazaarCardSnapshot> SellableItems { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
        public IReadOnlyList<AutoBazaarCardSnapshot> SelectionOptions { get; init; } = System.Array.Empty<AutoBazaarCardSnapshot>();
        public IReadOnlyList<AutoBazaarDecisionOption> AvailableActions { get; init; } = System.Array.Empty<AutoBazaarDecisionOption>();
    }

    internal sealed class AutoBazaarAction
    {
        public string? SchemaVersion { get; set; }
        public AutoBazaarActionKind ActionKind { get; set; }
        public string? CardInstanceId { get; set; }
        public AutoBazaarTargetSection? TargetSection { get; set; }
        public IReadOnlyList<string>? TargetSockets { get; set; }
        public string? Hero { get; set; }
        public string? PlayMode { get; set; }
        public string? Reason { get; set; }
        public ulong? ForTickId { get; set; }
    }
}
