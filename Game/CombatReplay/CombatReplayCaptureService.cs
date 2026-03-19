#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages;
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
            if (IsCombatOpeningMessage(message))
            {
                _candidate = new CombatReplaySequenceCandidate
                {
                    RunId = runId,
                    PlayerHandCards = CapturePlayerHandCards(),
                    PlayerSkills = CaptureSkills(ECombatantId.Player),
                    OpponentHandCards = CaptureOpponentHandCards(),
                    OpponentSkills = CaptureSkills(ECombatantId.Opponent),
                    SpawnMessage = message,
                };
            }

            return null;
        }

        if (_candidate.CombatMessage == null)
        {
            if (IsCombatOpeningMessage(message))
            {
                _candidate = new CombatReplaySequenceCandidate
                {
                    RunId = runId,
                    PlayerHandCards = CapturePlayerHandCards(),
                    PlayerSkills = CaptureSkills(ECombatantId.Player),
                    OpponentHandCards = CaptureOpponentHandCards(),
                    OpponentSkills = CaptureSkills(ECombatantId.Opponent),
                    SpawnMessage = message,
                };
            }
            else
            {
                _candidate = new CombatReplaySequenceCandidate();
            }

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

        return new CombatReplayRecord
        {
            ReplayId = Guid.NewGuid().ToString("N"),
            SavedAtUtc = _clock(),
            RunId = runId ?? candidate.RunId,
            Day = unchecked((int)spawnMessage.Data.Run.Day),
            Hour = unchecked((int)spawnMessage.Data.Run.Hour),
            EncounterId = spawnMessage.Data.CurrentState?.CurrentEncounterId,
            OpponentName = spawnMessage.Data.CurrentState?.PvpOpponent?.Name,
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

    private static List<CombatReplayCardSnapshot> CaptureOpponentHandCards()
    {
        try
        {
            return CaptureCards(ECombatantId.Opponent, EInventorySection.Hand);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to snapshot opponent hand cards for combat replay capture: {ex.Message}"
            );
            return new List<CombatReplayCardSnapshot>();
        }
    }

    private static List<CombatReplayCardSnapshot> CaptureSkills(ECombatantId combatantId)
    {
        try
        {
            var skills = combatantId == ECombatantId.Player
                ? Data.Run?.Player?.Skills
                : Data.Run?.Opponent?.Skills;
            return skills?
                    .Where(skill => skill != null)
                    .Select(CreateSnapshot)
                    .ToList()
                ?? new List<CombatReplayCardSnapshot>();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayCaptureService",
                $"Unable to snapshot {combatantId} skills for combat replay capture: {ex.Message}"
            );
            return new List<CombatReplayCardSnapshot>();
        }
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
        };
    }

    private static bool IsCombatOpeningMessage(NetMessageGameSim message)
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
