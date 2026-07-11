# 组合根运行时不变量测试（纯补测试） 设计稿

状态：红队零发现，经用户确认，已实施。来源：架构评审候选 9，**经盘点收窄**。

## 定性修正记录（供未来评审引用）

- 「interop-before-game 特性顺序」**证伪为承重不变量**：无注释背书，且逆序无害（`VoiceLineVoObserverBridge.Reset()` 不触 `_callbacks`）。不编码。
- `ComponentMount<T>` **确认无头不可测**（`AddComponent`/`DestroyImmediate` 原生调用；全 tests/ 零 GameObject 实例化先例）。留空不盖，也不写文本扫描凑数（5193ea9a 反戏剧边界）。
- **新发现**：`BppMountableRegistry.MountAll/UnmountAll` **无故障隔离**（与 `BppFeatureRegistry` 的逐项 try/catch 不对称，一个抛出的 mountable 中止其余挂载）——未注释的真实行为缺口。本候选（纯补测试）**钉住现状**并在测试注释里标记该不对称，不改行为。

## 变更清单

1. **新 xUnit 项目 `tests/CompositionRuntime.Tests`**（SettingsDockRegistry.Tests 模式：ProjectReference 主 csproj + ManagedPath 解析块 + UnityEngine.CoreModule Reference——`IBppMountable` 签名含 `GameObject`，类型解析需要，**但测试体零原生调用**，host 传 `null!`）。
2. **`AssemblyAttributes.cs` 增 `[assembly: InternalsVisibleTo("CompositionRuntime.Tests")]`**（现仅两条 grant，本批唯一动此文件的候选）。
3. 行为测试：
   - `BppFeatureRegistry`：Start 中某 feature 抛异常 → 后续 feature 照常 Start（隔离承诺首次可回归）；Stop 逆序 + 抛异常不中断；Start 正序。假件为本地 `IBppFeature` 实现，记录调用序列。
   - `BppMountableRegistry`：MountAll 正序、UnmountAll 逆序（假 `IBppMountable` 忽略 host，传 `null!`）；**钉现状**：Mount 抛异常中止后续（测试名/注释明示这是行为钉而非背书，与 FeatureRegistry 的隔离不对称已知）。
4. **`CoreLayeringTests` 加一条短文本序扫描**（本批该文件独占权在此候选）：`BppComposition.cs` 中 `overlayPanelHostMount` 注册行出现于 `CollectionPanelMount`/`HistoryPanelMount`/`LiveBuildPanelMount` 三行之前（IndexOf 链 ≤4 个片段——5193ea9a 存活形态：短、单目的序断言；对应 `BppComposition.cs:129-130` 的承重注释）。

## 反戏剧自查

- 两个注册表测试是**真实例化 + 假件行为断言**，非源文本转录 ✓。
- 文本扫描仅 1 条、4 片段、对应显式注释的承重不变量 ✓。
- 不写「注册清单全集」钉表（会随每个新特性碎一次，属维护税非回归网）。

## 测试与验证

- `dotnet test tests/CompositionRuntime.Tests` + `dotnet test tests/Architecture.Tests` + `dotnet build`。

## 不做的事

- `ComponentMount<T>` 覆盖（无头不可测，文本扫描=戏剧）。
- MountAll 故障隔离补齐（行为变化——记录为观察项，未来若做需连测试翻钉）。
- interop-before-game 顺序编码（证伪）。
- 注册清单全集钉表。
