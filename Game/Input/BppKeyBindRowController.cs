#nullable enable
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.Input;
using HarmonyLib;
using TheBazaar.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace BazaarPlusPlus;

internal sealed class BppKeyBindRowController : MonoBehaviour
{
    private readonly List<GameObject> _displayObjects = [];
    private readonly List<GameObject> _editObjects = [];

    private BppHotkeyActionId _actionId;
    private Button? _keybindButton;
    private Button? _resetButton;
    private TextMeshProUGUI? _keybindText;
    private TextMeshProUGUI? _warningText;
    private TextMeshProUGUI? _labelText;
    private bool _isRebinding;
    private bool _initialized;
    private int _rebindStartedFrame = -1;

    internal void Initialize(BppHotkeyActionId actionId, KeyBindController? templateController)
    {
        _actionId = actionId;
        _displayObjects.Clear();
        _editObjects.Clear();

        if (templateController != null)
        {
            templateController.enabled = false;
            CopyTemplateReferences(templateController);
        }

        FindFallbackReferences();

        if (_keybindButton != null)
        {
            _keybindButton.onClick.RemoveAllListeners();
            _keybindButton.onClick.AddListener(EnterRebindState);
        }

        if (_resetButton != null)
        {
            _resetButton.onClick.RemoveAllListeners();
            _resetButton.onClick.AddListener(ResetToDefault);
        }

        _initialized = true;
        EnterDefaultState();
    }

    internal void RefreshLanguage()
    {
        if (!_initialized)
            return;

        UpdateTexts();
        if (_isRebinding)
            ShowWarning(
                BppKeybindLabelResolver.ResolveRebindPrompt(PlayerPreferences.Data.LanguageCode)
            );
    }

    private void Update()
    {
        if (!_initialized || !_isRebinding)
            return;

        if (Time.frameCount == _rebindStartedFrame)
            return;

        var keyboard = Keyboard.current;
        if (keyboard?.escapeKey.wasPressedThisFrame == true)
        {
            EnterDefaultState();
            return;
        }

        if (keyboard != null)
        {
            foreach (var keyControl in keyboard.allKeys)
            {
                if (!keyControl.wasPressedThisFrame)
                    continue;

                var bindingPath = $"<Keyboard>/{keyControl.name}";
                if (BppHotkeyService.TrySetBindingPath(_actionId, bindingPath, out var errorMessage))
                {
                    EnterDefaultState();
                }
                else
                {
                    ShowWarning(errorMessage);
                }

                return;
            }
        }

        var mouse = Mouse.current;
        if (mouse == null)
            return;

        foreach (var buttonControl in mouse.allControls.OfType<ButtonControl>())
        {
            if (buttonControl.synthetic || !buttonControl.wasPressedThisFrame)
                continue;

            if (
                BppHotkeyService.TrySetBindingPath(
                    _actionId,
                    $"<Mouse>/{buttonControl.name}",
                    out var error
                )
            )
            {
                EnterDefaultState();
            }
            else
            {
                ShowWarning(error);
            }

            return;
        }
    }

    private void EnterRebindState()
    {
        _isRebinding = true;
        _rebindStartedFrame = Time.frameCount;
        SetObjectsActive(_displayObjects, false);
        SetObjectsActive(_editObjects, true);
        ShowWarning(
            BppKeybindLabelResolver.ResolveRebindPrompt(PlayerPreferences.Data.LanguageCode)
        );
    }

    private void EnterDefaultState()
    {
        _isRebinding = false;
        _rebindStartedFrame = -1;
        SetObjectsActive(_displayObjects, true);
        SetObjectsActive(_editObjects, false);
        UpdateTexts();
        ShowWarning(null);
    }

    private void ResetToDefault()
    {
        BppHotkeyService.ResetToDefault(_actionId);
        EnterDefaultState();
    }

    private void UpdateTexts()
    {
        var languageCode = PlayerPreferences.Data.LanguageCode;
        if (_labelText != null)
            _labelText.text = BppKeybindLabelResolver.ResolveActionLabel(_actionId, languageCode);

        if (_keybindText != null)
            _keybindText.text = BppHotkeyService.GetBindingDisplay(_actionId);

        if (_resetButton != null)
            _resetButton.interactable = !BppHotkeyService.UsesDefault(_actionId);
    }

    private void ShowWarning(string? message)
    {
        if (_warningText == null)
            return;

        var shouldShow = !string.IsNullOrWhiteSpace(message);
        _warningText.text = shouldShow ? message : string.Empty;
        _warningText.gameObject.SetActive(shouldShow);
    }

    private void CopyTemplateReferences(KeyBindController templateController)
    {
        _keybindButton = GetFieldValue<Button>(templateController, "_keybindButton");
        _resetButton = GetFieldValue<Button>(templateController, "_resetToDefaultButton");
        _keybindText = GetFieldValue<TextMeshProUGUI>(templateController, "_keybindText");
        _warningText = GetFieldValue<TextMeshProUGUI>(templateController, "_warningText");

        var displayObjects = GetFieldValue<List<GameObject>>(
            templateController,
            "_displayKeybindObjects"
        );
        if (displayObjects != null)
            _displayObjects.AddRange(displayObjects.Where(candidate => candidate != null));

        var editObjects = GetFieldValue<List<GameObject>>(templateController, "_editRebindObjects");
        if (editObjects != null)
            _editObjects.AddRange(editObjects.Where(candidate => candidate != null));
    }

    private void FindFallbackReferences()
    {
        _keybindButton ??= GetComponentInChildren<Button>(includeInactive: true);
        _resetButton ??= GetComponentsInChildren<Button>(includeInactive: true)
            .Skip(1)
            .FirstOrDefault();
        _keybindText ??= GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text =>
                text != null && text.transform.IsChildOf(_keybindButton?.transform)
            );
        _warningText ??= GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text =>
                text != null
                && text != _keybindText
                && !string.IsNullOrWhiteSpace(text.text)
                && text.gameObject != _labelText?.gameObject
            );

        _labelText = FindActionLabelText();

        if (_displayObjects.Count == 0 && _keybindButton != null)
            _displayObjects.Add(_keybindButton.gameObject);
    }

    private TextMeshProUGUI? FindActionLabelText()
    {
        return GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text =>
                text != null
                && text != _keybindText
                && text != _warningText
                && (_keybindButton == null || !text.transform.IsChildOf(_keybindButton.transform))
                && (_resetButton == null || !text.transform.IsChildOf(_resetButton.transform))
            );
    }

    private static T? GetFieldValue<T>(object instance, string fieldName)
        where T : class
    {
        return AccessTools.Field(instance.GetType(), fieldName)?.GetValue(instance) as T;
    }

    private static void SetObjectsActive(IEnumerable<GameObject> objects, bool active)
    {
        foreach (var gameObject in objects.Where(candidate => candidate != null))
            gameObject.SetActive(active);
    }
}
