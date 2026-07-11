#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.Settings;

internal enum BppSettingsControlKind
{
    Toggle,
    Choice,
    Action,
}

internal sealed class BppSettingsChoiceState
{
    internal BppSettingsChoiceState(
        IReadOnlyList<string> options,
        int selectedIndex,
        bool hasSyntheticCurrentOption
    )
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        SelectedIndex = selectedIndex;
        HasSyntheticCurrentOption = hasSyntheticCurrentOption;
    }

    internal IReadOnlyList<string> Options { get; }

    internal int SelectedIndex { get; }

    internal bool HasSyntheticCurrentOption { get; }
}

internal sealed class BppSettingsDockDefinition
{
    internal BppSettingsDockDefinition(
        string key,
        Func<string, string> resolveLabel,
        Func<string, string> resolveStatus,
        Func<bool> isActive,
        Action activate,
        bool collapseAfterActivate
    )
    {
        Key = !string.IsNullOrWhiteSpace(key)
            ? key
            : throw new ArgumentException("Key is required.", nameof(key));
        ResolveLabel = resolveLabel ?? throw new ArgumentNullException(nameof(resolveLabel));
        ResolveStatus = resolveStatus ?? throw new ArgumentNullException(nameof(resolveStatus));
        IsActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
        CollapseAfterActivate = collapseAfterActivate;
        ControlKind = BppSettingsControlKind.Action;
    }

    private BppSettingsDockDefinition(
        string key,
        Func<string, string> resolveLabel,
        Func<string, string> resolveStatus,
        Func<bool> isActive,
        Action activate,
        bool collapseAfterActivate,
        BppSettingsControlKind controlKind,
        Func<bool>? readToggle,
        Action<bool>? writeToggle,
        Func<bool>? isInteractable,
        Func<string, BppSettingsChoiceState>? resolveChoiceState,
        Action<int>? selectStandardChoice
    )
        : this(
            key,
            resolveLabel,
            resolveStatus,
            isActive,
            activate,
            collapseAfterActivate
        )
    {
        ControlKind = controlKind;
        ReadToggle = readToggle;
        WriteToggle = writeToggle;
        IsInteractable = isInteractable;
        ResolveChoiceState = resolveChoiceState;
        SelectStandardChoice = selectStandardChoice;
    }

    internal string Key { get; }

    internal Func<string, string> ResolveLabel { get; }

    internal Func<string, string> ResolveStatus { get; }

    internal Func<bool> IsActive { get; }

    internal Action Activate { get; }

    internal bool CollapseAfterActivate { get; }

    internal BppSettingsControlKind ControlKind { get; private set; }

    internal Func<bool>? ReadToggle { get; }

    internal Action<bool>? WriteToggle { get; }

    internal Func<bool>? IsInteractable { get; }

    internal Func<string, BppSettingsChoiceState>? ResolveChoiceState { get; }

    internal Action<int>? SelectStandardChoice { get; }

    internal static BppSettingsDockDefinition Toggle(
        string key,
        Func<string, string> resolveLabel,
        Func<string, string> resolveStatus,
        Func<bool> read,
        Action<bool> write,
        Func<bool>? isInteractable = null,
        Action? activate = null
    ) =>
        new(
            key,
            resolveLabel,
            resolveStatus,
            read,
            activate ?? (() => write(!read())),
            collapseAfterActivate: false,
            BppSettingsControlKind.Toggle,
            read,
            write,
            isInteractable ?? (() => true),
            resolveChoiceState: null,
            selectStandardChoice: null
        );

    internal static BppSettingsDockDefinition Choice(
        string key,
        Func<string, string> resolveLabel,
        Func<string, string> resolveStatus,
        Func<bool> isActive,
        Action activate,
        Func<string, BppSettingsChoiceState> resolveChoiceState,
        Action<int> selectStandardChoice
    ) =>
        new(
            key,
            resolveLabel,
            resolveStatus,
            isActive,
            activate,
            collapseAfterActivate: false,
            BppSettingsControlKind.Choice,
            readToggle: null,
            writeToggle: null,
            isInteractable: () => true,
            resolveChoiceState,
            selectStandardChoice
        );
}
