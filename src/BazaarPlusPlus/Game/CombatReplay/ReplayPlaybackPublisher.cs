#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class ReplayPlaybackPublisher
{
    private readonly IBppServices _services;
    private string? _activeBattleId;
    private PvpBattleManifest? _activeManifest;
    private CombatReplayPlaybackSource _activeSource;
    private bool _activeRecordVideo;
    private bool _startingPublished;

    public ReplayPlaybackPublisher(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public bool StartingPublished => _startingPublished;

    public void BeginSession(
        string battleId,
        PvpBattleManifest? manifest,
        CombatReplayPlaybackSource source,
        bool recordVideo
    )
    {
        _activeBattleId = battleId;
        _activeManifest = manifest;
        _activeSource = source;
        _activeRecordVideo = recordVideo;
        _startingPublished = false;
        BppLog.Info(
            "ReplayPlaybackPublisher",
            $"BeginSession battle={battleId} source={source} recordVideo={recordVideo}"
        );
    }

    public void PublishStarting()
    {
        if (_startingPublished)
            return;

        var battleId = _activeBattleId;
        if (string.IsNullOrEmpty(battleId))
            return;

        try
        {
            _services.EventBus.Publish(
                new CombatReplayPlaybackStarting
                {
                    BattleId = battleId,
                    Manifest = _activeManifest,
                    Source = _activeSource,
                    RecordVideo = _activeRecordVideo,
                }
            );
            _startingPublished = true;
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "ReplayPlaybackPublisher",
                "Failed to publish CombatReplayPlaybackStarting event.",
                ex
            );
        }
    }

    public void PublishEnded(string reason, bool failed)
    {
        if (!_startingPublished)
            return;

        var battleId = _activeBattleId ?? string.Empty;
        _startingPublished = false;
        _activeBattleId = null;
        _activeManifest = null;

        try
        {
            _services.EventBus.Publish(
                new CombatReplayPlaybackEnded
                {
                    BattleId = battleId,
                    Reason = reason,
                    Failed = failed,
                }
            );
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "ReplayPlaybackPublisher",
                "Failed to publish CombatReplayPlaybackEnded event.",
                ex
            );
        }
    }
}
