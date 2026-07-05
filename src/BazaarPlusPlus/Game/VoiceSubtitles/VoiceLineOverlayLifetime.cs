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

    private void Awake()
    {
        enabled = false;
    }

    public void Initialize(GameObject labelObject)
    {
        _labelObject = labelObject;
        enabled = false;
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
        enabled = true;
        if (VoiceSubtitlesLog.Verbose)
        {
            VoiceSubtitlesLog.Debug(
                "Subtitle lifetime start "
                    + $"display={_displayId} "
                    + $"attempt={_attemptId} "
                    + $"stem={_stem} "
                    + $"fallbackDuration={Mathf.Max(0.2f, fallbackDurationSeconds):F3}s "
                    + $"playbackState={PlaybackStateText()}"
            );
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
                HidePlaybackStopped();
                return;
            }
        }

        if (Time.unscaledTime >= _hideAt)
            HideFallbackTimeout();
    }

    private void HidePlaybackStopped()
    {
        if (VoiceSubtitlesLog.Verbose)
        {
            Hide("playback-" + PlaybackStateText());
            return;
        }

        Hide();
    }

    private void HideFallbackTimeout()
    {
        if (VoiceSubtitlesLog.Verbose)
        {
            Hide("fallback-timeout");
            return;
        }

        Hide();
    }

    private void Hide(string? reason = null)
    {
        if (_labelObject != null)
            _labelObject.SetActive(false);
        if (VoiceSubtitlesLog.Verbose && reason != null)
        {
            var elapsedSeconds = Mathf.Max(0f, Time.unscaledTime - _shownAt);
            VoiceSubtitlesLog.Debug(
                "Subtitle hidden "
                    + $"display={_displayId} "
                    + $"attempt={_attemptId} "
                    + $"stem={_stem} "
                    + $"reason={reason} "
                    + $"elapsed={elapsedSeconds:F3}s "
                    + $"playbackState={PlaybackStateText()}"
            );
        }
        _isPlaybackStoppedOrStopping = null;
        _playbackStateText = null;
        enabled = false;
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
