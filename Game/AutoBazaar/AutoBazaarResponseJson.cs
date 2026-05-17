#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal static class AutoBazaarResponseJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    public static string BuildValidationErrorBody(AutoBazaarValidationResult validation)
    {
        var code = validation.Code switch
        {
            AutoBazaarValidationCode.Invalid => "invalid",
            AutoBazaarValidationCode.StaleOrUnavailable => "stale-or-unavailable",
            AutoBazaarValidationCode.Cooldown => "cooldown",
            AutoBazaarValidationCode.Unavailable => "unavailable",
            _ => "internal",
        };
        var envelope = new Dictionary<string, object?>
        {
            ["error"] = code,
        };
        if (validation.Details is not null) envelope["details"] = validation.Details;
        if (validation.Extra is not null && validation.Extra.Count > 0)
        {
            envelope["extra"] = validation.Extra;
        }
        return JsonConvert.SerializeObject(envelope, Settings);
    }
}
