---
status: implemented
archived: 2026-07-10
calibrated: 2026-07-10
superseded-by: code
---

> Status: IMPLEMENTED at HEAD 7a68e8bb; all 6 PRs shipped as Game/GameInterop/Patches/VoiceSubtitles + tests/VoiceSubtitles.Tests. One locked decision REVERSED: settings moved from a standalone BazaarLine.cfg into BppConfig [VoiceSubtitles] (1830fe0b), then merged into Subtitle Mode (91622637). Font kept BazaarLine's original (no LXGW); remote endpoint live.

# BazaarLine → mod: VoiceSubtitles 集成计划

状态: **FINAL — 交接执行版(已过 8-seam 调研 + 设计 + 两轮独立红队/review,全部 must-fix 已折回并按真实代码核验)**。日期 2026-07-03。
源: `/Users/yxinyu/codes/bpp/BazaarLine`(独立 BepInEx 插件 `BazaarVoiceLine`, ~2381 LOC,源在 `src/BazaarVoiceLine/`)。
**in-run 挂载锚点已由用户确认存在**(VO 场景里有可挂的版本 label)——保留 scanner 即可,不再是阻塞项。**远端拉取端点已上线并验证**:`voice-lines.json` 已发布到 R2 桶 `bazaarline-installer`、key `data/voice-lines.json`,公网 `https://bazaarline-installer.bazaarplusplus.com/data/voice-lines.json` 返回 200 + 黄金 hash(见「数据产物与交接」)。**无遗留阻塞项**;PR4 的远端逻辑可端到端验证。

## 目标

把 BazaarLine 的 VO 双语字幕功能折进主插件,作为 `Game/VoiceSubtitles` 特性,复用 mod 现有 seam,不再作为独立插件。

## 已锁定决策

| 决策 | 结果 |
|---|---|
| 总开关默认 | **OFF**(沿用 BPP 每个功能 opt-in 惯例, `BppConfig.cs:57-62` 模式) |
| 开关实时性 | **observer 常驻安装、mask 常改,只 gate 显示端**(近实时开关) |
| 设置持久化 | **独立 `BepInEx/config/BazaarLine.cfg`**(key=value,不进 BppConfig / 不并入 `BazaarPlusPlus.cfg`) |
| 数据格式 | **JSON 单文件**(替换 CSV,**不留 CSV 兼容**):顶层 `schemaVersion` + `contentHash` + `generatedAt` + `count` + `lines[]`;每行 `{stem, english, chinese, durationSeconds}`,`durationSeconds` **round 到 2 位小数**。版本内嵌,**不要 sidecar manifest**;复用 Newtonsoft,不移植 CSV codec |
| 数据版本 | **JSON 顶层 `contentHash`**(自维护 + 下载完整性校验;= 之前选的 content-hash,住进单文件不再独立 manifest) |
| 拉取源 | **`https://bazaarline-installer.bazaarplusplus.com/data/voice-lines.json`**(BazaarLine 自有域名,不用 `bpp-metrics`;与 analyzers 无关,绕开 `publish` prune) |
| 字体 | **保留 BazaarLine 原行为**(FontDiagnostics OS 字体 + combined/split 双路),**不用** mod 的 LXGW/BppTmpFont |
| 旧用户 cfg | 新文件走默认值;若旧 `plugins/BazaarLine/settings.cfg` 存在则首启 best-effort 拷一次 |

> 拉取源背景:BazaarLine installer 是**离线安装器**(DLL+CSV 打进 `resources/Payload`,`manager.rs` 只做本地拷贝,仓库内无下载 URL)。`bazaarline-installer.bazaarplusplus.com` 是**安装器下载站**(Cloudflare Pages 或 R2 自定义域,在 dashboard 配,不在仓库)。mod 端只是 HTTPS GET;上传/发布按该站现有部署方式(Pages deploy / R2 put)——由维护者决定,不进 analyzers pipeline。

## 数据格式(`voice-lines.json`)

```jsonc
{
  "schemaVersion": 1,                     // int。解析门:不等于支持版本 → 整文件 reject 回落 cache/embedded
  "contentHash": "sha256:0bb0ce57…",      // string。lines 规范化内容哈希:身份 + 完整性 + 变更检测
  "generatedAt": "2026-07-03T16:16:13Z",  // string, ISO-8601 UTC。生成时间(信息性,不入 hash)
  "count": 5032,                          // int。lines 预期条数,解析后 sanity check
  "lines": [
    {                                          // (真实种子第一 Jules 行,示例)
      "stem": "001_JulesPvPIntro5",            // string, 必填, 唯一。语音事件 id/文件名 stem,匹配主键
      "english": "Oh, I'd rather feed you than fight you.", // string, 可 ""(部分 Dooley 行 english=stem,原始数据如此)
      "chinese": "哦，挨顿打还不如吃顿饭呢。",       // string, 可 ""
      "durationSeconds": 3.02                  // number, 2 位小数, ≥0。显示 fallback 超时
    }
  ]
}
```

字段全用**清晰全名**(不缩写)——可读性优先,体积代价可忽略。**明确不做传输压缩**:`BppHttpClientFactory` 保持裸 `new HttpClient()`(`BppHttpClientFactory.cs:18`),不开 `AutomaticDecompression`。理由:文件极少拉(TTL 内一次)、明文 ~1.2MB 无所谓,不为边际收益加 handler 复杂度。

解析规则:结构/类型错 → Newtonsoft **抛** → null 回落(截断 JSON 直接整文件拒,不像旧 CSV loader 那样跳坏行、静默载入短表);`count != lines.Length` 或 `contentHash` 重算不符 → 整文件 reject(**这两个校验才是堵"schema 合法但内容被截"的关键,不是 JSON 格式本身**);单条缺 `stem`/重复/en&zh 皆空 → 跳过 + warn。预留(现阶段**不加**):每行可加 `character`/`category` 让匹配比猜 stem 更稳。

## 代码落位(三层,遵守 mod 分层规则)

### `Patches/VoiceSubtitles/`(Harmony,`Plugin.cs:207` 自动发现,不注册)
- `VOPlayerPatches.cs` ← BazaarLine 同名。`SoundManager.OnSystemsInitialized` postfix / `VOPlayer.PlayVO`(prefix/postfix + setCallback-mask **transpiler**)/ `PlayTutorialVO` / `OnVOStopInternal`。
  - **显示端** body 首行 `VoiceSubtitlesGate.IsEnabled()` 早退(抄 `NameOverridePatches.cs:26,37`)。
  - **observer 安装端(`OnSystemsInitialized`)不 gate** —— 常驻,才能中途开启生效。
  - `PlayVO` / `OnVOStopInternal` 加 `[HarmonyPrepare]` AccessTools 探针(`RandomHeroSkinPoolPatches.cs:44-70`)。
- `VersionShow.BuildVersionLabel` **不新建 patch 类** —— 折进现有 `MainMenuVersionLabelPatches.cs:12-33`(见 PR3)。

### `GameInterop/VoiceSubtitles/`(游戏 DLL / FMOD 耦合适配器)
- `VoiceLineVoObserverBridge.cs` ← `VoiceLineObserver.cs` 的 FMOD 半边。**seam 画在"FMOD 类型进、POCO 出"**:凡碰 FMOD 类型的都在这里 —— `VOPlayer.VODebugPrint` 挂钩、`Services.Get<SoundManager>().CardAudioHandler`、`CreateVoiceAttempt`(`EventReference`/`RuntimeManager.GetEventDescription`/`RESULT`)、**`OnVoSoundPlayed`(`new Sound(ptr)` + `getName/getLength`/`TIMEUNIT`,`VoiceLineObserver.cs:242-244,314-342`)**、`OnVoPlaybackStopped`、`VoiceAttemptContext` 追踪。产出纯 POCO(stem/文本/时长)交给 `Game/` 的 catalog+display。`Install(VOPlayer)/Reset()`。FMODUnity 已在 csproj 引用,无需新增引用/decompile。
  - **红队 CONFIRMED(原计划分层画错)**:`OnVoSoundPlayed` 是 FMOD 耦合的,**不能**放 `Game/`,否则 PR6 的架构测试会 fail 它自己放进去的代码。

### `Game/VoiceSubtitles/`(功能逻辑 + UI)
- `VoiceSubtitlesModule.cs`(**新** `IBppFeature`)—— `Start()`: reset 静态 + `repository.BeginLoad()`;`Stop()`: reset 静态。取代 BazaarLine 的 `Plugin.Awake/OnDestroy`。
- `VoiceLineDisplay.cs` ← 同名,**保留 combined/split 双路 + 字体逻辑不变**。
- `VoiceLineDisplayDispatcher.cs` / `VoiceLineOverlayLifetime.cs` ← 同名(MonoBehaviour,ComponentMount)。
- `VoiceLineCatalog.cs` ← 同名,`ResolveCsvPath()` 换成 repository(embedded 种子 + 远端缓存)。**catalog 后端存储要原子换引用**:BazaarLine 原来是 `static Lazy<VoiceLine[]>`(一次性安全发布),换成后台可变的 repository 后,必须像 `BuildRecommendationRepository` 那样在 `_syncRoot` 锁里整体换引用(FMOD 回调线程在 `foreach` 扫这个数组),**别把 Lazy 换一半**留下半构造态。
- `VoiceLine.cs` / `VoiceLineResolution.cs` / `VoiceAttemptContext.cs` ← 逐字搬(纯 DTO/enum)。Game/ 这半边只留 **catalog 解析 + `QueueShow`**(POCO 进,不碰 FMOD);FMOD 观测(`OnVoSoundPlayed`/`CreateVoiceAttempt`)在 GameInterop bridge。
- `VersionLabelScanner.cs` ← 逐字搬,**保留**(见 PR3;它是唯一的 in-run 换场景后重挂机制,不删)。
- `FontDiagnostics.cs` ← **逐字搬**(按决策保留原字体行为,零 mod 耦合)。⚠️ split 路用 legacy `UnityEngine.UI.Text` + `Font.CreateDynamicFontFromOSFont` + `FontStyle`/`HorizontalWrapMode`(`TextRenderingModule`);BazaarLine csproj 显式引了 `UnityEngine.TextRenderingModule`,mod csproj 没有 —— PR2 编译时若报错就加这个 `<Reference>`(并同步 `decompile-all`,见记忆 [[feedback_csproj_decompile_in_sync]])。
- `VoiceLinesRepository.cs`(**新**,克隆 `BuildRecommendationRepository.cs`)+ `VoiceLinesDocument.cs`(**新** JSON 根 DTO:`schemaVersion/contentHash/generatedAt/count/lines[]`;无独立 manifest)。
- `Settings/VoiceLineSettings.cs` ← 同名,改动见 PR5。
- `Settings/VoiceSubtitlesSettingsDockEntry.cs`(**新**,每设置一行)。

### 删除
BazaarLine `Plugin.cs`、`MyPluginInfo/BepInPlugin`、`src/BazaarVoiceLine/Patches/VersionLabelPatches.cs`(见 PR3:靠 scanner 挂载,不新建 postfix)、`BazaarVoiceLineLog.cs`(→ `BppLog`)、`BazaarVoiceLine.csproj`、整个 `installer/`。命名空间 `BazaarVoiceLine.*` → `BazaarPlusPlus.{Game,GameInterop,Patches}.VoiceSubtitles`。**注意**:BazaarLine 源在 `src/BazaarVoiceLine/`(计划各处引用补上这个前缀)。**`VersionLabelScanner.cs` 不删** —— 见 PR3。

## 分阶段 PR

**PR1 — 总开关 + dock 脚手架(无 VO 代码)**
`EnableVoiceSubtitlesConfig` 加到 `IBppConfig.cs:13` + `BppConfig.cs:15` + `Bind("VoiceSubtitles","Enabled",false,…)`(section 名此后不改)。`VoiceSubtitlesGate.IsEnabled()` 静态(读 `BppPatchHost.Services.Config.EnableVoiceSubtitlesConfig?.Value`)。`BppComposition.cs:102` **无条件**注册占位 `VoiceSubtitlesSettingsDockEntry`(先只放总开关 toggle,抄 `CombatStatusBarSettingsDockEntry.cs:8-21`)+ 新 `BppSettingsDockOrder.VoiceSubtitles` 常量。`./run.sh build` 验证 dock 出现开关。

**PR2 — 搬 VO 领域代码(patch 默认 gate 关,惰性)**
搬 DTO/Catalog/Display(**保留双路字体**)/Dispatcher/Overlaylifetime/**VersionLabelScanner**/Observer/FontDiagnostics;FMOD 半边进 `GameInterop/VoiceSubtitles`;Harmony 类进 `Patches/VoiceSubtitles`。`BazaarVoiceLineLog.*` → `BppLog`。`BppComposition.cs:94/121` **无条件**注册 `VoiceSubtitlesModule` + `ComponentMount<VoiceLineDisplayDispatcher>` + `ComponentMount<VersionLabelScanner>`(实时开关模型)。显示端 gate:观测在 GameInterop bridge(非 patch),所以 **gate 放在 bridge 的 resolve/QueueShow 入口 + 显示端 patch body 首行**,不是 dispatcher `Update()`;`OnSystemsInitialized`(observer 安装)**不 gate**。
**本 PR 自带数据读取(修红队 chicken-and-egg)**:`<EmbeddedResource Include="Data\VoiceSubtitles\voice-lines.json"/>` 声明、`VoiceLinesDocument` DTO、embedded 种子读取路径都放**本 PR**(PR2 要能自己跑,不能等 PR4);PR4 只加远端 HTTP 拉取 + 磁盘缓存 + TTL。**彻底删除 CSV 解析(`ParseCsvRow`/`ResolveCsvPath`/CSV codec),不留兼容,仅 JSON**,换 Newtonsoft DTO(`stem/english/chinese/durationSeconds` 语义不变,匹配逻辑一行不改)。Steam 起游戏验证开关打开时字幕显示。

**PR3 — 挂载锚点:保留 scanner + 避开 postfix 冲突**
**红队 CRITICAL 改写**:原计划"删 scanner + 折进 mod 主菜单 postfix"是错的。`VersionShow.BuildVersionLabel` 只在**主菜单** `VersionShow.Awake()` 调(`decompiled/.../VersionShow.cs:13-15,22`),且 `MountFromVersionLabel` 把字幕 parent 到 `versionLabel.transform.parent`(换场景即销毁,`VoiceLineDisplay.cs:48`)。**`VersionLabelScanner`(0.75s 轮询,`VersionLabelScanner.cs:14-26`)是唯一的换场景后 in-run 重挂机制** —— VO 恰恰在 in-run(hero/merchant/tutorial/choice)播。
- **用户已确认 VO 场景存在可挂锚点**,所以 scanner 方案成立:**保留 scanner**;**不新建** `BuildVersionLabel` postfix(靠 scanner 挂载即可,顺带彻底避开与 mod `MainMenuVersionLabelBuildPatch` 的同方法冲突)。
- 验收时仍顺带确认 VO 播放的每个场景(战斗/商人/教程/选牌)字幕都出现;万一某场景锚点缺失,兜底是加常驻 canvas / RunStarted·encounter-enter 重挂(非阻塞)。
- **字体独立**:字幕**用 FontDiagnostics 自解析字体**,不 clone 版本 label(mod 可能已把它换成 LXGW,clone 就跑偏了)。位置/parent 仍取自 scanner 找到的 label。

**PR4 — 远端自动更新:tenwin 骨架 + 新增完整性/刷新层(不是照抄!)**
**红队 HIGH 改写**:tenwin `BuildRecommendationRepository` **没有** contentHash、**没有** count 校验、**没有**周期 timer(它只在缓存 stale/缺失时每会话拉一次)。PR4 = 复用 tenwin 的 HTTP/缓存/兜底/后台**骨架** + **自己新写**三样,要显式写清、别当"免费继承":
- HTTP: `BppHttpClientFactory.Create(...)` 静态字段(真实锚点 `BuildRecommendationRepository.cs:37-41`)。**不走** ModApi `ModOnlineClient`(鉴权后端,非 CDN GET)。
- URL: `https://bazaarline-installer.bazaarplusplus.com/data/voice-lines.json`(单文件)。
- 缓存: `<GameRoot>/BazaarPlusPlusV4/voice-lines.json`。**绝不用** `Application.dataPath` / `Assembly.Location`。
- 后台: `BeginLoad() → Task.Run`,**必须复刻 `316e6b12` 硬化**:never-awaited task 包 try/catch,失败清 `_warmUpTask`(`:243-255,462-465`),否则 load guard 永久短路、整局 brick。后台只填内存 catalog(无 Unity 对象),TMP 构造留主线程。
- **解析 key 对齐(红队 CONFIRMED 陷阱)**:tenwin gate 读 **snake_case** `root["schema_version"]`(`TenWinBuildCorpus.cs:114`),而我们的 JSON 是 **camelCase** `schemaVersion` —— 照抄 parser 会 reject 每个 payload、永久回落种子。**克隆时把 key 改成 `schemaVersion`**(或统一改 snake)。schema 不匹配 → `return null` 回落。
- **contentHash 规范化(已定死并经独立复算验证,发布脚本与客户端必须逐字节一致;任一处偏差 = 全客户端永久回落种子)**:对 **`lines`** 算,不含 header 字段(避开自引用/`generatedAt` 抖动),整数厘秒避 float 格式歧义:
  - **厘秒取整必须 `Math.Round`(默认 banker's / ToEven),严禁截断**。⚠️ 最高风险:`(long)(durationSeconds*100)` 截断会让 315/5032 行不同、hash 全错。C# `Math.Round(x)` 默认 ToEven == Python `round()`,二者一致;**不要**传 `MidpointRounding.AwayFromZero`。`centis = (long)Math.Round(durationSeconds * 100.0)`。客户端从**已存的** `durationSeconds`(本身 = centis/100)算,故 `round(durationSeconds*100)` 恒精确复现。
  - `record = stem + "\x1f" + english + "\x1f" + chinese + "\x1f" + centis`(`\x1f`=US 字段分隔;空字段就是分隔符之间的空串,如 english 空 → `stem\x1f\x1fchinese\x1f…`)。`centis` 用 invariant-culture 十进制、无分隔符/无符号填充。
  - `canonical = records.join("\x1e")`(`\x1e`=RS),按**文件原序**;**纯 join,无尾随 `\x1e`,无末尾换行**。
  - 字节:**UTF-8、无 BOM、不做 Unicode 归一化**(NFC/NFD 都不加,按文件/解析所得原字符串算)。
  - `contentHash = "sha256:" + lowercaseHex(sha256(utf8(canonical)))`;客户端复算比对,不符 → 拒收回落。
  - **黄金值**(当前 5032 行,写成 C# 单测断言):`sha256:0bb0ce57361dfbcefc64173370891bacf9b7aa85b5e3fc225422f4b8a299aec0`。已用"仅照本 prose 实现"独立复现该值。
  - **重生成种子**必须用**同一脚本**(文末附录;它对 CSV 原始值做 Python `round(raw*100)` = banker's;换一种 3dp→2dp 取整会让 22 个 x.xx5 边界行变化 → 不同 hash)。客户端不受影响(从已存 2dp 值算)。
- **count 校验(新写)**:`count != lines.Length` → 拒收(堵"schema 合法但内容被截"的静默短表)。
- **刷新节奏**:**用 (A) —— 和 tenwin 一样"每会话 stale 才拉一次"**(默认实现,别再纠结)。(B)"last-refresh 时间戳 + 最小间隔 gate"是备选,除非另有要求否则**不实现**。**都不要**在 dispatcher 每帧 `Update()` 里直拉(会重入 `BeginLoad`)。不加 ETag/条件请求/压缩,保持最简。
- 发布: 由 `bazaarline-installer.bazaarplusplus.com` 现有部署方式发(Pages/R2,维护者定)。**不进** analyzers pipeline。一次性转换脚本:authored 源 → embedded 种子 + `voice-lines.json`(`durationSeconds` round 2 位、算 `contentHash`,与客户端同一套规范)。

**PR5 — 4 个设置进 dock,持久化到 `BazaarLine.cfg`**
`VoiceLineSettings.SettingsPath` 从 `Assembly.Location`(`:70-72`)改成 `Path.Combine(BepInEx.Paths.ConfigPath, "BazaarLine.cfg")`(`Plugin.cs:146` 惯例)。
- **红队 CONFIRMED(cfg 现在根本没有 writer)**:`VoiceLineSettings` 四个属性全是 `{get; private set;}`、只由 `Load()` 构造新实例赋值(`:97-103`),**没有任何 Save/序列化路径**。且 `Current` getter 每读都 `ReloadIfChanged()`(`:39-64`)。所以 PR5 要**新写一整套序列化 + 可写状态**,不是"改个 getter"。**用 (A)(决策已定,别再纠结)**:
  - **(A)纯 load-once + `Save()`**:`Save()` 在 `lock(Sync)` 里 rebuild 新 `VoiceLineSettings` 并写盘,**删掉全部 mtime/reload 机制**(不支持外部手改,手改需重启)。最简、无竞态、全指定。
  - (B)"保留外部手改 + debounced reload + `File.SetLastWriteTimeUtc`" 是备选,**除非另有要求否则不实现**(它与现有每读 reload 的交互未细化)。
- **写穿要求(红队 CONFIRMED)**:dock `ActivateDefinition` 先 `Activate()` 再同步 `RefreshAll()`→`ApplyRowState` 立刻重读 `IsActive/ResolveStatus`(`BppSettingsDockController.cs:315-341`)。所以 `Save()` **必须先改内存 `_current` 再 flush**,否则点一下状态标签当帧不更新。验收:点 position/font/language 行,状态标签**同帧**变。
- dock 行(`Build()` 忽略 `IBppConfig`,闭包到 `VoiceLineSettings`):
  - position(3 段):抄 `PreviewVisibilityModeDockEntry.cs:44-49`。
  - language(3 段):抄 `ChineseLocaleModeSettingsDockEntry.cs:52-59`。
  - englishFontScale / chineseFontScale(1.0..2.5):**dock 无 slider** → 离散档位循环,ladder = `{1.0,1.25,1.5,1.75,2.0,2.25,2.5}`,`ResolveStatus` 显 "1.5x"。**off-grid 默认值处理**:BazaarLine 默认 en=1.0(在 grid 上)、**zh=1.1(不在 grid 上)**;若持久化值不在 ladder,原样显示(如 "1.1x"),点一下跳到**大于它的最小 ladder 档**(1.1→1.25)——不改默认值、不静默 snap。**保留双路字体故两个独立字号真正可用**(单 TMP label 只有一个 fontSize —— 这是保留 split 路的价值)。
- 标签/状态走 `L.Resolve(LocalizedTextSet(en,zh[,zhHant]))`。所有行在 `Catalog.Install` 前注册。旧 cfg best-effort 迁移放这。

**PR6 — 清理 + 架构测试**
确认删除项已不在合并树;`./run.sh format`(csharpier 波及无关文件另开 commit);加/扩架构测试断言 `Game/VoiceSubtitles` 无直接 FMOD/VOPlayer 引用(耦合须在 `GameInterop/VoiceSubtitles`)。Steam(App 1617400)终验:普通 VO **和教程 VO** 都测(见风险)。

## 必解硬风险(独立 review 已核验,CONFIRMED 除非标注)

**实现前必须解决(must-fix):**
1. ✅ **in-run 挂载锚点(原 CRITICAL,已解)**:见 PR3。用户已确认 VO 场景有可挂锚点 → **保留 scanner、不删**。仅验收时顺带确认各 VO 场景字幕都出现。
2. **HIGH — transpiler:把无条件覆盖改成带守卫的 matcher**:BazaarLine 原码(`VOPlayerPatches.cs:60-66`)在每个 setCallback 前**无条件覆盖** `codes[i-1]`(今天能用)。**本计划要求改成带守卫版**(硬化,非逐字照搬):仅当 `codes[i-1].LoadsConstant(0x20)` 才改,否则原样返回 + 一次性 Warn。原因:`EVENT_CALLBACK_TYPE.STOPPED = 0x20 = 32`(`EVENT_CALLBACK_TYPE.cs:13`)编译成 **`ldc.i4.s`(短式)**,所以守卫必须用 `LoadsConstant(0x20)`(Harmony 归一化 Ldc_I4/Ldc_I4_S),**严禁** `opcode==OpCodes.Ldc_I4`(永不命中→`patched==0`→整条路静默死)。写侧 mask `0x2020=8224` 超 sbyte 才用 `Ldc_I4`,是对的(`:65`)。仍要**跨 online/PTR 断言 `patched==1`**。
3. **HIGH — PR4 不是照抄 tenwin**:见 PR4。contentHash 规范化必须精确定死(否则永久回落种子);count 校验、刷新节奏都是新写。
4. **HIGH — FMOD 分层画错**:`OnVoSoundPlayed` 是 FMOD 耦合,归 GameInterop;见「代码落位」。
5. **HIGH — cfg 无 writer**:`VoiceLineSettings` 只读,PR5 要新写序列化 + 二选一持久化模型;见 PR5。
6. **MEDIUM — PR2/PR4 先有鸡**:EmbeddedResource + DTO + 种子读取移到 PR2;见 PR2。
7. **MEDIUM — schemaVersion key 大小写**:camelCase vs tenwin snake_case,照抄 parser 会全 reject;见 PR4。
8. **MEDIUM — catalog 原子换引用**:别把 `Lazy<>` 换一半;见「代码落位」。

**已知降级 / 记入验收清单(不阻塞,但要 in-game 验):**
9. **transpiler 与 OnVOStopInternal-prefix 原子对**:游戏 `OnVOStopInternal` 只处理 Dialogue 的 STOPPED(`VOPlayer.cs:208-210`);两 patch 任一 miss → 静默死。任一失败则**整条 VO 观测禁用**(feature-health flag,两 patch 都 set),`OnVOStopInternal` 也加 `[HarmonyPrepare]`。
10. **教程 VO 比"best-effort"更糟**:`PlayTutorialVO`(`VOPlayer.cs:354`)未 transpile → 无 SOUND_PLAYED;且其 STOPPED 走**另一个未被 patch 的** `OnTutorialVOStop`(`VOPlayer.cs:303`)→ 教程字幕既无 SOUND_PLAYED 也无 mod 观测的 STOPPED,只靠 lifetime 轮询/超时隐藏。列入验收。
11. **开关 OFF 不撤下当前字幕**:`IBppFeature.Stop()` 只在插件 teardown 触发;运行时关开关不调 `Reset()`,已显示的字幕会 linger 到 fallback 超时(几秒自愈)。要么接受并记文档,要么让 gate 订阅 config 变化调 `VoiceLineDisplay.Reset()`(**用 raw-value 比较**,`PublicizeAll` 让 `SettingChanged` 歧义,见 [[project_publicizer_settingchanged_ambiguity]])。设置改动同理只作用于**下一条**字幕,除非 activate 时重跑 `TryApplySettingsToLabel`。
12. **并发(实为安全,但要写清不变量)**:`_pendingContext/_activeContext` 被 FMOD 回调线程写、主线程读;安全是因为 `VoiceAttemptContext` 是 **sealed 不可变 class**(`VoiceAttemptContext.cs:5`)→ 写是原子引用交换。**别把它改成 struct**,否则变真 torn-read。
13. **坏数据回滚**:schema 合法但内容错的 JSON 会被全客户端缓存(TTL 内)。应急=改内容重算 `contentHash` 重发;记一条应急流程。
14. **TextRenderingModule 引用**(PLAUSIBLE):见「代码落位」FontDiagnostics 注。PR2 编译时确认。

**引用勘误(实现前修,均已核实真实位置)**:BazaarLine 源在 `src/BazaarVoiceLine/`;mod 版本 label patch 类名是 `MainMenuVersionLabelBuildPatch`(文件是 `MainMenuVersionLabelPatches.cs`);`RefreshCurrent` 在 `MainMenuVersionLabelUpdater.cs:28`;`BppConfig.cs:15`/`IBppConfig.cs:13` 是现有 `EnableCombatStatusBarConfig` 行(可作锚,非空行);tenwin 静态 HttpClient 字段在 `BuildRecommendationRepository.cs:37-41`。

## 复用清单(别手搓)

注册/生命周期(`BppComposition`/`ComponentMount`/`BppFeatureRegistry`)、单例 Harmony applier(`Plugin.cs:193-236`)、`BppPatchHost.Services`、config bind + `ISettingsDockEntry`、`BppHttpClientFactory` + tenwin 缓存/兜底/后台状态机、`IPathProvider`/GameRoot 锚、`EmbeddedResource` 加载、**Newtonsoft JSON codec**(换掉 CSV 后无需移植 parser)。**真正新写**:CSV→JSON 转换/发布脚本(已完成,见下)、VO 观测状态机的搬运/分层、4 个设置的 dock 行 + `Save()`、contentHash 客户端复算、catalog 原子换引用。

## 数据产物与交接

- **转换/发布脚本**:见文末**附录**(全文,已入库)。源 = BazaarLine `src/BazaarVoiceLine/Data/voice-lines.csv`。已实现 schemaVersion 1 + 上面定死的 contentHash 规范化;可重跑,contentHash 稳定(不含 `generatedAt`)。本机另有一份 `tools/voice-subtitles/build_voice_lines.py`(`tools/` 被 `.gitignore` 忽略,故正本在附录)。
- **已生成种子**:`src/BazaarPlusPlus/Data/VoiceSubtitles/voice-lines.json`(5032 行,0 丢弃,~954KB,`sha256:0bb0ce57…`)。PR2 直接把它标 `<EmbeddedResource>` 即可。
- **✅ 远端端点已上线并验证**(2026-07-03):`voice-lines.json` 已发布到 R2 桶 `bazaarline-installer` / key `data/voice-lines.json`,公网 `https://bazaarline-installer.bazaarplusplus.com/data/voice-lines.json` 返回 `200` + `content-type: application/json; charset=utf-8` + `cache-control: public, max-age=300, stale-while-revalidate=3600`,body `count=5032`、`contentHash` 匹配黄金值。mod 端靠 embedded 种子亦可离线跑通(远端是刷新,非硬依赖)。
  - **重新发布**:改数据后跑附录脚本重生成 → 再传同一 bucket/key。**注意**本机 `wrangler` 的 OAuth token **无 R2 scope**(只有 workers/kv/d1),`wrangler r2 object put` 会失败;用 R2 S3 凭据上传(`bazaarplusplus-analyzers/.env` 里的 `BPP_CF_ACCOUNT_ID`/`BPP_R2_ACCESS_KEY_ID`/`BPP_R2_SECRET_ACCESS_KEY`,S3 endpoint `https://<account>.r2.cloudflarestorage.com`,boto3 `put_object`,content-type/cache-control 同上),或给 wrangler 补 R2 scope。

## 交接给实现 Agent 的执行顺序

按 PR1→PR6 顺序做,每步 `./run.sh build` + 必要时 Steam 起游戏验证(App 1617400)。开工前先读:本计划「必解硬风险」全部 must-fix(#2 transpiler opcode、#3 PR4-非照抄、#4 FMOD 分层、#5 cfg 无 writer、#6 PR2/PR4 顺序、#7 schemaVersion key、#8 catalog 原子换引用)+ 项目记忆 `project_bazaarline_voicesubtitles_integration`。完成后交回本会话 review。

## 附录:voice-lines 转换/发布脚本(正本)

`python3 build_voice_lines.py <bazaarline voice-lines.csv> <out voice-lines.json>`。contentHash 规范化与上文 PR4 一致,已用"仅照 prose"独立复现黄金值。

```python
#!/usr/bin/env python3
"""Convert BazaarLine voice-lines.csv -> voice-lines.json (schemaVersion 1).

contentHash canonicalization (client MUST reproduce exactly):
  centis(line) = round(durationSeconds * 100)   # int, avoids float-format ambiguity; Python round()==banker's==C# Math.Round default
  record(line) = stem + US + english + US + chinese + US + str(centis)   # US = \x1f
  canonical    = RS.join(record(line) for line in file order)            # RS = \x1e  (no trailing sep, no final newline)
  contentHash  = "sha256:" + lowercase_hex( sha256( canonical.encode("utf-8") ) )   # UTF-8, no BOM, no NFC/NFD normalization
durationSeconds in the JSON = centis / 100 (single source of truth). generatedAt is NOT hashed.
"""
import csv, hashlib, json, sys
from datetime import datetime, timezone

US, RS = "\x1f", "\x1e"

def build(src_csv: str, out_json: str) -> None:
    lines, skipped = [], 0
    with open(src_csv, "r", encoding="utf-8-sig", newline="") as f:
        for i, row in enumerate(csv.reader(f)):
            if i == 0 and row and row[0].strip().lower() == "stem":
                continue
            if not row or all(c.strip() == "" for c in row):
                continue
            if len(row) != 4:
                sys.stderr.write(f"skip row {i+1}: expected 4 fields, got {len(row)}\n"); skipped += 1; continue
            stem, english, chinese, dur_raw = row
            try:
                centis = int(round(float(dur_raw) * 100))
            except ValueError:
                sys.stderr.write(f"skip row {i+1}: bad duration {dur_raw!r}\n"); skipped += 1; continue
            if centis < 0: centis = 0
            lines.append({"stem": stem, "english": english, "chinese": chinese,
                          "durationSeconds": centis / 100.0, "_centis": centis})
    canonical = RS.join(US.join((l["stem"], l["english"], l["chinese"], str(l["_centis"]))) for l in lines)
    content_hash = "sha256:" + hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    for l in lines: del l["_centis"]
    doc = {"schemaVersion": 1, "contentHash": content_hash,
           "generatedAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
           "count": len(lines), "lines": lines}
    with open(out_json, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=0, separators=(",", ":")); f.write("\n")
    print(f"rows={len(lines)} skipped={skipped} contentHash={content_hash}")

if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2])
```
