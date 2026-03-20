#pragma warning disable CS0436
#nullable enable
using System;
using System.Linq;
using BazaarPlusPlus.Game.Input;
using HarmonyLib;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class BppKeybindSettingsAwakePatch
{
    private const string EnchantPreviewEnglishLabel = "Show Enchant Preview";
    private const string UpgradePreviewEnglishLabel = "Show Upgrade Preview";

    private static readonly BppKeybindDefinition[] Definitions =
    [
        new(
            "BPP_Keybind_EnchantPreview",
            BppHotkeyActionId.HoldEnchantPreview,
            EnchantPreviewEnglishLabel
        ),
        new(
            "BPP_Keybind_UpgradePreview",
            BppHotkeyActionId.HoldUpgradePreview,
            UpgradePreviewEnglishLabel
        ),
    ];

    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            EnsureKeybindRows(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("BppKeybindSettings", "Failed to install keybind rows", ex);
        }
    }

    internal static void EnsureKeybindRows(OptionsDialogController instance)
    {
        var templateRow = GetTemplateRow(instance);
        if (templateRow == null)
        {
            BppLog.Warn("BppKeybindSettings", "Could not find keybind template row");
            return;
        }

        Transform anchorRow = templateRow;
        foreach (var definition in Definitions)
            anchorRow = EnsureKeybindRow(definition, templateRow, anchorRow) ?? anchorRow;

        ArrangeRows(templateRow, Definitions.Select(definition => definition.ObjectName).ToArray());
    }

    internal static void RefreshLanguage(OptionsDialogController instance)
    {
        if (instance == null)
            return;

        foreach (var controller in instance.GetComponentsInChildren<BppKeyBindRowController>(true))
            controller.RefreshLanguage();
    }

    private static Transform? EnsureKeybindRow(
        BppKeybindDefinition definition,
        Transform templateRow,
        Transform anchorRow
    )
    {
        var container = templateRow.parent;
        if (container == null)
            return null;

        var existing = container.Find(definition.ObjectName);
        if (existing != null)
        {
            ConfigureRow(existing.gameObject, definition);
            SettingsMenuLayoutUtility.ArrangeRow(anchorRow, existing);
            return existing;
        }

        var cloneObject = UnityEngine.Object.Instantiate(templateRow.gameObject, container);
        cloneObject.name = definition.ObjectName;

        ConfigureRow(cloneObject, definition);
        SettingsMenuLayoutUtility.ArrangeRow(anchorRow, cloneObject.transform);
        return cloneObject.transform;
    }

    private static void ConfigureRow(GameObject rowObject, BppKeybindDefinition definition)
    {
        var nativeController = rowObject.GetComponent<KeyBindController>();
        var controller =
            rowObject.GetComponent<BppKeyBindRowController>()
            ?? rowObject.AddComponent<BppKeyBindRowController>();
        controller.Initialize(definition.ActionId, nativeController);
    }

    private static Transform? GetTemplateRow(OptionsDialogController instance)
    {
        var field = AccessTools.Field(typeof(OptionsDialogController), "_keybindObjects");
        var keybindRows = field?.GetValue(instance) as RectTransform[];
        if (keybindRows == null)
            return null;

        return keybindRows
            .Where(candidate =>
                candidate != null && candidate.GetComponent<KeyBindController>() != null
            )
            .LastOrDefault();
    }

    private static void ArrangeRows(Transform templateRow, params string[] rowObjectNames)
    {
        if (templateRow == null || rowObjectNames == null || rowObjectNames.Length == 0)
            return;

        var parent = templateRow.parent;
        if (parent == null)
            return;

        var currentAnchor = templateRow;
        foreach (var rowObjectName in rowObjectNames)
        {
            var row = parent.Find(rowObjectName);
            if (row == null)
                continue;

            SettingsMenuLayoutUtility.ArrangeRow(currentAnchor, row);
            currentAnchor = row;
        }
    }

    private sealed class BppKeybindDefinition
    {
        internal BppKeybindDefinition(
            string objectName,
            BppHotkeyActionId actionId,
            string englishLabel
        )
        {
            ObjectName = objectName;
            ActionId = actionId;
            EnglishLabel = englishLabel;
        }

        internal string ObjectName { get; }
        internal BppHotkeyActionId ActionId { get; }
        internal string EnglishLabel { get; }
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class BppKeybindSettingsOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        try
        {
            BppKeybindSettingsAwakePatch.EnsureKeybindRows(__instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("BppKeybindSettings", "Failed to refresh keybind rows", ex);
        }
    }
}
