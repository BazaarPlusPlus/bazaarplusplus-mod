#nullable enable
using BazaarPlusPlus.Game.Online.Models;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadSnapshot
{
    public string ScreenshotId { get; init; } = string.Empty;

    public BazaarDbScreenshotUploadRequestV3 Payload { get; init; } =
        new BazaarDbScreenshotUploadRequestV3();
}
