# Online / PTR 双版本兼容方案

日期：2026-07-03（v2，已吸收三视角红队评审：机制核查 / 架构契合 / 运营漏洞，22 条发现全部处置）
状态：草案，待确认后实施
前置调查：28-agent 工作流（216 个游戏 API 依赖逐项 diff + 三症状根因分析 + 对抗复核）+ 3-agent 红队。本文所有结论均有 `file:line` 级证据。

## 1. 背景与关键事实

The Bazaar 当前同时存在 online（public 分支）与 PTR（`public_test_realm` beta 分支）两个版本。mod 在 online 正常，在 PTR 出现三个症状：快捷键全部失效；图鉴 dock 图标点击无反应；设置 dock 图标能弹出容器但没有任何菜单项。

调查确立的事实基础：

| # | 事实 | 证据 |
| --- | --- | --- |
| F1 | online/PTR **不是两个安装目录**：Steam beta 分支原地覆盖 `The Bazaar` 目录。本机当前装的是 PTR（buildid 23993765），**本机现在没有 online 程序集** | `steamapps/appmanifest_1617400.acf` `MountedConfig.BetaKey=public_test_realm` |
| F2 | mod 全源码对 PTR 程序集编译 **0 error / 0 warning**——不存在编译期 API 断裂 | `dotnet build`（ManagedPath=当前 PTR Managed）|
| F3 | 216 个运行时游戏 API 依赖（39 个 Harmony 目标 + 162 个字符串反射/UI 元素名/资产 key + feature trace 补充）逐项 diff，真实断裂 3 处（§3）。**基线注意**：对比基线是"5 月底的 online 快照 vs 当前 PTR"——`decompiled/` 中仅 TheBazaarRuntime 是 7-02 的 online 末版，Assembly-CSharp / BazaarGameClient / BazaarBattleService 为 5-29、BazaarGameShared 为 5-31。5 月底之后进入 online 的变化在本 diff 中不可见；下次回到 public 分支必须整树刷新 + 归档程序集后复核（见 L5 流程） | 工作流 diff + 二次复核；`decompiled/*/` mtime |
| F4 | 游戏输入系统**没有**迁移：两版都用 Unity InputSystem，`TheBazaar.Inputs/InputManager.cs` 与 `TheBazaar.UI/KeyBindController.cs` 两树逐字节一致；两树均无 legacy `UnityEngine.Input` 调用。（`TheBazaar.Inputs/` 目录整体 diff 有两处无害差异：`DraggingCardModel.cs` 仅存在于 online 树；PTR 的 `SteamDeckKeyboard.cs` 新增 Show/Hide 重载） | 两树按文件 diff |
| F5 | mod 的 dock 挂载点两树逐字节一致：`SettingDialogsView.cs`、`FightMenuDialog.cs` 完全相同，`OptionsDialogController` 仅增量变化（新增服务器选择器），被 patch 的方法全部健在 | 两树 diff |
| F6 | PTR 引擎从 Unity 2022.3.40 跳到 **6000.3.11f1** | `TheBazaar.app/Contents/Info.plist`；Player.log `Initialize engine version: 6000.3.11f1`；对照 `BazaarPlusPlus.csproj:90` `UnityEngine.Modules 2022.3.40` |
| F7 | PTR 版本串自带标记：`Application.version` = `1.0.11358-ptr-macos-arm64-947c079a`（`-ptr` token） | Player.log `[VersionShow]`；`decompiled-vptr/TheBazaarRuntime/TheBazaar/VersionShow.cs:25` 直接拼接 `Application.version`。online 侧 token 形状（预期无 `-ptr` 段）待下次回 public 分支时以同一日志行取证（§6 验证项） |
| F8 | PTR 的 +106 个新文件几乎全是新功能（Tournaments ~40 文件、重建的主菜单状态机、`UILocationManager` 注册表），对 mod 均为源码兼容的增量 | TheBazaarRuntime 1559→1665 文件结构 diff |
| F9 | 运行时 Harmony 是 **BepInEx core 0Harmony 2.15.0**（游戏安装与 installer resources 均为 2.15.0+d79a9ce8）；nuget HarmonyX 2.7.0 仅参与编译。本文引用的全部 Harmony 语义（中止顺序、Prepare、CreateClassProcessor、歧义异常）已对 2.15.0 二进制复核成立 | `BepInEx/core/0Harmony.dll` AssemblyVersion；红队复核 |

## 2. 根因（已对抗复核，高置信）

三个症状是**同一个机制**，不是三个独立的 API 断裂：

1. `Plugin.Awake` 先安装静态设施（`Plugin.cs:51` → `BppSettingsDockCatalog.Install` 填入 10 个条目，`Plugin.cs:117`），再 `_harmony.PatchAll()`（`Plugin.cs:168`）；`_patchesApplied = true` 在 PatchAll **返回之后**才置位（`Plugin.cs:169`）。
2. 在 PTR 上 PatchAll **中途必然抛异常**（§3 的 B1/B2 三个致命 patch 类）。0Harmony 2.15.0 的 `PatchAll` = `GetTypesFromAssembly().Do(t => CreateClassProcessor(t).Patch())`，无 try/catch：在第一个失败的类处中止，之前的类保持已应用，之后的类不再应用（F9，二进制复核）。当前 Debug DLL 的 TypeDef 顺序中，两个 dock 图标 patch 类（RID 21/22）排在第一个致命类 `RunInitializedPatch`（RID 25）之前 → **图标 patch 存活**。（注意：`GetTypes()` 顺序是 Mono 的元数据顺序惯例而非 ECMA 契约，且随重新编译可变——"哪些类幸存"是 build 相关的经验事实；最终确认以 §6 的日志取证为准。）
3. `Awake` 的 catch（`Plugin.cs:78`）→ `CleanupFailedInitialization()`（`:81`）→ `UnpatchHarmony()`（`:180`）因 `_patchesApplied == false` **直接 early-return**（`Plugin.cs:194-195`），已应用的 patch 全部残留；随后 `UninstallStaticUtilities()`（`:181`）无条件执行，`BppSettingsDockCatalog.Reset()`（`Plugin.cs:127`）清空条目、`BppHotkeyService.Reset()` 注销配置。
4. 由于异常发生在 `Plugin.cs:53`，其后的 `composition.Start()`（`:66`）与 `Mountables.MountAll`（`:73`）**从未执行**：没有任何 MonoBehaviour 被挂载，没有 Update 泵。

对应三症状：
- **快捷键失效**：热键轮询发生在各面板组件的 `Update()`（`CollectionPanel.cs:333/350`、`HistoryPanel.cs:167/181`、`LiveBuildPanel.cs:84/96`）——组件从未挂载，轮询不存在。与输入 API 无关（F4）。
- **图鉴图标点击无反应**：图标由存活的 dock patch 每次进菜单时重建，但 `CollectionPanel._instance` 从未赋值（挂载未执行），点击命中"requested before CollectionPanel mounted"的静默 warn-and-return 守卫。
- **设置 dock 空容器**：存活的 dock patch 用被 `Reset()` 清空的 catalog 构建面板 → 容器能弹出、行数为 0。

> 曾被考虑并否决的备选根因："Unity 6 导致 BepInEx manager GameObject 中途销毁触发 OnDestroy 拆除"。复核 agent 用 B2（PTR 上 patch 目标确实缺失，PatchAll 必然中止）证伪了其前提"Awake 完整跑完"。PatchAll 中止机制无需任何引擎级推测即可同时解释三症状。

## 3. 已验证的 PTR 断裂清单与处置

| # | 断裂 | online 证据 | PTR 证据 | 处置 |
| --- | --- | --- | --- | --- |
| B1 | `NetMessageProcessor.ReceiveOrQueue` 新增 internal 2 参重载 → 两个**无参数类型限定**的 patch（`RunInitializedPatch.cs:11`、`CombatReplayCapturePatch.cs:11`）字符串解析歧义，抛 HarmonyException | `decompiled/.../NetMessageProcessor.cs:40`（唯一重载） | `decompiled-vptr/.../NetMessageProcessor.cs:40 + :45` | **L0**：patch 属性钉定参数类型 `new[]{ typeof(INetMessage) }`（两版都存在 1 参重载，版本无关修复），**外加下述聚合消息验证项** |
| B2 | `CosmeticsListManager.OnRandomizeToggleChanged` 在 PTR 被移除（随机皮肤 toggle 移入 `CosmeticsPanelController`）→ `RandomHeroSkinPoolTogglePatch`（`RandomHeroSkinPoolPatches.cs:38`）目标缺失，抛"Undefined target method" | `decompiled/.../CosmeticsListManager.cs:118` | 该类 0 命中；迁至 `decompiled-vptr/.../CosmeticsPanelController.cs:178` | **L3**：`Prepare()` 探测目标，缺失时 log + 干净跳过。PTR 上 RandomHeroSkinPool 功能整体降级停用（L4）；完整 PTR 适配留作独立后续（postfix 的 `__instance` 类型、面板挂点都变了，不止改 target） |
| B3 | `CosmeticsListManager.RefreshView` 签名不变但函数体大幅变薄（去掉 FadeInMenu/UpdateHeroLoadoutUI/SetCategoryLabelAndIcon 等） | `decompiled/.../CosmeticsListManager.cs:123` | `decompiled-vptr/.../CosmeticsListManager.cs:57` | postfix 仍可绑定，不阻塞启动；随 B2 一起在 PTR 停用该功能，避免行为漂移 |

**B1 附加分析——聚合消息解包不对称（红队发现，PR1 已实施修复）**：`NetMessageAggregate` 是两版共有的在网型别（`BazaarGameShared/.../INetMessage.cs:10` `[Union(6, ...)]`）。online 在 `NetMessageProcessor.cs:89-92` 解包聚合时**自递归回公开的 `ReceiveOrQueue`**——patch 对每条内层消息都会触发；PTR 改为经私有 `Receive` 递归（`decompiled-vptr/.../NetMessageProcessor.cs:116-119`），公开方法只被进入一次、参数是外层聚合对象 → 钉 1 参重载在 PTR 上会**静默漏掉聚合下发的对局消息**。正常对局流量是否使用聚合无法静态判定，但失败模式是静默数据丢失，代码评审两轮均 CONFIRMED 该机制，故 PR1 直接实施按形态路由的 seam（`Patches/NetMessageDispatchSeam.cs`）：
- `ResolveTarget()` 优先钉私有 `Receive(INetMessage, bool)`（存在即 PTR 形态，是 PTR 一切消息含聚合子消息的汇聚点；`decompiled-vptr/.../NetMessageProcessor.cs:57`），online 无该方法（`decompiled/.../NetMessageProcessor.cs` 全文无 `Receive` 声明）→ 回退公开 1 参 `ReceiveOrQueue`，online 行为与旧版完全一致。
- 观战排除：PTR 上唯一以 `allowGameSimAfterStateSync=true` 进入 `Receive` 的是锦标赛观战回放（`TournamentSpectatePlaybackProcessor.cs:136-296` 经 2 参 `ReceiveOrQueue`；观战的 RunInitialized 走 `ReceiveHandledMessageAsync:132-144`，不经过 `Receive`）。patch 体经 `__args` 读第二参，true 即跳过（`NetMessageDispatchSeam.IsSpectatePlayback`）。
- §6 矩阵仍需实测 PTR 对局的 run 检测与回放逐帧完整性，作为该 seam 的验证项。

不可从源码判定的残余（均为动态数据，非阻塞）：Addressables 资产 key（卡面/VFX/音轨）、prefab 名（`EncounterPicker_Map`）、UITK 引擎内部常量（`unity-text-input`）——由验证矩阵覆盖。

## 4. 方案：五层结构

设计原则：**成功路径上 online 行为零变化**（L1 有意强化的是 online 的*失败路径*：今后 online 常规更新打断某个 patch 目标时，从"整 mod 死亡 + 僵尸图标"降级为"单功能跳过 + 日志"——这是行为改善，明确声明而非隐瞒）；PTR 恢复核心功能；单一 DLL、单一代码库（不 fork、不双发行）；每个分歧点局部路由（resolve-or-fallback），全局版本标识只用于策略门与日志。

### L0 — 立即修复（B1，PR1 已实施）

- `RunInitializedPatch` 与 `CombatReplayCapturePatch` 改为 `TargetMethod()` 经 `Patches/NetMessageDispatchSeam.ResolveTarget()` 按形态解析目标 + `IsSpectatePlayback` 观战过滤（详见 §3 B1 附加分析）。这同时消解了歧义匹配（B1 本体）与聚合解包不对称（B1 附加）；曾考虑的"钉 1 参重载"中间方案因聚合问题被 seam 取代。Harmony 语义已对运行时 0Harmony 2.15.0 复核（F9）。

### L1 — Patch 隔离 + 拆卸对称性（本方案的核心韧性层）

1. **逐类应用**：`ApplyHarmonyPatches` 从单发 `PatchAll()` 改为：`AccessTools.GetTypesFromAssembly(assembly)`（内部处理 `ReflectionTypeLoadException`，返回可加载子集——不要裸用 `assembly.GetTypes()` 或预先按属性过滤，否则"游戏*类型*整个消失"这类未来断裂会在枚举期就复现全灭）遍历**全部**类型，对每个类型在**独立 try/catch** 内调 `_harmony.CreateClassProcessor(type).Patch()`（对非 patch 类型是 no-op，2.15.0 复核；这与 PatchAll 的发现集严格等价，也天然覆盖现有的 `[HarmonyPatch]` 无参 + `TargetMethod()` 型类如 `NameOverridePatches.cs:58`、`CombatReplayVisualPatches.cs:27/52`）。失败的类记 `BppLog.Error`（含目标签名）并继续，启动结束汇总"N 个 patch 类失败：…"。
2. **flag 语义修复**：消除"部分已应用但 `_patchesApplied == false` → 清理跳过 unpatch"的僵尸态（`Plugin.cs:194-195`）：首个类成功应用即置位，或干脆改为无条件 `UnpatchSelf()`（对未应用状态是无害 no-op）。
3. **拆卸各步隔离**：`CleanupFailedInitialization`（`Plugin.cs:173-182`）与 `OnDestroy`（`:86-103`）中每一步独立 try/catch，`UnpatchHarmony` 提前到最先执行，保证"patch 已应用 ⇔ 静态设施已安装"不变式在任何异常路径下成立。
4. （可选加固）dock patch 在 catalog 空（`Definitions.Count == 0`）时跳过构建。目前 10 个条目恒注册，"空即未安装"成立；若未来允许合法的空 catalog，需给 `BppSettingsDockCatalog` 加 `IsInstalled` 标志代替。

仅 L0+L1 即可让 PTR 恢复全部核心功能（B2 被隔离为单功能降级），且 online 成功路径行为完全不变。

### L2 — 版本识别（GameBuildChannel）

- `Core/Runtime/`：`enum GameBuildChannel { Online, Ptr, Unknown }` + `IGameBuildInfo { string RawVersion; GameBuildChannel Channel; }`（纯抽象，Core 零游戏引用）。
- `GameInterop/GameBuildInfoResolver.cs`：在 `BppComposition` 构造期解析一次。实现注意：进 `IBppServices` 意味着同时改 `IBppServices.cs`、`BppRuntimeServices` 的定位参数构造器（`BppRuntimeServices.cs:13-30`）和 `BppComposition.cs:80-88` 调用点，resolver 须在 `:80` 之前构造完成；patch 经 `BppPatchHost`（`Plugin.cs:49` 先于 `:53` 安装）可达。
  - **主判据**：`Application.version` 含 `-ptr` token（`IndexOf("-ptr", OrdinalIgnoreCase)`）。mod 已有读该 API 的先例（`MainMenuVersionLabelUpdater.cs:20`）。
  - **旁证探针**：`AccessTools.Inner(typeof(TheBazaar.Config), "ServerOption") != null`（PTR 独有嵌套类；online `decompiled/TheBazaar/Config.cs` 无，PTR 有）。
  - 两者不一致 → **判 Ptr** 并高声告警（评审修订：把 PTR 误判成 Online 会静默污染生产数据且无告警；把 Online 误判成 Ptr 只是暂停上传，analyzers 的 staleness 告警会很快暴露——失败方向选可被监控发现的那边）；版本串不可读 → 信探针并告警；两个信号都失效 → `Unknown`，策略门按 Online 行为处理并告警。
  - **两个待取证假设**（§6 验证项，L4 接线前必须确认）：(a) chainloader-Awake 时点 `Application.version` 可读（先例在场景内读取，Awake 时点未经证明）；(b) online 版本串确实无 `-ptr` 形状 token。两者用同一根启动日志线在**两个分支上各确认一次**即闭环。
  - 不采用 Steam acf `BetaKey`（反映订阅而非运行中的二进制，跨平台路径脆弱）；不采用 `Config.NetURL`（`SetupCommandArgs` 前为 null，有时序竞态）。
- 启动时打一行 `BppLog.Info("Plugin", $"Game build: {RawVersion} → {Channel}")`，以后所有 PTR 排障从这行开始。

### L3 — 分歧 seam 的路由模式

- **Patch 类**：目标存在性分歧 → `static MethodBase TargetMethod()` 按 "online 位置 → PTR 位置" 顺序 resolve-or-fallback；`static bool Prepare()` 在两处都缺失时 log + return false（2.15.0 复核：Prepare=false 干净跳过单类不抛）。**Prepare()/TargetMethod() 体必须异常安全**——体内抛异常仍是硬失败（有 L1 时单类死、无 L1 时全灭），探测逻辑一律 try/catch 包裹、失败返回 false/null。
- **优先局部特征探测而非全局 enum 分支**：online 上第一优先命中 online 目标，行为可证不变；未来任一分支漂移最多退化为"该功能跳过"。
- **非 patch 分歧**（未来出现时）：在 `GameInterop/<Concept>/` 建 adapter 接口 + 按 `GameBuildChannel`（或局部探测）选实现，feature 层只消费 seam——版本逻辑不进 `Game/`。
- 当前实际需要路由的只有 B2 一个 seam（B1 若触发升级方案 (b) 则成为第二个）；本层主要是把**模式**定下来供未来复用。
- 与 repo 规则的关系说明：CLAUDE.md"替换子系统时不留旧路径 fallback"针对的是*迁移*场景；跨版本兼容 seam 的双路径是并存的运行时现实，不属于该规则的射程。

### L4 — 功能策略门 + 数据隔离（需要拍板的产品决策）

PTR 是不同的卡池/平衡/服务器，数据混入会污染 V4 服务端、analyzer 与 tenwin 推荐。

**关键设计约束（红队 BLOCKER）：会话级开关挡不住污染。** 上传选取是纯 dirty-flag 驱动、无 channel 概念（`RunLogSchema.cs:59-266` 的 runs/battles/run_sync_state 无任何 channel/build 列；`:245-246` 的 dirty 索引全量枚举；`BackgroundUploadPump.cs:70` 的 gate 是会话态）。在 PTR 会话里只关闭上传，PTR run 仍以 dirty=1 持久落库；切回 online 后同一个安装、同一个库，上传泵会把这些行**如数补传**——恰好是要防的事故。因此：

1. **行级持久标记（必做）**：录制时把 `GameBuildChannel`（或原始版本串）写进 run 行（additive 列，沿用现有 schema migration 机制），上传枚举在 SQL 层排除非 online 行。截图/BazaarDB 快照上传队列同理检查一遍。
2. 会话级策略门（`Channel == Ptr` 时）作为第二道闸：

| 功能 | PTR 默认 | 理由 |
| --- | --- | --- |
| Run bundle 上传（V4 服务端） | **关**（行级标记 + 会话门双保险） | 防止 D1/R2 与 analyzer-v4 被 PTR 数据污染；本地 SQLite 记录照常 |
| BazaarDB 截图/快照上传 | **关** | 同上 |
| Ghost battles 拉取 | **关** | online 数据在 PTR 卡池下无意义 |
| LiveBuild 十胜推荐 | **关**（或保留 + "数据来自 online"角标） | 推荐基于 online meta |
| 回放/历史面板/图鉴/快捷键/截图等本地功能 | 开 | 纯本地 |
| RandomHeroSkinPool | **关**（B2/B3 降级） | 待独立适配 |

3. **本地跨版本正确性**（红队补充）：历史面板全表列出 runs（`HistoryPanelRepository.cs:36-60` 无过滤），PTR 对局快照里的卡 GUID 在 online 卡典中可能不存在——绑定路径必须优雅降级（呼应既有约束"未知 GUID 不得进 `CollectionCardFactory.TryBind`"）；有了 channel 列后可顺手给 PTR 行加标签或按 channel 过滤显示。
4. 备选（phase 2）：run bundle 附带 `gameBuildChannel` 字段、服务端按 channel 隔离——需要服务端配合，等 PTR 数据确有分析价值再做。

### L5 — 工具链与流程（部分已落地）

已落地（本次会话）：
- `./run.sh decompile-ptr [Dll]` / `decompile-all-ptr` → 输出至 `decompiled-vptr/`。
- **Steam 分支守卫**：`decompile*` 命令前读 acf 的 `MountedConfig.BetaKey`，分支与目标树不匹配即拒绝；`BPP_SKIP_BRANCH_CHECK=1` 越过。acf 通过**从 `MANAGED`（DLL 实际来源）向上逐级查找**定位（评审发现：早期版本从 `GAME_ROOT` 推导，与可独立覆盖的 `BPP_MANAGED_PATH` 解耦，守卫会验证错对象；现已修正，且顺带支持二级 Steam 库）。裸拷贝的 Managed 目录（上方无 acf）会 fail-closed 并提示跳过开关。
- `decompiled-vptr/` 已生成（PTR build 1.0.11358 全 6 DLL），已被 .gitignore 覆盖。

待实施：
1. **`./run.sh snapshot-managed`**：把当前 Managed + 分支/buildid 归档到 `game-libs/<channel>-<buildid>/Managed/`（gitignored）。因 F1，这是保留"另一版本程序集"的唯一途径。现在先归档 PTR；下次切回 online 立即归档 online。
2. **Release 构建钉 online 程序集（红队 MAJOR，必做）**：当前 `./run.sh all --prod` 在 PTR 分支下会把**按 PTR 程序集编译的 DLL** 直接拷进 installer resources 发给 online 用户（`run.sh` 的 build/all 无分支守卫；`BazaarPlusPlus.csproj:282-342` Release 自动拷贝）。整改：Release/BuildAll 路径强制 `require_steam_branch public` **或**显式 `-p:ManagedPath=game-libs/online-<buildid>/Managed` 钉快照，二者缺一即拒绝打包。**在切回 online 分支并归档之前，本机不可产出正式 Release。**
3. **双版本编译矩阵**：`./run.sh build-matrix` 对 `game-libs/online-*/Managed` 与 `ptr-*/Managed` 各编译一次，保证单一源码树对两套程序集持续可编译。
4. **双树兼容性测试 `PtrCompatibility.Tests`**：沿用 exe-runner 源码文本断言模式（先例 `NativeCardPreviewCompatibility.Tests`——该模式是"签名守卫"而非覆盖率作秀，与 workspace 规则的紧张关系以此先例为准并在测试头注释说明）。断言两树的 seam 前提：两树都有 1 参 `ReceiveOrQueue(INetMessage)`；online 树有 `CosmeticsListManager.OnRandomizeToggleChanged`、PTR 树有 `CosmeticsPanelController.OnRandomizeToggleChanged`；PTR 树有 `Config.ServerOption` 而 online 树无；PTR 树聚合递归走 `Receive`（B1 附加分析的前提）。**可复现性约束（红队 MAJOR）**：两树都是 gitignored 本地产物，此测试是**本机专属门禁**（CI/新 checkout 不可用）——树缺失时必须 **skip-and-pass 并打印提示**（不得像现有先例那样硬抛，否则 online 分支下 `./run.sh test` 恒红）；TFM 用 net10.0（对齐 `RandomHeroPoolPatchCompatibility.Tests`；本机 net8.0 测试项目环境性失败）。
5. **流程与收敛触发器**：每次 PTR 更新 → `decompile-all-ptr` + 兼容性测试；每次回 public 分支 → `decompile-all`（整树刷新，见 F3 staleness）+ `snapshot-managed`。**收敛信号**：若刷新后的 online 树令"`CosmeticsListManager.OnRandomizeToggleChanged` 存在"断言失败 = PTR 改动已并入 online → 触发 PR4（重指目标 + 恢复功能 + 退役 PTR skip）。责任人：你（单人项目），信号载体就是这条测试失败。

## 5. 实施顺序（PR 切分）

1. **PR1（核心，恢复 PTR；本次会话已实现，待双版本测试）**：L0 `NetMessageDispatchSeam` 形态路由 + L1 逐类 patch/flag/拆卸对称（含 teardown 日志调用自身的异常防护）+ B2 `Prepare()` 跳过 + run.sh（decompile-ptr / MANAGED 推导的分支守卫）。预期效果：PTR 三症状全消，RandomHeroSkinPool 单功能降级留日志。
2. **PR2（已实现，2026-07-03）**：L2 `IGameBuildInfo`（`Core/Runtime/IGameBuildInfo.cs` + `GameInterop/GameBuildInfoResolver.cs`，入 `IBppServices.GameBuild`）+ L4 行级标记（`runs`/`run_screenshots` 新增 `build_channel` 列，schema v17，`EnsureInitialized` 内 ALTER 补列；录制时打标）+ 上传双闸（`BackgroundUploadPump.Initialize` PTR 直接不激活 feed；run bundle 与 BazaarDB 快照共 5 处枚举 SQL 排除 `build_channel='Ptr'` 行）+ 启动日志。按用户拍板：**只禁上传**，ghost 拉取/十胜推荐/图鉴等全部保留。注：PR1→PR2 之间在 PTR 上录的行无标记（NULL 按可上传处理），但 run bundle 枚举本就只挑 Ranked——实测该窗口只有 1 条 Unranked 行，无污染风险；**切回 online 前的一次性回填**（评审 CONFIRMED：截图路径没有 Ranked 门，窗口期的 NULL 截图行会漏传 BazaarDB）：`UPDATE runs SET build_channel='Ptr' WHERE started_at_utc >= '<PTR安装日>' AND build_channel IS NULL;` + `UPDATE run_screenshots SET build_channel='Ptr' WHERE captured_at_utc >= '<PTR安装日>' AND build_channel IS NULL;`（2026-07-03 16:55 实测：窗口内 1 条 Unranked run、0 条截图、0 条 pending 上传——当前无泄漏，但会话仍在进行）。
3. **PR3**：L5 的 snapshot-managed / Release 钉定 / build-matrix / PtrCompatibility.Tests。
4. **PR4（收敛触发或按需）**：RandomHeroSkinPool 完整 PTR 适配（新挂点 `CosmeticsPanelController`、`__instance` 适配、B3 行为验证）。

## 6. 验证方法

**先证根因（一次启动，PR1 之前强烈建议）**：
1. 先 `./run.sh build`（触发 macOS trampoline 修复——分支切换/更新后不修复则 BepInEx 不注入，即 04:15 Player.log 无 BPP 日志的状况），启动一次确认注入（LogOutput.log 出现 `[BPP]` 行）。
2. 经 Steam 启动（仅 `open steam://run/1617400`），复现症状后**先把 `BepInEx/LogOutput.log` 拷走再做任何重启**（BepInEx 每次启动截断该文件——上一次的证据就是这么丢的）。
3. 预期看到 `Plugin initialization failed` + HarmonyException（`Ambiguous match ... ReceiveOrQueue` 或 Cosmetics `Undefined target method`，先撞哪个取决于该 build 的类型枚举顺序，见 §2.2 注）。看到即根因闭环；看不到则回到备选机制排查（OnDestroy 探针）。

**PR1 后 PTR 矩阵**：启动日志无 patch 失败（除 RandomHeroSkinPool 显式 skip 行）→ `Game build: ... → Ptr` 日志行正确（L2 假设 (a)(b) 取证）→ 设置 dock 全条目 → 图鉴开/关（含快捷键）→ 历史/LiveBuild 面板快捷键 → keybind 设置行克隆 + 交互改键 → **实际打一场 PTR 对局：run 检测触发、run logging 完整、回放捕获逐帧完整（B1 聚合不对称的判定项——缺帧/漏 run 即触发 B1 升级方案 (b)）** → 战斗回放视频 → 图鉴卡面/VFX 抽查（动态资产 key 残余）。
**Online 回归矩阵（切回 public 后）**：`decompile-all` 整树刷新 + `snapshot-managed` → 同一清单全绿 + `Game build: ... → Online` 日志 + RandomHeroSkinPool 恢复 + 上传管线正常（此时若本地有 PTR 期间的 run 行，验证它们**不被**补传——L4 行级标记的判定项）。
**静态门**：`./run.sh test`（逐 runner 检查输出而非只看退出码）+ build-matrix 双编译 + PtrCompatibility.Tests（本机门禁）。

## 7. 风险与开放问题

1. **引擎跳版残余风险**（F6）：mod 编译引用 `UnityEngine.Modules 2022.3.40` nuget，运行在 6000.3 上。游戏侧托管 API 已由 F2 编译验证覆盖（UIElements/InputSystem 走 Managed 实际 DLL），Unity 核心模块（ScreenCapture、UITK 运行时行为、TMP 渲染）只能靠 in-game 矩阵覆盖。PTR 上 UI 渲染异常优先怀疑这层。
2. **online 基线陈旧**（F3）："216→3" 是对 5 月底 online 基线的结论。回 public 分支刷新前，"online 零变化"主张的证据链不完整；刷新后需重跑 PtrCompatibility.Tests 快速复核 seam 前提。
3. **PTR 迭代频率**：L1 保证新断裂最多单功能降级 + 日志可见；L5 流程一条命令重建证据基础；收敛触发器见 L5.5。
4. **B1 聚合不对称**的实际影响未经运行验证（§6 矩阵显式判定项，有明确的升级路径）。
5. **B3 行为变化**未经运行验证（当前被策略门冻结）。
6. **L4 决策**需拍板：行级标记方案、"PTR 本地照记只关上传"、历史面板对 PTR 行的展示策略（标签 vs 过滤）。
7. 动态资产 key 在 PTR 的有效性无法静态判定——矩阵发现图鉴卡面缺失即属此类，与路由层无关。

## 8. 否决过的备选方案

- **双 DLL / 双发行**：分发复杂度翻倍（installer、自动更新、用户选择），且 F2/F3 证明分歧面极小。
- **`#if PTR` 条件编译 fork**：违反单一源码树目标，PTR 短生命周期会留永久疤痕。
- **全局 enum 路由一切分歧**：版本逻辑会漏进 `Game/` 层；改为 seam 局部 resolve-or-fallback + channel 只做策略门与日志。
- **给 BepInEx manager GameObject 做 Unity 6 生命周期加固**：其依据的"OnDestroy 中途拆除"根因被复核证伪；L1 拆卸对称性以更小代价覆盖同一失效族。
- **Steam acf BetaKey / Config.NetURL 作为版本判据**：前者反映订阅而非运行二进制且跨平台脆弱，后者有初始化时序竞态。
