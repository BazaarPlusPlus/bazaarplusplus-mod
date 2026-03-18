#nullable enable
using System;
using BazaarGameShared;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages;
using MessagePack;

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
            SpawnMessageBase64 = SerializeMessage(spawnMessage),
            CombatMessageBase64 = SerializeMessage(combatMessage),
            DespawnMessageBase64 = SerializeMessage(despawnMessage),
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
