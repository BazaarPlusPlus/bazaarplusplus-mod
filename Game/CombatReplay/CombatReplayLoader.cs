#nullable enable
using System;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using MessagePack;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayLoader
{
    public CombatSequenceMessages Load(CombatReplayRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        return new CombatSequenceMessages(
            DeserializeGameSim(record.SpawnMessageBase64),
            DeserializeGameSim(record.DespawnMessageBase64),
            DeserializeCombatSim(record.CombatMessageBase64)
        );
    }

    private static NetMessageGameSim DeserializeGameSim(string payload)
    {
        return MessagePackSerializer.Deserialize<NetMessageGameSim>(
            Convert.FromBase64String(payload),
            MessagePackConfig.Options
        );
    }

    private static NetMessageCombatSim DeserializeCombatSim(string payload)
    {
        return MessagePackSerializer.Deserialize<NetMessageCombatSim>(
            Convert.FromBase64String(payload),
            MessagePackConfig.Options
        );
    }
}
