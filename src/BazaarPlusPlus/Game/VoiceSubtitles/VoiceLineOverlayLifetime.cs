#nullable enable

using System;
using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLineOverlayLifetime : MonoBehaviour
{
    private GameObject? _labelObject;
    private Func<bool>? _isPlaybackStoppedOrStopping;
    private Func<string>? _playbackStateText;
    private float _hideAt;
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
        _shownAt = Time.unscaledTime;
        _hideAt = _shownAt + Mathf.Max(0.2f, fallbackDurationSeconds);
        _displayId = displayId;
        _attemptId = attemptId;
        _stem = stem;
        VoiceSubtitlesLog.Info(
            "Subtitle lifetime start "
                + $"display={_displayId} "
                + $"attempt={_attemptId} "
                + $"stem={_stem} "
                + $"fallbackDuration={Mathf.Max(0.2f, fallbackDurationSeconds):F3}s "
                + $"playbackState={PlaybackStateText()}"
        );
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

        if (Time.unscaledTime >= _hideAt)
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
