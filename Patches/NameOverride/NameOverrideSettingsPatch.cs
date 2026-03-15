#pragma warning disable CS0436
using System;
using System.Linq;
using BazaarPlusPlus.Game.NameOverride;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class NameOverrideSettingsAwakePatch
{
    private const string ToggleObjectName = "BPP_NameOverrideToggle";

    private static readonly NameOverrideSettingsMenuBridge Bridge = new(
        ReadEnabledValue,
        WriteEnabledValue,
        NameOverrideUiRefresh.TryRefreshVisibleHeroBanners
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
            BppLog.Error("NameOverride", "Failed to add settings toggle", ex);
        }
    }

    internal static void EnsureToggleExists(OptionsDialogController instance)
    {
        var anchorToggle = GetAnchorToggle(instance);
        if (anchorToggle == null)
        {
            BppLog.Warn("NameOverride", "Could not find gameplay settings anchor toggle");
            return;
        }

        var anchorRow = anchorToggle.transform.parent;
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
        var labelText = NameOverrideSettingsMenuLabel.Resolve(PlayerPreferences.Data.LanguageCode);
        var label = toggleObject
            .GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text.text));
        if (label != null)
            label.text = labelText;
    }

    private static bool ReadEnabledValue()
    {
        var entry = ModState.EnableNameOverrideConfig;
        return entry != null && entry.Value;
    }

    private static void WriteEnabledValue(bool enabled)
    {
        var entry = ModState.EnableNameOverrideConfig;
        if (entry != null)
            entry.Value = enabled;
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class NameOverrideSettingsOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            NameOverrideSettingsAwakePatch.EnsureToggleExists(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("NameOverride", "Failed to sync settings toggle", ex);
        }
    }
}
