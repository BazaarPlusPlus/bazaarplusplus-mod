#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;
using TheBazaar.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryCollectionsEntryBridge : MonoBehaviour
{
    private const string EntryObjectName = "BPP_HistoryCollectionsEntry";
    private float _nextScanTime;
    private Button? _cachedAnchorButton;
    private Transform? _cachedAnchorParent;

    private void Update()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + 1f;
        TryEnsureEntry();
    }

    private void TryEnsureEntry()
    {
        var anchor = ResolveCollectionsAnchorButton();
        if (anchor == null)
            return;

        var parent = anchor.transform.parent;
        if (parent == null)
        {
            ClearCachedAnchor();
            return;
        }

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

    private Button? ResolveCollectionsAnchorButton()
    {
        if (HasValidCachedAnchor())
            return _cachedAnchorButton;

        var anchor = FindCollectionsAnchorButton();
        CacheAnchor(anchor);
        return anchor;
    }

    private bool HasValidCachedAnchor()
    {
        if (_cachedAnchorButton == null || _cachedAnchorParent == null)
            return false;

        if (!_cachedAnchorButton.gameObject.activeInHierarchy)
            return false;

        if (_cachedAnchorButton.name == EntryObjectName)
            return false;

        return _cachedAnchorButton.transform.parent == _cachedAnchorParent;
    }

    private void CacheAnchor(Button? anchor)
    {
        if (anchor == null)
        {
            ClearCachedAnchor();
            return;
        }

        var parent = anchor.transform.parent;
        if (parent == null)
        {
            ClearCachedAnchor();
            return;
        }

        _cachedAnchorButton = anchor;
        _cachedAnchorParent = parent;
    }

    private void ClearCachedAnchor()
    {
        _cachedAnchorButton = null;
        _cachedAnchorParent = null;
    }

    private static Button? FindCollectionsAnchorButton()
    {
        var anchoredButton = TryFindButtonFromCollectionsController();
        if (anchoredButton != null)
            return anchoredButton;

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

    private static Button? TryFindButtonFromCollectionsController()
    {
        foreach (var controller in Resources.FindObjectsOfTypeAll<CollectionUIController>())
        {
            if (controller == null || !controller.gameObject.activeInHierarchy)
                continue;

            var parent = AccessTools.Field(typeof(CollectionUIController), "collectionButtonParent")
                ?.GetValue(controller) as Transform;
            if (parent == null)
                continue;

            foreach (var button in parent.GetComponentsInChildren<CollectionsNavigationButton>(true))
            {
                if (button == null || !button.gameObject.activeInHierarchy)
                    continue;
                if (button.name == EntryObjectName)
                    continue;

                return button;
            }
        }

        return null;
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
