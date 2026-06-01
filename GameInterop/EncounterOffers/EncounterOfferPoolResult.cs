#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.GameInterop.EncounterOffers;

internal enum EncounterOfferPoolStatus
{
    NoneSelected,
    Loading,
    Ready,
    Unavailable,
}

internal sealed class EncounterOfferPoolResult
{
    private EncounterOfferPoolResult(
        EncounterOfferPoolStatus status,
        IReadOnlyCollection<Guid> templateIds,
        string? reason
    )
    {
        Status = status;
        TemplateIds = templateIds;
        Reason = reason;
    }

    public EncounterOfferPoolStatus Status { get; }

    public IReadOnlyCollection<Guid> TemplateIds { get; }

    public string? Reason { get; }

    public static EncounterOfferPoolResult NoneSelected() =>
        new(EncounterOfferPoolStatus.NoneSelected, Array.Empty<Guid>(), null);

    public static EncounterOfferPoolResult Loading(string reason) =>
        new(EncounterOfferPoolStatus.Loading, Array.Empty<Guid>(), reason);

    public static EncounterOfferPoolResult Ready(IReadOnlyCollection<Guid> templateIds) =>
        new(EncounterOfferPoolStatus.Ready, templateIds, null);

    public static EncounterOfferPoolResult Unavailable(string reason) =>
        new(EncounterOfferPoolStatus.Unavailable, Array.Empty<Guid>(), reason);
}
