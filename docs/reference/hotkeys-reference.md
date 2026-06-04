# Hotkeys Reference

## Player-Facing

| Hotkey | Function | Scope | Rebindable |
| --- | --- | --- | --- |
| `F8` | Toggle `HistoryPanel` | Lobby / non-combat UI | No |
| `Esc` | Close `HistoryPanel` | `HistoryPanel` only | No |
| `Ctrl` | Force enchant preview (overrides EnchantPreview Mode) | Tooltip hover | Yes |
| `Shift` | Force upgrade preview (overrides UpgradePreview Mode) | Tooltip hover | Yes |

## Rebindable Bazaar++ Actions

- `HoldEnchantPreview`
- `HoldUpgradePreview`

当前由 `Game/Input/BppHotkeyService.cs` 管理，默认值分别是 `Ctrl` 和 `Shift`，支持鼠标按键绑定，并在保存前拒绝 Bazaar++ 动作内部冲突。

## Card Set Preview

以下热键仅在卡组预览选择模式下生效：

| Hotkey | Function |
| --- | --- |
| `Caps Lock` | Toggle selection mode |
| `A` | Switch to `Selected Set` |
| `D` | Switch to `Ten-Win Build` |
| `Tab` | Cycle `Selected Set` -> `Ten-Win Build` |
| `W / S` | Browse matched build candidates |

## Source Files

- `Game/Input/KeyBindings.cs`
- `Game/Input/BppHotkeyService.cs`
- `Game/CardSetPreview/CardSetPreviewRuntime.cs`
- `Game/HistoryPanel/HistoryPanel.cs`
- `Patches/Settings/BppKeybindSettingsPatch.cs`
- `Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
