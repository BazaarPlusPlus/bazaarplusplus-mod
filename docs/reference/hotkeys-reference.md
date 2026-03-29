# Hotkeys Reference

## Scope

This note lists the Bazaar++ hotkeys that are currently active in the shipped mod, grouped by
player-facing features, debug-only features, and panel-internal tuning controls.

## Player-Facing Hotkeys

| Hotkey | Function | Scope | Rebindable | Notes |
| --- | --- | --- | --- | --- |
| `F8` | Toggle `HistoryPanel` | Global | No | Opens or closes the run-history panel. |
| `Esc` | Close `HistoryPanel` | `HistoryPanel` only | No | Closes the panel when it is open. |
| `Ctrl` | Show enchant preview | Tooltip hover | Yes | Default binding for `HoldEnchantPreview`. |
| `Shift` | Show upgrade preview | Tooltip hover | Yes | Default binding for `HoldUpgradePreview`. |

## Rebindable Bazaar++ Actions

Only two Bazaar++ actions currently integrate with the native keybind settings UI:

- `HoldEnchantPreview`
- `HoldUpgradePreview`

Runtime behavior is owned by `Game/Input/BppHotkeyService.cs`.

Current behavior:

- default enchant preview binding is `Ctrl`
- default upgrade preview binding is `Shift`
- supported mouse bindings include `LMB`, `RMB`, `MMB`, `BACK`, and `FORWARD`
- conflicting Bazaar++ bindings are rejected before save

## Debug-Only Hotkeys

These hotkeys are only available when the debug-only `DebugPanel` is mounted.

| Hotkey | Function | Scope | Rebindable |
| --- | --- | --- | --- |
| `F2` | Toggle `DebugPanel` | Debug only | No |
| `1` | Select `Summary` section | `DebugPanel` only | No |
| `2` | Select `Preview` section | `DebugPanel` only | No |
| `3` | Select `Run` section | `DebugPanel` only | No |
| `4` | Select `Encounters` section | `DebugPanel` only | No |
| `5` | Select `Replays` section | `DebugPanel` only | No |
| `Tab` | Toggle All / Single section view | `DebugPanel` only | No |

## HistoryPanel Preview Tuning

These are internal preview-tuning controls inside `HistoryPanel`. They are not exposed as normal
player-facing keybinds.

All of the following require `Ctrl` to be held while `HistoryPanel` is open:

| Hotkey | Function |
| --- | --- |
| `Ctrl + Left / Right` | Nudge board horizontal offset |
| `Ctrl + Up / Down` | Nudge camera depth |
| `Ctrl + PageUp / PageDown` | Nudge camera vertical center |
| `Ctrl + Q / E` or `Ctrl + Home / End` | Nudge card width scale |
| `Ctrl + Alt + Q / E` or `Ctrl + Alt + Home / End` | Nudge card height scale |
| `Ctrl + [ / ]` | Nudge card spacing |
| `Ctrl + - / =` | Nudge field of view |
| `Ctrl + Backspace` | Reset preview tuning |

## Source Files

- `Game/Input/KeyBindings.cs`
- `Game/Input/BppHotkeyService.cs`
- `Game/CombatStatusBar/CombatStatusBar.cs`
- `Game/HistoryPanel/HistoryPanel.cs`
- `Game/DebugPanel/DebugPanel.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`
- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
