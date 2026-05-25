#nullable enable
using BazaarPlusPlus.ModApi.Models;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal sealed class BazaarDbScreenshotUploadSnapshot
{
    public string ScreenshotId { get; init; } = string.Empty;

    public BazaarDbScreenshotUploadRequest Payload { get; init; } =
        new BazaarDbScreenshotUploadRequest();
}
