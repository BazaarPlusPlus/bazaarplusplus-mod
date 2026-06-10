---
status: superseded
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Codebase Health Audit — 2026-06-05

> 只读审计。覆盖 fallback 残留 / 不合理逻辑 / 分层 / 性能 / 文档漂移，并给出按优先级排序的优化方案。
> 基线 commit：`1f623f0`

> 注(854e4e1 后更新):摘要所列 6 条『仍未关闭』(F-001、F-008、F-012、F-035、F-037、F-044)已全部在 commit 854e4e1 修复;其余代码类发现仍待独立任务。

## 摘要

- 总发现数：**44**（P0 **1** / P1 **15** / P2 **28**）。
- 验证严谨性：除以下清单外，另有 **6 条候选发现在对抗式独立复核中被驳回（refuted）并剔除**，未计入本清单；进入清单的每条均经过 `file:line` 级证据复核，行号漂移处已用复核后的修正锚点替换。
- 去重：**4 组近重复发现（共吸收 6 条冗余条目）已合并**——PreviewTune 死方法（D1/D2 各报一次）、截图主线程阻塞（同一方法两个切面）、`BppKeybindSettingsPatch` 循环内分配（同文件两处）、`BazaarAgentHttpServer` 空 catch（同文件四处同机制）。原始 50 条 → 合并后 44 条。
- 本轮 D1 / D3 含多条**正面确认**（迁移边界已删干净、分层未违规），作为覆盖性验证记录保留：D1 的 5 条、D3 的 3 条。
- 与上一次审计（`docs/audits/2026-06-04-doc-code-drift-audit.md`）的关系：**已修复 3**（CollectionPanel 权威文档已建并入索引 F-041；CLAUDE.md 程序集清单已纠正为 4+2 F-039；CLAUDE.md Layer boundaries 已补 `Localization/` 条目 F-042）/ **仍未关闭 6**（PreviewTune 死方法 F-001、`ModApiJsonPost` 注释复数 F-008、FontAtlasSampleCache 缓存键 bug F-012、README 安装清单漏 Localization.dll F-035、combat-replay 音频设计文档无 Status banner F-037、settings dock 定义归因错误 F-044）/ **退化 1**（SQLite LocalDatabaseSchemaVersion 文档 15 vs 代码 16：上次对账当日 commit `33cf5aa` 把代码 bump 到 16，文档再度漂移 F-034）/ **新增 4 条 D5 漂移**（F-033、F-036、F-038、F-043）**+ 2 条修复后残留**（F-039、F-042 的可读性残留）；D1–D4 的代码体检发现均为本轮新增维度。

---

## 发现清单

### D1 Fallback / 旧路径残留

- **[F-001] HistoryPanelText 中 `PreviewTuneStatus` / `PreviewTuneHelp` 为零调用方死代码（未实现功能的遗留草稿）** — 严重度 P1
  - 证据：`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelText.cs:803-806`（`PreviewTuneStatus`）；`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelText.cs:890-896`（`PreviewTuneHelp`）
  - 问题：两个 `internal static` 方法属于从未实现的"预览调参"功能（Ctrl+方向键等热键调参）。全库 grep（src/ + tests/，含反射字符串与序列化用途排查）确认**零调用方**；上一次审计（2026-06-04，§3 comment-drift 表与执行步骤 24）已独立确认同一结论，至今未清理。按本仓库规则与审计分级口径，死代码归 P1。
  - 建议：整体删除两方法（含其中的死热键提示字符串）；同时删除 `docs/reference/hotkeys-reference.md` 中描述同一从未实现功能的 "HistoryPanel Preview Tuning" 段（上次审计已标 delete-section）。如未来要恢复该功能，从 git 历史找回。
  - 影响面 & 工作量：无运行时行为影响；已验证 tests/ 下无任何 csproj `Compile Include` 钉住 `HistoryPanelText.cs`，不会触发 exe-runner 测试工程的路径陷阱。工作量 **S**。
  - 状态：confirmed

- **[F-002] BazaarAgent 两插件分离已彻底完成：无 `#if` 编译开关、无隐式回退路径** — 严重度 P2（正面确认）
  - 证据：`src/BazaarPlusPlus/BppComposition.cs:123-131`（无条件发布被动 facade `BazaarAgentGameBridge.Current`，注释明确说明 host 插件经 `[BepInDependency]` 后加载）；`src/BazaarPlusPlus/GameInterop/BazaarAgent/BazaarAgentGameBridge.cs:1-15`（public static accessor + internal setter）；`src/BazaarPlusPlus/BazaarPlusPlus.csproj:103-107`（主插件仅引用 Storage/ModApi/Localization，零引用 BazaarAgent/BazaarAgentHost）
  - 问题：（验证目标）ADR-0006 要求的反向依赖是否落地、有无 `#if BPP_BAZAARAGENT_HOST` 残留或运行时回退。复核结论：编译开关零命中；依赖方向单向且干净；host 插件正确声明 `[BepInDependency(BppPluginMetadata.Guid)]`；facade 是被动 accessor，host 未安装时无人读取。
  - 建议：无需修改。`run.sh` 的 `--with-bazaaragent` 可选构建链路正确。
  - 影响面 & 工作量：无 / **S**（仅验证）。
  - 状态：confirmed

- **[F-003] V3 服务端迁移无残留：无 v3 端点 / DTO / 客户端命名** — 严重度 P2（正面确认）
  - 证据：`src/BazaarPlusPlus.ModApi/Models/ModApiUploadDefaults.cs:7`（默认端点 `https://mod-api-v4.bazaarplusplus.com`）；`src/BazaarPlusPlus.ModApi/Clients/ModApiRoutes.cs`（仅 v4 风格路径：`/run-bundles`、`/ghost-battles`、`/health`、`/bazaardb/snapshots`）
  - 问题：（验证目标）CLAUDE.md 称 V3 后端源码已移除。复核结论：src/ 与 tests/ 全树 grep `v3|V3|mod-api-v3` 零命中（.csproj 中 NuGet v3 index URL 属合法例外）。
  - 建议：无需修改。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

- **[F-004] 附魔预览迁移 shim 已在 `19dabcc` 清除，无旧 schema 兼容分支残留** — 严重度 P2（正面确认）
  - 证据：`src/BazaarPlusPlus/Core/Config/BppConfig.cs:41-46`（当前三态 `EnchantPreviewModeConfig` 直接 Bind，无迁移调用）；`src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewService.cs:1-30`（直接消费 config，无旧 schema 分支）
  - 问题：（验证目标）commit `19dabcc` 删除了 `MigrateLegacyEnchantPreviewAlwaysShow()` 与 `OrphanedEntries` 探测逻辑后是否留有兼容残留。复核结论：迁移代码已完全清除。
  - 建议：无需修改。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

- **[F-005] `src/` 物理重构无遗留构建胶水：无根级输出块、无旧路径引用** — 严重度 P2（正面确认）
  - 证据：`Directory.Build.props:1-6`（仅 `BppVersion` 与 `EnforceCodeStyleInBuild`，无遗留输出路径覆盖）；`src/BazaarPlusPlus/BazaarPlusPlus.csproj:103-107`（ProjectReference 全部为 `../<AssemblyName>/` 相对路径）；`run.sh:60,78`（指向 `src/BazaarPlusPlus.BazaarAgentHost/...csproj`）
  - 问题：（验证目标）commit `1f623f0` 物理重构后有无 `Compile Remove/Include` 胶水、根级 `obj/bin`、旧根路径引用。复核结论：六个程序集各自独立编译锥，重构干净。
  - 建议：无需修改。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

- **[F-006] `RunLogEvent` 死字段脚手架（Options / Selected\* / RunLogOptionSnapshot）已彻底清除** — 严重度 P2（正面确认）
  - 证据：`src/BazaarPlusPlus.Storage/RunLog/RunLogEvent.cs:6-55`（全类恰 19 个属性，无 Options / Selected\* 字段；全库 grep `RunLogOptionSnapshot` 零命中）
  - 问题：（验证目标）种子线索称这些字段可能无 producer 无 consumer。复核结论：相关脚手架已不存在于代码中，schema 字段清单已稳定。
  - 建议：无需修改。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

- **[F-007] RandomHeroSkinPool 旧 PlayerPrefs key 迁移为合法一次性数据升级 shim，非系统级 fallback** — 严重度 P2
  - 证据：`src/BazaarPlusPlus/Game/Lobby/RandomHeroSkinPool/RandomHeroSkinPoolPlayerPrefs.cs:13`（`LegacyHeroSkinPoolPrefsKeyPrefix`）；`:16-38`（`LoadSelectedIds` 先查新 scoped key，未命中且为 HeroSkins 类型时读旧 key 并立即另存为新 key）
  - 问题：这是数据格式升级（旧 prefs key → 新 scoped key）而非"新旧两套实现并跑"，范围单一（仅 HeroSkins 集合类型）、全库仅此一处引用旧前缀，不违反"整体删除旧实现"规则。但它没有移除期限，会无限期存活。
  - 建议：可保留。若希望收敛，定一个移除版本（如下一次 major），过期后连旧 key 读取一并删除（见 Open questions）。
  - 影响面 & 工作量：低风险 / **S**。
  - 状态：confirmed

- **[F-008] `ModApiJsonPost.cs` 注释误述"多个 upload clients"消费方（实为单一消费方）** — 严重度 P2
  - 证据：`src/BazaarPlusPlus.ModApi/Http/ModApiJsonPost.cs:10-13`（XML 注释 "used by the upload clients"，复数）；`src/BazaarPlusPlus.ModApi/Clients/BazaarDbSnapshotClient.cs:31`（唯一调用方）；`src/BazaarPlusPlus.ModApi/Clients/RunBundleClient.cs:30`（走 `RunBundleMultipartContent`，不经过 `ModApiJsonPost`）
  - 问题：全库（src/ + tests/）检索确认 `PostJsonAsync` 仅 `BazaarDbSnapshotClient` 一个消费方，注释误导读者对消费关系的理解。上一次审计 §3 已列为 comment-drift，仍未关闭。
  - 建议：将注释改为单数表述（如 "used by the snapshot upload client"）。
  - 影响面 & 工作量：仅可读性 / **S**。
  - 状态：confirmed

### D2 不合理逻辑 / 代码坏味

- **[F-009] `CardTooltipDataFactory` 六处私有字段反射全部用 null-forgiving 掩盖 `GetField` 失败** — 严重度 P0
  - 证据：`src/BazaarPlusPlus/Game/Tooltips/CardTooltipDataFactory.cs:20-23`（`_cardInstance`）、`:25-28`（`_cardTemplate`）、`:30-33`（`_monster`）、`:35-38`（`_valueContext`）、`:40-43`（`_compiledTooltips`）、`:45-48`（`_localizationService`）——六个 `static readonly FieldInfo` 均以 `GetField(..., NonPublic)!` 结尾；使用点 `:52`（`MonsterField.GetValue`）与 `:65-73`（连续 `SetValue`/`GetValue`）
  - 问题：`!` 只是编译期断言，运行期零保护。游戏更新一旦重命名任一私有字段，`GetField` 返回 null，静态字段初始化"成功"地装入 null，`Create()` 在首次 `SetValue/GetValue` 时抛 NullReferenceException，卡牌提示/预览功能整体崩塌且故障点远离根因。已对照 `decompiled/` 确认这六个字段当前存在——风险在于版本更新后的脆弱性，而非当下即错。
  - 建议：移除全部 `!`；在静态初始化（或首次使用）做一次性反射校验，缺字段时记 `BppLog.Warning` 并降级为"不增强该 tooltip"；或改用集中式的反射访问器并显式判空。
  - 影响面 & 工作量：Game/Tooltips 功能；不触 wire / MessagePack / 分层 / **M**。
  - 状态：confirmed

- **[F-010] `GhostBattleSyncService.TryExtractPayloadFromArtifact` 裸 catch 吞掉反序列化失败，幽灵对战加载不可诊断** — 严重度 P1
  - 证据：`src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs:187-190`（`catch { return null; }`）
  - 问题：`RunBundleArtifactCodec.Deserialize` 或后续处理抛出的任何异常（数据损坏、版本错配、脏数据）被无日志吞掉；上层调用点（`DownloadReplayAsync` 约 :90）只能给出泛化的 `replay_payload_missing`，用户与运维无法区分"文件不存在 / 文件损坏 / 版本不兼容"。对照同类 `CombatReplayPayloadStore.Load()` 已采用 `catch (Exception ex)` + `BppLog.Warn` 的正确模式，此处属遗漏。
  - 建议：改为 `catch (Exception ex)` 并 `BppLog.Warn` 记录异常详情；或让 codec 返回带错误类别的结果（与 F-011 联动）。
  - 影响面 & 工作量：仅诊断性，不改成功路径 / **S**。
  - 状态：confirmed

- **[F-011] `MessagePackGzipCodec.Deserialize<T>` 裸 catch 静默吞掉所有解压/反序列化异常（三个 codec 共用）** — 严重度 P1
  - 证据：`src/BazaarPlusPlus.ModApi/MessagePackGzipCodec.cs:54-57`（`catch { return null; }`）
  - 问题：该方法是 `RunBundleArtifactCodec`、`GhostBattlePayloadCodec`、`PvpReplayPayloadCodec` 三个 codec 的共享底座，承载回放、幽灵对战、artifact 的持久化加载。gzip IO 异常与 MessagePack 反序列化异常一律变成 null，调用方（`CombatReplayPayloadStore`、`GhostBattlePayloadStore` 等）拿不到任何失败原因。"文件不存在"与"文件坏了"在整条链路上不可区分。
  - 建议：ModApi 程序集零 BepInEx 依赖，不能直接用 `BppLog`——改为返回带错误信息的结果类型（`TryDeserialize(out string? error)` 或 Result<T>），或注入日志委托；按仓库规则取最干净签名，不留旧签名 shim（见方案 P0-2）。
  - 影响面 & 工作量：ModApi 公共方法签名变化，程序集内部消费方需同步更新；wire 字节格式不变 / **M**。
  - 状态：confirmed

- **[F-012] `FontAtlasSampleCache` 键缺 `CurrentMode` 维度：中文简繁模式切换后返回陈旧缓存值** — 严重度 P1
  - 证据：`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelText.cs:12-14`（`Dictionary<string, string>`，仅 languageCode 作键）；`:841-843`（命中即返回，不校验 mode）；`:858`（生成时却调用 `set.Resolve(languageCode, L.CurrentMode)`）；`:878`（仅以 languageCode 存储）
  - 问题：`LocalizedTextSet.Resolve`（`src/BazaarPlusPlus.Localization/LocalizedTextSet.cs:76`）对中文会按 `BppChineseLocaleMode` 经 `ChineseScriptConverter` 转换；缓存键却不含 mode，用户在简体/繁体模式间切换后，字体图集采样字符串命中错误的旧值，影响 CJK 字体渲染正确性。上一次审计已将其识别为"有计划的潜伏 bug"（本地化 P3 工作包），至今未修。
  - 建议：键改为 `(languageCode, mode)` 复合键（元组或拼接），读写两侧同时使用 `(L.CurrentLanguageCode, L.CurrentMode)`。
  - 影响面 & 工作量：HistoryPanel 字体采样路径；低频触发但属正确性 bug / **M**。
  - 状态：confirmed

- **[F-013] `EndOfRunScreenshotController` 对 `OnContinueClick` 的 `AccessTools.Method(...)!` 掩盖方法缺失，Invoke 处会 NRE** — 严重度 P1
  - 证据：`src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:22-25`（`AccessTools.Method(typeof(EndOfRunScreenController), "OnContinueClick")!`）；`:273`（`ContinueClickMethod.Invoke(controller, [])`，无判空无兜底）
  - 问题：与 F-009 同型。游戏更新重命名/删除 `OnContinueClick` 后，`AccessTools.Method` 返回 null，`!` 把它装进非空字段，端局"继续"路径在 :273 直接 NRE。代码库其他位置对 `AccessTools` 结果均有显式判空，此处属例外。
  - 建议：移除 `!`，初始化时校验并记日志；:273 调用前判空，缺失时降级（跳过自动 continue，仅记 Warning）。
  - 影响面 & 工作量：端局截图→继续流程 / **S**。
  - 状态：confirmed

- **[F-014] `ShopForecastLogPatch.TryGetStaticTemplate` 裸 catch 静默降级，反射破裂不可回溯** — 严重度 P2
  - 证据：`src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs:139-142`（`catch { return null; }`）；外层上下文 `:85-92`（`SafeBuildEnrichment` 的总兜底确实记日志）
  - 问题：`Data.GetStatic()?.GetCardById(templateId)` 失败时返回 null，日志富数据呈现为 "static-guid-not-found" / "template=null"，掩盖了真正原因（如游戏更新导致反射接口变化）。因外层 `SafeBuildEnrichment` 已有带日志的总兜底，影响被部分缓解，定级 P2。
  - 建议：改为 `catch (Exception ex)` + `BppLog.Debug` 留痕，保留返回 null 的降级行为。
  - 影响面 & 工作量：仅日志富数据质量 / **S**。
  - 状态：confirmed

- **[F-015] `ModApiErrorFormatter.TryParseHttpErrorPayload` 吞掉 JSON 解析异常** — 严重度 P2
  - 证据：`src/BazaarPlusPlus.ModApi/ModApiErrorFormatter.cs:48-51`（方法 `TryParseHttpErrorPayload` 内 `catch { return null; }`，吞掉 :37 `JObject.Parse` 的异常）
  - 问题：服务端返回非预期格式时解析失败无任何诊断；`FormatHttpFailure`（:20）会退回原始响应体，错误信息不至于完全丢失，但解析失败本身不可追踪。
  - 建议：改为 `catch (Exception ex)` 并经注入的诊断通道留痕（与 P0-2 的 ModApi 错误处理风格统一）。
  - 影响面 & 工作量：错误信息美化路径 / **S**。
  - 状态：confirmed

- **[F-016] `ResolvePlayerAccountId` 两处裸 catch 包装 `TryGetProfileAccountId`，反射异常不可追踪** — 严重度 P2
  - 证据：`src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs:115-118`；`src/BazaarPlusPlus/Game/RunLogging/Upload/RunBundleUploadService.cs:126-129`（两处相同的 `catch { return null; }`）
  - 问题：`BppClientCacheBridge.TryGetProfileAccountId()` 自身无异常处理，内部 `ReadMember` 的反射操作可能抛出；两处调用点把异常静默归约为"没登录"，无法区分"缺字段 / 反射破裂 / 真的未登录"。返回 null 本身是可接受降级，问题在零日志。
  - 建议：改为 `catch (Exception ex)` + `BppLog.Debug`；或让 `BppClientCacheBridge.TryGetProfileAccountId()` 统一吃掉并记录异常，调用方不再包 try。两处为复制粘贴同型，应一起修。
  - 影响面 & 工作量：诊断性 / **S**。
  - 状态：confirmed

- **[F-017] `BazaarAgentHttpServer` 同文件四处空 catch（Stop / AcceptLoop / HandleContextAsync / WriteErrorEnvelope）全部无日志** — 严重度 P2
  - 证据：`src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs:70-84`（`Stop()` 内三个相邻空 catch，分别在 :74 / :79 / :84，包 `_cts?.Cancel()`、`_listener?.Stop()`、`_listener?.Close()`）；`:102-105`（`AcceptLoop()` 的 `catch { return; }`——监听循环无声死亡，`IsRunning` 不复位，fire-and-forget Task 异常永不被观察）；`:135-151`（`HandleContextAsync` 外层 catch 内 :142 的 `WriteErrorEnvelope` 双层故障空 catch + finally :150 的 `ctx.Response.Close()` 空 catch）；`:260-265`（`WriteErrorEnvelope` 写响应失败空 catch）
  - 问题：同一文件、同一机制（空 catch 不记日志）四个站点。最值得关注的是 AcceptLoop：`GetContextAsync` 抛出（端口被回收、HttpListener 内部故障）会让监听循环无声退出，调用方无法区分"正常取消"与"异常死亡"，远程命令突然不响应且无据可查。其余三处属关闭/双层故障路径的优雅降级，但同样诊断不透明。`_logger`（`IBazaarAgentLogger`）在该类内可用，加日志无结构障碍。
  - 建议：四处统一改为 `catch (Exception ex) { _logger.Warning(...) }`；AcceptLoop 退出时必须记因；设计上确实要静默的（双层故障）至少加注释说明理由。
  - 影响面 & 工作量：BazaarAgent 传输层可观测性，不影响游戏本体 / **S**。
  - 状态：confirmed

- **[F-018] `BazaarAgentActionQueue.PendingAction.SetResponse` 中 `Timer.Dispose()` 异常被空 catch 吞掉** — 严重度 P2
  - 证据：`src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentActionQueue.cs:50-54`（`try { _timer?.Dispose(); } catch { }`；:48 经 `CompareExchange` 原子保护）
  - 问题：并发安全本身无问题（原子完成位 + GC 终将回收 Timer），但 dispose 失败完全无痕。`PendingAction` 无 logger 注入，是结构性小障碍。
  - 建议：经父 `BazaarAgentActionQueue` 的日志通道记 Warning，或加注释 `// Best-effort cleanup; timer will be collected anyway` 明示设计意图。
  - 影响面 & 工作量：极低 / **S**。
  - 状态：confirmed

- **[F-019] `BazaarAgentRuntimeController.StopListener` 两处空 catch 包关闭操作，端口残留不可诊断** — 严重度 P2
  - 证据：`src/BazaarPlusPlus.BazaarAgent/Runtime/BazaarAgentRuntimeController.cs:121-131`（:121-125 包 `_http?.Stop()`、:127-131 包 `_queue?.Dispose()`，均空 catch；`_logger` 在 :18 可用）
  - 问题：监听器关闭是生命周期关键路径（`ReconcileListener` :96/:115 与 `Dispose` :246 都会走到）。快速启停场景中失败的关闭可能导致端口残留阻塞下一次启动，而当前完全无日志可查。
  - 建议：改为 `catch (Exception ex) { _logger.Warning("Listener teardown failed", ex); }`。
  - 影响面 & 工作量：BazaarAgent 监听器生命周期 / **S**。
  - 状态：confirmed

### D3 分层违规

- **[F-020] D3 全维度复核：未发现分层违规；16 个架构测试有效守住既有边界** — 严重度 P2（正面确认）
  - 证据：`tests/Architecture.Tests/CoreLayeringTests.cs:19-75`（Core 不依赖 Game/GameInterop/游戏程序集）；`:304-340`（GameInterop 不依赖 Game 功能命名空间）；`src/BazaarPlusPlus/Patches/Combat/CombatSimulationPatches.cs:20-30`（Patches 经 `BppPatchHost.Services.EventBus` 发布，未直连功能内部）；`src/BazaarPlusPlus/GameInterop/` 全目录 50 个 .cs 中仅 2 个 public 类型（`BazaarAgentGameBridge`、`IBazaarAgentGameProbe`，即 ADR-0006 facade），其余全 internal
  - 问题：（验证目标）CLAUDE.md 各层规则。复核结论：无跨功能导入游戏运行时适配器的违规，Patches 正确走服务定位器，分层完好。
  - 建议：无需新增修改。可选项：为"两个 feature 复用同一运行时行为须抽到 GameInterop"补一条针对性架构测试——当前无此类案例，暂不必要。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

- **[F-021] 四个纯程序集（ModApi / Storage / Localization / BazaarAgent）确认零游戏 / Unity / BepInEx 依赖** — 严重度 P2（正面确认）
  - 证据：`src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj:1-17`（PackageReference 仅 NETStandard.Library / Newtonsoft.Json / MessagePack compile-only）；`src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj:1-16`（+ Microsoft.Data.Sqlite）；`src/BazaarPlusPlus.Localization/BazaarPlusPlus.Localization.csproj:1-14`（仅 NETStandard.Library）；`src/BazaarPlusPlus.BazaarAgent/BazaarPlusPlus.BazaarAgent.csproj:1-15`（+ Newtonsoft.Json）
  - 问题：（验证目标）隔离性约束。复核结论：四个 csproj 均无游戏 / Unity / BepInEx 引用（注意是 PackageReference 而非 ProjectReference，原始报告用词已修正），源码亦无相关 `using`。
  - 建议：无需修改。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

- **[F-022] Game/\* 跨功能协作（HistoryPanel → CombatReplay）走构造注入，模式正确** — 严重度 P2（正面确认）
  - 证据：`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelReplayService.cs:6,18-25`（`using BazaarPlusPlus.Game.CombatReplay` + `Func<CombatReplayRuntime?>` 构造参数，由组合根级联传入；全库无 `new CombatReplayRuntime` 或静态单例直取）
  - 问题：（验证目标）跨功能导入是否绕过依赖注入。复核结论：经 `Func<>` 惰性访问器注入，符合既有协作模式，不构成违规。
  - 建议：保持现状。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

### D4 性能瓶颈

- **[F-023] `BppClientCacheBridge.ReadStaticMember` 每次调用现做 GetProperty + GetField 反射，无成员缓存** — 严重度 P1
  - 证据：`src/BazaarPlusPlus/GameInterop/BppClientCacheBridge.cs:157-163`（`ReadStaticMember`：每次 `type.GetProperty(...)?.GetValue(...) ?? type.GetField(...)?.GetValue(...)`）；调用链 `src/BazaarPlusPlus/Patches/NameOverride/NameOverridePatches.cs:117-131`（`SetHeroNamePatch.Prefix` → `NameOverrideHelper.TryGetReplacementName`（:123，helper 内 :40 调 `TryGetProfileUsername`））
  - 问题：`HeroBannerController.SetHeroName` 在运行期间可被多次调用，每次都额外做 `AccessTools.TypeByName` + GetProperty + GetField 共 3+ 次反射查询。`ClientCache` 是游戏静态单例，会话内结构不变，重复查询是纯浪费（CPU，非分配热点）。
  - 建议：初始化时把常用成员（Profile / Leaderboard / Rank 等）的 `FieldInfo`/`PropertyInfo` 缓存进 `static readonly` 字段，`Read*` 方法改用预缓存；或建一个初始化时完成反射缓存的 `ClientCacheAccessor` 单例。
  - 影响面 & 工作量：GameInterop 内部，调用方签名不变 / **M**。
  - 状态：confirmed

- **[F-024] `ItemEnchantPreviewPatch` 在 tooltip 构建热路径上链式字符串分配（Replace×2 + TrimEnd + Split）** — 严重度 P1
  - 证据：`src/BazaarPlusPlus/Patches/Tooltips/ItemEnchantPreviewPatch.cs:40-97`（`CardTooltipDataPassivePatch.Postfix`，打在 `CardTooltipData.GetPassiveTooltipBlock` 上）；`:25-37`（helper `AppendTooltipText` 内 `.Replace("\r\n","\n").Replace('\r','\n').TrimEnd('\n').Split('\n')`，每链节各产生一个新 string / 数组）
  - 问题：`GetPassiveTooltipBlock` 由 `CardController.ShowTooltips` 与 `CardTooltipTypeHandler` 调用；频繁悬停/快速切换卡牌时帧内多次执行，每次产生多个临时 string 与一个数组分配，在集合面板等卡牌密集 UI 中累积 GC 压力。
  - 建议：把 Replace/Split 链改为单遍历手写行扫描（按 `\r\n`/`\r`/`\n` 切行直接 append），消除中间字符串与数组；可选：对短时间内同卡多次查询缓存 `ItemEnchantPreviewService.BuildPreviewSegments` 结果。
  - 影响面 & 工作量：补丁内部实现，输出文本不变 / **M**。
  - 状态：confirmed

- **[F-025] 端局截图整链同步阻塞主线程：ReadPixels（GPU→CPU stall）+ EncodeToPNG + File.WriteAllBytes** — 严重度 P1（合并：PNG 编码/落盘阻塞 + ReadPixels 同步回读两条）
  - 证据：`src/BazaarPlusPlus/Game/Screenshots/ScreenshotService.cs:63-91`（`WriteCurrentFrameToFile`：:76-78 `new Texture2D` + `texture.ReadPixels` + `Apply`，:79 `EncodeToPNG`，:83 `File.WriteAllBytes`，全部同步）；调用点 `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:228-242`（`CaptureAndContinue` 协程在 `WaitForEndOfFrame` 后同步执行）；对照先例 `src/BazaarPlusPlus/Game/CombatReplay/Video/ReplayVideoCaptureSession.cs:315-318`（`CaptureScreenshotIntoRenderTexture` + `AsyncGPUReadback.Request`，非阻塞）
  - 问题：单次端局截图把三段开销串在主线程：GPU 同步回读（通常 <100ms 但属可测主线程 stall）、PNG 编码（CPU 密集，可达秒级）、同步磁盘写。用户在端局画面→继续按钮之间能感知到帧冻结。本仓库的 CombatReplay 已有 AsyncGPUReadback 先例，应复用同一模式而非另起炉灶。
  - 建议：改为 `ScreenCapture.CaptureScreenshotIntoRenderTexture` + `AsyncGPUReadback.Request`（对齐 CombatReplay 先例），回调中把 PNG 编码与文件写入移到后台（`Task.Run` / 后台队列）；协程改为等待异步完成信号。
  - 影响面 & 工作量：截图时序从同步变异步，需保证 continue 时序与截图存在性约束不被破坏（见 Open questions）/ **M**。
  - 状态：confirmed

- **[F-026] 端局截图元数据走主线程同步 SQLite 写（`RunScreenshotSqliteStore.Save`）** — 严重度 P1
  - 证据：`src/BazaarPlusPlus.Storage/RunScreenshot/RunScreenshotSqliteStore.cs:13-72`（`Save` 同步 `ExecuteNonQuery`，:71）；调用链 `src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs:240-242`（协程内 `PersistCapture(capture, isPrimary: true)`）→ `:286-298`（`PersistCapture` → `_screenshotStore.Save(...)`，:293）
  - 问题：每次端局截图在主线程协程内同步执行一次 SQLite INSERT（量级 ~100ms 含磁盘同步），且与 F-025 的 PNG 编码串联在同一帧序列里，叠加端局卡顿。RunLogging 已有 `QueuedRunLogStore` 后台队列先例，此处未复用。
  - 建议：参照 `QueuedRunLogStore` 模式为 RunScreenshot 建后台队列 store（入队 → 后台工作线程写库），或在控制器侧把持久化整体挪进 F-025 的后台段。
  - 影响面 & 工作量：Storage + Screenshots；schema 不变 / **M**。
  - 状态：confirmed

- **[F-027] BazaarDB 截图上传的图片准备（读盘 + ImageSharp resize + 原子写）无超时/取消保护** — 严重度 P1
  - 证据：`src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadService.cs:39-46`（`UploadPendingInBackgroundAsync` 经 `Task.Run` 后台执行）；`src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotUploadStore.cs:122-213`（`TryBuildSnapshot` :159 调 `_imagePreparer`）；`src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotImagePreparer.cs:76-142`（`PrepareResized`：`Image.Load` + resize 循环 + 多次 `WriteAllBytesAtomically`）
  - 问题：虽已离开主线程，但单张大图的 I/O + CPU 编码可达秒级，`CancellationToken` 没有贯穿到图片准备段，无超时机制——后台上传任务可能被单张图长时间卡住，批次（3 张/批）整体停滞且不可中止。
  - 建议：把 `CancellationToken` 传入 `Prepare`/`PrepareResized` 并在循环间检查；加超时（如 30s），超时后放弃 resize、直接上传原始尺寸或标记失败进入重试。
  - 影响面 & 工作量：上传后台链路 / **S**。
  - 状态：confirmed

- **[F-028] `BppKeybindSettingsPatch.RefreshRoutine` 120 帧循环内每帧双倍 `GetComponentsInChildren<Transform>` 分配 + LINQ `Select().ToArray()`** — 严重度 P1（合并：HasInstalledRows 双数组 + EnsureKeybindRows LINQ 两条）
  - 证据：`src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs:278-290`（`HasInstalledRows`：两次 `GetComponentsInChildren<Transform>(true)` + 两个 `.Any()`，每次各分配一个全树 Transform 数组）；`:57`（`EnsureKeybindRows` 内 `Definitions.Select(d => d.ObjectName).ToArray()`，`Definitions` 为 :20-32 的 2 元素静态常量数组）；驱动循环 `:244-276`（`RefreshRoutine`：`RetryFrames = 120`（:213），每帧调 `EnsureKeybindRows`（:253）与 `HasInstalledRows`（:257）直至命中）
  - 问题：settings 对话框每次打开触发 RefreshRoutine，最坏 120 帧内每帧扫两遍整个 `OptionsDialogController` 子树（~50-100 个 Transform）再加一次 LINQ 数组分配，累积 ~12-24 KB GC 压力。功能上只需要查找两个固定名称的对象。
  - 建议：`HasInstalledRows` 改为单次 `GetComponentsInChildren` 复用结果，或直接按两个固定名称做 `transform.Find`/字典缓存；`Definitions` 的名称数组预建为静态字段，消除每帧 `Select().ToArray()`。
  - 影响面 & 工作量：Patches/Settings 内部 / **M**。
  - 状态：confirmed

- **[F-029] `NativeKeybindLabelAwakePatch.TryUpdateLabels` 在同一 120 帧循环内每帧分配组件数组且 `AccessTools.Field` 反射不缓存** — 严重度 P1
  - 证据：`src/BazaarPlusPlus/Patches/Settings/NativeKeybindLabelPatch.cs:32-48`（`TryUpdateLabels`：每次 `GetComponentsInChildren<KeyBindController>(true)`）；`:57-82`（`FindLabel`：每个 controller 重复 4 次 `AccessTools.Field(typeof(KeyBindController), ...)` 现查 + 一次 `GetComponentsInChildren<TextMeshProUGUI>(true)` 分配）；驱动循环 `src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs:244-276`（RefreshRoutine 每帧调 `TryUpdateLabels`，:255）
  - 问题：120 帧 × 每帧（1 个 KeyBindController 数组 + 每 controller 1 个 TMP 数组 + 4 次未缓存反射），按 5-10 个 KeyBindController 估算 ~600-1200 次数组分配 + 15+ KB GC 压力，全部发生在打开设置对话框的窗口期。
  - 建议：四个 `FieldInfo` 提升为 `static readonly` 缓存；`TryUpdateLabels` 成功设置后置标志，不在循环内重复执行；`FindLabel` 内组件数组复用。
  - 影响面 & 工作量：Patches/Settings 内部 / **M**。
  - 状态：confirmed

- **[F-030] CollectionPanel 首加载诊断缺口：首窗口 native binding / art 加载耗时仍未计时（EnsureView 计时已落地）** — 严重度 P2
  - 证据：`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:573-686`（`LoadPanelAsync` 仅记 catalog/filter/refresh 段，日志点 :660/:674/:681/:684）；`:234-254`（`ensureViewMs` 计时**已**实现——设计文档 `docs/design/2026-05-31-collection-panel-first-load-performance.md:100-104` 称 EnsureView 仍是缺口的说法已过期，属文档侧漂移）
  - 问题：复核修正后的真实缺口是：首窗口 `CollectionCardFactory.TryBind(...)` 与卡面 art 加载没有独立计时。若用户可见延迟主要在 binding 而非 catalog，现有日志无法定位。
  - 建议：在首个 virtualizer Tick 完成与首批 TryBind 前后加可选 debug 计时；同步把设计文档 §P0 的 instrumentation 段更新为"EnsureView 已计时，剩余缺口为首窗口 binding/art"。
  - 影响面 & 工作量：诊断补点 / **S**。
  - 状态：confirmed

- **[F-031] `SettingsMenuToggleInstaller.ArrangeRow` 的 `Cast/Where/OrderBy/ToList` LINQ 链被 RefreshRoutine 间接每帧调用** — 严重度 P2
  - 证据：`src/BazaarPlusPlus/Patches/Settings/SettingsMenuToggleInstaller.cs:51-56`（`parentRect.Cast<Transform>().Where(...).OrderBy(...).ToList().FindIndex(...)`）；调用链 `src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs:83,91`（`EnsureKeybindRow` → `ArrangeRow`）与 `:253`（RefreshRoutine 每帧 `EnsureKeybindRows`）
  - 问题：与 F-028/F-029 同一驱动循环。~20 个子对象的枚举器 + 排序 + List 分配，每帧 1-2 次，120 帧累积数 KB。单独看量小，定级 P2，但应随 F-028 一起治理。
  - 建议：改 for 循环直接遍历计数，或在行安装完成后只调用一次（脱离 RefreshRoutine 循环）。
  - 影响面 & 工作量：Patches/Settings 内部 / **M**（与 F-028 合并施工则边际成本 S）。
  - 状态：confirmed

- **[F-032] `RandomHeroPoolPatches.TrySelectConfiguredRandomHero` 用户触发路径上的 `Where().ToArray()` + `Select()` 临时分配** — 严重度 P2
  - 证据：`src/BazaarPlusPlus/Patches/Lobby/RandomHeroPoolPatches.cs:122`（`reflectedUnlockedHeroes.Where(view => view != null).ToArray()`）；`:129`（`unlockedHeroViews.Select(view => view.Hero.ToString())`）；补丁体 `:87-157`
  - 问题：仅在用户点击"随机选择"时触发（非逐帧），单次 ~400-800 字节临时分配，属低频路径上的不必要分配，非热点。
  - 建议：改 for/foreach 内联过滤与字符串转换，顺手清理即可，不必单独立项。
  - 影响面 & 工作量：极低 / **S**。
  - 状态：confirmed

### D5 文档漂移

- **[F-033] `docs/README.md` 仍把 BazaarAgent 标注为"当前 parked"，与 ADR-0006 落地后的代码现实不符** — 严重度 P1
  - 证据：文档断言 `docs/README.md:23,32,33`（"当前 parked" / "（parked）"×2）；代码事实 `docs/adr/0006-bazaaragent-as-its-own-plugin.md:1,15`（独立插件决策）、`src/BazaarPlusPlus/BppComposition.cs:123-131`（facade 无条件发布）、`run.sh:90,158-159,166`（`--with-bazaaragent` 可选构建链）、`src/BazaarPlusPlus.BazaarAgentHost/`（完整 `[BepInPlugin]`+`[BepInDependency]` 插件工程）。注：`docs/features/bazaar-agent.md:1-5` 本身**不含** parked 字样，其状态 banner（"默认物理不安装……独立的可选 BepInEx 插件"）已是 ADR-0006 之后的正确描述，可直接作为 README 措辞的对齐目标。
  - 问题：本仓库文档语境里 "parked" 一直意指"未启用/搁置"（见上次审计 §4 的 parked≠obsolete 区分）。BazaarAgent 现已**完整实现**为独立可选插件，仅默认不随构建分发；README 索引继续标 parked 会让读者误判功能成熟度，且与它链接的 features 页自身描述相互矛盾。该漂移由 ADR-0006 落地引入，属上次审计之后的新漂移。
  - 建议：将 `docs/README.md:23,32,33` 的 parked 措辞改为与 `docs/features/bazaar-agent.md:4` 一致的"可选独立插件（`--with-bazaaragent` 按需构建，默认不随包分发）"。
  - 影响面 & 工作量：仅文档 / **S**（README 三处一行级措辞）。
  - 状态：confirmed

- **[F-034] SQLite 文档 `LocalDatabaseSchemaVersion` 写 15，代码已是 16（含 `is_final_battle` 列名漂移）** — 严重度 P1
  - 证据：文档断言 `docs/reference/sqlite-schema-reference.md:28,31`（"Local schema version: 15" / "user_version = 15"）、`:33`（版本历史缺 v16 条目）、`:110`（写 `is_bundle_final_battle`，实际列为 `is_final_battle`）；代码事实 `src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs:10`（`LocalDatabaseSchemaVersion => 16`，commit `33cf5aa`，2026-06-04）
  - 问题：SQLite schema 是持久化稳定契约文档；上次审计当日完成对账后代码同日 bump 到 16，文档随即再度漂移——属**退化**项，提示 schema 变更没有"改代码必改 reference 文档"的硬性环节。
  - 建议：`:28,31` 的 15→16；`:33` 版本历史补 v16 条目（最终战斗元数据，commit `33cf5aa`）；`:110` 列名改 `is_final_battle`。可在 PR 模板/规则层面考虑 schema 变更与 reference 文档的捆绑要求（人类拍板）。
  - 影响面 & 工作量：仅文档 / **S**。
  - 状态：confirmed

- **[F-035] README 手动安装指导遗漏 `BazaarPlusPlus.Localization.dll`（及 ModApi/Storage 的明确列示）** — 严重度 P1
  - 证据：文档断言 `README.md:25`（只写复制 `BazaarPlusPlus.dll` + SQLite 依赖）；代码事实 `src/BazaarPlusPlus/BazaarPlusPlus.csproj:223`（Debug `PluginManagedRuntimeFiles` 无条件包含 Localization.dll；Release 侧 :317 同）、`:106`（ProjectReference）
  - 问题：Localization.dll 是无条件构建/复制的核心依赖，按文档手动安装会缺 DLL 导致运行时加载失败。上次审计 low 表已列同一问题（README.md:25），仍未关闭，且其用户影响（安装失败）按本轮口径应为 P1。
  - 建议：README.md:25 改为完整 DLL 清单（BazaarPlusPlus / ModApi / Storage / Localization + SQLite 原生依赖），或改为"复制构建输出目录中的所有托管程序集与原生依赖"。`README_en.md` 同步。
  - 影响面 & 工作量：仅文档 / **S**。
  - 状态：confirmed

- **[F-036] CollectionPanel 性能设计文档的 P3 "Deferred" 判决基于单次实测，无复验触发条件** — 严重度 P2
  - 证据：`docs/design/2026-05-31-collection-panel-first-load-performance.md:150`（"filter=2.7ms cold / 2.2ms reopen, not currently the bottleneck"；:148 "Status: Deferred."）；`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:660-684`（日志段无任何阈值告警/复验触发）
  - 问题：判决依据是单次运行日志；catalog 增长或硬件差异可能使其失效，而文档与代码都没有写明何时需要重新评估。
  - 建议：在 P3 段补 "Revalidate upon: catalog >3000 cards / filter 耗时超阈值 / 下次 profiling session"；或在日志侧加 warn 阈值。
  - 影响面 & 工作量：仅文档（可选加一行日志阈值）/ **S**。
  - 状态：confirmed

- **[F-037] combat-replay 音频/采集性能设计文档缺顶部 Status banner（音频侧已被 WASAPI 方案取代，视频侧仍现行）** — 严重度 P2
  - 证据：`docs/design/2026-05-30-combat-replay-audio-and-capture-perf-design.md:1-8`（顶部状态仍写"设计待定稿"，第 7 行更新注记称音频决策已改 WASAPI loopback，但无规范 banner）；代码侧已全量走 WASAPI/CoreAudio（`src/BazaarPlusPlus/Game/CombatReplay/Audio/ReplayAudioCaptureFactory.cs:24-53` 按平台选 `WasapiLoopbackCaptureTap`/`CoreAudioProcessTapCaptureTap`）
  - 问题：读者需通读全文并自行比对代码才能知道哪半篇已失效。上次审计清理计划第 7 条（split banner + 归档）未执行，仍未关闭。
  - 建议：顶部加 "Status: SUPERSEDED (audio) / IMPLEMENTED (video)" split banner；按上次审计的顺序约束（先确认兄弟 spec 链锚再移动）归档。
  - 影响面 & 工作量：仅文档 / **S**。
  - 状态：confirmed

- **[F-038] ADR-0006 缺失于 `docs/README.md` 的 ADR 索引** — 严重度 P2
  - 证据：`docs/README.md:38`（仅列 ADR 0001–0005）；`docs/adr/0006-bazaaragent-as-its-own-plugin.md:1`（实存，且 supersedes ADR-0005）
  - 问题：理解 BazaarAgent 插件化设计的关键决策记录无法从索引发现，读者可能停留在已被取代的 ADR-0005。
  - 建议：`docs/README.md:38` 追加 `[0006](../../adr/0006-bazaaragent-as-its-own-plugin.md) BazaarAgent 独立插件化`，并标注其取代 0005。
  - 影响面 & 工作量：仅文档 / **S**。
  - 状态：confirmed

- **[F-039] CLAUDE.md "Four assemblies … two BazaarAgent assemblies" 后紧跟 6 项连排列表，分组不直观** — 严重度 P2
  - 证据：`CLAUDE.md:48-55`（4+2 的陈述本身**准确**——上次审计的 "Three assemblies" high 级错误已修复；残留问题是 6 项列表无视觉分隔，易误数）
  - 问题：纯可读性残留：新读者对照"四个无条件"与 6 项连排列表时容易困惑。
  - 建议：列表分两组（无条件 4 个 / `--with-bazaaragent` 2 个），用分隔或小标题区分。
  - 影响面 & 工作量：仅文档 / **S**。
  - 状态：confirmed

- **[F-040] 版本号一致性确认：4.1.0 全仓统一，无新漂移** — 严重度 P2（正面确认）
  - 证据：`Directory.Build.props:3`（`<BppVersion>4.1.0</BppVersion>`）；`src/BazaarPlusPlus/obj/Release/netstandard2.1/MyPluginInfo.cs:7`（生成的 `PLUGIN_VERSION = "4.1.0"`）；`docs/adr/0006-bazaaragent-as-its-own-plugin.md:23`（"Release version is 4.1.0；中间的 5.0.0/6.0.0 内部 bump 未发布"）
  - 问题：（验证目标）`Unify mod version to 4.1.0` 后各处版本字符串。复核结论：一致，无发现。
  - 建议：无需修改。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

- **[F-041] 上次审计的 CollectionPanel 权威文档缺口已修复：`docs/features/collection-panel.md` 已建并入 README 索引** — 严重度 P2（正面确认 / fixed）
  - 证据：`docs/features/collection-panel.md:1`（"# Collection Panel"，49 行 as-shipped 契约：入口/生命周期/过滤维度/sources catalog v3/虚拟化与缓存）；`docs/README.md:16`（索引条目已含该链接与描述）
  - 问题：（验证目标）上次审计最大的 doc-gap（177 行处的 create-doc 建议）。复核结论：已落地。
  - 建议：无进一步行动。
  - 影响面 & 工作量：无 / **S**。
  - 状态：confirmed

- **[F-042] CLAUDE.md 的 Localization 层描述过简（条目已补，但缺入口/接口信息）** — 严重度 P2
  - 证据：`CLAUDE.md:72`（一句话描述：zero-dependency localization engine … extracted from `Game/Settings`）；对照实际入口 `src/BazaarPlusPlus.Localization/L.cs:11-15`（`Install` 签名等公共面）；上次审计该层整体漏列的问题（其 §2.2 `CLAUDE.md:62-68` 行）已在 commit `60c2bb0` 修复
  - 问题：修复后的残留：相比其他层（Core/GameInterop/Game 均有职责+示例），Localization 仅一句话，缺 `L` 单例、入口接线方式等帮助新读者建立心智模型的信息。
  - 建议：扩写一句话为两三句：`L` 单例（lookup / locale switching / language code resolution）、独立程序集、由组合根接线（按 `L.cs:11-15` 的真实签名写，勿照抄旧 review 文本）。
  - 影响面 & 工作量：仅文档 / **M**（小幅改写）。
  - 状态：confirmed

- **[F-043] CONTEXT.md 术语表未覆盖 CollectionSources 相关活跃概念（Collection Source Catalog / Offer Pool / Merchant Kind）** — 严重度 P2
  - 证据：`CONTEXT.md:1-16`（全文件仅 Run/encounters 一节）；活跃概念 `src/BazaarPlusPlus/Game/CollectionPanel/Sources/CollectionSourceCatalog.cs:13`（catalog 类，:101-104 强制 schema v3）、`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionMerchantKind.cs:6`（15 类商人分桶枚举）
  - 问题：非严格漂移（既有词条仍准确），是术语表不完整：后来者要理解 source catalog / offer pool / merchant kind 需通读代码或散落的设计文档。
  - 建议：在 CONTEXT.md 补三条词条（Collection Source Catalog / Offer Pool / Merchant Kind）。是否值得纳入术语表由人类拍板（见 Open questions）。
  - 影响面 & 工作量：仅文档 / **S**。
  - 状态：confirmed（是否补充词条待人类拍板）

- **[F-044] settings dock 条目定义源归因错误：文档指向 `BppSettingsDockCatalog.cs`，实际注册在 `BppComposition.cs`** — 严重度 P2
  - 证据：文档断言 `docs/reference/settings-and-debug-surfaces.md:5`（"具体定义来自 `Game/Settings/BppSettingsDockCatalog.cs`"）；代码事实 `src/BazaarPlusPlus/BppComposition.cs:86-92`（7 个 `ISettingsDockEntry` 直接 `_settingsDockRegistry.Register(...)`）、`src/BazaarPlusPlus/Game/Settings/BppSettingsDockCatalog.cs:13-28`（仅收集/排序/物化，不含定义）
  - 问题：把开发者引到错误的源码位置。上次审计 §2.2 已列同一问题，仍未关闭。
  - 建议：文档改为"条目在 `BppComposition.cs`（:86-92）注册，`BppSettingsDockCatalog.cs` 负责收集、排序与物化"。
  - 影响面 & 工作量：仅文档 / **S**。
  - 状态：confirmed

---

## 优化方案（D6）

> 分级口径：P0 = 正确性/运行期风险；P1 = 分层 + 死代码 + 明确性能问题；P2 = 文档 + 低风险整洁。
> 通用约束：删除类条目一律**整体删除旧实现，不留 fallback、不搭新旧并跑的构建链**（仓库规则）。
> 通用验证注意：当前 `./run.sh test` 会在失败时返回非零退出码；仍建议每次跑完同步检查输出中的 `Failed test projects:`，以确认失败项目清单完整，不要只看 tail 截断输出。

### P0 — 正确性 / 运行期风险

- **[P0-1] 脆弱反射加固：移除 null-forgiving，启动期/首次使用校验 + 降级** ← 解决 F-009, F-013
  - 改动范围：`src/BazaarPlusPlus/Game/Tooltips/CardTooltipDataFactory.cs`（6 个 FieldInfo 声明 + :52/:65-73 使用点）、`src/BazaarPlusPlus/Game/Screenshots/EndOfRunScreenshotController.cs`（:22-25 声明 + :273 调用）。仅主插件程序集。
  - 影响面与风险：不触 wire 契约 / MessagePack 图 / 架构测试。行为变化：游戏字段/方法缺失时从"抛 NRE 崩功能"变为"记 Warning 并降级"（tooltip 不增强；端局自动 continue 跳过）。需定义清楚两处的降级语义。`CardTooltipDataFactory` 目前由 tooltip refresh 路径懒触发，若实现"启动期校验"必须显式接入组合根或 mountable 初始化；否则方案应落为"首次使用时一次性校验"，避免假称启动期覆盖。
  - 版本影响：无 breaking change；随常规版本（4.1.x → 下一次发布）即可。
  - 验证方法：`./run.sh build`；`dotnet run --project tests/TooltipPreviewTargetResolver.Tests/TooltipPreviewTargetResolver.Tests.csproj`；`dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`；`dotnet run --project tests/EndOfRunScreenshotGate.Tests/EndOfRunScreenshotGate.Tests.csproj`；游戏内（Steam `steam://run/1617400` 启动）悬停卡牌 + 跑完一局，读 `BepInEx/LogOutput.log` 确认无 `[BPP]` Error。

- **[P0-2] 反序列化路径吞异常治理：codec 失败可诊断** ← 解决 F-010, F-011（并为 F-015 定风格基调）
  - 改动范围：`src/BazaarPlusPlus.ModApi/MessagePackGzipCodec.cs`（公共签名改为携带错误信息的形态，如 `TryDeserialize<T>(byte[] payload, out T? value, out string? error)` 或 Result 类型）；其 ModApi 内包装层 `RunBundleArtifactCodec` / `GhostBattlePayloadCodec` / `PvpReplayPayloadCodec` 同步；主插件消费方（`CombatReplayPayloadStore`、`GhostBattlePayloadStore`、`src/BazaarPlusPlus/Game/HistoryPanel/Ghost/GhostBattleSyncService.cs:187-190` 等）改为记 `BppLog.Warn(error)`。
  - 影响面与风险：**wire 字节格式（gzip+MessagePack）不变**，仅进程内 API 形态变化；MessagePack DTO 图不动（保持全 public）。ModApi 程序集的全部消费方都在本仓库且与主插件同捆分发，无外部消费者。按仓库规则**直接改签名、删除旧签名，不留兼容 shim**。
  - 版本影响：进程内 API breaking 但无对外表面变化——建议随常规 minor；若人类认定 ModApi.dll 属对外 API 表面，则按规则 bump major（见 Open questions Q2）。
  - 验证方法：`dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj`；`dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj`；`dotnet run --project tests/CombatReplayRecording.Tests/CombatReplayRecording.Tests.csproj`；`./run.sh build`；全量 `./run.sh test` 并 grep `Failed test projects:`。ModApi 测试需新增"gzip magic 正确但 gzip/body 损坏"用例，确保它与 null/empty/non-gzip 输入能被区分或至少能产出诊断 error。

- **[P0-3] FontAtlasSampleCache 缓存键补 `CurrentMode` 维度** ← 解决 F-012
  - 改动范围：`src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelText.cs:12-14,841-878`（键类型与读/写两侧）。
  - 影响面与风险：纯内部缓存键修复，无契约影响；风险仅在键构造遗漏一侧导致缓存失效（性能回退而非错误值，可接受）。
  - 版本影响：无。
  - 验证方法：`./run.sh build`；`dotnet run --project tests/HistoryPanelPreview.Tests/HistoryPanelPreview.Tests.csproj`；游戏内将中文模式在简/繁间切换并打开 HistoryPanel，核对 CJK 渲染无 tofu、`LogOutput.log` 无字体告警（CJK 问题须走字体路由而非改文案——仓库规则）。

### P1 — 分层 + 死代码 + 性能

- **[P1-1]（删除类）整体删除 PreviewTune 死代码及其死文档段** ← 解决 F-001
  - 改动范围：删 `src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelText.cs:803-806,890-896` 两方法；删 `docs/reference/hotkeys-reference.md` 的 "HistoryPanel Preview Tuning" 段（功能从未实现）。
  - 影响面与风险：零调用方（已 grep 确认，含反射字符串），且已验证无测试 csproj `Compile Include` 钉住该文件——无 exe-runner 路径陷阱。**整体删除，不留 fallback**；不保留注释化残骸。
  - 版本影响：无。
  - 验证方法：`./run.sh build`；`rg -n "PreviewTune" src tests docs` 应零命中（docs 仅余本审计文档自身）；`./run.sh test` + grep `Failed test projects:`。

- **[P1-2] `BppClientCacheBridge` 反射成员缓存化** ← 解决 F-023
  - 改动范围：`src/BazaarPlusPlus/GameInterop/BppClientCacheBridge.cs`（`ReadStaticMember`/`ReadMember` 改用启动期缓存的 `FieldInfo/PropertyInfo`；公共方法签名不变）。
  - 影响面与风险：GameInterop 层内部，符合"共享运行时适配留在 GameInterop"的分层规则；注意保持**反射而非 Publicizer**的既有约定。缓存须容忍成员缺失（null 缓存 + 一次性 Warning），与 P0-1 风格一致。
  - 版本影响：无。
  - 验证方法：`./run.sh build`；游戏内进对局确认名字覆盖（NameOverride）仍生效；`dotnet test tests/Architecture.Tests/Architecture.Tests.csproj`（确认 GameInterop 边界测试仍绿）。

- **[P1-3] 设置对话框 RefreshRoutine 分配与反射治理（三文件一揽子）** ← 解决 F-028, F-029, F-031
  - 改动范围：`src/BazaarPlusPlus/Patches/Settings/BppKeybindSettingsPatch.cs`（`HasInstalledRows` 单扫/名称直查；`:57` 名称数组静态化）、`NativeKeybindLabelPatch.cs`（4 个 `AccessTools.Field` 提升 `static readonly`；成功后短路不重复执行）、`SettingsMenuToggleInstaller.cs:51-56`（LINQ 链改 for）。
  - 影响面与风险：仅 Patches/Settings；行为等价（行安装与标签刷新结果不变），风险在于短路条件写错导致行未装上——RefreshRoutine 的 120 帧重试语义须保留。
  - 版本影响：无。
  - 验证方法：`./run.sh build`；`dotnet test tests/SettingsDockRegistry.Tests/SettingsDockRegistry.Tests.csproj`；游戏内打开设置→Gameplay 确认两条 BPP keybind 行存在且本地化标签正确。

- **[P1-4] 端局截图链路异步化：AsyncGPUReadback + 后台编码/落盘 + DB 后台队列** ← 解决 F-025, F-026
  - 改动范围：`src/BazaarPlusPlus/Game/Screenshots/ScreenshotService.cs`（ReadPixels/EncodeToPNG/WriteAllBytes → `CaptureScreenshotIntoRenderTexture` + `AsyncGPUReadback`，在 Unity 主线程 copy readback bytes 到托管 buffer，后台使用纯托管编码/写盘；不要把 `Texture2D`、`RenderTexture`、`AsyncGPUReadbackRequest` 或 Unity API 调用带到 `Task.Run`。编码可复用现有 ImageSharp 依赖，对齐 `ReplayVideoCaptureSession.cs:315-318` 的 readback/copy 先例）；`EndOfRunScreenshotController.cs`（协程改等待异步完成）；`src/BazaarPlusPlus.Storage/RunScreenshot/`（参照 `QueuedRunLogStore` 增后台队列 store，或由控制器把 `Save` 挪进后台段）。
  - 影响面与风险：**本方案中最大的一项**。SQLite schema 不变、wire 不变；风险在时序——截图完成与 continue 触发、上传扫描（`BazaarDbSnapshotUploadStore.TryBuildSnapshot` 依赖文件已落盘）之间的先后关系必须保持。实施硬约束：**原子落盘成功后才写入 `run_screenshots` DB 行**；若实现上无法保证该顺序，则必须先把上传链路里的 `image_file_missing` 从 permanent failure 改为 transient retry，避免后台上传扫描到"已入库但尚未完整落盘"的截图后永久标失败。替换为新链路后**删除旧同步路径，不留双路径开关**。复用游戏/仓库既有组件（AsyncGPUReadback、后台队列先例），不自研新链。
  - 版本影响：无对外契约变化；行为时序变化需游戏内验证。
  - 验证方法：`dotnet run --project tests/RunScreenshotSqliteStore.Tests/RunScreenshotSqliteStore.Tests.csproj`；`dotnet run --project tests/EndOfRunScreenshotGate.Tests/EndOfRunScreenshotGate.Tests.csproj`；`dotnet run --project tests/BazaarDbScreenshotUploadStore.Tests/BazaarDbScreenshotUploadStore.Tests.csproj`；游戏内跑完一局：确认截图文件落盘、DB 行存在、端局→继续无可感知冻结、`LogOutput.log` 无 Error（见 Open questions Q3）。

- **[P1-5] 截图上传图片准备贯穿 CancellationToken + 超时** ← 解决 F-027
  - 改动范围：`src/BazaarPlusPlus/Game/Screenshots/Upload/BazaarDbSnapshotImagePreparer.cs`（`Prepare`/`PrepareResized` 加 token 参数与 ~30s 超时降级）、`BazaarDbSnapshotUploadStore.cs:159` 调用点、`BazaarDbSnapshotUploadService.cs` 传递既有 token。
  - 影响面与风险：后台链路；超时后的降级策略需在实现时定一个并写进代码注释。注意：进入 resize 路径说明原图已超过 `BazaarDbSnapshotUploadLimits.MaxUploadImageBytes`，因此超时后**不能无条件上传原图**；应标记 transient retry，或在确认原图仍满足大小限制时才走直传。
  - 版本影响：无。
  - 验证方法：`dotnet run --project tests/BazaarDbScreenshotUploadService.Tests/BazaarDbScreenshotUploadService.Tests.csproj`；`dotnet run --project tests/BazaarDbScreenshotUploadStore.Tests/BazaarDbScreenshotUploadStore.Tests.csproj`。

- **[P1-6] tooltip 附魔预览字符串处理单遍历化** ← 解决 F-024
  - 改动范围：`src/BazaarPlusPlus/Patches/Tooltips/ItemEnchantPreviewPatch.cs:25-37`（`AppendTooltipText` 行切分改单遍历，消除 Replace/Split 中间分配）。
  - 影响面与风险：输出文本必须逐字符等价（含 `\r\n`/`\r`/`\n` 与末尾空行语义）；建议先为现函数的输入输出对补一组用例再改。
  - 版本影响：无。
  - 验证方法：`dotnet run --project tests/ItemEnchantPreview.Tests/ItemEnchantPreview.Tests.csproj`；游戏内悬停附魔台对象核对预览文本不变。

### P2 — 文档 + 低风险整洁

- **[P2-1] reference 契约与安装文档修正** ← 解决 F-034, F-035
  - 改动范围：`docs/reference/sqlite-schema-reference.md:28,31,33,110`（15→16、补 v16 历史、`is_final_battle`）；`README.md:25` 与 `README_en.md`（完整 DLL 清单）。
  - 影响面与风险：仅文档；schema 数字以 `RunLogSchema.cs:10` 当刻值为准（动手前重读代码，防再漂移）。
  - 版本影响：无。
  - 验证方法：人工比对 `src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs` 与 `src/BazaarPlusPlus/BazaarPlusPlus.csproj:222-223,316-317` 的复制清单。

- **[P2-2] 导航 / 措辞 / 状态 banner 批量修正** ← 解决 F-033, F-036, F-037, F-038, F-039, F-042
  - 改动范围：`docs/README.md:23,32,33,38`（parked 措辞×3 + ADR-0006 索引）；`docs/features/bazaar-agent.md`（措辞同步）；`docs/design/2026-05-30-combat-replay-audio-and-capture-perf-design.md`（split banner，归档前按上次审计顺序约束先核兄弟 spec 链锚）；`docs/design/2026-05-31-collection-panel-first-load-performance.md`（P3 复验触发条件 + §P0 instrumentation 段更新 EnsureView 已计时）；`CLAUDE.md:48-55,72`（程序集列表分组 + Localization 层描述扩写，按 `L.cs:11-15` 真实签名）。
  - 影响面与风险：仅文档；CLAUDE.md 属 agent 规则文件，按规则措辞改进（澄清既有内容）可直接做，不引入新规则。
  - 版本影响：无。
  - 验证方法：人工通读 + `rg -n "parked" docs/README.md docs/features/bazaar-agent.md` 确认措辞已替换或已就地定义。

- **[P2-3] 注释与归因修正** ← 解决 F-008, F-044
  - 改动范围：`src/BazaarPlusPlus.ModApi/Http/ModApiJsonPost.cs:12`（复数→单数）；`docs/reference/settings-and-debug-surfaces.md:5`（定义源改 `BppComposition.cs:86-92`，catalog 职责改为收集/排序/物化）。
  - 影响面与风险：无。版本影响：无。
  - 验证方法：`./run.sh build`（注释改动不影响编译，跑一次防笔误）。

- **[P2-4] 空 catch 日志化批量治理（沿用 P0-2 定下的错误处理风格）** ← 解决 F-014, F-015, F-016, F-017, F-018, F-019
  - 改动范围：`src/BazaarPlusPlus/Patches/ShopForecast/ShopForecastLogPatch.cs:139-142`；`src/BazaarPlusPlus.ModApi/ModApiErrorFormatter.cs:48-51`；`GhostBattleSyncService.cs:115-118` + `RunBundleUploadService.cs:126-129`（或下沉到 `BppClientCacheBridge` 统一处理）；`src/BazaarPlusPlus.BazaarAgent/Transport/BazaarAgentHttpServer.cs`（:74/:79/:84/:102-105/:142/:150/:260-265 全部站点）；`BazaarAgentActionQueue.cs:50-54`；`BazaarAgentRuntimeController.cs:121-131`。
  - 影响面与风险：仅加日志/注释，不改控制流（降级行为保留）；BazaarAgent 程序集须保持零 BepInEx 依赖（用其自带 `IBazaarAgentLogger`，勿引 `BppLog`）。当前 `IBazaarAgentLogger.Warning` 只接收 `string`，只有 `Error` 接收异常参数；若要记录 warning 级异常，要么扩展 logger 接口并同步 host 实现/测试，要么把异常类型与 message 格式化进 warning 文本，不能直接写不存在的 `Warning(message, ex)` 调用。AcceptLoop 处顺手核对 `IsRunning` 标志在异常退出时是否应复位（如改动超出"加日志"，单独列 commit 说明）。
  - 版本影响：无。
  - 验证方法：`dotnet test tests/BazaarAgent.Tests/BazaarAgent.Tests.csproj`；`dotnet run --project tests/ModApi.Tests/ModApi.Tests.csproj`；`dotnet run --project tests/GhostBattleSync.Tests/GhostBattleSync.Tests.csproj`；全量 `./run.sh test` + grep `Failed test projects:`。

- **[P2-5] 低风险性能整洁 + 诊断补点** ← 解决 F-030, F-032
  - 改动范围：`src/BazaarPlusPlus/Patches/Lobby/RandomHeroPoolPatches.cs:122,129`（LINQ 改内联遍历）；`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs`（首窗口 TryBind/art 加载 debug 计时，Debug 级日志仅 Debug 构建发射）。
  - 影响面与风险：极低；随机英雄选择行为须等价（含日志字符串内容）。
  - 版本影响：无。
  - 验证方法：`dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj`；`dotnet run --project tests/RandomHeroPoolPatchCompatibility.Tests/RandomHeroPoolPatchCompatibility.Tests.csproj`；`dotnet run --project tests/CollectionGridLayout.Tests/CollectionGridLayout.Tests.csproj`；游戏内首开 CollectionPanel 看 Debug 日志出现 binding 计时段。

- **[P2-6] CONTEXT.md 术语补充（待人类拍板后执行）** ← 解决 F-043
  - 改动范围：`CONTEXT.md`（新增 Collection Source Catalog / Offer Pool / Merchant Kind 三词条）。
  - 影响面与风险：无；唯一前提是 Q1 拍板"是的，纳入术语表"。
  - 版本影响：无。
  - 验证方法：人工通读，确认定义与 `CollectionSourceCatalog.cs` / `CollectionMerchantKind.cs` 的代码事实一致。

### 建议执行顺序与依赖

1. **先行并可并行**：P0-1、P0-3、P1-1、P1-2、P2-1、P2-2、P2-3 互相独立，可立即并行开工（文档批次 P2-1/2/3 与任何代码批次零冲突）。
2. **P0-2 先定调，P2-4 跟随**：P0-2 决定 ModApi/错误传播的统一风格（Result vs out-error vs 日志委托）；P2-4 的 ModApi 部分（F-015）与 Ghost/RunBundle 部分（F-016）必须沿用同一风格，故 P2-4 排在 P0-2 之后；P2-4 的 BazaarAgent 部分不依赖 P0-2，可提前。
3. **Screenshots 域串行**：P1-4（异步化）是该域的结构性改动，P1-5（超时）与之同域——建议 P1-4 → P1-5 顺序执行（或同一分支内先后提交），避免上传链路在两套时序假设间反复。P0-1 中 `EndOfRunScreenshotController` 的判空加固改动小，建议在 P1-4 动工前先落地合入，减少冲突面。
4. **Settings 域一揽子**：P1-3 三文件同一驱动循环，一个分支完成，与其他批次无依赖。
5. **P1-6、P2-5 随时插入**：独立小项，可塞进任意空档。
6. **P2-6 等 Q1 拍板**。
7. 每个批次收尾跑：`./run.sh build` + 全量 `./run.sh test`（grep `Failed test projects:`）+ `csharpier format .`（注意 format 若波及批次外文件，不入本批 commit——仓库规则）。

方案到此为止，停下等人类确认，不进入实现。

---

## Open questions / to-verify

- **Q1（决策）F-043 / P2-6**：CONTEXT.md 是否扩展覆盖 CollectionSources 术语（Collection Source Catalog / Offer Pool / Merchant Kind）？验证法：人工检查这些概念在近期任务中的复现频率，若团队认为足够主流则补词条；否则记 won't-do。
- **Q2（决策）P0-2 版本影响**：`BazaarPlusPlus.ModApi.dll` 是否视为对外 API 表面？所有已知消费方都在本仓库且同捆分发——若仅进程内表面，签名 breaking 随 minor；若认定对外，按仓库规则 bump major。验证法：确认 installer / 第三方是否单独引用 ModApi.dll（检查 `bazaarplusplus-installer` 资源清单与发布包结构）。
- **Q3（需进游戏验证）P1-4 时序**：截图异步化后，(a) 端局自动 continue 是否仍等到截图落盘（或允许并行）；(b) `run_screenshots` DB 行是否只在 PNG 原子落盘成功后写入；(c) 若实现允许上传扫描早于落盘，`image_file_missing` 是否已被改为 transient retry 而非 permanent failure。验证法：游戏内连跑 ≥2 局，核对 `<GameRoot>/BazaarPlusPlusV4/` 截图文件与 DB 行一一对应、上传日志无永久性 file-not-found；崩溃路径用任务管理器中途杀进程验证无半截 PNG 被入库。
- **Q4（决策）F-007**：RandomHeroSkinPool 旧 PlayerPrefs key 迁移 shim 是否设移除期限（建议：下一次 major 删除旧 key 读取分支）？验证法：人类拍板；若设限，在该 major 的 checklist 中登记。
- **Q5（流程）F-034 复发预防**：SQLite schema 变更与 `docs/reference/sqlite-schema-reference.md` 的同步是否需要流程级约束（PR 模板项或规则条目）？本轮已观察到对账次日即复发漂移。验证法：人类拍板是否按 "Suggested rule additions" 流程提案；规则文件不做 drive-by 修改。
- **Q6（待量化）F-030 / P2-5**：首窗口 binding/art 计时补点落地后，需一次游戏内 profiling 判断 binding 是否为首开延迟主因，再决定是否立项优化 art cache / binding 策略。验证法：Debug 构建首开 CollectionPanel，比对 `bind` 段与 `catalog` 段耗时。
