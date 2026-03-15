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
            return;

        var parent = anchorToggle.transform.parent;
        if (parent == null)
            return;

        var existing = parent.Find(ToggleObjectName)?.GetComponent<Toggle>();
        if (existing != null)
        {
            SyncToggle(existing);
            return;
        }

        var cloneObject = UnityEngine.Object.Instantiate(anchorToggle.gameObject, parent);
        cloneObject.name = ToggleObjectName;
        cloneObject.transform.SetSiblingIndex(anchorToggle.transform.GetSiblingIndex() + 1);

        var cloneToggle = cloneObject.GetComponent<Toggle>();
        if (cloneToggle == null)
            return;

        SetToggleLabel(cloneObject);
        SyncToggle(cloneToggle);
        cloneToggle.onValueChanged.RemoveAllListeners();
        cloneToggle.onValueChanged.AddListener(Bridge.ApplyValue);
    }

    internal static void SyncToggle(Toggle toggle)
    {
        SetToggleLabel(toggle.gameObject);
        toggle.SetIsOnWithoutNotify(Bridge.GetInitialValue());
    }

    private static Toggle GetAnchorToggle(OptionsDialogController instance)
    {
        var field = AccessTools.Field(typeof(OptionsDialogController), "_fastForwardFirstFight");
        return field?.GetValue(instance) as Toggle;
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
