# Tooltip Lock Keybinding Analysis (2026-03-15)

## Summary

关于“是不是有人把查看 tooltip 的按键改掉了”这个问题，当前结论是：

- 原生默认绑定仍然是鼠标右键。
- 原生支持用户改键，改键结果会覆盖默认绑定。
- 当前这台机器上的 `PlayerPreferences.Data.KeyBindings` 为空，没有保存任何 override。
- 因此就当前本机配置而言，tooltip lock / 查看信息的触发键仍然是默认右键。

## Native Default Binding

原生控制 tooltip 锁定切换的主入口不在 `CardController.ShowTooltips()`，而在输入映射链路：

`InputManager.Actions.Lock`
-> `CardTooltipController.LockTooltipToggle()`

关键位置：

- `decompiled/TheBazaarRuntime/TheBazaar.Inputs/InputManager.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipController.cs`
- `decompiled/TheBazaarRuntime/InputSystemActions.cs`

### 1. `Actions.Lock` 的默认映射

在 `InputManager.MapInput()` 中：

```csharp
_mappedActions.Add(
    new MappedKey(
        _inputSystemActions.UI.RightClick,
        Actions.Lock,
        MappedKey.ToggleType.OnRelease,
        _inputSystemActions.UI.RightClick
    ),
    new List<Action>()
);
```

这说明：

- tooltip lock 对应的是 `Actions.Lock`
- 默认 action 是 `UI.RightClick`
- 触发时机是 `OnRelease`

对应文件：

- `decompiled/TheBazaarRuntime/TheBazaar.Inputs/InputManager.cs`

### 2. `RightClick` 的底层设备绑定

`InputSystemActions` 的静态 JSON 里，`RightClick` 绑定的是：

```json
{
  "path": "<Mouse>/rightButton",
  "action": "RightClick"
}
```

对应文件：

- `decompiled/TheBazaarRuntime/InputSystemActions.cs`

这说明原生默认值确实是鼠标右键。

### 3. Tooltip 锁定切换的执行点

`CardTooltipController` 初始化时会注册：

```csharp
_inputManager.AddAction(InputManager.Actions.Lock, LockTooltipToggle);
```

而 `LockTooltipToggle()` 本身的逻辑是：

```csharp
if (isLocked)
{
    Unlock();
}
else if (CurrentCard != null)
{
    Lock();
}
```

对应文件：

- `decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/CardTooltipController.cs`

## User Rebinding Path

虽然默认是右键，但原生支持改键，并且改键结果会覆盖默认值。

### 1. 启动时加载 override

`InputManager.Initialize()` 中会先调用：

```csharp
LoadBindingOverrides();
```

而 `LoadBindingOverrides()` 会从：

```csharp
PlayerPreferences.Data.KeyBindings
```

读取 JSON override，再调用：

```csharp
_inputSystemActions.asset.LoadBindingOverridesFromJson(keyBindings);
```

对应文件：

- `decompiled/TheBazaarRuntime/TheBazaar.Inputs/InputManager.cs`

### 2. 改键后保存 override

原生 `KeyBindController` 在 interactive rebinding 完成后会调用：

```csharp
_inputManager.SaveBindingOverrides();
```

`SaveBindingOverrides()` 会把当前输入系统的 override 导出成 JSON，并写回：

```csharp
PlayerPreferences.Data.KeyBindings = keyBindings;
PlayerPreferences.Save();
```

对应文件：

- `decompiled/TheBazaarRuntime/TheBazaar.UI/KeyBindController.cs`
- `decompiled/TheBazaarRuntime/TheBazaar.Inputs/InputManager.cs`

### 3. 偏好数据的持久化方式

`PlayerPreferences` 使用 Unity `PlayerPrefs`，把整个 `PreferencesData` 序列化到：

```text
preference_data
```

对应文件：

- `decompiled/TheBazaarRuntime/PlayerPreferences.cs`

因此“当前实际生效的 tooltip 按键”应当以 `PlayerPreferences.Data.KeyBindings` 为准，而不是只看代码默认值。

## Local Machine Result

在当前 macOS 环境中，The Bazaar 的偏好文件位于：

```text
/Users/yxinyu/Library/Preferences/com.TempoStorm.TheBazaar.plist
```

读取方式：

```bash
defaults read /Users/yxinyu/Library/Preferences/com.TempoStorm.TheBazaar preference_data
```

当前读到的关键字段如下：

```json
"KeyBindings": "",
"KeyLockedTooltip": 0
```

结论：

- `KeyBindings` 为空，说明没有保存任何输入 override
- 因此当前机器上 `Actions.Lock` 仍然使用默认绑定
- 默认绑定即 `RightClick -> <Mouse>/rightButton`

## About `KeyLockedTooltip`

`PreferencesData` 中仍然存在：

```csharp
public int KeyLockedTooltip
```

但在当前反编译结果中，没有发现 runtime 读取这个字段来决定 tooltip lock 按键的地方。

当前真正生效的链路是：

`PlayerPreferences.Data.KeyBindings`
-> `InputManager.LoadBindingOverrides()`
-> Input System override

因此 `KeyLockedTooltip` 更像是旧字段或遗留字段，不是当前 tooltip lock 绑定的 source of truth。

## Practical Conclusion

如果后续再遇到“不是右键查看信息了”的情况，排查优先级应当是：

1. 先读本机 `PlayerPreferences.Data.KeyBindings`
2. 如果 `KeyBindings` 非空，检查其中是否 override 了 `RightClick` / `Actions.Lock`
3. 如果 `KeyBindings` 为空，再回头看代码默认绑定是否被 patch 或运行时拦截

对当前机器的结论是：

- 不是用户改键导致
- 当前配置下，原生 tooltip lock 仍然是右键触发
