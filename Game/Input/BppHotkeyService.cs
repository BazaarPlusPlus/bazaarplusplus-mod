#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BazaarPlusPlus.Game.Input;

internal static class BppHotkeyService
{
    private const string KeyboardPrefix = "<Keyboard>/";
    private const string MousePrefix = "<Mouse>/";
    private const string CtrlAliasPath = "<Keyboard>/ctrl";
    private const string ShiftAliasPath = "<Keyboard>/shift";

    private static readonly IReadOnlyDictionary<string, string> BindingDisplayAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CtrlAliasPath] = "Ctrl",
            [ShiftAliasPath] = "Shift",
            [MousePrefix + "leftButton"] = "LMB",
            [MousePrefix + "rightButton"] = "RMB",
            [MousePrefix + "middleButton"] = "MMB",
            [MousePrefix + "backButton"] = "BMB",
            [MousePrefix + "forwardButton"] = "FMB",
        };

    private static readonly IReadOnlyDictionary<BppHotkeyActionId, string> DefaultBindingPaths =
        new Dictionary<BppHotkeyActionId, string>
        {
            [BppHotkeyActionId.HoldEnchantPreview] = CtrlAliasPath,
            [BppHotkeyActionId.HoldUpgradePreview] = ShiftAliasPath,
        };

    internal static bool IsHeld(
        BppHotkeyActionId actionId,
        Keyboard? keyboard = null,
        Mouse? mouse = null
    )
    {
        keyboard ??= Keyboard.current;
        mouse ??= Mouse.current;

        return ExpandBindingPaths(GetBindingPath(actionId))
            .Any(path => IsPathHeld(path, keyboard, mouse));
    }

    internal static string GetBindingPath(BppHotkeyActionId actionId)
    {
        var configValue = GetConfigValue(actionId);
        var normalized = NormalizeBindingPath(configValue);
        return string.IsNullOrWhiteSpace(normalized) ? GetDefaultBindingPath(actionId) : normalized;
    }

    internal static string GetBindingDisplay(BppHotkeyActionId actionId)
    {
        return GetBindingDisplay(GetBindingPath(actionId));
    }

    internal static string GetBindingDisplay(string bindingPath)
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (BindingDisplayAliases.TryGetValue(normalized, out var alias))
            return alias;

        var display = InputControlPath.ToHumanReadableString(
            normalized,
            InputControlPath.HumanReadableStringOptions.OmitDevice
        );
        if (string.IsNullOrWhiteSpace(display))
            return normalized;

        return normalized.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase)
            ? $"{display}"
            : display;
    }

    internal static bool UsesDefault(BppHotkeyActionId actionId)
    {
        return string.Equals(
            GetBindingPath(actionId),
            GetDefaultBindingPath(actionId),
            StringComparison.OrdinalIgnoreCase
        );
    }

    internal static void ResetToDefault(BppHotkeyActionId actionId)
    {
        SetConfigValue(actionId, GetDefaultBindingPath(actionId));
    }

    internal static bool TrySetBindingPath(
        BppHotkeyActionId actionId,
        string? bindingPath,
        out string? errorMessage
    )
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            errorMessage = BppKeybindLabelResolver.ResolveUnsupportedKey(
                PlayerPreferences.Data.LanguageCode
            );
            return false;
        }

        if (TryGetConflictingAction(actionId, normalized, out var conflictingAction))
        {
            errorMessage =
                $"{BppKeybindLabelResolver.ResolveActionLabel(actionId, PlayerPreferences.Data.LanguageCode)} conflicts with {BppKeybindLabelResolver.ResolveActionLabel(conflictingAction, PlayerPreferences.Data.LanguageCode)}";
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
        var candidatePaths = new HashSet<string>(
            ExpandBindingPaths(candidatePath),
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var otherAction in DefaultBindingPaths.Keys)
        {
            if (otherAction == actionId)
                continue;

            if (candidatePaths.Overlaps(ExpandBindingPaths(GetBindingPath(otherAction))))
            {
                conflictingAction = otherAction;
                return true;
            }
        }

        conflictingAction = default;
        return false;
    }

    private static IEnumerable<string> ExpandBindingPaths(string bindingPath)
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return normalized;

        if (string.Equals(normalized, CtrlAliasPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return KeyboardPrefix + "leftCtrl";
            yield return KeyboardPrefix + "rightCtrl";
            yield break;
        }

        if (string.Equals(normalized, ShiftAliasPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return KeyboardPrefix + "leftShift";
            yield return KeyboardPrefix + "rightShift";
        }
    }

    private static bool IsPathHeld(string bindingPath, Keyboard? keyboard, Mouse? mouse)
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (string.Equals(normalized, CtrlAliasPath, StringComparison.OrdinalIgnoreCase))
        {
            if (keyboard == null)
                return false;
            return keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        }

        if (string.Equals(normalized, ShiftAliasPath, StringComparison.OrdinalIgnoreCase))
        {
            if (keyboard == null)
                return false;
            return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        if (normalized.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryFindSupportedMouseButton(normalized, mouse, out var button))
                return false;

            return button.isPressed;
        }

        if (!TryParseKey(normalized, out var key))
            return false;

        if (keyboard == null)
            return false;

        return keyboard[key].isPressed;
    }

    private static bool TryParseKey(string bindingPath, out Key key)
    {
        key = default;
        const string separator = "/";
        var segmentIndex = bindingPath.LastIndexOf(separator, StringComparison.Ordinal);
        if (segmentIndex < 0 || segmentIndex >= bindingPath.Length - 1)
            return false;

        var keyName = bindingPath[(segmentIndex + 1)..];
        return Enum.TryParse(keyName, ignoreCase: true, out key);
    }

    private static string NormalizeBindingPath(string? bindingPath)
    {
        if (string.IsNullOrWhiteSpace(bindingPath))
            return string.Empty;

        var trimmed = bindingPath.Trim();
        if (trimmed.StartsWith(KeyboardPrefix, StringComparison.OrdinalIgnoreCase))
            return KeyboardPrefix + trimmed[KeyboardPrefix.Length..];

        if (!trimmed.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (!TryGetMouseButtonName(trimmed, out var buttonName))
            return string.Empty;

        var normalized = MousePrefix + buttonName;
        if (IsExplicitlyUnsupportedMousePath(normalized))
            return string.Empty;

        if (
            TryFindMouseControl(buttonName, Mouse.current, out var control)
            && control is not ButtonControl
        )
        {
            return string.Empty;
        }

        return normalized;
    }

    private static bool TryFindSupportedMouseButton(
        string bindingPath,
        Mouse? mouse,
        out ButtonControl button
    )
    {
        button = default!;
        return TryGetMouseButtonName(bindingPath, out var buttonName)
            && TryFindMouseControl(buttonName, mouse, out var control)
            && control is ButtonControl candidate
            && !candidate.synthetic
            && (button = candidate) != null;
    }

    private static bool TryGetMouseButtonName(string bindingPath, out string buttonName)
    {
        buttonName = string.Empty;
        if (!bindingPath.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        buttonName = bindingPath[MousePrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(buttonName);
    }

    private static bool TryFindMouseControl(
        string buttonName,
        Mouse? mouse,
        out InputControl? control
    )
    {
        control = mouse?.allControls.FirstOrDefault(candidate =>
            string.Equals(candidate.name, buttonName, StringComparison.OrdinalIgnoreCase)
        );
        return control != null;
    }

    private static bool IsExplicitlyUnsupportedMousePath(string bindingPath)
    {
        return bindingPath.Contains("scroll", StringComparison.OrdinalIgnoreCase)
            || bindingPath.Contains("position", StringComparison.OrdinalIgnoreCase)
            || bindingPath.Contains("delta", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultBindingPath(BppHotkeyActionId actionId)
    {
        return DefaultBindingPaths[actionId];
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
            BppHotkeyActionId.HoldEnchantPreview => BppRuntimeHost
                .Config
                .EnchantPreviewHotkeyPathConfig,
            BppHotkeyActionId.HoldUpgradePreview => BppRuntimeHost
                .Config
                .UpgradePreviewHotkeyPathConfig,
            _ => null,
        };
    }
}
