#nullable enable
using BazaarPlusPlus.ModApi;

namespace BazaarPlusPlus.Game.PvpBattles;

internal static class PvpReplayPayloadCodec
{
    public static byte[] Serialize(PvpReplayPayload payload) =>
        MessagePackGzipCodec.Serialize(payload);

    public static PvpReplayPayload? Deserialize(byte[] payloadBytes) =>
        MessagePackGzipCodec.Deserialize<PvpReplayPayload>(payloadBytes);
}
