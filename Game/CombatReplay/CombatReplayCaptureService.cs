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
            SpawnMessageBase64 = SerializeMessage(spawnMessage),
            CombatMessageBase64 = SerializeMessage(combatMessage),
            DespawnMessageBase64 = SerializeMessage(despawnMessage),
        };
    }

    private static List<CombatReplayCardSnapshot> CapturePlayerHandCards()
    {
        try
        {
            return Data.GetCards<Card>(ECombatantId.Player, EInventorySection.Hand)
                .Where(card => card != null)
                .Select(card => new CombatReplayCardSnapshot
                {
                    InstanceId = card.InstanceId.ToString(),
                    TemplateId = card.TemplateId.ToString(),
                    Type = card.Type,
                    Size = card.Size,
                    Section = card.Section,
                    Socket = card.LeftSocketId,
                })
                .ToList();
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
