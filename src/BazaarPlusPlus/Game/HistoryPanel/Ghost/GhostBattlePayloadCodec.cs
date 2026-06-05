#nullable enable
using BazaarPlusPlus.ModApi;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal static class GhostBattlePayloadCodec
{
    public static byte[] Serialize(GhostBattlePayload payload) =>
        MessagePackGzipCodec.Serialize(payload);

    public static GhostBattlePayload? Deserialize(byte[] payloadBytes) =>
        MessagePackGzipCodec.Deserialize<GhostBattlePayload>(payloadBytes);
}
