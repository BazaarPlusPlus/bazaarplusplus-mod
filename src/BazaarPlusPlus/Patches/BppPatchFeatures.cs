#nullable enable
using System;
using BazaarPlusPlus.Game.EventPreview;

namespace BazaarPlusPlus.Patches;

internal sealed class BppPatchFeatures
{
    internal BppPatchFeatures(IEncounterPreviewModule encounterPreview)
    {
        EncounterPreview =
            encounterPreview ?? throw new ArgumentNullException(nameof(encounterPreview));
    }

    internal IEncounterPreviewModule EncounterPreview { get; }
}
