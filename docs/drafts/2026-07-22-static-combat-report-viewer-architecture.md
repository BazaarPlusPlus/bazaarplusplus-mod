# 战斗报告：静态 HTML、共享 Viewer 与全局 Asset Cache

- 状态：主链实现完成；Chrome、Safari 的 `file://`/MP4 seek 门禁已通过，Edge 因本机未安装仍待发行验证
- 日期：2026-07-22
- 当前范围：immutable report HTML、版本化共享 Viewer、全局内容寻址 Asset Cache、稳定相对资源路径、F8 默认浏览器入口
- 明确不包含：Tauri Viewer、Unity 原生 Viewer、本地 HTTP 服务、BazaarAgent 依赖、便携导出、录像编码/码率/分辨率/裁切参数调整

## 1. 结论

默认链路只有一条：

> **小型静态战斗 HTML（内嵌 JSON）+ 版本化共享 Viewer + 全局内容寻址 Asset Cache + 现有 MP4**

F8 的“查看战斗详细记录”直接用默认浏览器打开物理 HTML。页面在游戏、Tauri App、BazaarAgent 均未运行时仍可加载。不开端口，不保留 HTTP/file 双路径。

## 2. 已验证前提

- 正式 BazaarPlusPlus.app 4.4.3 的 `127.0.0.1:17654` 是 Tauri/Axum overlay，只读 SQLite/截图；不是 BazaarAgent，也不是报告服务。
- 正式 4.4.3.prod installer 与当前游戏目录均只有四个基础 DLL，没有 BazaarAgent/BazaarAgentHost，不能把 47900 当成默认能力。
- 当前录像事实源仍是 recorder terminal/SQLite 的 typed recording row；报告不能靠 glob 或文件名前缀猜录像身份。

## 3. 磁盘布局

概念布局：

```text
<GameRoot>/BazaarPlusPlusV4/
  reports/
    <recording-id>.html

  report-viewer/
    v1/
      viewer.js
      viewer.css

  report-assets/
    objects/
      ab/<content-sha256>.png
    keys/
      12/<render-key-sha256>.json
    staging/

  CombatReplayVideos/<date>/
    <recorder-owned-name>.mp4
```

用户给出的 `<battle-id>.html` 是产品示意；实际文件用稳定 `recording-id`，因为同一 battle 可重新录制多次，不能覆盖旧报告。F8/历史记录通过 typed recording row 映射到对应 HTML。

录像继续使用现有 `CombatReplayVideos` 路径和 recorder 生成的文件名，不复制、不重编码、不改录制参数。HTML 内保存从报告目录到该已验证 MP4 的 normalized relative URL。

## 4. 每场 HTML 合同

HTML 只包含：

- 基础文档壳、locale/title；
- 指向固定 Viewer 版本的相对 `<link>` 与经典 `<script src>`；
- 一个 `<script id="bpp-report-data" type="application/json">`，内嵌 immutable report envelope；
- 可选的 report JSON digest/version meta。

禁止：

- `fetch()` 本地 JSON、XHR、ES Module、dynamic import、Worker、service worker；
- 每场复制 JS/CSS/ECharts/图片；
- absolute file path、`file://` URL、Tauri/localhost URL 写入 report JSON；
- inline executable script。

JSON 由 `StringEscapeHandling.EscapeHtml` 等价规则编码，至少保证 `<`、`>`、`&`、U+2028/U+2029 不会结束 JSON script；所有 UI 文本继续用 DOM text API。

## 5. 共享 Viewer

- Viewer 以 `report-viewer/v<version>` 安装一次；HTML 固定引用生成时版本。
- `viewer.js` 是一个传统 IIFE 单文件。若继续使用 ECharts，它在 build/安装阶段并入该 bundle；运行时不再加载第二个 JS。
- Viewer 由 release registry 定义：`version -> schema compatibility + JS/CSS length + SHA-256 + embedded bytes`。资源 bytes 变化但 version 未提升时 build/test 必须失败；支持期内的旧版本继续随包分发。
- 安装使用 temp + flush + no-overwrite commit。目标已存在时逐字节/hash 验证，相同即复用，不同则要求提升 Viewer version 并 fail closed。
- 旧 Viewer 版本不自动删除；升级不能原地改写 v1。
- Viewer 从 `#bpp-report-data` 读取 JSON，所有 asset/video URL 都相对当前 HTML 解析。

## 6. 全局 `IReportAssetCache`

### 6.1 RenderKey

```text
ReportAssetRenderKeyV1
  schemaVersion
  gameBuild
  rendererVersion
  assetType
  templateId
  locale
  gameDataIdentity
  resolvedSkinIdentity
  size
  tier
  enchantment
  variant
  attributes[]  # ordinal sorted
```

`variant` 必须覆盖 socket、固定输出 canvas、capture profile、color space、encoder 与所有影响像素但没有独立字段的 renderer 输入。`gameDataIdentity` 使用 GameData ETag/DB SHA 等内容身份，而不是只靠 `Application.version`；稳定 template ID 不得替换为 instance ID、展示名或 ArtKey。

`renderKeyDigest = SHA-256(canonical RenderKey bytes)`；最终对象身份为实际 PNG bytes 的 `contentSha256`。

### 6.2 强制顺序

1. 整场 visual descriptor 先按 RenderKey 去重。
2. 每个 key 先 `TryResolve`，验证 mapping、object、hash、MIME 与 geometry。合法 hit 时连 materializer factory 都不得构造。
3. 只有 miss/corrupt key 进入 Unity materializer；命中直接返回 ContentKey。
4. materializer 写唯一 staging file，cache 先提交 immutable object，再提交 RenderKey mapping。
5. report HTML 只记录 entity → ContentKey/relative URL，不复制对象。

硬验收：相同 game build、renderer version、locale、capture profile 与阵容连续生成第二份报告时，Unity materializer 调用数为 **0**。

### 6.3 并发与清理

- object/mapping 使用 `CreateNew`/no-overwrite；同 key 同 content 幂等，不同 content 记录 nondeterminism 并拒绝新映射。
- object-before-mapping；崩溃可留下安全 orphan，不能留下指向不存在 object 的已提交 mapping。
- corrupt mapping/object 先在 exclusive repair lock 下移入 quarantine，再二次 lookup/materialize/提交；不能让损坏文件永久占据 no-overwrite 目标。quarantine 操作本身不允许跟随 symlink。
- 进程内按 renderKeyDigest single-flight，并在取得 claim 后二次 lookup。
- v1 明确永久 append-only，不实现 GC，也不做盲目 LRU。未来若另立 GC，任一 committed HTML 无法严格解析时整轮 abort，并需 publisher/GC epoch lock、两次成功 mark 与至少 7 天 grace；不属于本次 scope。

## 7. Report publisher

```text
CombatReportEnvelopeV1
  locale
  battleDocument
  recordingManifest
    recordingId / battleId
    viewerVersion
    videoRelativeUrl
    syncMetadataStatus / anchors
    assets[] { entityIds, contentSha256, relativeUrl, geometry, semanticRole }
```

- publisher 以 recording ID 将 battle document、asset terminal 与 video terminal 幂等 join。
- 当前录制链没有可证明的 frame→final media PTS 映射时，明确写 `ReadyUnsynced`，不伪造 exact anchors。
- 先确保 Viewer version 已安装、ContentKey 均已提交、video identity/physical path 合法，再生成完整 HTML bytes。
- HTML 在 `reports/` 同目录 staging，flush 后 no-overwrite atomic move；同 recording ID 已存在时只允许完全相同 bytes。该路径必须使用独立 `ImmutableArtifactCommitter`，禁止复用会 `File.Replace` 的 `AtomicFileWriter`。
- HTML commit 是 report-ready 唯一标志；不再生成 `.report/` generation、recovery manifest 或 per-report resource manifest。

## 8. 相对路径与物理边界

- 仅 producer 在可信 roots 之间计算相对 URL；report JSON 不接受外部 path。Viewer 不开放通用 `../`，只接受两种 typed sibling URL：`../report-assets/objects/<2hex>/<64hex>.png` 与 `../CombatReplayVideos/<escaped-segments>.mp4`。
- URL separator 始终为 `/`，每段 URI escape；Windows drive/root 不一致时 fail closed。
- typed URL parser 对 raw 与一次 decode 后的 `..`、encoded slash/backslash、scheme、query、fragment、错误 sibling root 全部拒绝；覆盖 `%2e%2e`、`%252e%252e`、`%2f`、`%5c`、空格与 CJK。
- 目标 HTML、Viewer、asset、video 必须处于 `<GameRoot>/BazaarPlusPlusV4/` 的允许 roots，拒绝 symlink/reparse/traversal。
- asset SHA 为 64 位 lowercase hex；recording ID 与 Viewer version 严格解析。
- 图片缺失可显示 placeholder；视频缺失可继续看时间轴，但 F8 candidate 本身必须是已原子提交的有效 HTML。

## 9. 入口与 UI

- F8/历史记录按 newest-first completed recording rows 找对应 `reports/<recording-id>.html`，不用 glob。
- 入口稳定 test ID 继续为 `bpp-history-detailed-report`。
- 点击调用 scoped `SystemReportOpener(reportRoot, typedRecordingId)`，由它自行构造 `reports/<recording-id>.html`；不接受 caller 提供任意 path。只允许 root 下非 symlink/reparse 的物理普通文件，再交给系统默认浏览器。
- 当前回放完成态可显示同一入口；“重新录制”和 reveal MP4 保持独立。
- 页面保持时间轴事件、同帧 inspector、metrics/stats/i18n/纵横 zoom；资源交付架构不改变这些交互要求。

## 10. 替换范围

删除旧默认链：

- per-report `.report/` bundle、Viewer/ECharts/图片复制或 hardlink；
- generation marker、recovery manifest、bundle-relative resource base；
- loopback Host、route、token、Range、AgentHost/Tauri 依赖设计；
- file/server fallback 和两套 publication chain。

复用并重构：

- `CombatReportProjector`、report DTO、join coordinator；
- embedded Viewer/CSS/ECharts bytes（安装为共享 versioned Viewer，而不是逐场复制）；
- `SystemReportOpener` 的跨平台默认浏览器逻辑；
- F8/current entry UI/test ID、录像完成竞态修复；
- 回放/录像启动前的原生资源 exporter：技能/头像/状态直接复制 `Texture/Sprite`，物品用游戏 Collection/Inspect 的 `ItemVisualsController` 普通 Renderer，经不连接 display 的 Camera -> RenderTexture 离屏烘焙；不得创建 uGUI CardPreview、可见 Overlay、读主 backbuffer 或裁棋盘画面，历史恢复只消费 cache。

可选 BazaarAgent 自己的独立 47900 server 不属于报告默认链，也不在本次删除范围；门禁只禁止 main/report 生产路径引入 listener、Tauri 或 Agent 依赖。

## 11. 实施计划

### Phase 0 — 设计与浏览器 gate

- [x] 用户确认静态 file-only 方向与默认磁盘布局。
- [x] ADR-0009 明确不依赖 Tauri/BazaarAgent/loopback server。
- [x] 独立 red-team 审查完成；新增 typed sibling URL、独立 immutable committer、完整 render environment、repair quarantine、Viewer release registry 与 typed opener 约束。
- [x] 用真实 sibling 目录在 Chrome、Safari 验证 classic JS、CSS、img、`<video>` load/seek；失败则先修静态布局，不回退 server。
- [ ] 在安装了 Edge 的发行环境补跑同一份真实 `file://` 报告门禁；本机没有 Edge，不得把 Chromium 结果冒充 Edge 结果。

### Phase A — shared Viewer installer

- [x] 把 ECharts + Viewer 产成一个 classic IIFE `viewer.js`，消除 fetch/module/worker。
- [x] 实现 versioned no-overwrite installer 与 content verification；v1/v2/v3/v4 保留，当前报告固定引用 v5（v5 补齐泳道实体类型的本地化）。
- [x] HTML emitter 仅生成壳 + escaped embedded JSON + stable relative URLs。
- [x] 实现 typed sibling URL parser； hostile envelope 覆盖 `</script>`、attribute payload、scheme、双重编码 traversal 与 duplicate data node。

### Phase B — global Asset Cache

- [x] 实现 canonical RenderKey、immutable object/mapping 与 miss-only materialization。
- [x] 相同阵容第二次 materializer invocation = 0；字段变化、属性排序、损坏/并发/crash 有测试与实机证据。

### Phase C — immutable report publication

- [x] coordinator 改为单 HTML commit，不再生成 bundle/recovery manifest。
- [x] typed recording→report lookup、strict roots/symlink checks、relative video URL。
- [x] 删除旧 per-report copy/build chain与对应测试，替换为静态 artifact tests。

### Phase D — F8/current entry 与 Viewer 回归

- [x] F8/current entry 直接打开 HTML，保持 stable test ID。
- [x] timeline/frame inspector/metrics/stats/i18n/纵横 zoom 保持可用。
- [x] Canvas 时间轴在 6,463 条真实原始记录样本上达到 60fps，838×857/480×857 视觉回归通过。
- [x] Chrome、Safari 的真实 `file://` sibling resource 与 MP4 seek 人工发布门禁。
- [ ] Edge 的同项人工发布门禁（当前开发机未安装）。

## 12. 量化验收

| 范围 | 断言 |
|---|---|
| 每场产物 | 仅新增一个小型 HTML、原 MP4 与真实 cache misses；无逐场 JS/CSS/ECharts/命中图片副本 |
| 网络 | 默认路径不创建 listener，不引用 Tauri/BazaarAgent，不发 fetch/XHR |
| Viewer | 报告固定相对引用一个 immutable version；classic IIFE 单 JS；旧报告升级后仍可打开 |
| cache | 相同阵容第二份报告 Unity materializer 调用数 `0`；对象 SHA 等于实际 PNG bytes |
| HTML | JSON script 无 `</script>` breakout；无 inline executable JS；atomic no-overwrite commit |
| path | 报告不含 absolute path/`file://`；仅两种 typed sibling URL；所有目标位于允许 roots且非 symlink/reparse |
| browser | Chrome/Safari/Edge 从 `file://` 加载 sibling JS/CSS/img/video；MP4 可随机 seek |
| 入口 | F8 与 current entry 打开对应物理 HTML；单独移动 HTML 失效属于预期 |
| 时间轴 | 全部事件可达、位置可还原、聚合可逆；同帧多事件可进入完整列表 |
| 性能 | 10k raw events 无逐 raw-event DOM，交互目标 60fps |

## 13. 待实机验证，不得写成已验证事实

- Edge 对 sibling-directory file URL、CSP 与 H.264/AAC seek 的具体行为。
- Windows 浏览器占用 MP4 时的重录/清理行为。

## 14. 已完成的实机证据

- 空 cache 的真实回放在录像启动前物化 native-final 资源；专用 Camera 始终 disabled，只通过 URP `SingleCameraRequest` 向私有 RT 提交，连续游戏画面抽样未出现导出 UI。
- 相同阵容第二次报告：`cache_hit_count=34`、`cache_miss_count=0`、`unity_materializer_invocation_count=0`，cache object count 不变。
- 最新 Viewer v5 报告包含 39 个实体、42 个共享素材引用；全部对象存在且 content hash 匹配。HTML 仅相对引用 v5 JS/CSS 与同目录树下 MP4，无 fetch/module/Worker/http。
- 录像 metadata 有 883 个严格单调的 exact anchors；H.264 文件为 3,600×2,040、60fps，时长 14.7167s、大小 39,130,599 bytes。
- F8 已用默认浏览器打开真实 `file://` v5 报告；Arc/Chromium 成功加载 sibling JS/CSS/img/video，点击 3.15s / Frame 63 聚合事件后播放器定位到 0:03。
- 同一物理报告在正式 Google Chrome 中成功加载共享 Viewer、图片和本地 MP4；原生视频进度条从 0:00 定位到 0:03。Safari 同样成功加载全部 sibling 资源与 14.7s MP4，并把播放位置从结尾随机回跳到 0:07。Edge 本机未安装，结果仍明确留空。
