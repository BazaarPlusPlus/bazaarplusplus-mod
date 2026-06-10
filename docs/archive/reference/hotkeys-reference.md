---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Hotkeys Reference

## Player-Facing

| Hotkey | Function | Scope | Rebindable |
| --- | --- | --- | --- |
| `F8` | Toggle `HistoryPanel` | Lobby / non-combat UI | No |
| `Esc` | Close `HistoryPanel` | `HistoryPanel` only | No |
| `Caps Lock` | Toggle `LiveBuildPanel` | Live run / non-combat UI | No |
| `Esc` | Close `LiveBuildPanel` | `LiveBuildPanel` only | No |
| `Tab` | Toggle `CollectionPanel` | Lobby / non-combat UI | No |
| `Esc` | Close `CollectionPanel` | `CollectionPanel` only | No |
| `Ctrl` | Force enchant preview (overrides EnchantPreview Mode) | Tooltip hover | Yes |
| `Shift` | Force upgrade preview (overrides UpgradePreview Mode) | Tooltip hover | Yes |

## Rebindable Bazaar++ Actions

- `HoldEnchantPreview`
- `HoldUpgradePreview`

当前由 `src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs` 管理，默认值分别是 `Ctrl` 和 `Shift`，支持鼠标按键绑定，并在保存前拒绝 Bazaar++ 动作内部冲突。

## Source Files

- `src/BazaarPlusPlus/Game/Input/KeyBindings.cs`
- `src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs`
- `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs`
- `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs`
- `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs`
- `src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs`
- `src/BazaarPlusPlus/Patches/Tooltips/ItemEnchantPreviewPatch.cs`
- `src/BazaarPlusPlus/Patches/Tooltips/UpgradePreviewTooltipPatch.cs`
