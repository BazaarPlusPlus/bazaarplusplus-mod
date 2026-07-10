# HotkeyBindingPathCore：热键绑定路径纯代数提取 设计稿

状态：批量流水线 Phase B 草稿，待红队 + 用户统一确认。来源：架构评审候选 8。

## 事实基础（盘点已核）

- `Game/Input/BppHotkeyService.cs`（537 行，0 测试）混编三类成员：纯字符串代数（`IsModifierBindingPath`、`TryNormalizeSupportedMouseButtonName`、`IsExplicitlyUnsupportedMousePath`、数据表、`GetDefaultBindingPath`）、Unity 触达（`GetOrCreateAction`/`IsHeld`/`TryFindMouseControl` 等）、以及**混合**成员（纯代数中嵌 Unity 触点）。
- **关键发现——`NormalizeBindingPath` 的 Unity 触点是死代码**（`:390-396`）：5 个白名单鼠标键名在 Unity `Mouse` 上恒为 `ButtonControl`（守卫从未命中）；且 `Mouse.current == null` 时 `?.` 短路 + `&&` 短路使守卫成为空操作（不拒绝）。纯化 = 直接删除该守卫，任何可达输入下行为不变。
- 归一化是**不受信输入边界**：cfg 文件手编的 `[Hotkeys]` 值必须降级为 `""` → 回退默认，不能抛——契约相邻，值得表驱动测试。
- 鼠标键名白名单**三份手写**（`BindingDisplayAliases` 键、`TryFindSupportedMouseButton` switch、`TryNormalizeSupportedMouseButtonName` 链）；默认绑定串在 `DefaultBindingPaths` 与 `BppConfig.cs:139-168` **两文件重复**（本轮只单源化前者内部三份，跨文件重复记录不动）。
- 纯模块测试先例：`OverlayLifecycleCore` + Compile-Include 进零 ManagedPath 的 exe-runner（无需 InternalsVisibleTo）。前提：链入文件签名与体内**零 Unity 类型**。
- 消费者面窄：`OverlayPanelHost`、`TooltipPreviewModePolicy`/`TooltipModifierRefreshController`、`BppKeyBindRowController`、`Plugin`。全部经 `BppHotkeyService` 门面——门面签名不变则消费者零改动。

## 设计（推荐决策，待批）

1. **新文件** `Game/Input/HotkeyBindingPathCore.cs`：`internal static class`，**签名与体内零 Unity/游戏类型**（OverlayLifecycleCore 模式，文件头注释声明该约束 + compile-linked 说明）。承载：
   - `Normalize(string? bindingPath) : string` —— 现 `NormalizeBindingPath` 语义**减去死守卫**（键盘前缀无验证照旧、鼠标白名单链、显式不支持路径拒绝）；
   - `Expand(string normalizedPath) : IEnumerable<string>`（ctrl/shift 别名对，纯部分）；
   - `IsModifierPath`、`IsExplicitlyUnsupportedMousePath`、`TryNormalizeSupportedMouseButtonName`；
   - `FindConflict(BppHotkeyActionId candidateId, string normalizedCandidatePath, IReadOnlyDictionary<BppHotkeyActionId, string> currentPaths) : BppHotkeyActionId?` —— 现 `TryGetConflictingAction` 的集合代数，配置读取上移调用方；
   - 数据表单源化：`SupportedMouseButtonNames`（一份，三处消费）、`DisplayAliases`、`DefaultBindingPaths` + `GetDefault(actionId)`。
   - 注意 `BppHotkeyActionId` 是零依赖 enum，可被纯文件引用 ✓。
2. **`BppHotkeyService` 保留为 Unity/IO 门面**：全部公开签名不变；混合成员改为「组合纯核 + Unity 触达」——红队补全的成员归属清单：
   - `GetBindingPath`：读 cfg + `Core.Normalize` + 回退默认（门面）；
   - `TryGetConflictingAction`（门面）：**currentPaths 必须是 `{id → GetBindingPath(id)}` 对全部 `DefaultBindingPaths.Keys` 的已解析字典（含默认值回填），不得用原始 cfg 值**——否则默认绑定的冲突被静默漏检（红队 rev）；`FindConflict` 内部跳过 candidateId；
   - `TryGetSupportedMouseButtonName` **纯化后移入 Core**（白名单链版）；`TryFindSupportedMouseButton`（活设备 switch）留门面并改为消费 Core 白名单；
   - **`TryFindMouseControl`（`:444-454`）随 D1+D2 一并删除**（唯二调用点都被删，勿留孤儿——红队 rev）；
   - `IsExplicitlyUnsupportedMousePath`：**保留**入 Core 作 belt-and-suspenders（其守卫在白名单链之后实为防御性 no-op，红队核实；测试对它**直接**测，不靠 Normalize("<Mouse>/scroll") 间接覆盖）；
   - `WasPressedThisFrame`/`IsPressed`/`IsHeld`/`GetOrCreateAction`/`GetBindingDisplay`/`Reset`/`Install`/config I/O：全留门面。
3. **行为保真与已知偏差**：
   - D1：删除 `NormalizeBindingPath` 的 ButtonControl 死守卫——理论偏差仅存在于「某异形设备把 5 个标准键名暴露为非 ButtonControl」的未观测场景。
   - D2（若红队确认同类）：`TryGetSupportedMouseButtonName` 的活控件规范化改纯链。若红队发现活规范化有真实语义（如大小写/别名映射差异），该点回退为保留触点、只提取其余。
   - 其余逐语义照抄（含键盘路径不验证、`GetDefaultBindingPath` 对未知 id 抛 KeyNotFound 的现状不对称——不修）。

## 测试计划

- 新 exe-runner `tests/HotkeyBindingPath.Tests`（OverlayLifecycleCore 模式：Compile-Include `HotkeyBindingPathCore.cs` + `BppHotkeyActionId.cs`，零 ManagedPath）：
  - Normalize 表驱动：`"<Keyboard>/CTRL"` 保形、`"<Keyboard>/bogus"` 接受（现状语义！）、`"<Mouse>/scroll"` 拒绝、`"<Mouse>/LEFTBUTTON"` 规范化、垃圾输入 → `""`、null/空白 → `""`；
  - Expand：ctrl→{ctrl,leftCtrl,rightCtrl}、shift 同理、普通键单元素；
  - FindConflict：别名对称冲突（leftCtrl vs ctrl 双向命中）、自身跳过、无冲突 null；
  - 白名单单源断言（DisplayAliases 键 ⊇ 鼠标白名单）。
- `BppHotkeyService` 门面本身维持零测试（Unity 绑定），风险面已缩到薄贴片。
- 验证：新测试项目 + `dotnet build`（+ 现有 `./run.sh test` 全量确认无涟漪）。

## 不做的事

- Alt 不可绑定的功能缺口（特性工作非重构）。
- `DefaultBindingPaths` 与 `BppConfig` 的跨文件默认串重复（涉及 BppConfig 结构，另立）。
- `GetDefaultBindingPath` 抛 KeyNotFound 的不对称（行为变化）。
- CoreLayeringTests（归候选 9 独占）。
