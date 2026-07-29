#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.GameInterop.Events;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// Captures only the two useful combat boundaries: the opening lineups and the terminal combat
/// attributes. Combat frames are deliberately never published to the agent.
/// </summary>
internal sealed class BazaarAgentCombatSummaryModule : IBppFeature, IBazaarAgentBattleSummarySource
{
    private readonly IBppEventBus _eventBus;
    private readonly object _gate = new();
    private IDisposable? _messageSubscription;
    private OpeningBattle? _opening;
    private BazaarAgentBattleSummarySnapshot? _completed;

    public BazaarAgentCombatSummaryModule(IBppEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public void Start() => _messageSubscription = _eventBus.Subscribe<NetMessageObserved>(Observe);

    public void Stop()
    {
        _messageSubscription?.Dispose();
        _messageSubscription = null;
        lock (_gate)
        {
            _opening = null;
            _completed = null;
        }
    }

    public BazaarAgentBattleSummarySnapshot? TakeCompletedSummary()
    {
        lock (_gate)
        {
            var completed = _completed;
            _completed = null;
            return completed;
        }
    }

    private void Observe(NetMessageObserved observed)
    {
        switch (observed.Message)
        {
            case NetMessageGameSim gameSim when IsCombatOpening(gameSim):
                lock (_gate)
                    _opening = CaptureOpening(gameSim);
                break;
            case NetMessageCombatSim combatSim:
                lock (_gate)
                {
                    if (_opening is null)
                        return;
                    _completed = BuildSummary(_opening, combatSim);
                    _opening = null;
                }
                break;
        }
    }

    private static OpeningBattle CaptureOpening(NetMessageGameSim message)
    {
        var player = CaptureCards(message, ECombatantId.Player);
        var opponent = CaptureCards(message, ECombatantId.Opponent);
        return new OpeningBattle(
            message.Data.CurrentState?.StateName == ERunState.PVPCombat ? "pvp" : "pve",
            player,
            opponent,
            new Dictionary<EPlayerAttributeType, int>(message.Data.Player.Attributes),
            new Dictionary<EPlayerAttributeType, int>(message.Data.Opponent.Attributes)
        );
    }

    private static bool IsCombatOpening(NetMessageGameSim message) =>
        message.Data.CurrentState?.StateName is ERunState.Combat or ERunState.PVPCombat;

    private static IReadOnlyList<BazaarAgentBattleCardSnapshot> CaptureCards(
        NetMessageGameSim message,
        ECombatantId owner
    )
    {
        var cards = new Dictionary<string, BazaarAgentBattleCardSnapshot>(StringComparer.Ordinal);
        foreach (
            var spawned in message
                .Data.Events.OfType<GameSimEventCardSpawned>()
                .Where(evt => evt.CombatantId == owner && evt.Section == EInventorySection.Hand)
        )
        {
            message.Data.Cards.TryGetValue(spawned.InstanceId, out var update);
            cards[spawned.InstanceId] = CreateCard(spawned, update);
        }

        foreach (
            var equipped in message
                .Data.Events.OfType<GameSimEventPlayerSkillEquipped>()
                .Where(evt => evt.Owner == owner)
        )
        {
            if (cards.ContainsKey(equipped.InstanceId))
                continue;
            message.Data.Cards.TryGetValue(equipped.InstanceId, out var update);
            cards[equipped.InstanceId] = new BazaarAgentBattleCardSnapshot
            {
                InstanceId = equipped.InstanceId,
                Type = "Skill",
                Size = update?.Size?.ToString(),
                Section = update?.Placement?.Section?.ToString(),
                SocketId = update?.Placement?.Socket?.ToString(),
                Attributes = Attributes(update),
            };
        }

        // The opening GameSim reliably carries the opponent's spawned cards, but the local board
        // can already exist when its event arrives. Mirror the replay capture's live fallback so
        // the player opening lineup is still complete in that ordering.
        if (owner == ECombatantId.Player && cards.Count == 0)
        {
            foreach (var card in Data.GetCards<Card>(ECombatantId.Player, EInventorySection.Hand))
            {
                cards[card.InstanceId.ToString()] = new BazaarAgentBattleCardSnapshot
                {
                    InstanceId = card.InstanceId.ToString(),
                    TemplateId = card.TemplateId.ToString(),
                    Type = card.Type.ToString(),
                    Size = card.Size.ToString(),
                    Section = card.Section?.ToString(),
                    SocketId = card.LeftSocketId?.ToString(),
                    Attributes = card.Attributes.ToDictionary(
                        pair => pair.Key.ToString(),
                        pair => pair.Value
                    ),
                };
            }

            foreach (var skill in Data.Run?.Player?.Skills?.Where(skill => skill is not null) ?? [])
            {
                cards[skill.InstanceId.ToString()] = new BazaarAgentBattleCardSnapshot
                {
                    InstanceId = skill.InstanceId.ToString(),
                    TemplateId = skill.TemplateId.ToString(),
                    Type = skill.Type.ToString(),
                    Size = skill.Size.ToString(),
                    Section = skill.Section?.ToString(),
                    SocketId = skill.LeftSocketId?.ToString(),
                    Attributes = skill.Attributes.ToDictionary(
                        pair => pair.Key.ToString(),
                        pair => pair.Value
                    ),
                };
            }
        }

        return cards
            .Values.OrderBy(card => card.SocketId)
            .ThenBy(card => card.InstanceId)
            .ToArray();
    }

    private static BazaarAgentBattleCardSnapshot CreateCard(
        GameSimEventCardSpawned spawned,
        SimUpdateCard? update
    ) =>
        new()
        {
            InstanceId = spawned.InstanceId,
            TemplateId = spawned.TemplateId,
            Type = spawned.Type.ToString(),
            Size = update?.Size?.ToString(),
            Section = update?.Placement?.Section?.ToString() ?? spawned.Section?.ToString(),
            SocketId = update?.Placement?.Socket?.ToString() ?? spawned.Socket?.ToString(),
            Attributes = Attributes(update),
        };

    private static IReadOnlyDictionary<string, int> Attributes(SimUpdateCard? update) =>
        update?.Attributes.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.Value)
        ?? new Dictionary<string, int>();

    private static BazaarAgentBattleSummarySnapshot BuildSummary(
        OpeningBattle opening,
        NetMessageCombatSim message
    ) =>
        new()
        {
            BattleType = opening.BattleType,
            Result = message.Data.Winner switch
            {
                ECombatantId.Player => "win",
                ECombatantId.Opponent => "loss",
                _ => null,
            },
            Player = new BazaarAgentBattleCombatantSnapshot
            {
                OpeningCards = opening.PlayerCards,
                Attributes = SummarizeAttributes(
                    message.Data.Frames,
                    static frame => frame.PlayerUpdates,
                    opening.PlayerAttributes
                ),
            },
            Opponent = new BazaarAgentBattleCombatantSnapshot
            {
                OpeningCards = opening.OpponentCards,
                Attributes = SummarizeAttributes(
                    message.Data.Frames,
                    static frame => frame.OpponentUpdates,
                    opening.OpponentAttributes
                ),
            },
        };

    private static BazaarAgentBattleAttributesSnapshot SummarizeAttributes(
        IEnumerable<CombatSimFrame> frames,
        Func<CombatSimFrame, CombatSimPlayerUpdate?> selectUpdate,
        IReadOnlyDictionary<EPlayerAttributeType, int> openingValues
    )
    {
        var values = openingValues
            .Where(pair => IsCapturedAttribute(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => new BazaarAgentBattleValueChange { Start = pair.Value, End = pair.Value }
            );
        foreach (var update in frames.Select(selectUpdate).Where(update => update is not null))
        foreach (var attribute in update!.Attributes)
        {
            if (!IsCapturedAttribute(attribute.Key))
                continue;
            values.TryGetValue(attribute.Key, out var existing);
            values[attribute.Key] = new BazaarAgentBattleValueChange
            {
                Start = existing?.Start ?? attribute.Value.PreviousValue,
                End = attribute.Value.CurrentValue,
            };
        }

        return new BazaarAgentBattleAttributesSnapshot
        {
            Health = Value(EPlayerAttributeType.Health),
            MaxHealth = Value(EPlayerAttributeType.HealthMax),
            Shield = Value(EPlayerAttributeType.Shield),
            Burn = Value(EPlayerAttributeType.Burn),
            Poison = Value(EPlayerAttributeType.Poison),
        };

        BazaarAgentBattleValueChange? Value(EPlayerAttributeType type) =>
            values.TryGetValue(type, out var value) ? value : null;
    }

    private static bool IsCapturedAttribute(EPlayerAttributeType type) =>
        type
            is EPlayerAttributeType.Health
                or EPlayerAttributeType.HealthMax
                or EPlayerAttributeType.Shield
                or EPlayerAttributeType.Burn
                or EPlayerAttributeType.Poison;

    private sealed record OpeningBattle(
        string BattleType,
        IReadOnlyList<BazaarAgentBattleCardSnapshot> PlayerCards,
        IReadOnlyList<BazaarAgentBattleCardSnapshot> OpponentCards,
        IReadOnlyDictionary<EPlayerAttributeType, int> PlayerAttributes,
        IReadOnlyDictionary<EPlayerAttributeType, int> OpponentAttributes
    );
}
