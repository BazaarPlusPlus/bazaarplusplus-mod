#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Cards;
using TheBazaar;
using TheBazaar.DataManagement.Json;

namespace BazaarPlusPlus.GameInterop.StaticCards;

/// <summary>
/// <c>Data.GetStatic()</c> returns the manager synchronously. This helper centralises
/// the readiness check so a future change to the upstream API only needs updating in
/// one place.
/// </summary>
internal static class BppStaticDataAccess
{
    public static object? TryGet()
    {
        if (!Data.IsManagerCreated())
            return null;

        return Data.GetStatic();
    }

    public static TCardBase? GetCardTemplate(object? staticData, Guid templateId)
    {
        if (staticData is not JsonGameDataManager manager || templateId == Guid.Empty)
            return null;

        return manager.GetCardById(templateId) as TCardBase;
    }

    public static bool TryGetCardMap(
        out object? managerObject,
        out Dictionary<Guid, ITCard>? map,
        out string unavailableReason
    )
    {
        managerObject = TryGet();
        map = null;
        unavailableReason = string.Empty;

        if (managerObject is not JsonGameDataManager manager)
        {
            unavailableReason = "static-data-not-ready";
            return false;
        }

        try
        {
            map = manager.GetCardMap();
        }
        catch
        {
            unavailableReason = "get-card-map-threw";
            throw;
        }

        if (map != null)
            return true;

        unavailableReason = "card-map-null";
        return false;
    }
}
