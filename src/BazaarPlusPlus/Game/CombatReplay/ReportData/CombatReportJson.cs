#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Reports;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.CombatReplay.ReportData;

internal static class CombatReportJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
        StringEscapeHandling = StringEscapeHandling.EscapeHtml,
    };

    internal static string Serialize(EmbeddedReportEnvelopeV1 envelope) =>
        JsonConvert.SerializeObject(envelope, Settings);

    internal static string Serialize(CombatReportDocumentV1 document) =>
        JsonConvert.SerializeObject(document, Settings);

    internal static void RefreshDocumentId(CombatReportDocumentV1 document)
    {
        document.DocumentId = string.Empty;
        document.DocumentId = StaticReportIntegrity.Sha256Utf8(Serialize(document));
    }
}
