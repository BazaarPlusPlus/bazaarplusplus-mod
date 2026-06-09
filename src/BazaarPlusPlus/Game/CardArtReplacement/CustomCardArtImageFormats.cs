#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Game.CardArtReplacement;

/// <summary>
/// Image formats accepted for custom card art. Unity's <c>Texture2D.LoadImage</c>
/// decodes PNG and JPEG from the byte header, so discovery is format-agnostic
/// across these extensions. Single source of truth shared by the on-disk catalog
/// and the bundled-resource installer so the two never drift.
/// </summary>
internal static class CustomCardArtImageFormats
{
    public static bool IsSupportedExtension(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    public static bool TryGetTemplateId(string fileName, out Guid templateId)
    {
        templateId = Guid.Empty;
        if (!IsSupportedExtension(Path.GetExtension(fileName)))
            return false;

        return Guid.TryParse(Path.GetFileNameWithoutExtension(fileName), out templateId)
            && templateId != Guid.Empty;
    }
}
