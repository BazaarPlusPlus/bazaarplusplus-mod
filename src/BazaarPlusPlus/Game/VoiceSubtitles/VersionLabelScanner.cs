#nullable enable

using System;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VersionLabelScanner : MonoBehaviour
{
    private const float ScanIntervalSeconds = 0.75f;
    private float _nextScanAt;

    private void Update()
    {
        if (Time.unscaledTime < _nextScanAt)
            return;

        _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
        if (VoiceLineDisplay.IsMountedFromVersionLabel)
            return;

        var versionLabel = FindVisibleVersionLabel();
        if (versionLabel != null)
            VoiceLineDisplay.MountFromVersionLabel(versionLabel);
    }

    private static TextMeshProUGUI? FindVisibleVersionLabel()
    {
        TextMeshProUGUI? best = null;
        var bestScore = float.NegativeInfinity;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (!IsUsableVersionLabel(candidate))
                continue;

            var rect = candidate.rectTransform;
            var score = candidate.transform.root.gameObject.scene.isLoaded ? 1000f : 0f;
            score += candidate.gameObject.activeInHierarchy ? 100f : 0f;
            score += -rect.position.x * 0.01f;
            score += -rect.position.y * 0.01f;

            if (score <= bestScore)
                continue;

            best = candidate;
            bestScore = score;
        }

        if (best != null)
            VoiceSubtitlesLog.Info(
                $"Found visible version label at '{BuildPath(best.transform)}' text='{best.text}'"
            );

        return best;
    }

    private static bool IsUsableVersionLabel(TextMeshProUGUI? text)
    {
        if (text == null)
            return false;
        if (text.name.StartsWith("BazaarLine_", StringComparison.Ordinal))
            return false;
        if (text.transform.root.name.StartsWith("BazaarLine_", StringComparison.Ordinal))
            return false;
        if (!text.gameObject.activeInHierarchy)
            return false;
        if (!text.transform.root.gameObject.scene.isLoaded)
            return false;
        if (string.IsNullOrWhiteSpace(text.text))
            return false;

        return text.text.TrimStart().StartsWith("Version:", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPath(Transform transform)
    {
        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
