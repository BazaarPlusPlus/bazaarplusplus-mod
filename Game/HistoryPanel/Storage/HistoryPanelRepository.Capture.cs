#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Game.PvpBattles;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

internal sealed partial class HistoryPanelRepository
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy(),
        },
        Converters = new List<JsonConverter> { new StringEnumConverter() },
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
    };

    private static PvpBattleCardSetCapture DeserializeCapture(string json)
    {
        return JsonConvert.DeserializeObject<PvpBattleCardSetCapture>(json, SerializerSettings)
            ?? new PvpBattleCardSetCapture();
    }
}
