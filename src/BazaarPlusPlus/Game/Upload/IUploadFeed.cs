#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.Upload;

internal interface IUploadFeed
{
    UploadFeedDescriptor Descriptor { get; }
    UploadFeedActivation? Activate(IBppServices services);
}

internal readonly record struct UploadFeedDescriptor(
    string LogScope,
    string SkipLiveRunMessage,
    string StartMessage,
    string FailureMessage
);

internal sealed class UploadFeedActivation
{
    public Func<CancellationToken, Task> UploadInBackgroundAsync { get; init; } =
        _ => throw new InvalidOperationException("Upload delegate is not configured.");
    public Func<bool> IsEnabled { get; init; } = static () => true;
    public IDisposable? Disposable { get; init; }
    public UploadArmHook? ExtraArmHook { get; init; }
}

internal sealed record UploadArmHook(Func<IBppServices, Action, IDisposable> Subscribe);
