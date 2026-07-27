#nullable enable
namespace BazaarPlusPlus.Game.Upload;

/// <summary>
/// Shared arm signal for a specific upload feed kind. Lives in Game/Upload because
/// <see cref="UploadFeedKind"/> is a Game-layer type and must not enter Core/Events.
/// </summary>
internal sealed class UploadArmRequested
{
    public UploadArmRequested(UploadFeedKind kind)
    {
        Kind = kind;
    }

    public UploadFeedKind Kind { get; }
}
