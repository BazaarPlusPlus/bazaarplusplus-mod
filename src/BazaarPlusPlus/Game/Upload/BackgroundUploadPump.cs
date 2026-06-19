#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.ModApi;
using UnityEngine;

namespace BazaarPlusPlus.Game.Upload;

internal sealed class BackgroundUploadPump : MonoBehaviour
{
    private static readonly Dictionary<string, BackgroundUploadPump> CurrentByScope = new();

    private IBppServices? _services;
    private IUploadFeed? _feed;
    private UploadFeedActivation? _activation;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private StartupUploadAttemptRunner? _startupRunner;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _extraArmHookSubscription;

    private void Awake() { }

    public void Initialize(IBppServices services, IUploadFeed feed)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));

        var descriptor = _feed.Descriptor;
        var activation = _feed.Activate(_services);
        if (activation == null)
            return;

        var startupDelaySeconds = Math.Max(5, ModApiUploadDefaults.StartupDelaySeconds);
        var retryIntervalSeconds = Math.Max(1, ModApiUploadDefaults.IntervalSeconds);

        _activation = activation;
        _shutdown = new CancellationTokenSource();
        _startupGate = new StartupUploadAttemptGate(
            Time.unscaledTime + startupDelaySeconds,
            retryIntervalSeconds
        );
        _startupRunner = new StartupUploadAttemptRunner(
            descriptor.LogScope,
            descriptor.SkipLiveRunMessage,
            descriptor.StartMessage,
            descriptor.FailureMessage
        );
        _runLifecycleSubscription = _services.EventBus.Subscribe<RunLifecycleChanged>(
            OnRunLifecycleChanged
        );
        _extraArmHookSubscription = activation.ExtraArmHook?.Subscribe(_services, ArmImmediate);
        CurrentByScope[descriptor.LogScope] = this;
    }

    private void Update()
    {
        if (
            _activation == null
            || _shutdown == null
            || _startupGate == null
            || _startupRunner == null
            || _services == null
        )
            return;

        if (!_activation.IsEnabled())
            return;

        _startupRunner.Tick(
            _startupGate,
            Time.unscaledTime,
            _services.RunContext.IsInGameRun,
            _activation.UploadInBackgroundAsync,
            _shutdown.Token
        );
    }

    private void OnDestroy()
    {
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;
        _extraArmHookSubscription?.Dispose();
        _extraArmHookSubscription = null;

        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        var descriptor = _feed?.Descriptor;
        _activation?.Disposable?.Dispose();
        _activation = null;

        if (
            descriptor.HasValue
            && CurrentByScope.TryGetValue(descriptor.Value.LogScope, out var current)
        )
        {
            if (ReferenceEquals(current, this))
                CurrentByScope.Remove(descriptor.Value.LogScope);
        }

        _startupGate = null;
        _startupRunner = null;
        _services = null;
        _feed = null;
    }

    public static void ArmImmediate(string logScope)
    {
        if (CurrentByScope.TryGetValue(logScope, out var current))
            current.ArmImmediate();
    }

    private void ArmImmediate()
    {
        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return;

        ArmImmediate();
    }
}
