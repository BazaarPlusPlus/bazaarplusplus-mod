#nullable enable
using System;
using BazaarGameClient.Domain.Models.Cards;
using TheBazaar;

namespace BazaarPlusPlus.Game.Encounter;

/// <summary>Resolves the encounter-card type name from a
/// <c>RunState.CurrentEncounterId</c> GUID by scanning <c>Data.Entities</c> for the
/// matching card template. Pure read, main thread only.</summary>
internal static class EncounterTypeResolver
{
    public static string? Resolve(string? currentEncounterId)
    {
        if (string.IsNullOrWhiteSpace(currentEncounterId)) return null;
        if (!Guid.TryParse(currentEncounterId, out var templateId)) return null;

        foreach (var entity in Data.Entities.Values)
        {
            if (entity is not Card card) continue;
            if (card.TemplateId != templateId) continue;
            return card.Template?.GetType().Name ?? card.Type.ToString();
        }
        return null;
    }
}
