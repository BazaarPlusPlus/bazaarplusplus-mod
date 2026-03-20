#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus;

internal sealed class HistoryCollectionsEntryBridge : MonoBehaviour
{
    private const string EntryObjectName = "BPP_HistoryCollectionsEntry";
    private float _nextScanTime;

    private void Update()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + 1f;
        TryEnsureEntry();
    }

    private static void TryEnsureEntry()
    {
        var anchor = FindCollectionsAnchorButton();
        if (anchor == null)
            return;

        var parent = anchor.transform.parent;
        if (parent == null)
            return;

        var existing = parent.Find(EntryObjectName);
        if (existing != null)
        {
            ConfigureEntry(existing.gameObject, anchor.transform);
            return;
        }

        var clone = Instantiate(anchor.gameObject, parent);
        clone.name = EntryObjectName;
        ConfigureEntry(clone, anchor.transform);
    }

    private static Button? FindCollectionsAnchorButton()
    {
        Button? best = null;
        var bestScore = int.MinValue;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
                continue;
            if (candidate.name == EntryObjectName)
                continue;

            var score = ScoreCandidate(candidate);
            if (score <= bestScore)
                continue;

            best = candidate;
            bestScore = score;
        }

        return bestScore >= 100 ? best : null;
    }

    private static int ScoreCandidate(Button candidate)
    {
        var score = 0;
        var objectName = candidate.name ?? string.Empty;
        if (objectName.IndexOf("Collection", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 40;
        if (objectName.IndexOf("Collectible", StringComparison.OrdinalIgnoreCase) >= 0)
            score += 30;

        foreach (var component in candidate.GetComponents<MonoBehaviour>())
        {
            var typeName = component?.GetType().Name ?? string.Empty;
            if (
                typeName.IndexOf("CollectionsNavigationButton", StringComparison.OrdinalIgnoreCase)
                >= 0
            )
                score += 120;
            else if (typeName.IndexOf("Collection", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 30;
        }

        foreach (var label in EnumerateTexts(candidate.gameObject))
        {
            var text = label?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (IsCollectionsLabel(text))
                score += 100;
        }

        if (candidate.transform.parent != null && candidate.transform.parent.childCount >= 2)
            score += 10;

        return score;
    }

    private static bool IsCollectionsLabel(string text)
    {
        return string.Equals(text, "Collections", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Collection", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Collectibles", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "收藏品", StringComparison.OrdinalIgnoreCase);
    }

    private static void ConfigureEntry(GameObject entryObject, Transform anchorTransform)
    {
        entryObject.transform.SetSiblingIndex(anchorTransform.GetSiblingIndex() + 1);
        DisableForeignBehaviors(entryObject);

        var button = entryObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OpenHistoryPage);
        }

        SetEntryLabel(entryObject, ResolveLabel(PlayerPreferences.Data.LanguageCode));
    }

    private static void OpenHistoryPage()
    {
        HistoryPanel.Instance?.OpenFromUiEntry();
    }

    private static void DisableForeignBehaviors(GameObject entryObject)
    {
        foreach (var behaviour in entryObject.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null)
                continue;
            if (behaviour is Button || behaviour is LayoutElement)
                continue;

            var type = behaviour.GetType();
            var ns = type.Namespace ?? string.Empty;
            if (
                ns.StartsWith("UnityEngine", StringComparison.Ordinal)
                || ns.StartsWith("TMPro", StringComparison.Ordinal)
                || ns.StartsWith("BazaarPlusPlus", StringComparison.Ordinal)
            )
            {
                continue;
            }

            behaviour.enabled = false;
        }
    }

    private static void SetEntryLabel(GameObject entryObject, string label)
    {
        foreach (var text in entryObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text == null)
                continue;
            if (string.IsNullOrWhiteSpace(text.text))
                continue;
            text.text = label;
        }

        foreach (var text in entryObject.GetComponentsInChildren<Text>(true))
        {
            if (text == null)
                continue;
            if (string.IsNullOrWhiteSpace(text.text))
                continue;
            text.text = label;
        }
    }

    private static IEnumerable<string> EnumerateTexts(GameObject gameObject)
    {
        foreach (var text in gameObject.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                yield return text.text;
        }

        foreach (var text in gameObject.GetComponentsInChildren<Text>(true))
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                yield return text.text;
        }
    }

    private static string ResolveLabel(string languageCode)
    {
        if (LanguageCodeMatcher.IsSimplifiedChinese(languageCode))
            return "战绩";
        if (LanguageCodeMatcher.IsGerman(languageCode))
            return "Verlauf";
        if (LanguageCodeMatcher.IsPortuguese(languageCode))
            return "Historico";
        if (LanguageCodeMatcher.IsKorean(languageCode))
            return "전적";
        if (LanguageCodeMatcher.IsItalian(languageCode))
            return "Cronologia";

        return "History";
    }
}
