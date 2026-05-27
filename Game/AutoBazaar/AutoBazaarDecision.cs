#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal static class AutoBazaarSchema
{
    public const string Version = "1.2.0";
}

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
    public string? Type { get; init; }
    public string? TemplateId { get; init; }
    public string? DisplayName { get; init; }
    public string? Tier { get; init; }
    public string? Size { get; init; }
    public string? Enchantment { get; init; }
    public string? SocketId { get; init; }
    public AutoBazaarCardLocation Location { get; init; }
    public int Order { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<string> HiddenTags { get; init; } = System.Array.Empty<string>();
    public IReadOnlyDictionary<string, int> Attributes { get; init; } =
        new Dictionary<string, int>();
    public IReadOnlyList<AutoBazaarCardAbilitySnapshot> ActiveAbilities { get; init; } =
        System.Array.Empty<AutoBazaarCardAbilitySnapshot>();

    // Selection-only fields
    public int? BuyPrice { get; init; }
    public int? SellPrice { get; init; }
    public bool? CanAfford { get; init; }
    public bool? CanFit { get; init; }
    public bool? CanSelect { get; init; }
    public bool? IsFree { get; init; }
    public AutoBazaarTargetSection? TargetSection { get; init; }

    /// <summary>Comma-joined informational hint (e.g. "Socket_2,Socket_3") for human/UI display. Not the authoritative dispatch input — clients must POST <see cref="AutoBazaarAction.TargetSockets"/> sourced from <see cref="AutoBazaarDecisionOption.TargetSockets"/>.</summary>
    public string? TargetSockets { get; init; }
    public string? UnavailableReason { get; init; }

    // Owned (Board/Chest/Skill)
    public bool? CanSell { get; init; }
}

internal sealed class AutoBazaarCardAbilitySnapshot
{
    public string Id { get; init; } = "";
    public string? InternalName { get; init; }
    public string? InternalDescription { get; init; }
    public string? Trigger { get; init; }
    public string? Action { get; init; }
    public string? ActiveIn { get; init; }
    public string? WorksIn { get; init; }
    public string? Priority { get; init; }
}

internal sealed class AutoBazaarDecisionOption
{
    public AutoBazaarActionKind ActionKind { get; init; }
    public AutoBazaarActionGroup Group { get; init; }
    public string DisplayKey { get; init; } = "";
    public string? CardInstanceId { get; init; }
    public AutoBazaarTargetSection? TargetSection { get; init; }

    /// <summary>Structured socket list the validator and dispatcher use to match a POST body. Order-sensitive. Clients pick a triplet `(CardInstanceId, TargetSection, TargetSockets)` from this list verbatim.</summary>
    public IReadOnlyList<string>? TargetSockets { get; init; }
    public AutoBazaarCardSnapshot? Card { get; init; }
}

internal sealed class AutoBazaarContext
{
    public string SchemaVersion { get; init; } = AutoBazaarSchema.Version;
    public ulong TickId { get; init; }
    public string ServerTimeUtc { get; init; } = "";

    public bool IsEnabled { get; init; }
    public bool IsInRun { get; init; }
    public bool HasActiveRun { get; init; }
    public bool CanStartOrContinueRun { get; init; }
    public bool IsClientBusy { get; init; }

    public string? RunId { get; init; }
    public AutoBazaarRunStateName StateName { get; init; }
    public string? PlayerHero { get; init; }
    public int? Day { get; init; }
    public int? Hour { get; init; }
    public int? Wins { get; init; }
    public int? Losses { get; init; }
    public int PlayerGold { get; init; }
    public int? PlayerIncome { get; init; }
    public int? PlayerHealth { get; init; }
    public int? PlayerMaxHealth { get; init; }
    public int? PlayerPrestige { get; init; }
    public int? PlayerLevel { get; init; }
    public bool SelectionIsFree { get; init; }
    public bool CanExit { get; init; }
    public bool CanReroll { get; init; }
    public int RerollCost { get; init; }
    public int RerollsRemaining { get; init; }
    public string? CurrentEncounterId { get; init; }
    public string? CurrentEncounterType { get; init; }
    public double ActionCooldownRemainingSeconds { get; init; }

    /// <summary>Template IDs the game currently restricts player clicks to (target-selection mode: upgrade, enchant). Null/empty means no filter is active. When non-empty, only owned board/chest item cards whose templateId is in this set accept a SelectItem POST; offer-based SelectItem actions are suppressed from <see cref="AvailableActions"/>.</summary>
    public IReadOnlyList<string>? InteractableTemplateIds { get; init; }

    public IReadOnlyList<AutoBazaarCardSnapshot> BoardItems { get; init; } =
        System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarCardSnapshot> ChestItems { get; init; } =
        System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarCardSnapshot> PlayerSkills { get; init; } =
        System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarCardSnapshot> SellableItems { get; init; } =
        System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarCardSnapshot> SelectionOptions { get; init; } =
        System.Array.Empty<AutoBazaarCardSnapshot>();
    public IReadOnlyList<AutoBazaarDecisionOption> AvailableActions { get; init; } =
        System.Array.Empty<AutoBazaarDecisionOption>();
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
