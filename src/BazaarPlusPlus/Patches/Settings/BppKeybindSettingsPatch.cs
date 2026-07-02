#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Patches.Settings;

[HarmonyPatch(typeof(OptionsDialogController), "Awake")]
internal static class BppKeybindSettingsAwakePatch
{
    internal const string EnchantPreviewObjectName = "BPP_Keybind_EnchantPreview";
    internal const string UpgradePreviewObjectName = "BPP_Keybind_UpgradePreview";
    internal const string ToggleCollectionPanelObjectName = "BPP_Keybind_ToggleCollectionPanel";
    internal const string ToggleLiveBuildPanelObjectName = "BPP_Keybind_ToggleLiveBuildPanel";
    internal const string ToggleHistoryPanelObjectName = "BPP_Keybind_ToggleHistoryPanel";

    private static readonly BppKeybindDefinition[] Definitions =
    [
        new(EnchantPreviewObjectName, BppHotkeyActionId.HoldEnchantPreview),
        new(UpgradePreviewObjectName, BppHotkeyActionId.HoldUpgradePreview),
        new(ToggleCollectionPanelObjectName, BppHotkeyActionId.ToggleCollectionPanel),
        new(ToggleLiveBuildPanelObjectName, BppHotkeyActionId.ToggleLiveBuildPanel),
        new(ToggleHistoryPanelObjectName, BppHotkeyActionId.ToggleHistoryPanel),
    ];
    internal static readonly string[] DefinitionObjectNames =
    [
        EnchantPreviewObjectName,
        UpgradePreviewObjectName,
        ToggleCollectionPanelObjectName,
        ToggleLiveBuildPanelObjectName,
        ToggleHistoryPanelObjectName,
    ];
    private static readonly FieldInfo? KeybindObjectsField = AccessTools.Field(
        typeof(OptionsDialogController),
        "_keybindObjects"
    );

    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        BppKeybindSettingsPatchSupport.RunRefresh(
            __instance,
            "Failed to install keybind rows",
            instance => EnsureKeybindRows(instance)
        );
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

        ArrangeRows(templateRow, DefinitionObjectNames);
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

    internal static Transform? FindRowContainer(OptionsDialogController instance)
    {
        return GetTemplateRow(instance)?.parent;
    }

    private static Transform? GetTemplateRow(OptionsDialogController instance)
    {
        var keybindRows = KeybindObjectsField?.GetValue(instance) as RectTransform[];
        if (keybindRows == null)
            return null;

        Transform? templateRow = null;
        foreach (var candidate in keybindRows)
        {
            if (candidate != null && candidate.GetComponent<KeyBindController>() != null)
                templateRow = candidate;
        }

        return templateRow;
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
        internal BppKeybindDefinition(string objectName, BppHotkeyActionId actionId)
        {
            ObjectName = objectName;
            ActionId = actionId;
        }

        internal string ObjectName { get; }
        internal BppHotkeyActionId ActionId { get; }
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnEnable")]
internal static class BppKeybindSettingsOnEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        BppKeybindSettingsPatchSupport.RunRefresh(
            __instance,
            "Failed to refresh keybind rows",
            BppKeybindSettingsAwakePatch.EnsureKeybindRows
        );
    }
}

[HarmonyPatch(typeof(OptionsDialogController), "OnGameplayButtonClick")]
internal static class BppKeybindSettingsGameplayOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(OptionsDialogController __instance)
    {
        BppKeybindSettingsPatchSupport.RunRefresh(
            __instance,
            "Failed to refresh keybind rows after gameplay menu opened",
            instance => BppKeybindSettingsAwakePatch.EnsureKeybindRows(instance)
        );
    }
}

internal static class BppKeybindSettingsPatchSupport
{
    internal static void RunRefresh(
        OptionsDialogController instance,
        string failureLogMessage,
        Action<OptionsDialogController> action
    )
    {
        BppKeybindSettingsRefreshDriver.Attach(instance).RequestRefresh();

        try
        {
            action(instance);
        }
        catch (Exception ex)
        {
            BppLog.Error("BppKeybindSettings", failureLogMessage, ex);
        }
    }
}

internal sealed class BppKeybindSettingsRefreshDriver : MonoBehaviour
{
    private const int RetryFrames = 120;

    private OptionsDialogController? _controller;
    private Coroutine? _refreshCoroutine;

    internal static BppKeybindSettingsRefreshDriver Attach(OptionsDialogController controller)
    {
        var driver =
            controller.GetComponent<BppKeybindSettingsRefreshDriver>()
            ?? controller.gameObject.AddComponent<BppKeybindSettingsRefreshDriver>();
        driver._controller = controller;
        return driver;
    }

    internal void RequestRefresh()
    {
        if (_refreshCoroutine != null)
            StopCoroutine(_refreshCoroutine);

        _refreshCoroutine = StartCoroutine(RefreshRoutine());
    }

    private void OnDisable()
    {
        if (_refreshCoroutine == null)
            return;

        StopCoroutine(_refreshCoroutine);
        _refreshCoroutine = null;
    }

    private IEnumerator RefreshRoutine()
    {
        for (var frame = 0; frame < RetryFrames; frame++)
        {
            if (_controller == null)
                yield break;

            try
            {
                BppKeybindSettingsAwakePatch.EnsureKeybindRows(_controller);
                BppKeybindSettingsAwakePatch.RefreshLanguage(_controller);

                if (HasInstalledRows(_controller))
                {
                    _refreshCoroutine = null;
                    yield break;
                }
            }
            catch (Exception ex)
            {
                BppLog.Error(
                    "BppKeybindSettings",
                    "Failed during deferred keybind row refresh",
                    ex
                );
            }

            yield return null;
        }

        _refreshCoroutine = null;
    }

    private static bool HasInstalledRows(OptionsDialogController controller)
    {
        var container = BppKeybindSettingsAwakePatch.FindRowContainer(controller);
        if (container == null)
            return false;

        foreach (var objectName in BppKeybindSettingsAwakePatch.DefinitionObjectNames)
        {
            if (container.Find(objectName) == null)
                return false;
        }

        return true;
    }
}
