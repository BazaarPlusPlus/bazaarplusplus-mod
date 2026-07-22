# Post-combat timeline 视觉恢复计划

- 状态：P1/P2/P3 与静态 Viewer 发布主链均已实现；Chrome/Safari 门禁与整仓回归已通过，只剩 Edge 人工发布门禁及提交收尾
- 日期：2026-07-21
- 范围：`prototypes/post-combat-timeline/`；P2 同时包含现有 `GameInterop/CardPreview`、原生纹理 GPU copy seam，以及由显式 Replay 驱动的 report asset export workflow；录像编码参数不在本轮范围内
- 外部 review：`/tmp/bpp-pw/review-for-codex.md`

## 1. 结论

按 P1 → P2 → P3 修复，但不再把“CSS 规则存在”或“图片成功加载”当作视觉验收：

1. Viewer 不再提供“导入/更换录像”入口。录像及同步 metadata 必须由用户在游戏中重新播放并录制该场战斗时生成；Viewer 只读消费 battle ID 匹配的录制结果。有效录像仍使用可折叠的全宽工作台，低高度窗口默认收起；缺失或只有 legacy estimated metadata 时只显示紧凑的“需要重新录制”提示，不渲染黑色视频大框。时间横向缩放与泳道 UI 纵向缩放拆成两个独立维度；用户可在固定时间位置不变的前提下放大或压缩泳道。
2. 物品/技能图不再把 `cardMaterial` 的源纹理误当最终卡面，也不允许创建可见的导出专用 `CardPreview`。技能直接从 `ArtKey` 对应 Texture 导出；物品在回放与录像开始前用隔离的 URP Camera/RenderTexture 烘焙游戏 Collection 的原生 `ItemVisualsController`。只处理全局 cache miss，任一资源失败只降级为 Viewer placeholder，不阻断报告。
3. 统计页改成内容密度驱动的响应式 dashboard：所有视口都紧接标签页顶端排列，内容超出时只在面板内滚动，不用高视口居中制造大块空白，也不把稀疏图表强行拉满。当前样本只有燃烧伤害，单类别时不再画无信息量的环图；多类别时才显示构成图。统计页可读文字的字号下限统一为 11px。

时间轴事件本身不删、不改时间、不做不可逆聚合。录像折叠和图标修复只改变布局与资源呈现。

## 2. 必须保持的三项不变量

1. **全部事件可达**：2,527 条原始记录及其 296 组报告事件仍可从时间轴进入。
2. **时间位置可还原**：固定宽度继续等于 `DurationMs / 1000 × pixelsPerSecond`；任何事件都能恢复到精确 `AtMs`。
3. **聚合可逆**：同帧堆叠标记仍能进入“本帧 N 个事件”，再进入单事件详情和原始 CombatSim 数据。

这三项是 P1 的回归门槛，不允许为了多显示几条泳道而减少事件数据或关闭 drill-down。

## 3. 已验证事实

### 3.1 P1：录像在低高度窗口抢占了核心区域

- 页面固定为 `100dvh`，主体只获得 header、toolbar 之外的剩余空间（`prototypes/post-combat-timeline/styles.css:41`）。
- timeline workspace 把录像作为自然高度的第一行，把泳道放到剩余的 `1fr`（`prototypes/post-combat-timeline/styles.css:143-166`）。
- 有录像时，舞台保持全宽 16:9，最高可到 `55dvh`（`prototypes/post-combat-timeline/styles.css:165-166`）；680px 和 480px 媒体规则仍保留 16:9 自然高度（`prototypes/post-combat-timeline/styles.css:546-600`）。
- Viewer 当前直接渲染“更换录像”按钮和本地 file input（`prototypes/post-combat-timeline/app.js:1295-1332`），随后通过 object URL 把任意文件改成 estimated sync（`prototypes/post-combat-timeline/app.js:469-473,552-565`）。这让录像来源、battle identity 和对齐 metadata 的所有权落到了 Viewer，边界错误。
- 当前 fixture 明确是 `mode: "estimated"`、`provenance: "visual-review-of-legacy-recording"`（`prototypes/post-combat-timeline/battle-video.js:1-18`），不能冒充录制时产生的对齐证据；它应触发“重新录制”状态。
- 外部 review 在约 857px 高窗口量到 `#timeline-scroll.clientHeight = 131px`，内容高 1,160px，只能同时看到约 1.5 条实体泳道。证据：`/tmp/bpp-pw/desktop_viewport.png`、`/tmp/bpp-pw/tall_main.png`。
- 时间尺 36px、状态图 156px、桌面实体行 44px；要同时看到 5 条完整实体行，`#timeline-scroll` 至少需要 `36 + 156 + 5 × 44 = 412px`（`prototypes/post-combat-timeline/styles.css:229-264,299-300`；`prototypes/post-combat-timeline/app.js:6,1827-1831`）。
- 现有工具栏的 `− / ↺ / +` 只修改 `pixelsPerSecond`（`prototypes/post-combat-timeline/app.js:1421-1424,1672-1674,2713-2739`）；泳道高度则分别硬编码在 CSS 的 `--row-height:44px/42px` 和 JS 的 `rowHeight = 44/42`（`prototypes/post-combat-timeline/styles.css:25,299-300,547`；`prototypes/post-combat-timeline/app.js:1825-1827`）。因此目前既没有泳道缩放入口，也存在左右泳道 DOM 与 ECharts 高度常量漂移的风险。

结论：这是纵向空间分配问题，不是滚动实现问题。`overflow:auto` 已存在（`prototypes/post-combat-timeline/styles.css:224`），但滚动不能替代首屏信息密度。

### 3.2 P2：容器比例正确，但导出的不是游戏最终卡面

- lane 已按物品 1/2/3 槽和技能圆形区分，归属色也已独立；该语义框架保留（`prototypes/post-combat-timeline/styles.css:299-326`；`prototypes/post-combat-timeline/app.js:1725-1726`）。
- Medium/Large 当前靠 `object-fit:contain` 后再分别 `scale(1.35)` / `scale(1.8)`（`prototypes/post-combat-timeline/styles.css:318-319`）。它既会留下透明构图，也会在宽图块里裁掉不可预测区域。
- 当前 verifier 只断言上述 CSS 字符串存在，实际上把失败实现固化成了测试（`prototypes/post-combat-timeline/verify-report.mjs:290-296`）。
- 四个 review 样本的 128×128 原图非零 alpha 占比分别为：Black Pepper 71.1%、Dragon Steak 48.4%、Serving Platter 26.4%、Jumbo Wok 16.9%。把它们合成到浅色底后可以确认：这些 PNG 是带大量透明/半透明区域的 material 主纹理，不是已经组合好的卡面。普通 ImageMagick trim 对前三张仍得到近全图 bbox，alpha crop 既无法补回缺失的背景/边框，也会被边缘像素污染。
- 游戏实际用 `CardAssetDataSO.cardMaterial` 创建新 Material、替换为 `_cardMaterialShader`，再应用 premium/enchantment keyword（`decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewItem.cs:78-103`）。tier frame 是另一个异步实例化层（`decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewBase.cs:69-99,146-167`）。
- 代码库已能按 template、tier、enchantment 和 size 创建并 resize 原生 preview（`src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewFactory.cs:65-147`；`src/BazaarPlusPlus/GameInterop/CardPreview/NativeCardPreviewRuntime.cs:43-66,97-117`）。
- 证据 `/tmp/bpp-pw/lane_icons.png` 显示 Serving Platter、Dragon Steak 的宽图块大面积空白，Black Pepper 在 44px 行高中难以辨认。

结论：继续裁主纹理或微调两个 CSS scale 常量都在错误层级修问题。正确输入必须是原生 CardPreview 合成后的最终渲染结果。

### 3.3 P3：统计数据映射正确，但当前可视化选择不适合样本

- 统计页把三个图固定为 242px、210px、190px，高窗口不会利用剩余高度（`prototypes/post-combat-timeline/styles.css:491-510`）。证据 `/tmp/bpp-pw/stats_tab.png` 中底部约 60% 为空。
- heading、KPI 标签、chart subtitle、legend 分别只有 8px、8px、7px、7px；ECharts 数据标签与数轴刻度也只有 8px（`prototypes/post-combat-timeline/styles.css:494-512`；`prototypes/post-combat-timeline/app.js:1932,1974,1989`）。
- 数据投影按 `Health:<subtype>` 正确累计到 Damage/Burn/Poison/Other（`prototypes/post-combat-timeline/app.js:1599-1625`）。对当前 fixture 逐条汇总后，我方 6,567、对手 10,352 的伤害都 100% 为 Burn，没有其他伤害类型。
- 现实现无条件渲染内外两层 pie，且隐藏常态标签（`prototypes/post-combat-timeline/app.js:1937-1963`）。因此单色双环不是映射 bug，而是“单类别数据仍强行画 part-to-whole 图”的产品问题。

## 4. 方案讨论

### ADR-001：低高度窗口中的录像布局

#### Context

录像需要和时间轴联动，展开时也应使用完整内容宽度；但它是辅助证据，不能在常见 857px 高窗口里把核心实体泳道压到 1.5 行。同时，Viewer 不能用任意本地文件替换由 recorder 与 battle metadata 共同定义的录像。

#### Considered Options

1. 永久缩小全宽 16:9 视频：仍会在窄屏占据大量高度，且视频内容被压得太小。
2. 左侧小视频、右侧控制台：能节省高度，但违反已确认的“有视频时展开画面占满宽度”。
3. **可折叠全宽工作台**：低高度默认显示单行 transport；用户展开后显示全宽 16:9 舞台。高窗口可默认展开，用户操作优先于媒体默认。
4. 浮动/覆盖视频：不占布局，但会遮挡时间轴并增加拖动、焦点和移动端复杂度。

#### Decision

选择 3，并收紧录像来源边界：

1. 删除 Viewer 内的“导入/更换录像”、file input 与 object URL 路径。Viewer 不允许手工替换媒体文件或把缺 metadata 的文件降级成 estimated sync。
2. 只有 `battleId` 与报告一致、sync mode 为 `captured`/`exact`、至少两个 anchor 且 anchor 带 `combatFrame + combatMs + mediaPtsMs` 的录制结果才进入播放器。
3. 缺录像、battle ID 不匹配、legacy estimated metadata 或 anchor 不完整时，只显示紧凑的只读提示：“请在游戏中重新播放并录制该场战斗”；不显示空/黑色视频舞台，也不提供浏览器内修复入口。
4. 对有效录像使用真实 button 和 `aria-expanded`，不复制第二套播放器。`max-height: 1000px` 的窗口默认收起，较高窗口默认展开；用户切换后在本次报告会话保持该状态。收起态继续显示播放/暂停、战斗时间 ↔ 视频时间和展开入口，现有 hover/click seek 链路不变。

#### Consequences

- 857px 高窗口默认能为时间轴释放约一个 16:9 舞台的高度。
- 展开态仍是用户要求的全宽视频，而不是小窗。
- 用户要更新录像时必须回到游戏重放并重新录制，录制管线重新写视频与 anchors；Viewer 不承担媒体来源编辑。
- 需要给展开/收起、resize 和 locale 切换补回归测试。

### ADR-002：直接导出原生纹理，离屏烘焙需组合的物品卡面

#### Context

`cardMaterial` 只是渲染输入，不是浏览器可直接显示的完整物品卡面；但技能原图本身是 `ArtKey -> Texture`，头像与状态也能直接得到 `Sprite/Texture`。曾实现的独立 `ScreenSpaceOverlay` 会把原生 `CardPreview` 放在最高层、alpha 设为 1，并逐个等待帧截图；实机因此在游戏中心轮流闪现多个巨大技能图标。这不是偶发 bug，而是采集架构错误地依赖了主 backbuffer。

#### Considered Options

1. 继续调 Medium/Large CSS scale：样本相关、不可验证，已连续失败。
2. 对主纹理做 alpha crop/cover：无法补回 shader/frame；2:1/3:1 cover 还会把方形主体切成中间横条，前景密度反而更容易“达标”。
3. 拼一个近似游戏卡面的 HTML backplate：会产生第二套卡面规则，品质/附魔/尺寸容易继续漂移。
4. **能直接解析的 `Texture/Sprite` 用 GPU copy 导出；需要 shader/frame 组合的物品复用游戏 Collection/Inspect 的 `ItemVisualsController` 普通 Renderer，在专用 Camera + RenderTexture 中离屏烘焙**：不读主 backbuffer，不依赖棋盘状态，能保留原生材质效果。
5. 在独立 ScreenSpaceOverlay export surface 上逐个渲染 CardPreview：图像可用，但必然污染玩家画面；实机已否决。
6. 从真实棋盘整屏抓帧后裁切：没有新增 UI，但会带入棋盘背景、特效、遮挡和屏幕分辨率，也无法稳定生成干净的标准缩略图。

#### Decision

选择 Option 4，并把它作为唯一默认路径：

1. `ReplayBootstrap` 在回放真正启动和录像采集之前执行可选的 report asset export。全 cache hit 时该 hook 立即返回，不创建离屏 surface，不调用 Unity materializer。
2. 技能、英雄头像和状态图标优先从游戏已加载的 `Texture/Sprite` 直接导出。即使 source texture 不可读，也只用 `Graphics.Blit/CopyTexture -> RenderTexture -> AsyncGPUReadback`，不截取屏幕。
3. 物品不再复用 uGUI `CardPreviewItem`。改用游戏 Collection/Inspect 已有的 `AssetLoader.ConstructAndInstantiateCardVisuals(...)` 与 `ItemVisualsController.Setup(CardAssetDataSO, tier, ..., enchantment)`，生成普通 `Renderer` 组成的最终物品外观；该路径原生负责 Small/Medium/Large prefab、`CardAssetDataSO.cardMaterial`、品质框与附魔 shader。
4. 三组实机探针已经证明 `RenderPipeline.SubmitRenderRequest`、Screen Space - Camera 和 World Space Canvas 的正常单帧渲染在当前 Unity 6/URP build 都没有把 uGUI 写入目标纹理；透明哨兵同样未进入 RT，而相机 clear 已写满 RGB，故障边界确定为“uGUI Canvas 没有被该离屏 camera 提交”，不是卡面几何或读回。这个结果也与 ADR-0003 的既有约束一致。删除整个 Canvas/CardPreview report 路径，不再继续调 Canvas 参数。
5. Collection visual 在 inactive 离屏 root 内完成异步实例化与 `ItemVisualsController.Setup`；递归切到报告专用 Layer，启用普通 Renderer 后按 compound `Renderer.bounds` 自动取景。专用 Base Camera 始终 disabled、只 cull 该 Layer，并通过 URP 官方 `SingleCameraRequest` 显式渲染到私有 destination RT；完成后立即 inactive 并异步读回。显式 request 只覆盖普通 Renderer，不能据此恢复已被 ADR-0003 否决的 uGUI 路径。禁止回退 `Camera.Render()`、ScreenCapture、Overlay 或任何连接 display 的 Canvas。
6. 一个 RenderKey 只物化一次。v1 对每个真实 miss 单独提交一次离屏 request 与读回；不做离屏 atlas。实机证明相同阵容的第二份报告已经是 0 次物化/渲染/读回，而 atlas 会重新引入跨尺寸排版、切片和 alpha-bounds 错位面，属于只有数据证明首次导出成为瓶颈后才值得做的后续优化。读回后的 PNG 编码和 cache 写入不再调用 Unity API。
7. RenderKey 必须包含 game build/data identity、renderer/exporter version、asset type、template/skin identity、size/tier/enchantment/variant/attributes、ArtKey/material identity 与离屏 capture profile；直接 texture 与烘焙 preview 不得共用 renderer key。
8. 输出尺寸按 Small/Medium/Large 保持 1/2/3 槽自然比例；技能保持正方形，Viewer 再做圆形 clip。源纹理透明留白可在读回后按 alpha bounds 归一化，但禁止 cover 裁掉主体或拉伸。
9. 启动恢复只复用 cache 并发布 placeholder，绝不在没有用户发起回放时加载/烘焙 Unity 资源。报告 HTML 仍须发布。
10. 完整删除旧 `PostCombatReportCardPreviewMaterializer`、ScreenSpaceOverlay export surface、逐项截图 work queue、Replay exit gate、相关 test IDs 与 fallback；`rg` 是验收门禁。
11. 路径锚定 `<GameRoot>/BazaarPlusPlusV4/report-assets/`；不修改录像分辨率、帧率、码率、编码器、裁剪或时长策略。
12. export factory 必须验证普通 Renderer 数量、compound bounds、专用 Layer 与相机 targetTexture/cullingMask；读回后验证 alpha。任何失败只影响单一资源并发布 placeholder，不允许切回 uGUI 或 backbuffer。

#### Consequences

- 报告仍是全本地静态资源；浏览器首屏不做像素分析。
- 不再有任何导出 UI 出现在玩家画面，也不再因资源导出延迟 Replay exit。
- 物品输出是游戏 Collection/Inspect 原生 Renderer + shader 的离屏结果，技能/头像/状态是原生纹理副本；二者都不含棋盘背景或录像帧。
- 缓存和 placeholder 让资源质量问题与报告可用性解耦；第二份相同阵容报告不做 Unity 工作。

### ADR-003：统计页的单类别伤害与空间利用

#### Context

当前 fixture 的伤害只有 Burn，双环图没有比较信息；固定图高又让高窗口像未加载完成。

#### Considered Options

1. 保留 donut，只在中心加 100%：仍然没有新信息。
2. 无条件移除伤害构成：会损失多伤害类型战斗的分析能力。
3. **按 active category 数量降级**：0 类显示无伤害 empty state；1 类合并进产出区，用直接标注显示“燃烧 100%”及双方数值；2 类以上才显示双方 100% stacked bar。

#### Decision

选择 3。统计图按类别/行数决定高度并设上限，不保留三组历史固定值，也不强迫稀疏图填满 1700px。dashboard 始终紧接标签页顶端；高度不足时由统计面板内部滚动，页面根节点保持视口高度。所有统计页 essential/secondary 字号最低 11px，文字对实际背景的对比度不低于 4.5:1。

#### Consequences

- 当前 fixture 不再显示单色 bullseye。
- 多类别战斗仍能横向比较双方构成，而且直接标签比内外环更容易读。
- chart resize 必须随 grid 容器尺寸变化调用，不能只监听 window width。

### ADR-004：泳道 UI 纵向缩放

#### Context

时间缩放只改变每秒像素数，不能解决泳道文字、物品图标和事件节点需要按用户偏好放大或压缩的问题。直接使用浏览器缩放或对 Canvas/CSS 整体 `transform:scale()` 会同时改变时间尺度、产生模糊，并可能让左侧实体行与右侧 ECharts 网格错位。

#### Considered Options

1. 复用现有 `− / ↺ / +` 同时缩放时间和泳道：两个维度耦合，用户无法区分，事件横坐标也会意外变化。
2. 连续 range slider：精度没有实际价值，并会在拖动时持续触发 Canvas resize 和 series 更新。
3. **独立的离散泳道缩放**：在 sticky 左上角提供 `− / 百分比 / +`，按 80% / 100% / 125% / 150% 四档改变整套泳道布局。

#### Decision

选择 3：

1. 把泳道缩放控件放进 sticky `.axis-corner`，与工具栏中的时间缩放视觉和状态完全分离；百分比按钮恢复 100%，`− / +` 在四档间步进。所有按钮使用本地化 `aria-label` 和 tooltip，窄屏仍完整可用。
2. 用单一 `laneScale` layout spec 派生实体行高、左侧 art viewport、主/副文字、owner marker、事件可见 symbol、同帧碰撞 offset 与不可见 hit area；禁止在 CSS 和 JS 分别维护 42/44px 常量，也禁止用视觉 `transform` 假缩放。
3. 行高档位为 36 / 44 / 56 / 66px；默认保持 44px。可见事件 symbol 随档位有界缩放，hover/click hit area 始终不小于 24×24px，避免紧凑档重新引入难以命中的问题。
4. 泳道缩放只更新 Y 轴相关布局：`pixelsPerSecond`、timeline width、`scrollLeft`、事件 `AtMs` 和状态图高度保持不变。缩放前记录视口中心的实体 lane 与 lane 内相对位置，缩放后恢复该锚点，避免跳到另一实体。
5. 一次点击只在一个 `requestAnimationFrame` 中更新 CSS variables、lane list 高度、ECharts Y 布局和 symbol series；不调用完整 `render()`，不 dispose/re-init chart，不启动连续动画。

#### Consequences

- 用户可以在“首屏多看几条泳道”和“放大图标/事件”之间主动取舍，而 P1 的默认首屏验收仍以 100% 档计算。
- 时间坐标与事件分组不受纵向缩放影响；展开同帧和单事件详情仍指向相同原始记录。
- 需要分别验证四档的左右行对齐、锚点保持、事件命中区、窄屏控件布局和缩放更新耗时。

## 5. 独立 red-team 后的修订

只读 review 指出了五个可执行缺口，已全部纳入：

1. 删除会切掉主体、又能靠密度指标做绿的 2:1/3:1 cover crop；进一步核验游戏源码后，改为导出原生 CardPreview 最终渲染。
2. 删除“stats 下沿必须贴底”和“高视口整体居中”的错误指标；高视口改为内容驱动高度、紧接标签页并在面板内滚动。
3. 删除不适用于细长/低 alpha 主体的统一 raw-art 占比阈值；改量最终 preview 的完整度、自然比例和实际 DOM 可辨性。
4. manifest 继续保持 item 单一 `asset` 与 `/cards/` 目录合同；合成多伤害 fixture 使用独立输入与 expected stats，不改当前 battle 的锁定值。
5. 给 838×857 的录像展开态增加单独几何验收；把 2,527/296 提升为 verifier 中显式命名的 fixture snapshot contract。
6. 用户进一步确认录像所有权应在 recorder：删除 Viewer 的“导入/更换录像”，legacy estimated fixture 改为“需重新录制”，有效媒体只接受录制时写入且 battle ID 匹配的 metadata。
7. 用户补充泳道本身必须可缩放：新增与时间轴横向缩放独立的四档纵向 UI zoom，并把固定行高、图标、事件节点和 hit area 收敛到同一个 layout spec。
8. 第二次只读 review 指出 `ScreenSpaceOverlay` 可显示与 `ScreenCapture` 会捕获该 overlay 是两个不同命题，因此 Option 5 前置 B0 主路径门禁；同时补入受控 backplate、capture/canvas 像素比例、top-left crop 坐标和输出 materialization 边界。
9. Review 对“CardPreview 没有 frame-ready 信号”的担忧经源码复核不成立：`SetUp` 已 await frame/art，factory 也 await `SetUp`。保留一帧合成等待，但不再发明额外轮询或固定延时。
10. 删除导出过程修改全局 `timeScale` 的设计；它会影响游戏其他系统，且对已 await 的资源加载没有必要。
11. 第三次独立 review 在实机闪屏后否决整个可见 Overlay 路径：导出成功不能覆盖玩家画面污染。后续源码核验进一步否决整屏棋盘裁切：技能/头像/状态直接导出纹理，物品在不连接 display 的 Camera -> RenderTexture 中离屏烘焙；历史恢复只读 cache；旧 materializer、exit gate 和 fallback 全部删除。

## 6. 实施清单（确认后执行）

### Phase A：P1 泳道空间

- [x] 增加单一 video expanded state、全宽展开舞台和紧凑收起 transport；补齐 zh-CN/en-US 文案与 `aria-expanded`。
- [x] 删除 `video-import-button`、`video-import-file`、`importVideoFile`、object URL 和对应“导入/更换录像”文案。
- [x] 增加只读 recording metadata gate：battle ID、schema、captured/exact mode、至少两个带 media PTS 的 anchors 必须同时通过。
- [x] 当前 legacy estimated fixture 显示紧凑的“请重新录制该战斗回放”状态，不创建 `<video>` 或黑色 stage。
- [x] 用 viewport height 决定首次默认值；用户显式切换后不再被 resize 覆盖。
- [x] 低高度默认收起录像并把剩余视口交给时间轴；显式展开后改为报告根容器内滚动，录像舞台占满可用宽度并保持 16:9，时间轴作为固定高度工作区完整保留在下方。
- [x] 在 sticky axis corner 增加四档泳道 `− / 100% / +` 控件；与现有时间缩放使用独立 state、文案和 reset 行为。
- [x] 用一个 lane layout spec 同步更新行高、art、文字、owner marker、事件 symbol、碰撞 offset 与 ≥24px hit area；移除 CSS/JS 重复的 42/44px 常量。
- [x] 泳道缩放以视口中心 lane 为纵向锚点，只批量更新 Y 轴布局，不改变时间宽度、横向滚动位置、状态曲线或事件数据。
- [x] 保持既有 video/timeline seek、同步锚点和 frame drill-down 事件绑定。
- [x] 加入 838×857 与 480×857 的 DOM 几何断言和截图。
- [x] 在 verifier 中增加命名的 `EXPECTED_RAW_EVENTS = 2527` / `EXPECTED_PROJECTED_GROUPS = 296` fixture contract，并断言固定时间宽度和聚合 drill-down 都未变化。
- [x] 将真实 captured video 的播放/seek/展开态 E2E 明确移到录像链路的独立后续；本轮 legacy fixture 继续 fail closed，不手工伪造成 captured。

### Phase B：P2 物品资源

- [x] 完整删除 `PostCombatReportCardPreviewMaterializer`、export Overlay/test IDs、逐项 work queue、Replay exit gate 与旧测试；禁止隐藏式 fallback。
- [x] 在回放/录像真正启动前加入可选 asset-export hook；全 cache hit 立即返回。
- [x] 技能/头像/状态直接从 `Texture/Sprite` 做 GPU copy；不可读纹理也不读主 backbuffer。
- [x] 物品用 native Collection `ItemVisualsController` 普通 Renderer + 专用 Camera/Layer/RenderTexture 烘焙；按 compound bounds 自动取景，Camera 保持 disabled、每个 miss 只提交一次 URP `SingleCameraRequest` 到 destination RT；实机 Small/Medium/Large alpha 与自然比例通过。
- [x] 根据实机结果从 v1 删除 atlas release gate：每个真实 miss 独立渲染/读回，RenderKey single-flight 与全局 cache 消除重复工作；atlas 只在未来有首次导出性能数据支持时另立任务。
- [x] 对 texture/material 丢失、preview 组合失败、输出过小或 alpha 异常的单项发布 placeholder；报告生成不失败。
- [x] 启动恢复改成 cache-only，不排队任何 Unity materialization。
- [x] 相同阵容第二份报告断言 offscreen render/GPU readback = 0、Unity materializer invocation = 0、cache object count 不变。
- [x] 报告 exporter 不再依赖 `Rgba32ScreenFrameCapture`；截图功能可保留自己的 screen-capture seam，但 report 代码与测试不得引用它。
- [x] 删除 Medium/Large `transform:scale()`；item 容器改用实测 natural ratio，技能圆形与归属色条保持不变。
- [x] 用真实运行截图覆盖 Small/Medium/Large/Skill 与敌我双方，并确认游戏画面 0 次导出图标闪现。

### Phase C：P3 统计页

- [x] 增加 stats dashboard wrapper；所有高度都紧接标签页顶端排列，chart 高度按内容计算并设上限、溢出只在面板内滚动；窄屏仍单列且无横向溢出。
- [x] 0/1/多伤害类别分别渲染 empty state、直接摘要、双方 100% stacked bar。
- [x] 提升统计页 DOM 文本、legend、axis 和 data label 字号与对比度。
- [x] 让 ECharts 在 grid 容器尺寸变化时 resize。
- [x] 增加当前单 Burn fixture 与一个独立合成多类别 fixture 的数据/UI 回归；二者各有独立 expected-stats block。

### Phase D：统一回归与收尾

- [x] 运行 `node prototypes/post-combat-timeline/verify-report.mjs`。
- [x] 通过旧 URL `?variant=D&lang=zh-CN&rev=20260721e` 回归，证明旧 rev 不会加载陈旧实现。
- [x] 以 838×857、480×857 生成 timeline 截图；另以 1280×1700 生成 stats 截图。
- [x] 检查 console error、页面横向溢出和两级事件详情链路；legacy fixture 按合同没有播放器；真实 captured 录像、exact anchors、完整画面与 F8 打开链路已完成实机验证。
- [ ] 完成整仓测试、自审、提交、合并与推送。

当前证据（P2 旧 Overlay 结果仅作失败证据，不是验收通过）：

- 已验证根因：旧 materializer 为了让 `CardPreview` 进入 backbuffer，把最高层 Overlay alpha 设为 1 并逐项等待帧；用户实际看到多个巨大技能图标轮流闪过。
- 旧实机 batch 的 13/13、12/12 只证明它能产图，同时也证明它污染玩家画面；不能再作为成功门禁。
- 新离屏采集路径三组实机门禁中未观察到导出图标闪现（前两组 contact sheets：`/private/tmp/bpp-offscreen-runtime-review/contact-4x4-methods.png`、`/private/tmp/bpp-offscreen-runtime-v2-review/contact-4x4-methods.png`）；Screen Space Camera 与 World Space Canvas 都生成 alpha 0 的物品纹理。透明 uGUI 哨兵同样未进入 RT，而相机 clear 已写满 RGB，证明相机/RT/读回健康、Canvas 提交失败。Canvas 路径已否决。最终 Collection 普通 Renderer 通过 URP `SingleCameraRequest` 写入私有 RT；真实双次录制已完成，游戏画面未出现导出图标。
- 最新真实报告的第二次相同阵容导出日志为 `cache_hit_count=34`、`cache_miss_count=0`、`unity_materializer_invocation_count=0`，cache object count 不变；报告仍正常发布。
- 最新 native-final 输出像素比例为 Small `280×512`、Medium `544×512`、Large `1072×512`；alpha/visible 占比分别约为 `43.56% / 44.14% / 30.90%`。Small/Medium/Large/Skill contact sheet：`/private/tmp/bpp-native-v8-assets.png`；连续游戏画面抽样：`/private/tmp/bpp-native-v8-game-1784741746444/contact.png`。
- Prototype：report revision `20260722b` 的 verifier 通过，锁定 raw = 2,527、projected = 296、47 个本地 native asset；12 张 item preview 均为 RGBA、alpha 100%、foreground ratio ≥55%，比例分别落在 Small 0.52–0.56、Medium 1.04–1.08、Large 2.06–2.11。
- 838×857：`#timeline-scroll.clientHeight = 626`，同时可见 10 条泳道；12 张 item 全部加载，页面横向 overflow = 0（`/tmp/bpp-post-combat-838x857-full.png`）。
- 480×857：`#timeline-scroll.clientHeight = 650`，同时可见 11 条泳道；页面横向 overflow = 0（`/tmp/bpp-post-combat-480x857-full.png`）。
- 交互：泳道 100%→125%→100% 后 item 比例保持；时间轴 `scrollLeft 0→320→0`；控制台 0 warning/error；同帧事件、角色高亮与详情合同仍由 verifier 锁定。
- 1280×1700 stats：单类别伤害降级为“伤害全部来自燃烧”的直接摘要；output/tempo 图高分别为 228/280px，dashboard 上下留白差 8px（`/tmp/bpp-pw/final-stats-1280x1700.png`）。
- Shipping Viewer v5 的真实 3,393 事件报告：838×857 收起态 `timeline-scroll.clientHeight = 323px`，能完整显示 5 条 54px 泳道；480×857 为 `292px`，能完整显示 4 条。两个视口均无横向 overflow，zh-CN / zh-Hant 的 `hero/item/skill` UI 类型也已分别本地化为英雄/物品/技能。
- 同一真实报告展开录像后，838×857 舞台为 `816×459`，480×857 为 `458×258`，比例均为 16:9、宽度等于工作区；根容器内部滚动，时间轴面板在录像下方固定保留 720px，不删除或降采样事件。
- Shipping stats 在 1280×1700 下 `tabs.bottom == panel.top`、间隙为 0，统计面板高 635.75px 并自身滚动；伤害构成为双方 100% stacked bar，没有 pie/donut，页面级横向 overflow 为 0（`/tmp/bpp-real-v5-stats-1280x1700.png`）。
- F8 默认浏览器入口已打开 `file://.../reports/79db757e300b4d69aca1e5add3c4c0e6.html`；在 Arc/Chromium 中点击 3.15s / Frame 63 的 21 事件聚合后，内嵌本地 MP4 跳到 0:03，证明 sibling JS/CSS/img/video 与 exact seek 在默认浏览器链路可用。
- 正式 Google Chrome 对同一份报告完成 sibling JS/CSS/img/video 加载，原生进度条定位到 0:03；Safari 完成相同资源加载，并把 14.7s MP4 从结尾随机回跳到 0:07。Edge 因本机未安装仍是明确的发行门禁，不能用其他 Chromium 浏览器结果代替。

## 7. 量化验收矩阵

| 范围 | 视口 / fixture | 断言 |
|---|---|---|
| P1 首屏泳道 | 838×857 / 默认 100% | `#timeline-scroll.clientHeight >= 310`；至少 5 个 54px 泳道有 ≥80% 高度可见 |
| P1 首屏泳道 | 480×857 / 默认 100% | `#timeline-scroll.clientHeight >= 256`；至少 4 个 54px 泳道有 ≥80% 高度可见 |
| P1 展开录像 | 838×857 / 480×857 | stage 宽度等于 workspace 可用宽度且比例为 16:9；根容器可内部滚动；下方 timeline panel 高度 `>= 720px`，全部事件仍可达 |
| P1 录像入口 | 任意 fixture | DOM 无 video file input、导入/更换按钮；源码无 object URL/import path |
| P1 录制身份 | legacy estimated fixture | 不创建 `<video>`；显示“需重新录制”；报告 battle ID、事件与时间轴仍可正常查看 |
| P1 有效录像 | 用户重新录制 fixture | video metadata battle ID 精确匹配；mode 为 captured/exact；anchors ≥2 且都含 combat frame/time 与 media PTS；hover/click seek 使用该映射 |
| P1 事件完整性 | 当前 fixture | verifier 中命名并锁定 raw = 2,527、projected groups = 296；选定同帧 aggregate 的详情条目数与 marker count 一致 |
| P1 时间还原 | 当前 fixture | timeline width = `8.5 × pixelsPerSecond`；点 marker 后 cursor `AtMs` 与原始事件一致 |
| P1 泳道缩放几何 | 四档 × 838×857 / 480×857 | 左侧 `.lane-row` 与右侧 ECharts category band 顶边/高度误差 ≤1px；实际行高依次为 36/44/56/66px；控件无裁切或横向溢出 |
| P1 泳道缩放语义 | 四档 × 当前 fixture | `pixelsPerSecond`、timeline width、`scrollLeft`、事件 `AtMs`、projected group count 均不变；缩放前后的中心 lane 相同且 lane 内锚点误差 ≤1px |
| P1 泳道缩放命中 | 四档 × 当前 fixture | 每个可交互事件 hit area ≥24×24px；同帧重叠事件仍可通过帧检查器访问，详情条目数不变 |
| P1 泳道缩放性能 | 当前 fixture，四档往返 20 次 | 每次操作最多一次 rAF layout/series update 和一次 chart resize；无 chart dispose/re-init；更新期间无 >50ms long task，稳定后的 hover/滚动仍达到 60fps |
| P2 无可见导出 UI | 两个历史 battle / 游戏运行 | 游戏中心 0 次导出 CardPreview/技能图标闪现；源码无 export Overlay、退出 gate 或逐项截图 fallback |
| P2 隔离门禁 | 空 cache / 真实回放 | export Camera 始终 disabled；唯一 `SingleCameraRequest.destination` 是私有 RT且只 cull 专用 Layer；不存在 export Canvas；游戏其他 Camera/Canvas 未被修改；连续画面采样 0 次闪现 |
| P2 离屏物品 | 空 cache / 真实回放 | 每个 miss 只调用一次 URP `SubmitRenderRequest`；不调用 `Camera.Render()`、ScreenCapture 或 Overlay；输出 Rect/alpha 有效；失败单项只使用 placeholder |
| P2 cache 命中 | 相同阵容第二次报告 | offscreen Camera frame = 0；GPU readback = 0；Unity materializer invocation = 0；cache object count 不变；报告仍生成 |
| P2 DOM 几何 | 当前 fixture | img natural ratio 与记录 size 一致；默认 100% 下实测 art 高 28px、宽约 15.17/29.62/58.35px；技能保持 1:1；img 未被裁切，Small→Medium→Large 宽度严格递增 |
| P2 可辨性 | 四个回归样本 | Black Pepper、Dragon Steak、Serving Platter、Jumbo Wok 必须存在并进入 native-final manifest；4×4 多采样 contact sheet 能辨认 art/frame/tier/enchant，不能只验“加载成功” |
| P3 高视口利用 | 1280×1700 stats | `statsPanel.top - tabs.bottom = 0`；首个内容块距 panel top ≤32px；任一 sparse chart 高度 ≤360px；超出只在 stats panel 内滚动，不存在页面级横向溢出 |
| P3 单类别 | 当前 fixture | active damage type = 1（Burn）；没有 pie series/单色环；显示双方 Burn 数值与 100% |
| P3 多类别 | 合成 fixture | active damage type ≥2；双方 stacked bar 总和各为 100%，原始数值仍可在 tooltip/label 访问 |
| P3 字体 | 838×857 / 1280×1700 | stats subtitle、KPI label、legend、axis label、data label 的 computed/configured font size 均 ≥11px；DOM 文字对比度 ≥4.5:1 |
| 全局 | 838×857 / 480×857 | `scrollWidth <= clientWidth`；0 console error；本地资源 0 非预期 request failure |

阈值必须写进自动化，不接受仅凭截图主观判断。截图用于检查指标无法覆盖的识别性、层级和拥挤问题。

## 8. 回滚

P1/P3 只改未提交的 Viewer 实现。P2 替换必须删除旧 Overlay 路径，不保留新旧双链；若原生纹理导出或 URP 离屏烘焙失败，回滚态是“报告继续发布 + placeholder”，不是恢复可见 CardPreview。游戏安装目录中的原始 native 资源不被覆盖；录像参数不在本轮修改范围。
