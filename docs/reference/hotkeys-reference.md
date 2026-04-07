# Hotkeys Reference

## Player-Facing

| Hotkey | Function | Scope | Rebindable |
| --- | --- | --- | --- |
| `Esc` | Close `HistoryPanel` | `HistoryPanel` only | No |
| `F9` | Save a screenshot | Global | No |
| `Ctrl` | Show enchant preview | Tooltip hover | Yes |
| `Shift` | Show upgrade preview | Tooltip hover | Yes |

## Rebindable Bazaar++ Actions

- `HoldEnchantPreview`
- `HoldUpgradePreview`

当前由 `Game/Input/BppHotkeyService.cs` 管理，默认值分别是 `Ctrl` 和 `Shift`，支持鼠标按键绑定，并在保存前拒绝 Bazaar++ 动作内部冲突。

## DebugPanel

仅在 debug build 可用：

| Hotkey | Function |
| --- | --- |
| `F2` | Toggle `DebugPanel` |
| `1` | `Summary` |
| `2` | `Run` |
| `3` | `Encounters` |
| `4` | `Replays` |
| `Tab` | Toggle All / Single section view |

## HistoryPanel Preview Tuning

以下热键仅在 `HistoryPanel` 打开且按住 `Ctrl` 时生效：

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
- `Game/Screenshots/EndOfRunScreenshotController.cs`
- `Game/HistoryPanel/HistoryPanel.cs`
- `Game/DebugPanel/DebugPanel.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`
- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
