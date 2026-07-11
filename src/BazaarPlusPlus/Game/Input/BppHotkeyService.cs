#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BazaarPlusPlus.Game.Input;

internal static class BppHotkeyService
{
    private static IBppConfig? _config;

    public static void Install(IBppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ResetBindingPathCache();
    }

    public static void Reset()
    {
        _config = null;
        ResetBindingPathCache();
        foreach (var action in CachedActions.Values)
        {
            action.Disable();
            action.Dispose();
        }
        CachedActions.Clear();
        LoggedInvalidBindingPaths.Clear();
        LoggedUnresolvedBindingPaths.Clear();
        LoggedModifierDisagreements.Clear();
    }

    private static void ResetBindingPathCache()
    {
        CachedBindingPaths.Clear();
    }

    private static IBppConfig Config =>
        _config
        ?? throw new InvalidOperationException(
            "BppHotkeyService.Install must be called at startup."
        );

    private static readonly Dictionary<
        BppHotkeyActionId,
        (string? Raw, string Resolved)
    > CachedBindingPaths = new();

    private static readonly Dictionary<string, InputAction> CachedActions = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> LoggedInvalidBindingPaths = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> LoggedUnresolvedBindingPaths = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static readonly HashSet<string> LoggedModifierDisagreements = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal static bool IsHeld(
        BppHotkeyActionId actionId,
        Keyboard? keyboard = null,
        Mouse? mouse = null
    )
    {
        keyboard ??= Keyboard.current;
        mouse ??= Mouse.current;

        return IsPressed(GetBindingPath(actionId), keyboard, mouse);
    }

    internal static bool WasPressedThisFrame(string bindingPath)
    {
        var normalized = HotkeyBindingPathCore.Normalize(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (LoggedInvalidBindingPaths.Add(bindingPath ?? string.Empty))
            {
                BppLog.Warn(
                    "BppHotkeyService",
                    $"Rejected hotkey binding path '{bindingPath ?? "<null>"}' because it could not be normalized."
                );
            }

            return false;
        }

        var action = GetOrCreateAction(normalized);
        if (action.controls.Count == 0 && LoggedUnresolvedBindingPaths.Add(normalized))
        {
            BppLog.Warn(
                "BppHotkeyService",
                $"Hotkey binding '{normalized}' resolved to zero input controls after enabling its InputAction."
            );
        }

        return action.WasPressedThisFrame();
    }

    // Toggle-style hotkeys fire on a plain press: while the user is capturing a rebind
    // no toggle may fire, and unless the binding itself is a modifier key, a held
    // Ctrl/Alt/Shift suppresses the press (preserves the legacy plain-Tab semantics).
    internal static bool WasToggleHotkeyPressedThisFrame(
        BppHotkeyActionId actionId,
        Keyboard? keyboard = null
    )
    {
        if (BppKeyBindRowController.IsRebindCaptureActive)
            return false;

        var path = GetBindingPath(actionId);
        if (!WasPressedThisFrame(path))
            return false;

        if (HotkeyBindingPathCore.IsModifierPath(path))
            return true;

        keyboard ??= Keyboard.current;
        return !KeyBindings.Modifiers.IsCtrlPressed(keyboard)
            && !KeyBindings.Modifiers.IsAltPressed(keyboard)
            && !KeyBindings.Modifiers.IsShiftPressed(keyboard);
    }

    // normalizedPath must already be normalized (GetBindingPath output).
    private static bool IsPressed(
        string normalizedPath,
        Keyboard? keyboard = null,
        Mouse? mouse = null
    )
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        if (
            string.Equals(
                normalizedPath,
                HotkeyBindingPathCore.CtrlAliasPath,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return IsModifierPressed(
                normalizedPath,
                () => KeyBindings.Modifiers.IsCtrlPressed(keyboard)
            );

        if (
            string.Equals(
                normalizedPath,
                HotkeyBindingPathCore.ShiftAliasPath,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return IsModifierPressed(
                normalizedPath,
                () => KeyBindings.Modifiers.IsShiftPressed(keyboard)
            );

        if (TryFindSupportedMouseButton(normalizedPath, mouse, out var button))
            return button.isPressed;

        return GetOrCreateAction(normalizedPath).IsPressed();
    }

    internal static string GetBindingPath(BppHotkeyActionId actionId)
    {
        var raw = GetConfigValue(actionId);
        if (
            CachedBindingPaths.TryGetValue(actionId, out var cached)
            && string.Equals(cached.Raw, raw, StringComparison.Ordinal)
        )
            return cached.Resolved;

        var normalized = HotkeyBindingPathCore.Normalize(raw);
        var resolved = string.IsNullOrWhiteSpace(normalized)
            ? HotkeyBindingPathCore.GetDefault(actionId)
            : normalized;
        CachedBindingPaths[actionId] = (raw, resolved);
        return resolved;
    }

    internal static string GetBindingDisplay(BppHotkeyActionId actionId)
    {
        return GetBindingDisplay(GetBindingPath(actionId));
    }

    internal static string GetBindingDisplay(string bindingPath)
    {
        var normalized = HotkeyBindingPathCore.Normalize(bindingPath);
        if (HotkeyBindingPathCore.DisplayAliases.TryGetValue(normalized, out var alias))
            return alias;

        if (string.IsNullOrWhiteSpace(normalized))
            return normalized;

        // Native rows resolve the live control (OS keyboard-layout name); match that
        // instead of the static US-layout name from a control-less ToHumanReadableString.
        var action = GetOrCreateAction(normalized);
        if (action.controls.Count > 0)
        {
            var displayName = action.controls[0].displayName;
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
        }

        var display = InputControlPath.ToHumanReadableString(
            normalized,
            InputControlPath.HumanReadableStringOptions.OmitDevice
        );
        return string.IsNullOrWhiteSpace(display) ? normalized : display;
    }

    internal static bool UsesDefault(BppHotkeyActionId actionId)
    {
        return string.Equals(
            GetBindingPath(actionId),
            HotkeyBindingPathCore.GetDefault(actionId),
            StringComparison.OrdinalIgnoreCase
        );
    }

    internal static void ResetToDefault(BppHotkeyActionId actionId)
    {
        SetConfigValue(actionId, HotkeyBindingPathCore.GetDefault(actionId));
    }

    internal static bool TrySetBindingPath(
        BppHotkeyActionId actionId,
        string? bindingPath,
        out string? errorMessage
    )
    {
        var normalized = HotkeyBindingPathCore.Normalize(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            errorMessage = BppKeybindLabelResolver.ResolveUnsupportedKey(
                PlayerPreferences.Data.LanguageCode
            );
            return false;
        }

        if (TryGetConflictingAction(actionId, normalized, out var conflictingAction))
        {
            errorMessage = BppKeybindLabelResolver.ResolveConflictWarning(
                actionId,
                conflictingAction,
                PlayerPreferences.Data.LanguageCode
            );
            return false;
        }

        SetConfigValue(actionId, normalized);
        errorMessage = null;
        return true;
    }

    private static bool TryGetConflictingAction(
        BppHotkeyActionId actionId,
        string candidatePath,
        out BppHotkeyActionId conflictingAction
    )
    {
        var currentPaths = new Dictionary<BppHotkeyActionId, string>();
        foreach (var otherAction in HotkeyBindingPathCore.DefaultBindingPaths.Keys)
        {
            currentPaths[otherAction] = GetBindingPath(otherAction);
        }

        var conflict = HotkeyBindingPathCore.FindConflict(actionId, candidatePath, currentPaths);
        conflictingAction = conflict.GetValueOrDefault();
        return conflict.HasValue;
    }

    // normalizedPath must already be normalized; every caller passes a
    // HotkeyBindingPathCore.Normalize or GetBindingPath result.
    private static InputAction GetOrCreateAction(string normalizedPath)
    {
        if (CachedActions.TryGetValue(normalizedPath, out var existingAction))
            return existingAction;

        var action = new InputAction(type: InputActionType.Button);
        foreach (var expandedPath in HotkeyBindingPathCore.Expand(normalizedPath))
            action.AddBinding(expandedPath);

        action.Enable();
        CachedActions[normalizedPath] = action;
        return action;
    }

    private static bool IsModifierPressed(string normalizedBindingPath, Func<bool> legacyCheck)
    {
        var legacyPressed = legacyCheck();
        var actionPressed = GetOrCreateAction(normalizedBindingPath).IsPressed();
        if (
            legacyPressed != actionPressed
            && LoggedModifierDisagreements.Add(normalizedBindingPath)
        )
        {
            BppLog.Info(
                "BppHotkeyService",
                $"Modifier disagreement binding={normalizedBindingPath} legacy={legacyPressed} action={actionPressed}"
            );
        }

        return legacyPressed || actionPressed;
    }

    private static bool TryFindSupportedMouseButton(
        string bindingPath,
        Mouse? mouse,
        out ButtonControl button
    )
    {
        button = default!;
        if (
            !HotkeyBindingPathCore.TryGetSupportedMouseButtonName(bindingPath, out var buttonName)
            || mouse == null
        )
            return false;

        var control = buttonName switch
        {
            HotkeyBindingPathCore.LeftMouseButtonName => mouse.leftButton,
            HotkeyBindingPathCore.RightMouseButtonName => mouse.rightButton,
            HotkeyBindingPathCore.MiddleMouseButtonName => mouse.middleButton,
            HotkeyBindingPathCore.BackMouseButtonName => mouse.backButton,
            HotkeyBindingPathCore.ForwardMouseButtonName => mouse.forwardButton,
            _ => null,
        };

        if (control == null || control.synthetic)
            return false;

        button = control;
        return true;
    }

    private static string? GetConfigValue(BppHotkeyActionId actionId)
    {
        return GetConfigEntry(actionId)?.Value;
    }

    private static void SetConfigValue(BppHotkeyActionId actionId, string bindingPath)
    {
        var entry = GetConfigEntry(actionId);
        if (entry != null)
            entry.Value = bindingPath;
    }

    private static BepInEx.Configuration.ConfigEntry<string>? GetConfigEntry(
        BppHotkeyActionId actionId
    )
    {
        return actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => Config.EnchantPreviewHotkeyPathConfig,
            BppHotkeyActionId.HoldUpgradePreview => Config.UpgradePreviewHotkeyPathConfig,
            BppHotkeyActionId.ToggleCollectionPanel => Config.ToggleCollectionPanelHotkeyPathConfig,
            BppHotkeyActionId.ToggleLiveBuildPanel => Config.ToggleLiveBuildPanelHotkeyPathConfig,
            BppHotkeyActionId.ToggleHistoryPanel => Config.ToggleHistoryPanelHotkeyPathConfig,
            _ => null,
        };
    }
}
