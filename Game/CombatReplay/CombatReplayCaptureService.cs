#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using MessagePack;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayCaptureService
{
    private readonly Func<DateTimeOffset> _clock;
    private CombatReplaySequenceCandidate _candidate = new CombatReplaySequenceCandidate();

    public CombatReplayCaptureService()
        : this(null) { }

    public CombatReplayCaptureService(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public CombatReplayRecord? Accept(INetMessage message, string? runId)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        switch (message)
        {
            case NetMessageGameSim gameSimMessage:
                return AcceptGameSim(gameSimMessage, runId);
            case NetMessageCombatSim combatSimMessage:
                return AcceptCombatSim(combatSimMessage, runId);
            default:
                return null;
        }
    }

    private CombatReplayRecord? AcceptGameSim(NetMessageGameSim message, string? runId)
    {
        if (_candidate.SpawnMessage == null)
        {
            if (IsPvpCombatOpeningMessage(message))
            {
                _candidate = CreateOpeningCandidate(message, runId);
            }

            return null;
        }

        if (_candidate.CombatMessage == null)
        {
            if (IsPvpCombatOpeningMessage(message))
            {
                _candidate = CreateOpeningCandidate(message, runId);
            }
            else
            {
                _candidate = new CombatReplaySequenceCandidate();
            }

            return null;
        }

        if (IsAnyCombatOpeningMessage(message))
        {
            _candidate = IsPvpCombatOpeningMessage(message)
                ? CreateOpeningCandidate(message, runId)
                : new CombatReplaySequenceCandidate();
            return null;
        }

        var record = CreateRecord(_candidate, message, runId);
        _candidate = new CombatReplaySequenceCandidate();
        return record;
    }

    private CombatReplayRecord? AcceptCombatSim(NetMessageCombatSim message, string? runId)
    {
        if (_candidate.SpawnMessage == null)
            return null;

        if (_candidate.CombatMessage != null)
        {
            _candidate = new CombatReplaySequenceCandidate();
            return null;
        }

        _candidate.RunId ??= runId;
        _candidate.CombatMessage = message;
        CaptureLiveSnapshots(_candidate);
        return null;
    }

    private CombatReplayRecord CreateRecord(
        CombatReplaySequenceCandidate candidate,
        NetMessageGameSim despawnMessage,
        string? runId
    )
    {
        var spawnMessage =
            candidate.SpawnMessage
            ?? throw new InvalidOperationException("Spawn message is required.");
        var combatMessage =
            candidate.CombatMessage
            ?? throw new InvalidOperationException("Combat message is required.");
        CaptureLiveSnapshots(candidate);

        return new CombatReplayRecord
        {
            ReplayId = Guid.NewGuid().ToString("N"),
            SavedAtUtc = _clock(),
            RunId = runId ?? candidate.RunId,
            CombatKind = spawnMessage.Data.CurrentState?.StateName.ToString(),
            Day = unchecked((int)spawnMessage.Data.Run.Day),
            Hour = unchecked((int)spawnMessage.Data.Run.Hour),
            EncounterId = spawnMessage.Data.CurrentState?.CurrentEncounterId,
            PlayerName = TryGetPlayerNameSafe(),
            PlayerAccountId = TryGetPlayerAccountIdSafe(),
            OpponentName = candidate.OpponentName,
            OpponentAccountId = candidate.OpponentAccountId,
            Result = ResolvePlayerResult(combatMessage),
            WinnerCombatantId = combatMessage.Data.Winner.ToString(),
            LoserCombatantId = combatMessage.Data.Loser.ToString(),
            PlayerHandCards = new List<CombatReplayCardSnapshot>(candidate.PlayerHandCards),
            PlayerSkills = new List<CombatReplayCardSnapshot>(candidate.PlayerSkills),
            OpponentHandCards = new List<CombatReplayCardSnapshot>(candidate.OpponentHandCards),
            OpponentSkills = new List<CombatReplayCardSnapshot>(candidate.OpponentSkills),
            SpawnMessageBase64 = SerializeMessage(spawnMessage),
            CombatMessageBase64 = SerializeMessage(combatMessage),
            DespawnMessageBase64 = SerializeMessage(despawnMessage),
        };
    }

    private static List<CombatReplayCardSnapshot> CapturePlayerHandCards()
    {
        try
        {
            return CaptureCards(ECombatantId.Player, EInventorySection.Hand);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to snapshot player hand cards for combat replay capture: {ex.Message}"
            );
            return new List<CombatReplayCardSnapshot>();
        }
    }

    private static List<CombatReplayCardSnapshot> CapturePlayerSkills()
    {
        try
        {
            return CapturePlayerSkillsUnsafe();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to snapshot player skills for combat replay capture: {ex.Message}"
            );
            return new List<CombatReplayCardSnapshot>();
        }
    }

    private static List<CombatReplayCardSnapshot> CapturePlayerSkillsUnsafe()
    {
        return Data.Run?.Player?.Skills?
                .Where(skill => skill != null)
                .Select(CreateSkillSnapshot)
                .ToList()
            ?? new List<CombatReplayCardSnapshot>();
    }

    private static List<CombatReplayCardSnapshot> CaptureCards(
        ECombatantId combatantId,
        EInventorySection section
    )
    {
        return Data.GetCards<Card>(combatantId, section)
            .Where(card => card != null)
            .Select(CreateSnapshot)
            .ToList();
    }

    private static CombatReplayCardSnapshot CreateSnapshot(Card card)
    {
        return new CombatReplayCardSnapshot
        {
            InstanceId = card.InstanceId.ToString(),
            TemplateId = card.TemplateId.ToString(),
            Type = card.Type,
            Size = card.Size,
            Section = card.Section,
            Socket = card.LeftSocketId,
            Name = card.Template?.InternalName,
            Tier = card.Tier.ToString(),
            Enchant = card.GetEnchantment().ToString(),
            Tags = card.Tags?.Select(tag => tag.ToString()).ToList() ?? new List<string>(),
            Attributes = card.Attributes?.ToDictionary(
                    entry => entry.Key.ToString(),
                    entry => entry.Value
                ) ?? new Dictionary<string, int>(),
        };
    }

    private static string? ResolvePlayerResult(NetMessageCombatSim message)
    {
        if (message.Data.Winner == ECombatantId.Player)
            return "win";
        if (message.Data.Loser == ECombatantId.Player)
            return "loss";
        return null;
    }

    private static string? TryGetPlayerNameSafe()
    {
        try
        {
            return Data.Profile?.Username;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetPlayerAccountIdSafe()
    {
        try
        {
            return Data.Profile?.AccountId.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static CombatReplaySequenceCandidate CreateOpeningCandidate(
        NetMessageGameSim message,
        string? runId
    )
    {
        var (opponentName, opponentAccountId) = CaptureOpponentIdentityAtOpening();
        var candidate = new CombatReplaySequenceCandidate
        {
            RunId = runId,
            OpponentName = opponentName,
            OpponentAccountId = opponentAccountId,
            SpawnMessage = message,
        };

        (
            candidate.PlayerHandCardsCapturedFromOpening,
            candidate.PlayerHandCards
        ) = CaptureCurrentHandCardsAtOpening(ECombatantId.Player);
        (
            candidate.PlayerSkillsCapturedFromOpening,
            candidate.PlayerSkills
        ) = CaptureCurrentSkillsAtOpening(ECombatantId.Player);
        (
            candidate.OpponentHandCardsCapturedFromOpening,
            candidate.OpponentHandCards
        ) = CaptureOpeningHandCards(message, ECombatantId.Opponent);
        (
            candidate.OpponentSkillsCapturedFromOpening,
            candidate.OpponentSkills
        ) = CaptureOpponentSkillsFromOpening(message);
        return candidate;
    }

    private static CombatReplayCardSnapshot CreateSkillSnapshot(SkillCard skill)
    {
        var snapshot = CreateSnapshot(skill);
        snapshot.Name = skill.Template?.Localization?.Title?.Text ?? skill.Template?.InternalName;
        return snapshot;
    }

    private static (bool Captured, List<CombatReplayCardSnapshot> Snapshots) CaptureOpeningHandCards(
        NetMessageGameSim message,
        ECombatantId combatantId
    )
    {
        try
        {
            var snapshots = message
                .Data.Events.OfType<GameSimEventCardSpawned>()
                .Where(evt => evt.CombatantId == combatantId)
                .Where(evt => evt.Section == EInventorySection.Hand)
                .OrderBy(evt => evt.Socket ?? EContainerSocketId.Socket_0)
                .Select(entry =>
                    CreateOpeningSnapshot(
                        entry.InstanceId,
                        message.Data.Cards.TryGetValue(entry.InstanceId, out var cardUpdate)
                            ? cardUpdate
                            : null,
                        entry
                    )
                )
                .OfType<CombatReplayCardSnapshot>()
                .ToList();

            return (true, snapshots);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to capture opening {combatantId} hand cards from GameSim: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static (bool Captured, List<CombatReplayCardSnapshot> Snapshots) CaptureCurrentHandCardsAtOpening(
        ECombatantId combatantId
    )
    {
        try
        {
            return (true, CaptureCards(combatantId, EInventorySection.Hand));
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to capture opening {combatantId} hand cards from current Data: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static (bool Captured, List<CombatReplayCardSnapshot> Snapshots) CaptureCurrentSkillsAtOpening(
        ECombatantId combatantId
    )
    {
        try
        {
            return combatantId == ECombatantId.Player
                ? (true, CapturePlayerSkillsUnsafe())
                : (false, new List<CombatReplayCardSnapshot>());
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to capture opening {combatantId} skills from current Data: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static (bool Captured, List<CombatReplayCardSnapshot> Snapshots) CaptureOpponentSkillsFromOpening(
        NetMessageGameSim message
    )
    {
        try
        {
            var spawnedCards = message
                .Data.Events.OfType<GameSimEventCardSpawned>()
                .ToDictionary(evt => evt.InstanceId, StringComparer.Ordinal);
            var skillEvents = message
                .Data.Events.OfType<GameSimEventPlayerSkillEquipped>()
                .Where(evt => evt.Owner == ECombatantId.Opponent)
                .ToList();
            if (skillEvents.Count == 0)
                return (true, new List<CombatReplayCardSnapshot>());

            var snapshots = skillEvents
                .Select(evt =>
                {
                    message.Data.Cards.TryGetValue(evt.InstanceId, out var cardUpdate);
                    return CreateOpeningSnapshot(
                        evt.InstanceId,
                        cardUpdate,
                        spawnedCards.TryGetValue(evt.InstanceId, out var spawnedCard)
                            ? spawnedCard
                            : null,
                        fallbackType: ECardType.Skill
                    );
                })
                .OfType<CombatReplayCardSnapshot>()
                .ToList();
            return (true, snapshots);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to capture opening opponent skills from GameSim: {ex.Message}"
            );
            return (false, new List<CombatReplayCardSnapshot>());
        }
    }

    private static CombatReplayCardSnapshot? CreateOpeningSnapshot(
        string instanceId,
        SimUpdateCard? cardUpdate,
        GameSimEventCardSpawned? spawnedCard,
        ECardType? fallbackType = null
    )
    {
        var existingCard = TryGetExistingCardSafe(instanceId);
        var existingSkill = existingCard as SkillCard;
        var attributes = cardUpdate?.Attributes?.ToDictionary(
                entry => entry.Key.ToString(),
                entry => entry.Value.Value
            ) ?? new Dictionary<string, int>();

        var snapshot = new CombatReplayCardSnapshot
        {
            InstanceId = instanceId,
            TemplateId = spawnedCard?.TemplateId ?? existingCard?.TemplateId.ToString() ?? string.Empty,
            Type = spawnedCard?.Type ?? existingCard?.Type ?? fallbackType ?? default,
            Size = cardUpdate?.Size ?? existingCard?.Size ?? default,
            Section = cardUpdate?.Placement?.Section ?? existingCard?.Section,
            Socket = cardUpdate?.Placement?.Socket ?? existingCard?.LeftSocketId,
            Name = existingSkill?.Template?.Localization?.Title?.Text
                ?? existingCard?.Template?.InternalName,
            Tier = cardUpdate?.Tier?.ToString() ?? existingCard?.Tier.ToString(),
            Enchant = cardUpdate?.Enchantment?.ToString() ?? existingCard?.GetEnchantment().ToString(),
            Tags = cardUpdate?.Tags?.Select(tag => tag.ToString()).ToList()
                ?? existingCard?.Tags?.Select(tag => tag.ToString()).ToList()
                ?? new List<string>(),
            Attributes = attributes.Count > 0
                ? attributes
                : existingCard?.Attributes?.ToDictionary(
                        entry => entry.Key.ToString(),
                        entry => entry.Value
                    ) ?? new Dictionary<string, int>(),
        };

        return snapshot;
    }

    private static Card? TryGetExistingCardSafe(string instanceId)
    {
        try
        {
            return Data.GetCard(instanceId);
        }
        catch
        {
            return null;
        }
    }

    private static void CaptureLiveSnapshots(CombatReplaySequenceCandidate candidate)
    {
        if (!candidate.PlayerHandCardsCapturedFromOpening && candidate.PlayerHandCards.Count == 0)
            candidate.PlayerHandCards = CapturePlayerHandCards();

        if (!candidate.PlayerSkillsCapturedFromOpening && candidate.PlayerSkills.Count == 0)
            candidate.PlayerSkills = CapturePlayerSkills();
    }

    private static (string? Name, string? AccountId) CaptureOpponentIdentityAtOpening()
    {
        try
        {
            var name = Data.SimPvpOpponent?.Name;
            var accountId = Data.SimPvpOpponent?.PlayerLoadout?.accountId;
            return (
                string.IsNullOrWhiteSpace(name) ? null : name,
                string.IsNullOrWhiteSpace(accountId) ? null : accountId
            );
        }
        catch
        {
            return (null, null);
        }
    }

    private static bool IsPvpCombatOpeningMessage(NetMessageGameSim message)
    {
        var state = message.Data.CurrentState?.StateName;
        return state == ERunState.PVPCombat;
    }

    private static bool IsAnyCombatOpeningMessage(NetMessageGameSim message)
    {
        var state = message.Data.CurrentState?.StateName;
        return state == ERunState.Combat || state == ERunState.PVPCombat;
    }

    private static string SerializeMessage<TMessage>(TMessage message)
        where TMessage : INetMessage
    {
        var bytes = MessagePackSerializer.Serialize(message, MessagePackConfig.Options);
        return Convert.ToBase64String(bytes);
    }
}
