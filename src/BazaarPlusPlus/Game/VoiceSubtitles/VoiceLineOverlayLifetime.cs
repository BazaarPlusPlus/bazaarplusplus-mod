#nullable enable

using System;
using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLineOverlayLifetime : MonoBehaviour
{
    private const float MinFallbackDurationSeconds = 0.2f;
    private const float PlaybackStopGraceSeconds = 4f;

    private GameObject? _labelObject;
    private Func<bool>? _isPlaybackStoppedOrStopping;
    private Func<string>? _playbackStateText;
    private float _softHideAt;
    private float _forceHideAt;
    private float _shownAt;
    private int _displayId;
    private int _attemptId;
    private string _stem = "<none>";

    public void Initialize(GameObject labelObject)
    {
        _labelObject = labelObject;
    }

    public void ShowUntilVoiceStops(
        Func<bool>? isPlaybackStoppedOrStopping,
        Func<string>? playbackStateText,
        float fallbackDurationSeconds,
        int displayId,
        int attemptId,
        string stem
    )
    {
        _isPlaybackStoppedOrStopping = isPlaybackStoppedOrStopping;
        _playbackStateText = playbackStateText;
        var fallbackDuration = Mathf.Max(MinFallbackDurationSeconds, fallbackDurationSeconds);
        _shownAt = Time.unscaledTime;
        _softHideAt = _shownAt + fallbackDuration;
        _forceHideAt =
            _softHideAt
            + (_isPlaybackStoppedOrStopping != null ? PlaybackStopGraceSeconds : 0f);
        _displayId = displayId;
        _attemptId = attemptId;
        _stem = stem;
        VoiceSubtitlesLog.Info(
            "Subtitle lifetime start "
                + $"display={_displayId} "
                + $"attempt={_attemptId} "
                + $"stem={_stem} "
                + $"fallbackDuration={fallbackDuration:F3}s "
                + $"stopGrace={(_isPlaybackStoppedOrStopping != null ? PlaybackStopGraceSeconds : 0f):F3}s "
                + $"playbackState={PlaybackStateText()}"
        );
    }

    public void Cancel(string reason)
    {
        if (_labelObject != null && _labelObject.activeSelf)
            Hide(reason);
        else
        {
            _isPlaybackStoppedOrStopping = null;
            _playbackStateText = null;
        }
    }

    private void Update()
    {
        if (_labelObject == null || !_labelObject.activeSelf)
            return;

        if (_isPlaybackStoppedOrStopping != null)
        {
            if (IsPlaybackStoppedOrStopping())
            {
                Hide($"playback-{PlaybackStateText()}");
                return;
            }
        }

        if (Time.unscaledTime < _softHideAt)
            return;

        if (_isPlaybackStoppedOrStopping == null || Time.unscaledTime >= _forceHideAt)
            Hide("fallback-timeout");
    }

    private void Hide(string reason)
    {
        var elapsedSeconds = Mathf.Max(0f, Time.unscaledTime - _shownAt);
        if (_labelObject != null)
            _labelObject.SetActive(false);
        VoiceSubtitlesLog.Info(
            "Subtitle hidden "
                + $"display={_displayId} "
                + $"attempt={_attemptId} "
                + $"stem={_stem} "
                + $"reason={reason} "
                + $"elapsed={elapsedSeconds:F3}s "
                + $"playbackState={PlaybackStateText()}"
        );
        _isPlaybackStoppedOrStopping = null;
        _playbackStateText = null;
    }

    private bool IsPlaybackStoppedOrStopping()
    {
        try
        {
            return _isPlaybackStoppedOrStopping?.Invoke() == true;
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to query VO playback state: {ex.Message}");
            return false;
        }
    }

    private string PlaybackStateText()
    {
        try
        {
            return _playbackStateText?.Invoke() ?? "<none>";
        }
        catch (Exception ex)
        {
            VoiceSubtitlesLog.Warn($"Failed to format VO playback state: {ex.Message}");
            return "<error>";
        }
    }
}
