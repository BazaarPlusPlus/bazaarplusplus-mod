#pragma warning disable CS0436
using System;
using System.Linq;
using BazaarPlusPlus.Game.CombatStatusBar;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class CombatStatusBarSettingsAwakePatch
{
    private const string ToggleObjectName = "BPP_CombatStatusBarToggle";

    private static readonly CombatStatusBarSettingsMenuBridge Bridge = new(
        CombatStatusBar.GetEnabledSettingValue,
        CombatStatusBar.SetEnabledSettingValue
    );

    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            EnsureToggleExists(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatStatusBar", "Failed to add settings toggle", ex);
        }
    }

    internal static void EnsureToggleExists(OptionsDialogController instance)
    {
        var anchorToggle = GetAnchorToggle(instance);
        if (anchorToggle == null)
        {
            BppLog.Warn("CombatStatusBar", "Could not find gameplay settings anchor toggle");
            return;
        }

        var anchorRow = GetAnchorRow(anchorToggle);
        if (anchorRow == null)
            return;

        var container = anchorRow.parent;
        if (container == null)
            return;

        var existing = container.Find(ToggleObjectName);
        if (existing != null)
        {
            var existingToggle = existing.GetComponentInChildren<Toggle>(includeInactive: true);
            if (existingToggle == null)
                return;

            ConfigureToggle(existing.gameObject, existingToggle);
            SettingsMenuLayoutUtility.ArrangeRow(anchorRow, existing);
            return;
        }

        var cloneObject = UnityEngine.Object.Instantiate(anchorRow.gameObject, container);
        cloneObject.name = ToggleObjectName;

        var cloneTransform = cloneObject.transform;
        var cloneToggle = cloneObject.GetComponentInChildren<Toggle>(includeInactive: true);
        if (cloneToggle == null)
            return;

        ConfigureToggle(cloneObject, cloneToggle);
        SettingsMenuLayoutUtility.ArrangeRow(anchorRow, cloneTransform);
    }

    internal static void SyncToggle(GameObject toggleObject, Toggle toggle)
    {
        SetToggleLabel(toggleObject);
        toggle.SetIsOnWithoutNotify(Bridge.GetInitialValue());
    }

    private static void ConfigureToggle(GameObject toggleObject, Toggle toggle)
    {
        SyncToggle(toggleObject, toggle);
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(Bridge.ApplyValue);
    }

    private static Transform GetAnchorRow(Toggle anchorToggle)
    {
        return anchorToggle.transform.parent;
    }

    private static Toggle GetAnchorToggle(OptionsDialogController instance)
    {
        var field = AccessTools.Field(typeof(OptionsDialogController), "_fastForwardFirstFight");
        var toggle = field?.GetValue(instance) as Toggle;
        if (toggle != null)
            return toggle;

        return instance
            .GetComponentsInChildren<Toggle>(includeInactive: true)
            .FirstOrDefault(candidate =>
                candidate != null
                && !string.IsNullOrWhiteSpace(candidate.name)
                && candidate.name.IndexOf("FastForward", StringComparison.OrdinalIgnoreCase) >= 0
            );
    }

    private static void SetToggleLabel(GameObject toggleObject)
    {
        var labelText = CombatStatusBarSettingsMenuLabel.Resolve(PlayerPreferences.Data.LanguageCode);
        var label = toggleObject
            .GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text.text));
        if (label != null)
            label.text = labelText;
    }

}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class CombatStatusBarSettingsOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            CombatStatusBarSettingsAwakePatch.EnsureToggleExists(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatStatusBar", "Failed to sync settings toggle", ex);
        }
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnGameplayButtonClick")]
internal static class CombatStatusBarSettingsGameplayOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            CombatStatusBarSettingsAwakePatch.EnsureToggleExists(__instance);
            NameOverrideSettingsAwakePatch.EnsureToggleExists(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("CombatStatusBar", "Failed to refresh settings toggles after gameplay menu opened", ex);
        }
    }
}

internal static class SettingsMenuLayoutUtility
{
    private const float FallbackSpacing = 8f;

    internal static void Rebuild(RectTransform rectTransform)
    {
        var current = rectTransform;
        while (current != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(current);
            current = current.parent as RectTransform;
        }
    }

    internal static void ArrangeRow(Transform anchorRow, Transform cloneRow)
    {
        if (anchorRow == null || cloneRow == null)
            return;

        cloneRow.SetSiblingIndex(anchorRow.GetSiblingIndex() + 1);

        var parentRect = anchorRow.parent as RectTransform;
        if (parentRect == null)
            return;

        if (HasAutomaticLayout(parentRect))
        {
            BppLog.Debug("SettingsMenu", $"Using automatic layout for {cloneRow.name}");
            Rebuild(parentRect);
            return;
        }

        var anchorRect = anchorRow as RectTransform;
        var cloneRect = cloneRow as RectTransform;
        if (anchorRect == null || cloneRect == null)
        {
            Rebuild(parentRect);
            return;
        }

        var additionalIndex = parentRect
            .Cast<Transform>()
            .Where(child => child != null && child != anchorRow && child.name.StartsWith("BPP_"))
            .OrderBy(child => child.GetSiblingIndex())
            .ToList()
            .FindIndex(child => child == cloneRow);

        if (additionalIndex < 0)
            additionalIndex = 0;

        var step = GetVerticalStep(anchorRect, cloneRect);
        cloneRect.anchorMin = anchorRect.anchorMin;
        cloneRect.anchorMax = anchorRect.anchorMax;
        cloneRect.pivot = anchorRect.pivot;
        cloneRect.sizeDelta = anchorRect.sizeDelta;
        cloneRect.anchoredPosition = anchorRect.anchoredPosition + new Vector2(0f, -step * (additionalIndex + 1));
        cloneRect.localScale = anchorRect.localScale;
        cloneRect.localRotation = anchorRect.localRotation;

        BppLog.Info(
            "SettingsMenu",
            $"Positioned {cloneRow.name} below {anchorRow.name}: index={additionalIndex + 1}, step={step:F1}, position={cloneRect.anchoredPosition}"
        );
        ExpandParentIfNeeded(parentRect, anchorRect, step, additionalIndex + 1);
        Rebuild(parentRect);
    }

    private static bool HasAutomaticLayout(RectTransform rectTransform)
    {
        return rectTransform.GetComponent<LayoutGroup>() != null
            || rectTransform.GetComponent<ContentSizeFitter>() != null;
    }

    private static float GetVerticalStep(RectTransform anchorRect, RectTransform cloneRect)
    {
        var preferredAnchorHeight = LayoutUtility.GetPreferredHeight(anchorRect);
        var preferredCloneHeight = LayoutUtility.GetPreferredHeight(cloneRect);
        var height = Mathf.Max(anchorRect.rect.height, cloneRect.rect.height, preferredAnchorHeight, preferredCloneHeight);
        if (height <= 0f)
            height = 48f;

        return height + FallbackSpacing;
    }

    private static void ExpandParentIfNeeded(RectTransform parentRect, RectTransform anchorRect, float step, int cloneCount)
    {
        var requiredBottom = Mathf.Abs(anchorRect.anchoredPosition.y) + step * cloneCount + anchorRect.rect.height;
        if (requiredBottom <= parentRect.rect.height)
            return;

        var size = parentRect.sizeDelta;
        size.y += requiredBottom - parentRect.rect.height;
        parentRect.sizeDelta = size;
        BppLog.Info(
            "SettingsMenu",
            $"Expanded parent {parentRect.name} height to {parentRect.sizeDelta.y:F1} for additional settings toggles"
        );
    }
}
