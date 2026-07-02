# Keybind Settings Text Fixes + Panel Toggle Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复快捷键设置界面已确认的两处文字显示缺陷（冲突提示混语言、重绑后键名与原生行风格不一致），删除一段已死的原生行改标签 Patch，并把三个面板的硬编码开关键（图鉴 Tab / 终局阵容 CapsLock / 对局历史 F8）提升为设置界面里可重绑定的快捷键设置项。

**Architecture:** 沿用现有 `BppHotkeyActionId` 枚举驱动的快捷键子系统：枚举成员 + `BppHotkeyService` 的 `DefaultBindingPaths`/`GetConfigEntry` 表 + `BppKeybindLabelResolver` 的本地化标签 + `BppKeybindSettingsPatch` 的 `Definitions` 行注入。新增一个 `WasToggleHotkeyPressedThisFrame(actionId)` 服务方法承载"无修饰键的单键切换"语义，三个面板的 `Update()` 把各自的硬编码按键检查替换为该调用。不新建任何 UI/渲染链路。

**Tech Stack:** C# 12 / netstandard2.1、BepInEx 5（`ConfigEntry<string>` 持久化）、HarmonyLib、Unity InputSystem、TextMeshPro、自研 `BazaarPlusPlus.Localization`（`LocalizedTextSet` + `L` facade）。

## Global Constraints

- 构建命令：`cd /Users/yxinyu/codes/bpp/bazaarplusplus-mod && dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`（Debug；游戏程序集经 `ManagedPath` 自动探测）。**不要修改 `BazaarPlusPlus.csproj`**。若在 `.claude/worktrees/` 下的 git worktree 构建，必须额外传 `-p:BPPInstallerSourcePath=<installer resources 绝对路径>`，否则 MSB3030 报错——传参解决，不改 csproj。
- 格式化：完成所有代码任务后运行 `./run.sh format`（csharpier）。若 format 改动了本计划之外的文件，不要把它们混进提交（repo 规则：keep commits scoped）。
- `decompiled/` 是只读参考，禁止编辑。
- **不新增单元测试项目**：本次改动全部耦合游戏 DLL 与 Unity 运行时（MonoBehaviour / InputSystem / TMP），repo 规则禁止 coverage-theater 测试；验证方式 = 编译通过 + 文末的游戏内人工验证清单。
- `LocalizedTextSet` 六语言构造函数参数顺序固定为 `(english, chineseMainland, german, portuguese, korean, italian)`（[LocalizedTextSet.cs:14-22](../../src/BazaarPlusPlus.Localization/LocalizedTextSet.cs)）。德/葡/意文案沿用现有惯例**去除变音符号**（现有先例："druecken"、"previa"、"nao"）。繁体中文由 `ChineseScriptConverter` 从简体自动转换，不需要手写。
- 现有 wire/config 兼容性：已存在的 `[Hotkeys] EnchantPreview / UpgradePreview` 配置键名不得改动。
- 每个任务结束时构建通过并单独提交（在工作分支上，不直接在 master 上做）。提交信息用祈使句、无 conventional-commit 前缀。

---

## 背景：现状与证据（实现前先读，全部 file:line 已核实于 2026-07-02 master 10c51db0）

### 快捷键子系统现状

一个可重绑定快捷键 action 的定义散布在 5 个文件、必须手工保持同步：

| 同步点 | 位置 |
| --- | --- |
| 枚举成员 | [BppHotkeyActionId.cs:4-8](../../src/BazaarPlusPlus/Game/Input/BppHotkeyActionId.cs)（目前仅 `HoldEnchantPreview`、`HoldUpgradePreview`） |
| 默认绑定路径 | `BppHotkeyService.DefaultBindingPaths`，[BppHotkeyService.cs:70-75](../../src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs)。**同时是冲突检测的遍历集**（:254），漏加则新 action 不参与冲突检测 |
| 配置项映射 | `BppHotkeyService.GetConfigEntry` switch，[BppHotkeyService.cs:477-487](../../src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs)。漏加则静默不能持久化 |
| 本地化标签 | `BppKeybindLabelResolver.ResolveActionLabel` switch，[BppKeybindLabelResolver.cs:45-59](../../src/BazaarPlusPlus/Game/Input/BppKeybindLabelResolver.cs) |
| 设置行注入 | `BppKeybindSettingsAwakePatch.Definitions` + `DefinitionObjectNames`，[BppKeybindSettingsPatch.cs:22-39](../../src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs)；安装完成检查 `HasInstalledRows` 硬编码了两个对象名（:301-309） |

持久化 = BepInEx `ConfigEntry<string>`（`[Hotkeys]` 节，[BppConfig.cs:69-80](../../src/BazaarPlusPlus/Core/Config/BppConfig.cs)，接口在 [IBppConfig.cs:19-21](../../src/BazaarPlusPlus/Core/Config/IBppConfig.cs)）。设置行 = 克隆原生 `KeyBindController` 模板行、禁用原生组件、由 `BppKeyBindRowController` 全权驱动（[BppKeyBindRowController.cs:33-61](../../src/BazaarPlusPlus/Game/Input/BppKeyBindRowController.cs)）。语言切换刷新走 [OptionsDialogLanguageRefreshPatch.cs:11-33](../../src/BazaarPlusPlus/Patches/Settings/OptionsDialogLanguageRefreshPatch.cs)。

### 三个面板的现状（本次要提升为可配置项的硬编码键）

| 面板 | 硬编码键 | 位置 | 切换入口 | 门控 |
| --- | --- | --- | --- | --- |
| 图鉴 CollectionPanel（"卡牌图鉴 / Card Collection"） | 无修饰 Tab | [CollectionPanel.cs:349-354, 430-434](../../src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs) | `ToggleFromHotkey()`（:436-442） | Update 内 `TheBazaar.Data.IsInCombat` 自动关闭（:343-347）；无文本输入框 |
| 终局阵容 LiveBuildPanel（"终局阵容 / Final Build"，即十胜阵容推荐面板） | 无修饰 CapsLock | [LiveBuildPanel.cs:93-103](../../src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs) | `Toggle()`（:119-125） | 同上（:87-91），`Open()` 里也拦截战斗中（:129-133）；无文本输入框 |
| 对局历史 HistoryPanel（"对局历史 / Game History"） | F8（无修饰键守卫） | [HistoryPanel.cs:25, 181-185](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs) | `ToggleFromHotkey()`（:228-243，内部有 `CanOpenHistoryReview` 门控） | 已经在用 `BppHotkeyService.WasPressedThisFrame(常量路径)`；**面板内有账号绑定码 TextField**（[HistoryPanelUiToolkitView.Tree.cs:474-478](../../src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.Tree.cs)） |

三个面板的开/关门控（战斗中关闭、互斥 `BppOverlayPanelMutex.CloseOthers`、Escape 关闭）都在各自 Toggle/Open 路径内部，**本计划只替换按键检测来源，不碰任何门控**。

### 已确认的文字显示缺陷（经对抗验证，均可复现于代码）

**缺陷 1（确认）：冲突提示混语言。** [BppHotkeyService.cs:232-237](../../src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs) 把两个已本地化的 action 标签用硬编码英文 `" conflicts with "` 拼接。中文用户重绑到冲突键时看到"显示附魔预览 conflicts with 显示升级预览"。同方法内相邻的 `ResolveUnsupportedKey`（:226-228）是正确本地化的，证明这是遗漏而非风格。该消息经 [BppKeyBindRowController.cs:266-273](../../src/BazaarPlusPlus/Game/Input/BppKeyBindRowController.cs) 写入原生行的 `_warningText`，用户可见。

**缺陷 2（确认）：重绑后的键名显示与原生行风格不一致，且非美式键盘布局下显示错误。** [BppHotkeyService.cs:185-201](../../src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs) 的 `GetBindingDisplay` 在 7 条别名表（:58-68）未命中时调用**不带 control 参数**的 `InputControlPath.ToHumanReadableString(path, OmitDevice)`，返回的是 InputSystem 静态键盘布局（美式）的键名（如 `<Keyboard>/leftCtrl` → "Left Control"）；而同屏的原生 `KeyBindController` 行走 `GetBindingDisplayString(0)` 解析**活的 KeyControl**，displayName 来自操作系统键盘布局（`QueryKeyNameCommand`）。后果：修饰键与非美式布局的字母/符号键上，BPP 行和原生行对同一物理键显示不同字符串，非美式布局下 BPP 行显示的甚至是错误键名。另外 :198-200 的三元表达式两分支等价（`$"{display}"` == `display`），是无操作死代码，顺带清除。

**死代码（确认，非显示缺陷但建议删除）：`NativeKeybindLabelPatch` 整体已死。** 它反射 `KeyBindController._keybindAction`（[NativeKeybindLabelPatch.cs:18-21](../../src/BazaarPlusPlus/Patches/Settings/NativeKeybindLabelPatch.cs)），但当前游戏该字段已改名为 `_action`（decompiled/TheBazaarRuntime/TheBazaar.UI/KeyBindController.cs:17-18），故 `TryUpdateLabels` 在 :56-57 恒早退、整个改标签逻辑静默 no-op；即使修好字段名，匹配串 `"Lock"`（:79）也已过期（现为 `Gameplay/LockTooltip`，且 `InputActionReference.ToString()` 从不返回裸 action 名）；且它要写上去的 "Show Monster Preview" 描述的 MonsterPreview 功能已在 commit 39706719 整体删除。当前屏幕上显示的是游戏自己本地化的原生标签，**是正确的**——复活这个 Patch 反而会写错文案。正确处置 = 整体删除（符合 repo 规则"移除旧实现，不留 fallback"）。

### 已排查并排除的候选（实现者不要"顺手修"这些）

- 注入行的中文标签 CJK 字体 tofu：未复现，现网中文标签渲染正常。
- `FindActionLabelText` 首个 TMP 启发式绑错元素：机制上依赖顺序，但在实际预制体上首个未排除 TMP 就是标签（多版本在线验证），非缺陷。
- CN/TW 切换不即时刷新键位行：dock 的 CN/TW 项翻转后设置界面即时刷新链路经核实覆盖不到位的场景不可达，非用户可见缺陷。
- `PlayerPreferences.Data.LanguageCode` 绕过 `L` facade：行为无差异，纯架构洁癖，不动。

---

## File Structure

| 文件 | 动作 | 职责变化 |
| --- | --- | --- |
| `src/BazaarPlusPlus/Game/Input/BppKeybindLabelResolver.cs` | 修改 | +冲突提示模板与解析方法；+三个新 action 标签 |
| `src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs` | 修改 | 冲突消息改用本地化模板；`GetBindingDisplay` 改用活 control 的 displayName；+3 个默认绑定/配置映射；+`WasToggleHotkeyPressedThisFrame` |
| `src/BazaarPlusPlus/Game/Input/BppHotkeyActionId.cs` | 修改 | +3 枚举成员 |
| `src/BazaarPlusPlus/Game/Input/KeyBindings.cs` | 修改 | +`IsAltPressed` |
| `src/BazaarPlusPlus/Game/Input/BppKeyBindRowController.cs` | 修改 | +静态 `IsRebindCaptureActive` |
| `src/BazaarPlusPlus/Core/Config/IBppConfig.cs`、`BppConfig.cs` | 修改 | +3 个 `ConfigEntry<string>` |
| `src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs` | 修改 | +3 行定义；`HasInstalledRows` 改为遍历全部对象名；删除未使用的 `EnglishLabel`；删除对 NativeKeybindLabelPatch 的调用 |
| `src/BazaarPlusPlus/Patches/Settings/NativeKeybindLabelPatch.cs` | **删除** | 死代码整体移除 |
| `src/BazaarPlusPlus/Patches/Settings/OptionsDialogLanguageRefreshPatch.cs` | 修改 | 删除对 NativeKeybindLabelPatch 的调用 |
| `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs` | 修改 | Tab 硬编码 → 服务调用 |
| `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs` | 修改 | CapsLock 硬编码 → 服务调用 |
| `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs` | 修改 | F8 常量 → 枚举 action；+文本输入焦点守卫 |
| `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.UiToolkit.cs` | 修改 | +焦点守卫桥接 |
| `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs` | 修改 | +`IsTextInputFocused()` |

---

### Task 1: 本地化冲突提示消息

**Files:**
- Modify: `src/BazaarPlusPlus/Game/Input/BppKeybindLabelResolver.cs`
- Modify: `src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:232-237`

**Interfaces:**
- Produces: `internal static string ResolveConflictWarning(BppHotkeyActionId actionId, BppHotkeyActionId conflictingActionId, string languageCode)`（后续任务不依赖，但 Task 5 的标签会经由同文件的 `ResolveActionLabel` 输出到这里）

- [ ] **Step 1: 在 `BppKeybindLabelResolver.cs` 增加模板与解析方法**

在 `UnsupportedKey` 字段（:36-43）之后追加字段：

```csharp
    private static readonly LocalizedTextSet ConflictWarningFormat = new(
        "{0} conflicts with {1}",
        "{0} 与 {1} 冲突",
        "{0} steht in Konflikt mit {1}",
        "{0} conflita com {1}",
        "{0}이(가) {1}과(와) 충돌합니다",
        "{0} in conflitto con {1}"
    );
```

在 `ResolveUnsupportedKey` 方法（:66-69）之后追加方法：

```csharp
    internal static string ResolveConflictWarning(
        BppHotkeyActionId actionId,
        BppHotkeyActionId conflictingActionId,
        string languageCode
    )
    {
        return string.Format(
            ConflictWarningFormat.Resolve(languageCode, L.CurrentMode),
            ResolveActionLabel(actionId, languageCode),
            ResolveActionLabel(conflictingActionId, languageCode)
        );
    }
```

- [ ] **Step 2: 在 `BppHotkeyService.TrySetBindingPath` 使用它**

把 :232-237 的

```csharp
        if (TryGetConflictingAction(actionId, normalized, out var conflictingAction))
        {
            errorMessage =
                $"{BppKeybindLabelResolver.ResolveActionLabel(actionId, PlayerPreferences.Data.LanguageCode)} conflicts with {BppKeybindLabelResolver.ResolveActionLabel(conflictingAction, PlayerPreferences.Data.LanguageCode)}";
            return false;
        }
```

替换为

```csharp
        if (TryGetConflictingAction(actionId, normalized, out var conflictingAction))
        {
            errorMessage = BppKeybindLabelResolver.ResolveConflictWarning(
                actionId,
                conflictingAction,
                PlayerPreferences.Data.LanguageCode
            );
            return false;
        }
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/BazaarPlusPlus/Game/Input/BppKeybindLabelResolver.cs src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs
git commit -m "Localize keybind conflict warning message"
```

---

### Task 2: 重绑键名显示对齐原生行（活 control displayName）

**Files:**
- Modify: `src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs:185-201`

**Interfaces:**
- Consumes: 既有 `GetOrCreateAction(string normalizedPath)`（:294-306，返回已 Enable、controls 已解析的缓存 `InputAction`）
- Produces: `GetBindingDisplay(string)` 行为变化 —— 别名表未命中时优先 `action.controls[0].displayName`（与原生 `GetBindingDisplayString(0)` 同源：OS 键盘布局），仅在设备缺失/解析为空时回落到旧的 `ToHumanReadableString`

- [ ] **Step 1: 重写 `GetBindingDisplay(string)`**

把 :185-201 的方法体替换为：

```csharp
    internal static string GetBindingDisplay(string bindingPath)
    {
        var normalized = NormalizeBindingPath(bindingPath);
        if (BindingDisplayAliases.TryGetValue(normalized, out var alias))
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
```

（此重写同时消灭了旧 :198-200 的无操作三元表达式。5 个鼠标键与 ctrl/shift 别名路径仍然优先命中别名表，行为不变。）

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs
git commit -m "Resolve keybind display names from the live control like native rows"
```

---

### Task 3: 删除已死的 NativeKeybindLabelPatch

证据见"背景"一节。删除的是整个文件及其三处调用点；`BppKeybindSettingsRefreshDriver._nativeLabelsUpdated` 字段也随之删除。

**Files:**
- Delete: `src/BazaarPlusPlus/Patches/Settings/NativeKeybindLabelPatch.cs`
- Modify: `src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs:190-206, 229-299`
- Modify: `src/BazaarPlusPlus/Patches/Settings/OptionsDialogLanguageRefreshPatch.cs:21`

- [ ] **Step 1: 删除文件**

```bash
git rm src/BazaarPlusPlus/Patches/Settings/NativeKeybindLabelPatch.cs
```

- [ ] **Step 2: 清理 `BppKeybindSettingsPatch.cs` 的引用**

`BppKeybindSettingsGameplayOpenPatch.Postfix`（:190-206）中，把 lambda

```csharp
            instance =>
            {
                BppKeybindSettingsAwakePatch.EnsureKeybindRows(instance);
                NativeKeybindLabelAwakePatch.TryUpdateLabels(instance);
            }
```

替换为

```csharp
            instance => BppKeybindSettingsAwakePatch.EnsureKeybindRows(instance)
```

`BppKeybindSettingsRefreshDriver` 中：删除字段 `private bool _nativeLabelsUpdated;`（:235）；删除 `RequestRefresh` 里的 `_nativeLabelsUpdated = false;`（:251）；删除 `RefreshRoutine` 里的

```csharp
                if (!_nativeLabelsUpdated)
                    _nativeLabelsUpdated = NativeKeybindLabelAwakePatch.TryUpdateLabels(
                        _controller
                    );
```

（:275-278）。

- [ ] **Step 3: 清理 `OptionsDialogLanguageRefreshPatch.cs`**

删除 :21 一行：

```csharp
            NativeKeybindLabelAwakePatch.TryUpdateLabels(__instance);
```

- [ ] **Step 4: 确认无残留引用**

Run: `grep -rn "NativeKeybindLabel" src/`
Expected: 无输出

- [ ] **Step 5: 构建验证 + Commit**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` → `Build succeeded. 0 Error(s)`

```bash
git add -A src/BazaarPlusPlus/Patches/Settings/
git commit -m "Remove dead monster preview keybind relabel patch"
```

---

### Task 4: 新枚举成员、配置项与服务表扩展 + 切换语义 API

**Files:**
- Modify: `src/BazaarPlusPlus/Game/Input/BppHotkeyActionId.cs`
- Modify: `src/BazaarPlusPlus/Core/Config/IBppConfig.cs:19-21` 附近
- Modify: `src/BazaarPlusPlus/Core/Config/BppConfig.cs:21-23, 69-80` 附近
- Modify: `src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs`（`DefaultBindingPaths`、`GetConfigEntry`、新方法）
- Modify: `src/BazaarPlusPlus/Game/Input/KeyBindings.cs`
- Modify: `src/BazaarPlusPlus/Game/Input/BppKeyBindRowController.cs`

**Interfaces:**
- Produces（后续任务消费）:
  - 枚举成员 `BppHotkeyActionId.ToggleCollectionPanel / ToggleLiveBuildPanel / ToggleHistoryPanel`
  - `internal static bool BppHotkeyService.WasToggleHotkeyPressedThisFrame(BppHotkeyActionId actionId, Keyboard? keyboard = null)`
  - `internal static bool BppKeyBindRowController.IsRebindCaptureActive`
  - 配置键 `[Hotkeys] ToggleCollectionPanel / ToggleLiveBuildPanel / ToggleHistoryPanel`

- [ ] **Step 1: 扩展枚举**

`BppHotkeyActionId.cs` 改为：

```csharp
#nullable enable
namespace BazaarPlusPlus.Game.Input;

internal enum BppHotkeyActionId
{
    HoldEnchantPreview,
    HoldUpgradePreview,
    ToggleCollectionPanel,
    ToggleLiveBuildPanel,
    ToggleHistoryPanel,
}
```

- [ ] **Step 2: 配置接口与实现**

`IBppConfig.cs` 在 `UpgradePreviewHotkeyPathConfig`（:21）后追加：

```csharp
    ConfigEntry<string>? ToggleCollectionPanelHotkeyPathConfig { get; }

    ConfigEntry<string>? ToggleLiveBuildPanelHotkeyPathConfig { get; }

    ConfigEntry<string>? ToggleHistoryPanelHotkeyPathConfig { get; }
```

`BppConfig.cs` 在属性区 :23 后追加：

```csharp
    public ConfigEntry<string>? ToggleCollectionPanelHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? ToggleLiveBuildPanelHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? ToggleHistoryPanelHotkeyPathConfig { get; private set; }
```

并在 `UpgradePreviewHotkeyPathConfig = config.Bind(...)`（:75-80）之后追加：

```csharp
        ToggleCollectionPanelHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "ToggleCollectionPanel",
            "<Keyboard>/tab",
            "Binding path for toggling the card collection panel."
        );
        ToggleLiveBuildPanelHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "ToggleLiveBuildPanel",
            "<Keyboard>/capsLock",
            "Binding path for toggling the final build panel."
        );
        ToggleHistoryPanelHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "ToggleHistoryPanel",
            "<Keyboard>/f8",
            "Binding path for toggling the game history panel."
        );
```

- [ ] **Step 3: 服务表扩展**

`BppHotkeyService.DefaultBindingPaths`（:70-75）改为：

```csharp
    private static readonly IReadOnlyDictionary<BppHotkeyActionId, string> DefaultBindingPaths =
        new Dictionary<BppHotkeyActionId, string>
        {
            [BppHotkeyActionId.HoldEnchantPreview] = CtrlAliasPath,
            [BppHotkeyActionId.HoldUpgradePreview] = ShiftAliasPath,
            [BppHotkeyActionId.ToggleCollectionPanel] = KeyboardPrefix + "tab",
            [BppHotkeyActionId.ToggleLiveBuildPanel] = KeyboardPrefix + "capsLock",
            [BppHotkeyActionId.ToggleHistoryPanel] = KeyboardPrefix + "f8",
        };
```

`GetConfigEntry` switch（:477-487）改为：

```csharp
        return actionId switch
        {
            BppHotkeyActionId.HoldEnchantPreview => Config.EnchantPreviewHotkeyPathConfig,
            BppHotkeyActionId.HoldUpgradePreview => Config.UpgradePreviewHotkeyPathConfig,
            BppHotkeyActionId.ToggleCollectionPanel => Config.ToggleCollectionPanelHotkeyPathConfig,
            BppHotkeyActionId.ToggleLiveBuildPanel => Config.ToggleLiveBuildPanelHotkeyPathConfig,
            BppHotkeyActionId.ToggleHistoryPanel => Config.ToggleHistoryPanelHotkeyPathConfig,
            _ => null,
        };
```

- [ ] **Step 4: `KeyBindings.Modifiers` 增加 Alt 检测**

在 `IsShiftPressed`（KeyBindings.cs:16-20）后追加：

```csharp
        public static bool IsAltPressed(Keyboard? keyboard)
        {
            return keyboard != null
                && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
        }
```

- [ ] **Step 5: 暴露"重绑捕获进行中"状态**

`BppKeyBindRowController.cs` 在 `_activeController` 字段（:16）后追加：

```csharp
    internal static bool IsRebindCaptureActive => _activeController != null;
```

- [ ] **Step 6: 新增切换语义 API**

`BppHotkeyService.cs` 在 `WasPressedThisFrame(string)`（:107-133）之后追加：

```csharp
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

        if (IsModifierBindingPath(path))
            return true;

        keyboard ??= Keyboard.current;
        return !KeyBindings.Modifiers.IsCtrlPressed(keyboard)
            && !KeyBindings.Modifiers.IsAltPressed(keyboard)
            && !KeyBindings.Modifiers.IsShiftPressed(keyboard);
    }

    private static bool IsModifierBindingPath(string normalizedPath)
    {
        return normalizedPath.Contains("ctrl", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("shift", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("alt", StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 7: 构建验证 + Commit**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` → `Build succeeded. 0 Error(s)`

```bash
git add src/BazaarPlusPlus/Game/Input/ src/BazaarPlusPlus/Core/Config/
git commit -m "Add panel toggle hotkey actions with config-backed bindings"
```

---

### Task 5: 三个新 action 的本地化标签

**Files:**
- Modify: `src/BazaarPlusPlus/Game/Input/BppKeybindLabelResolver.cs`

**Interfaces:**
- Consumes: Task 4 的三个枚举成员
- Produces: `ResolveActionLabel` 对三个新成员返回本地化标签（设置行、冲突提示两处消费）

标签文案对齐各面板自己的标题（Card Collection/卡牌图鉴 · Final Build/终局阵容 · Game History/对局历史），繁体自动转换，德/葡/意去变音符号：

- [ ] **Step 1: 追加三个 `LocalizedTextSet` 字段**（放在 `UpgradePreviewLabel` 之后）

```csharp
    private static readonly LocalizedTextSet ToggleCollectionPanelLabel = new(
        "Toggle Card Collection",
        "开关卡牌图鉴",
        "Kartensammlung umschalten",
        "Alternar colecao de cartas",
        "카드 도감 열기/닫기",
        "Mostra/nascondi collezione carte"
    );

    private static readonly LocalizedTextSet ToggleLiveBuildPanelLabel = new(
        "Toggle Final Build",
        "开关终局阵容",
        "Endaufstellung umschalten",
        "Alternar build final",
        "최종 빌드 열기/닫기",
        "Mostra/nascondi build finale"
    );

    private static readonly LocalizedTextSet ToggleHistoryPanelLabel = new(
        "Toggle Game History",
        "开关对局历史",
        "Spielverlauf umschalten",
        "Alternar historico de partidas",
        "게임 전적 열기/닫기",
        "Mostra/nascondi cronologia partite"
    );
```

- [ ] **Step 2: 扩展 `ResolveActionLabel` switch**（在 `HoldUpgradePreview` 分支后追加）

```csharp
            BppHotkeyActionId.ToggleCollectionPanel => ToggleCollectionPanelLabel.Resolve(
                languageCode,
                L.CurrentMode
            ),
            BppHotkeyActionId.ToggleLiveBuildPanel => ToggleLiveBuildPanelLabel.Resolve(
                languageCode,
                L.CurrentMode
            ),
            BppHotkeyActionId.ToggleHistoryPanel => ToggleHistoryPanelLabel.Resolve(
                languageCode,
                L.CurrentMode
            ),
```

- [ ] **Step 3: 构建验证 + Commit**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` → `Build succeeded. 0 Error(s)`

```bash
git add src/BazaarPlusPlus/Game/Input/BppKeybindLabelResolver.cs
git commit -m "Add localized labels for panel toggle hotkeys"
```

---

### Task 6: 设置界面注入三个新行

**Files:**
- Modify: `src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs`

**Interfaces:**
- Consumes: Task 4 的枚举成员
- Produces: 设置对话框 Gameplay 页出现五个 BPP 键位行（顺序：EnchantPreview、UpgradePreview、ToggleCollectionPanel、ToggleLiveBuildPanel、ToggleHistoryPanel）

同时做两处就地清理：`BppKeybindDefinition.EnglishLabel` 从未被读取（行标签实际来自 `BppKeybindLabelResolver`），删除该参数；`HasInstalledRows` 从硬编码两个名字改为遍历 `DefinitionObjectNames`。

- [ ] **Step 1: 重写定义区**（`BppKeybindSettingsAwakePatch` 的 :17-39）

```csharp
    internal const string EnchantPreviewObjectName = "BPP_Keybind_EnchantPreview";
    internal const string UpgradePreviewObjectName = "BPP_Keybind_UpgradePreview";
    internal const string ToggleCollectionPanelObjectName = "BPP_Keybind_ToggleCollectionPanel";
    internal const string ToggleLiveBuildPanelObjectName = "BPP_Keybind_ToggleLiveBuildPanel";
    internal const string ToggleHistoryPanelObjectName = "BPP_Keybind_ToggleHistoryPanel";

    private static readonly BppKeybindDefinition[] Definitions =
    [
        new(EnchantPreviewObjectName, BppHotkeyActionId.HoldEnchantPreview),
        new(UpgradePreviewObjectName, BppHotkeyActionId.HoldUpgradePreview),
        new(ToggleCollectionPanelObjectName, BppHotkeyActionId.ToggleCollectionPanel),
        new(ToggleLiveBuildPanelObjectName, BppHotkeyActionId.ToggleLiveBuildPanel),
        new(ToggleHistoryPanelObjectName, BppHotkeyActionId.ToggleHistoryPanel),
    ];
    internal static readonly string[] DefinitionObjectNames =
    [
        EnchantPreviewObjectName,
        UpgradePreviewObjectName,
        ToggleCollectionPanelObjectName,
        ToggleLiveBuildPanelObjectName,
        ToggleHistoryPanelObjectName,
    ];
```

（`EnchantPreviewEnglishLabel`/`UpgradePreviewEnglishLabel` 两个 const 一并删除。）

- [ ] **Step 2: 精简 `BppKeybindDefinition`**（:157-173）

```csharp
    private sealed class BppKeybindDefinition
    {
        internal BppKeybindDefinition(string objectName, BppHotkeyActionId actionId)
        {
            ObjectName = objectName;
            ActionId = actionId;
        }

        internal string ObjectName { get; }
        internal BppHotkeyActionId ActionId { get; }
    }
```

- [ ] **Step 3: `HasInstalledRows` 遍历全部对象名**（:301-309）

```csharp
    private static bool HasInstalledRows(OptionsDialogController controller)
    {
        var container = BppKeybindSettingsAwakePatch.FindRowContainer(controller);
        if (container == null)
            return false;

        foreach (var objectName in BppKeybindSettingsAwakePatch.DefinitionObjectNames)
        {
            if (container.Find(objectName) == null)
                return false;
        }

        return true;
    }
```

- [ ] **Step 4: 构建验证 + Commit**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` → `Build succeeded. 0 Error(s)`

```bash
git add src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs
git commit -m "Inject settings rows for panel toggle hotkeys"
```

---

### Task 7: CollectionPanel 接入

**Files:**
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:349-354, 430-434`

**Interfaces:**
- Consumes: `BppHotkeyService.WasToggleHotkeyPressedThisFrame(BppHotkeyActionId.ToggleCollectionPanel, keyboard)`（Task 4）

- [ ] **Step 1: 替换按键检测**

`Update()` 内 :349-354 的

```csharp
        var keyboard = Keyboard.current;
        if (keyboard != null && IsPlainTabPressed(keyboard))
        {
            ToggleFromHotkey();
            return;
        }
```

替换为

```csharp
        var keyboard = Keyboard.current;
        if (
            BppHotkeyService.WasToggleHotkeyPressedThisFrame(
                BppHotkeyActionId.ToggleCollectionPanel,
                keyboard
            )
        )
        {
            ToggleFromHotkey();
            return;
        }
```

（`keyboard` 局部变量保留——同方法 :377 的 Escape 检查还在用它。）文件头部需要 `using BazaarPlusPlus.Game.Input;`（如已存在则不重复添加）。

- [ ] **Step 2: 删除 `IsPlainTabPressed`**（:430-434 整个方法）

- [ ] **Step 3: 构建验证 + Commit**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` → `Build succeeded. 0 Error(s)`

```bash
git add src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs
git commit -m "Drive collection panel toggle from the rebindable hotkey"
```

---

### Task 8: LiveBuildPanel 接入

**Files:**
- Modify: `src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs:93-103`

**Interfaces:**
- Consumes: `BppHotkeyService.WasToggleHotkeyPressedThisFrame(BppHotkeyActionId.ToggleLiveBuildPanel, keyboard)`（Task 4）

- [ ] **Step 1: 替换按键检测**

`Update()` 内 :93-103 的

```csharp
        var keyboard = Keyboard.current;
        if (
            keyboard?.capsLockKey.wasPressedThisFrame == true
            && keyboard.ctrlKey.isPressed == false
            && keyboard.altKey.isPressed == false
            && keyboard.shiftKey.isPressed == false
        )
        {
            Toggle();
            return;
        }
```

替换为

```csharp
        var keyboard = Keyboard.current;
        if (
            BppHotkeyService.WasToggleHotkeyPressedThisFrame(
                BppHotkeyActionId.ToggleLiveBuildPanel,
                keyboard
            )
        )
        {
            Toggle();
            return;
        }
```

（`keyboard` 局部变量保留——:108 的 Escape 检查还在用。）文件头部需要 `using BazaarPlusPlus.Game.Input;`（如已存在则不重复添加）。

- [ ] **Step 2: 构建验证 + Commit**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` → `Build succeeded. 0 Error(s)`

```bash
git add src/BazaarPlusPlus/Game/LiveBuildPanel/LiveBuildPanel.cs
git commit -m "Drive final build panel toggle from the rebindable hotkey"
```

---

### Task 9: HistoryPanel 接入 + 文本输入焦点守卫

HistoryPanel 是唯一含 `TextField`（账号绑定码输入格）的面板。F8 是功能键所以现状无冲突，但一旦用户把开关键重绑到字母/数字键，输入绑定码时每敲一下就会把面板关掉。因此在面板可见且文本输入聚焦时跳过开关键。

**Files:**
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs`
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.UiToolkit.cs`
- Modify: `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanel.cs:25, 181-185`

**Interfaces:**
- Consumes: `BppHotkeyService.WasToggleHotkeyPressedThisFrame(BppHotkeyActionId.ToggleHistoryPanel)`（Task 4）
- Produces: `internal bool HistoryPanelUiToolkitView.IsTextInputFocused()`；`private bool HistoryPanel.IsTextInputFocused()`（partial 桥接）

- [ ] **Step 1: 视图暴露焦点查询**

`HistoryPanelUiToolkitView.cs`（类内任意合适位置，`_root` 字段在 :44）追加：

```csharp
    internal bool IsTextInputFocused()
    {
        // UITK may focus the TextField itself or its inner text element depending on
        // Unity version — treat any element inside a TextField as "typing".
        var focused = _root?.focusController?.focusedElement as VisualElement;
        if (focused == null)
            return false;

        return focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null;
    }
```

（该 partial 已 `using UnityEngine.UIElements;`；若无则补。）

- [ ] **Step 2: partial 桥接**

`HistoryPanel.UiToolkit.cs`（`_uiView` 字段在 :13）追加：

```csharp
    private bool IsTextInputFocused()
    {
        return _uiView?.IsTextInputFocused() == true;
    }
```

- [ ] **Step 3: 替换按键检测**

`HistoryPanel.cs` 删除 :25 的

```csharp
    private const string ToggleHistoryPanelBindingPath = "<Keyboard>/f8";
```

把 `Update()` 内 :181-185 的

```csharp
        if (BppHotkeyService.WasPressedThisFrame(ToggleHistoryPanelBindingPath))
        {
            ToggleFromHotkey();
            return;
        }
```

替换为

```csharp
        if (
            BppHotkeyService.WasToggleHotkeyPressedThisFrame(BppHotkeyActionId.ToggleHistoryPanel)
            && !IsTextInputFocused()
        )
        {
            ToggleFromHotkey();
            return;
        }
```

**已知行为变化（有意为之）：** F8 从"无修饰键守卫"变为"带守卫"——按住 Ctrl/Alt/Shift 时 F8 不再触发，与另两个面板对齐。

- [ ] **Step 4: 构建验证 + Commit**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj` → `Build succeeded. 0 Error(s)`

```bash
git add src/BazaarPlusPlus/Game/HistoryPanel/
git commit -m "Drive history panel toggle from the rebindable hotkey with text focus guard"
```

---

### Task 10: 格式化、全量测试与收尾

- [ ] **Step 1: 格式化**

Run: `./run.sh format`
若 csharpier 改动了本计划之外的文件，将其排除出提交。

- [ ] **Step 2: 全量测试**

Run: `./run.sh test`
Expected: 输出中不出现 `Failed test projects:`，且逐个 runner 无失败断言（repo 备忘：**exit code 不足信，必须 grep 全量输出**，不要 tail 截断）。

- [ ] **Step 3: Release 构建冒烟（仅编译，不打包）**

Run: `dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj -c Release`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: 提交格式化残余（如有）并自查 diff**

```bash
git add -u && git commit -m "Format hotkey settings changes"   # 仅当 format 产生了改动
git log --oneline master..HEAD
git diff master...HEAD --stat
```

自查后**停在工作分支等待 review**——按用户流程，本计划的实现由 review 通过后再合并。

---

## 游戏内人工验证清单（实现者交付时随附，由用户执行）

通过 Steam 启动（macOS: `open "steam://run/1617400"`），日志在 `<GameDir>/BepInEx/LogOutput.log`（`[BPP]` 前缀）：

1. **五个设置行**：设置 → Gameplay 键位区出现 5 个 BPP 行，中文标签依次为：显示附魔预览 / 显示升级预览 / 开关卡牌图鉴 / 开关终局阵容 / 开关对局历史；键名列显示 Ctrl / Shift / Tab / Caps Lock / F8。
2. **默认键行为不回归**：Tab 开关图鉴、CapsLock 开关终局阵容、F8 开关对局历史；战斗中自动关闭、Escape 关闭、面板互斥（开一个关另一个）全部不变；Ctrl+Tab 不触发图鉴。
3. **重绑生效**：把"开关卡牌图鉴"重绑到 B → 行内键名显示与原生行同风格；按 B 开关图鉴，Tab 不再触发；重启游戏后仍是 B（检查 `BepInEx/config/BazaarPlusPlus.cfg` 的 `[Hotkeys] ToggleCollectionPanel`）。
4. **冲突提示全中文**：中文语言下，把"开关终局阵容"重绑到 F8 → 警告应为"开关终局阵容 与 开关对局历史 冲突"，不得出现英文 "conflicts with"。
5. **键名一致性（缺陷 2 修复）**：把"显示附魔预览"重绑到左 Ctrl → BPP 行显示的键名应与原生行对同一物理键的写法一致（不再是美式静态名 "Left Control" 与原生风格并存两套）。
6. **重绑捕获期间不误触**：点击某行进入"按下一个键或鼠标按钮"状态后，按 Tab/CapsLock/F8 只应被捕获为新绑定，不得同时把对应面板开出来。
7. **语言切换即时刷新**：设置里切换 中文 ↔ English，五个行标签立即跟随；繁体模式下显示"開關卡牌圖鑑"等自动转换文案。
8. **历史面板输入守卫**：把"开关对局历史"重绑到字母键（如 H），打开历史面板 → 账号绑定码输入格聚焦时敲 H 应输入字符而不是关面板；失焦后 H 正常开关。
9. **死代码删除无回归**：原生 Lock/锁定 键位行仍显示游戏自己的本地化标签（本来就如此，只需确认无异常/无空标签，日志无新增 `[BPP]` Error）。

## 明确不做（Out of scope，实现者不得顺手扩展）

- 不提供"解绑/禁用快捷键"能力：空绑定回落到默认值是既有行为（[BppHotkeyService.cs:172-175](../../src/BazaarPlusPlus/Game/Input/BppHotkeyService.cs)），保持一致。
- 不改设置对话框打开时面板仍可被热键唤起的既有行为（重绑捕获期已被 Task 4 守卫覆盖）。
- 不做 BPP 快捷键与游戏原生键位的跨系统冲突检测（现状即无）。
- 不动 `docs/MEMORY.md`/`INDEX.md`（consolidation-run 专属）；不更新站点教程页（bazaarplusplus-site 是另一个 repo，作为后续跟进项）。

## Release Notes（供最终 PR 使用）

- Added: 图鉴 / 终局阵容 / 对局历史三个面板的开关快捷键现可在设置中重新绑定（默认 Tab / Caps Lock / F8）
- Fixed: 快捷键冲突提示不再夹带英文；重绑后的键名显示与原生设置行一致
