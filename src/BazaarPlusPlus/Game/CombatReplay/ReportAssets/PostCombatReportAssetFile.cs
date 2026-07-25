#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

internal enum PostCombatReportAssetBindingKind
{
    Entity,
    EventSemantic,
}

/// <summary>
/// A content-addressed report asset plus the typed report-document slot that consumes it.
/// BindingKey is an entity ID for Entity bindings and a stable report semantic key for
/// EventSemantic bindings. It is never a physical path or a localized/display string.
/// </summary>
internal sealed record PostCombatReportAssetFile(
    PostCombatReportAssetBindingKind BindingKind,
    string BindingKey,
    string SemanticRole,
    string FilePath,
    string RenderKeyHash = "",
    string ContentHash = "",
    int PixelWidth = 0,
    int PixelHeight = 0,
    string DisplayName = ""
);
