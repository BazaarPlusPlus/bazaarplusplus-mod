# Hotkeys Reference

## Player-Facing

| Hotkey | Function | Scope | Rebindable |
| --- | --- | --- | --- |
| `F8` | Toggle `HistoryPanel` | Lobby / non-combat UI | No |
| `Esc` | Close `HistoryPanel` | `HistoryPanel` only | No |
| `Ctrl` | Show enchant preview when always-show is disabled | Tooltip hover | Yes |
| `Shift` | Show upgrade preview | Tooltip hover | Yes |

## Rebindable Bazaar++ Actions

- `HoldEnchantPreview`
- `HoldUpgradePreview`

当前由 `Game/Input/BppHotkeyService.cs` 管理，默认值分别是 `Ctrl` 和 `Shift`，支持鼠标按键绑定，并在保存前拒绝 Bazaar++ 动作内部冲突。

## Card Set Preview

以下热键仅在卡组预览选择模式下生效：

| Hotkey | Function |
| --- | --- |
| `Caps Lock` | Toggle selection mode |
| `1` | Switch to `Selected Set` |
| `2` | Switch to `Winner Build` |
| `3` | Switch to `Final Build` |
| `Tab` | Cycle `Selected Set` -> `Winner Build` -> `Final Build` |
| `Up / Down` | Browse matched build candidates |

## HistoryPanel Preview Tuning

以下热键仅在 `HistoryPanel` 打开且按住 `Ctrl` 时生效。按住 `Shift` 会使用更大的步进。

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
- `Game/MonsterPreview/CardSetPreviewRuntime.cs`
- `Game/HistoryPanel/HistoryPanel.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`
- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
