# Live Build Panel Target Document

Status: IMPLEMENTED(已落地;细节见下方实现状态注)
Date: 2026-06-05
Scope: replace old CardSetPreview, add in-run live build panel, and migrate HistoryPanel onto the same socketed board preview contract
UI name: 终局阵容
Code feature name: `LiveBuildPanel`

> **实现状态(2026-06-05 起):** 核心架构已落地——Game/LiveBuildPanel/、GameInterop/ItemBoardPreview/BppItemBoard*、Game/BuildRecommendations/、Game/OverlayPanels/BppOverlayPanelMutex;HistoryPanel 已迁移至 BppItemBoardPreview;Game/CardSetPreview/ 已删除。Ui/ 目标文件(LiveBuildPanelView.Tree.cs/.Rows.cs)实际合并为单文件 LiveBuildPanelView.cs。『数据来源』章节引用的 AutoBazaarGameContextReader.cs 等为 WP-R 改名前旧路径(现 src/BazaarPlusPlus.BazaarAgentHost/BazaarAgentGameContextReader.cs)。

## 背景

当前 Caps 触发的是旧 `CardSetPreview` 选择模式。它在 `Update()` 中监听 Caps，进入模式后继续监听 A/D/Tab 和 W/S，并通过全局 tooltip/card click patch 把卡牌点击转发给 `CardSetPreviewRuntime`。Evidence: [`CardSetPreviewRuntime.cs:93-112`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L93-L112), [`CardSetPreviewRuntime.cs:179-201`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L179-L201), [`CardSetPreviewRuntime.cs:225-241`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L225-L241), [`CardSetPreviewClickPatch.cs:11-34`](../../../Patches/Tooltips/CardSetPreviewClickPatch.cs#L11-L34).

旧模式的问题是交互不可见。用户按下 Caps 后，界面没有形成一个明确的操作面板；用户必须知道要继续点卡、按 A/D/Tab 或 W/S，才能理解当前模式。旧 runtime 也把左键添加、右键移除、tooltip lock suppression、推荐切换和 native item-board overlay 都耦在同一个组件里。Evidence: [`CardSetPreviewRuntime.cs:115-154`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L115-L154), [`CardSetPreviewRuntime.cs:299-326`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L299-L326).

`CollectionPanel` 不能直接承担这个职责。它是图鉴/模板目录面板，核心状态是 `CollectionCatalog` 和 `CollectionFilterState`，显示 VM 是 `CollectionCardVm`。终局阵容面板需要读取当前 run 的 live shop/board/stash item，而不是过滤全量模板目录。Evidence: [`CollectionPanel.cs:59-73`](../../../Game/CollectionPanel/CollectionPanel.cs#L59-L73), [`CollectionCardVm.cs:8-27`](../../../Game/CollectionPanel/Data/CollectionCardVm.cs#L8-L27).

`HistoryPanel` 本次进入第一期范围，不再作为后续清理项。它当前通过 `HistoryPanelPreviewSource` 选择 run/battle/ghost preview 数据，再把 `HistoryItemSpec` 交给 `BattleBoardPreview.Render(...)`。Evidence: [`HistoryPanelPreviewSource.cs:21-49`](../../../Game/HistoryPanel/HistoryPanelPreviewSource.cs#L21-L49), [`HistoryPanel.cs:303-316`](../../../Game/HistoryPanel/HistoryPanel.cs#L303-L316), [`BattleBoardPreview.cs:48-97`](../../../Game/HistoryPanel/Preview/BattleBoardPreview.cs#L48-L97).

把 HistoryPanel 拉进来不是为了 DRY，而是为了修正 board socket fidelity。历史快照已经带着真实 socket：`PvpBattleCardSnapshot.Socket` 进入 `HistoryItemSpec.SocketId`，再进入 `NativeCardPreviewSpec.SocketId`。Evidence: [`PvpBattleCardSnapshot.cs:13-20`](../../../Game/PvpBattles/PvpBattleCardSnapshot.cs#L13-L20), [`HistoryBattlePreviewProjection.cs:100-146`](../../../Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs#L100-L146), [`BattleBoardPreview.cs:83-92`](../../../Game/HistoryPanel/Preview/BattleBoardPreview.cs#L83-L92).

游戏自己的 board contract 是“左 socket + size 连续占位”。`CardContainer.ApplyMetadata(...)` 设置 `LeftSocketId` 后按 `Size - 1` 跳过被同一张卡占用的 socket；`SocketedContainer.GetCardsAndSockets()` 也只返回每张卡的左 socket，并按 size 跳过后续占位。Evidence: [`CardContainer.cs:17-28`](../../../decompiled/BazaarGameClient/BazaarGameClient.Domain.Cards/CardContainer.cs#L17-L28), [`SocketedContainer.cs:198-210`](../../../decompiled/BazaarGameShared/BazaarGameShared.Domain.Cards/SocketedContainer.cs#L198-L210). 原生 `MonsterBoardTooltip` 也把 item preview parent 到 `_sockets[(int)SocketId]`，并按 card size 选择小/中/大 prefab。Evidence: [`MonsterBoardTooltip.cs:208-221`](../../../decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/MonsterBoardTooltip.cs#L208-L221), [`MonsterBoardTooltip.cs:222-305`](../../../decompiled/TheBazaarRuntime/TheBazaar.UI.Tooltips/MonsterBoardTooltip.cs#L222-L305).

当前 `BattleBoardPreview` 强制 `ItemBoardPreviewLayoutMode.Packed`。`ItemBoardPreviewSurface` 先按 socket parent 生成卡，但 Packed 模式在 setup 后按 `FrameContainer` world width 重新排列所有 active card，忽略原始 socket 间隙。Evidence: [`BattleBoardPreview.cs:30-37`](../../../Game/HistoryPanel/Preview/BattleBoardPreview.cs#L30-L37), [`ItemBoardPreviewSurface.cs:159-162`](../../../GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L159-L162), [`ItemBoardPreviewSurface.cs:412-443`](../../../GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L412-L443). 因此本次对 HistoryPanel 的修改是数据保真修复：历史棋盘必须按 snapshot socket 展示，不能被 Packed layout 改写成连续 row；大卡只是这个问题最明显的表现。

## 动机

新设计要把 Caps 从“隐藏选择模式”改成一个明确可见的实时面板。用户打开面板后，应同时看到十胜推荐、当前商店 item、场上 item 和箱子 item，并能通过左键直接把可选 item 的 `TemplateId` 加入或移出候选集。

新面板的核心产品判断是：用户关心的是“当前 live run 里有哪些 item 可以作为候选”，而不是旧 `CardSetPreview` 的“先构建一个 selected set，再切换到 final build display mode”。因此新面板不保留 A/D/Tab 显示模式，不保留 W/S 推荐导航，也不保留右键移除候选。推荐切换用右侧操作区按钮，候选切换统一用左键 toggle。

本次也要把旧 `Game/CardSetPreview` 的混合职责清掉。旧目录里既有应删除的交互 runtime，也有仍被 HistoryPanel 使用的 final-build 数据逻辑。`HistoryPanelDataService` 当前调用 `CardSetBuildDataRepository.TryRefreshFinalBuildsFromRemote(...)`，所以 final-build repository 必须迁到中性模块后再删除旧目录。Evidence: [`HistoryPanelDataService.cs:177-202`](../../../Game/HistoryPanel/Storage/HistoryPanelDataService.cs#L177-L202).

## 最终验收标准

### 产品行为

1. Caps 打开/关闭新的 `LiveBuildPanel`，旧 `CardSetPreview` Caps 选择模式不再并行挂载。
2. 面板第一屏就是可操作的“终局阵容”体验，不出现单独说明页或 URL 打开入口。
3. 面板按四行 10-slot item board 展示：
   - 第一行：十胜推荐 build；
   - 第二行：当前商店 item options；
   - 第三行：场上 item；
   - 第四行：箱子 item。
4. 商店行只展示当前 shop selection 中的 item card；skill 和 encounter selection 不显示、不占 slot。
5. 场上行和箱子行展示 live run 中的 item，并保留真实 socket 位置。
6. 左键点击商店/场上/箱子 item 会 toggle 这个 item 的 `TemplateId` 候选状态。
7. 同一个 `TemplateId` 出现在多行或多张卡上时，所有匹配卡同时显示候选样式。
8. 右键不再承担“移除候选”语义。
9. 十胜推荐基于候选 `TemplateId` 集合刷新，右侧操作区提供上一条/下一条推荐按钮。
10. 第一版不执行任何游戏动作：不购买、不售卖、不移动、不 reroll、不 dispatch action。
11. HistoryPanel 的 run/battle/ghost preview 继续工作，但历史棋盘改为 socketed display；历史快照中的空位和大卡 span 不再被 Packed layout 压缩。

### UI 验收

1. 候选样式使用 BPP-owned overlay：金色描边、右上角 check badge、轻微暖色 tint。
2. 候选样式不修改 native card preview 的 material、art、template 或 `CardPreviewBase.SetUp(...)`。原生 setup 会加载 frame/art 并初始化 tooltip 数据。Evidence: [`CardPreviewBase.cs:64-86`](../../../decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewBase.cs#L64-L86), [`CardPreviewItem.cs:80-101`](../../../decompiled/TheBazaarRuntime/TheBazaar.UI/CardPreviewItem.cs#L80-L101).
3. 候选 marker 不接收 raycast；点击只由 row hit target 处理。
4. 四行 slot layout 稳定，空 slot 保持占位，不因数据变化导致整体布局跳动。
5. 小卡占 1 slot，中卡占 2 slot，大卡占 3 slot。
6. 没有匹配十胜推荐时，候选样式仍保留，并显示 no-match 状态。
7. LiveBuildPanel 保留 supporter attribution，但放在面板 chrome / 右侧操作区，不再复用旧 `ItemBoardService` sponsor chrome。旧 runtime 会抽样并把 supporter 文案塞进 overlay request。Evidence: [`CardSetPreviewRuntime.cs:284-297`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L284-L297), [`CardSetPreviewRuntime.cs:299-326`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L299-L326).

### 技术验收

1. 新 feature 目录为 `Game/LiveBuildPanel/`，runtime live card 读取适配放在 `GameInterop/LiveCards/`。
2. 通用 board contract 是 `BppItemBoard`，放在 `GameInterop/ItemBoardPreview/`，由 LiveBuildPanel、HistoryPanel 和 final-build recommendation 共同消费。
3. `BattleBoardPreview` / `HistoryItemSpec` 被共享 `BppItemBoardPreview` / `BppItemBoard` 替换；HistoryPanel 不再使用 `ItemBoardPreviewLayoutMode.Packed`。
4. final-build 数据逻辑迁到中性模块，例如 `Game/BuildRecommendations/`；`Game/CardSetPreview/` 不作为 fallback 保留。
5. `LiveBuildPanel` 和 `HistoryPanel` 不直接依赖彼此内部类型；共享 preview/model 只能通过 `GameInterop.ItemBoardPreview`。
6. 删除旧 `CardSetPreviewRuntime` 时同步删除 composition mount、tooltip patches、hotkey/mode/status classes、旧 board service/chrome 和旧 tests。
7. 面板互斥不再继续扩散 feature-to-feature static import。新增共享 overlay panel mutex/registry，CollectionPanel、HistoryPanel、LiveBuildPanel 打开前都关闭同 sorting band 的其他面板。当前 CollectionPanel 已经用 direct import 关闭 HistoryPanel，因为两者共享 26/27 sorting band。Evidence: [`CollectionPanel.cs:234-237`](../../../Game/CollectionPanel/CollectionPanel.cs#L234-L237), [`CollectionGridConstants.cs:46-50`](../../../Game/CollectionPanel/Grid/CollectionGridConstants.cs#L46-L50), [`HistoryPanelUiToolkitView.cs:107-110`](../../../Game/HistoryPanel/Ui/HistoryPanelUiToolkitView.cs#L107-L110).
8. Four-row native preview 有明确预算：最多四个 row surface、最多约 40 个 active item previews。`ItemBoardPreviewSurface` 每个 surface 会持有自己的 pool/factory 并等待 active setup tasks；Steam 验证必须覆盖打开瞬间的卡顿和空白。Evidence: [`ItemBoardPreviewSurface.cs:15-28`](../../../GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L15-L28), [`ItemBoardPreviewSurface.cs:128-166`](../../../GameInterop/ItemBoardPreview/ItemBoardPreviewSurface.cs#L128-L166), [`NativeCardPreviewPool.cs:10-24`](../../../GameInterop/CardPreview/NativeCardPreviewPool.cs#L10-L24).

### 测试与验证

1. 新增或改写 focused tests 覆盖 `BppItemBoard` slot planner、candidate state、row VM、snapshot reader、HistoryPanel projection、preview renderer mapping 和 architecture boundary。
2. 删除或重写仍在断言 A/D/W/S、display mode enum、旧 status text 的测试。
3. `git diff --check` 通过。
4. mod build 通过。
5. Steam 启动 The Bazaar 后手动验证：
   - Caps 打开新面板；
   - 商店行只显示 item；
   - 场上/箱子保留位置；
   - 左键候选 toggle 生效；
   - 同 template 多处同步显示候选样式；
   - 十胜推荐和上一条/下一条按钮工作；
   - 旧 Caps/CardSetPreview 模式不再出现；
   - CollectionPanel / HistoryPanel / LiveBuildPanel 不会同时可见；
   - Escape 只关闭当前可见面板；
   - 打开含多张大卡的四行面板没有明显首帧卡顿或 native preview 空白；
   - HistoryPanel run/battle/ghost preview 都能渲染，历史空位和大卡 socket 位置保真。

## 范围与非目标

本设计包括对已发布 HistoryPanel preview 的强制迁移。目标是一次性替换旧 `CardSetPreview`、统一 socketed item-board preview contract、删除旧目录和旧交互，不保留旧路径作为 fallback。

本设计不做游戏动作、不改变 run 状态、不新增外部 URL 入口，也不把 live build 功能塞进 `CollectionPanel`。HistoryPanel 的数据列表、回放、ghost sync、server-health、supporter footer 等非 preview 行为不重写。

不重新提出 offscreen RenderTexture preview。ADR-0003 已记录 offscreen camera 无法渲染 uGUI `CardPreviewBase` 的约束，当前路线继续使用 ScreenSpaceOverlay + UITK bounds sync。Reference: [`docs/adr/0003-history-panel-preview-overlay.md`](../../adr/0003-history-panel-preview-overlay.md).

不保留的旧交互：

- A/D 切换 selected set/final build；
- Tab display-mode cycling；
- W/S 推荐导航；
- 左键添加、右键移除的 split 操作；
- 旧 mode indicator/status text 教学 overlay；
- `CardSetPreviewClickPatch` 和 `CardSetPreviewLockTogglePatch` 对旧 runtime 的转发。

## 目标架构

```text
Game/LiveBuildPanel/
  LiveBuildPanel.cs
  LiveBuildPanelMount.cs
  LiveBuildPanelText.cs
  Data/
    LiveBuildPanelSnapshot.cs
    LiveItemBoardRowVm.cs
    LiveBuildCandidateState.cs
  Ui/
    LiveBuildPanelView.cs
    LiveBuildPanelView.Tree.cs
    LiveBuildPanelView.Rows.cs
  Preview/
    LiveBuildPreviewRenderer.cs
    LiveItemBoardRowPreview.cs

Game/BuildRecommendations/
  BuildRecommendationRepository.cs
  BuildRecommendation.cs
  BuildRecommendationSource.cs

Game/OverlayPanels/
  BppOverlayPanelMutex.cs
  IBppOverlayPanel.cs

GameInterop/LiveCards/
  LiveCardSnapshotReader.cs
  LiveCardSnapshot.cs

GameInterop/ItemBoardPreview/
  BppItemBoard.cs
  BppItemBoardCard.cs
  BppItemBoardId.cs
  BppItemBoardType.cs
  BppItemBoardSlotPlanner.cs
  BppItemBoardPreview.cs
```

`LiveBuildPanel` 是 feature owner，负责打开/关闭、候选状态、推荐刷新、按钮动作和生命周期。

`LiveCardSnapshotReader` 是 runtime adapter，负责从 The Bazaar runtime 读取 shop/board/stash live item snapshot。runtime adapters over game/Unity surfaces 属于 `GameInterop`，feature UI state 和 product policy 留在 `Game/LiveBuildPanel`。

`BppItemBoard` 是跨 feature 的 10-slot item board contract。它不表示游戏动作 target，只表示“要按哪个 display socket 渲染哪些 native preview card”。

`BppItemBoardPreview` 是 shared renderer wrapper，持有 `ItemBoardPreviewSurface`，把 `BppItemBoardCard` 映射为 `NativeCardPreviewSpec`，并转发 `Render` / `PollHover` / `Hide` / `Dispose`。HistoryPanel 和 LiveBuildPanel 可以各自包一层 feature adapter，但不得再复制 `BattleBoardPreview.MapSpecs(...)` 这种 feature-owned preview mapping。

`BuildRecommendationRepository` 只负责 final-build 数据、cache/remote refresh 和候选匹配。它可以返回 `BuildRecommendation`，其中包含一个 `BppItemBoard(Id=FinalBuild, Type=Reference)`，从而删除旧 `ItemBoardItemSpec`。

`BppOverlayPanelMutex` 是三块 overlay panel 的互斥点，替换当前 CollectionPanel -> HistoryPanel 的 direct static dependency。它只知道 panel id、visibility 和 close callback，不知道 feature 内部状态。

## BppItemBoard

`BppItemBoard` 放在 `GameInterop/ItemBoardPreview/`，因为它是 native item-board preview surface 的输入 contract，且现在有三个消费者：LiveBuildPanel、HistoryPanel、final-build recommendation rendering。

```text
BppItemBoard
  Id
  Type
  Signature
  Cards

BppItemBoardCard
  TemplateId
  InstanceId
  Order
  Tier
  Size
  Span
  EnchantmentType
  Attributes
  SourceSocketId
  DisplaySocketId
```

`Id` 表示来源身份；`Type` 表示交互和 slot planning 行为：

```text
BppItemBoardId
  FinalBuild
  Historical
  LiveBoard
  LiveStash
  LiveShop

BppItemBoardType
  Reference
  SelectableContainer
  SelectableShop
```

映射固定为：

```text
FinalBuild -> Type=Reference
Historical -> Type=Reference
LiveBoard  -> Type=SelectableContainer
LiveStash  -> Type=SelectableContainer
LiveShop   -> Type=SelectableShop
```

`Reference` 不可点选，不显示 candidate marker；`SelectableContainer` 和 `SelectableShop` 可点选。`TemplateId` 是候选 key。`InstanceId`、`Id` 和 `Order` 只用于 UI identity、hit target、duplicate rendering 和 row identity，不参与候选集合。

HistoryPanel 的 run preview、battle preview 和 ghost preview 都先由 `HistoryPanelPreviewSource` 选出一个 battle snapshot，然后投影成 `BppItemBoard(Id=Historical, Type=Reference)`；run/battle/ghost 只进入 `Signature` 或调用方 selection state，不进入 board taxonomy。

### Row Contract

LiveBuildPanel 四行：

```text
LiveBuildPanelSnapshot
  FinalBuild -> BppItemBoard(Id=FinalBuild, Type=Reference)
  Shop       -> BppItemBoard(Id=LiveShop,   Type=SelectableShop)
  Board      -> BppItemBoard(Id=LiveBoard,  Type=SelectableContainer)
  Stash      -> BppItemBoard(Id=LiveStash,  Type=SelectableContainer)
```

HistoryPanel preview：

```text
HistoryPanel preview -> BppItemBoard(Id=Historical, Type=Reference)
```

### Row VM

```text
LiveItemBoardRowVm
  Board
  Title
  EmptyText
  CanToggleCandidates
```

`CanToggleCandidates` 从 `Board.Type` 派生：`SelectableContainer` 和 `SelectableShop` 为 true，`Reference` 为 false。它可以留在 VM 里服务 UI 绑定，但不应成为独立业务规则。

### Row View

`ItemBoardRow` 的 UITK view 只画非 native card art 的 UI：

1. row title 和状态/空态文案；
2. 固定 10-slot 背板；
3. 按 occupied slot span 对齐的透明 hit targets；
4. 按 occupied slot span 对齐的 candidate markers。

native card art 由 `BppItemBoardPreview` 通过 `ItemBoardPreviewSurface` 画在 overlay canvas 上。marker layer 不接收 raycast；透明 hit target 在 UITK layer，避免 native overlay raycaster 吃掉点击/滚轮。CollectionPanel 已经用 polled hover 避免 overlay raycaster 抢事件。Evidence: [`CollectionGridConstants.cs:46-60`](../../../Game/CollectionPanel/Grid/CollectionGridConstants.cs#L46-L60).

### Row Preview

所有新的 `BppItemBoardPreview` consumer 都使用 `ItemBoardPreviewLayoutMode.Socketed`。现有 default option 已经是 `Socketed`，但 renderer 仍要显式设置，避免以后默认值变化。Evidence: [`ItemBoardPreviewOptions.cs:5-18`](../../../GameInterop/ItemBoardPreview/ItemBoardPreviewOptions.cs#L5-L18).

## Slot Planning

所有 row 都是 10 slots。`ItemBoardSocketLayout` 当前定义 `SocketCount = 10`，native board size 为 2600x600。Evidence: [`ItemBoardSocketLayout.cs:7-11`](../../../GameInterop/ItemBoardPreview/ItemBoardSocketLayout.cs#L7-L11).

item span 规则复用 native preview factory 的语义：small = 1，medium = 2，large = 3。Evidence: [`NativeCardPreviewFactory.cs:170-177`](../../../GameInterop/CardPreview/NativeCardPreviewFactory.cs#L170-L177).

`BppItemBoardSlotPlanner` 负责为 `BppItemBoardCard.DisplaySocketId` 赋值：

- `Reference`: 优先保留 source/recommendation/snapshot 提供的 socket。没有 source socket 时，按 `Order` 做 span-aware fallback 并写 warning。
- `SelectableContainer`: 必须保留 live runtime/container 提供的真实 socket；invalid/overflow card 跳过并写 warning。
- `SelectableShop`: 按来源顺序计算总 span，在 10-slot board 上居中展示。

`SelectableShop` 的居中规则是 socket-based，不是当前 `Packed` 的 frame-width based layout：先求 `totalSpan`，再用 `startSocket = max(0, floor((10 - totalSpan) / 2))`，然后按 span 递增分配 `DisplaySocketId`。如果 `totalSpan > 10`，从 0 开始放能放下的卡，跳过剩余并写 warning。现有 `ItemBoardSocketResolver` 只把 `requestedIndex ?? fallbackIndex` clamp 到合法起点，不会按 span 做完整 packing。Evidence: [`ItemBoardSocketResolver.cs:8-24`](../../../GameInterop/ItemBoardPreview/ItemBoardSocketResolver.cs#L8-L24).

display slot 只服务 UI 展示，不能作为游戏动作 target。

## 数据来源

### Shop Items

从 `Data.CurrentState.SelectionSet` 读取当前商店 selection，逐项通过 `Data.Entities` resolve，只保留 `ItemCard`。AutoBazaar reader 已经证明这条 runtime path：它遍历 `runState.SelectionSet`，把 entry 解析成 `InstanceId`，再从 `Data.Entities` 取实体。Evidence: [`AutoBazaarGameContextReader.cs:379-406`](../../../Game/AutoBazaarHost/AutoBazaarGameContextReader.cs#L379-L406).

selection 中的 skill 和 encounter 分支不进入本面板。AutoBazaar contract 当前已经区分 item、skill、encounter selection。Evidence: [`AutoBazaarGameContextReader.cs:408-415`](../../../Game/AutoBazaarHost/AutoBazaarGameContextReader.cs#L408-L415), [`AutoBazaarDecision.cs:55-69`](../../../AutoBazaar/Contract/AutoBazaarDecision.cs#L55-L69).

### Board And Stash Items

从 `run.Player.Hand` 和 `run.Player.Stash` 读取容器，再通过 `CardContainer.Container.GetCardsAndSockets()` 枚举 item 和 socket。AutoBazaar reader 已经用这条路径读取 board/chest snapshots。Evidence: [`AutoBazaarGameContextReader.cs:267-321`](../../../Game/AutoBazaarHost/AutoBazaarGameContextReader.cs#L267-L321).

LiveBuildPanel 不能直接依赖 `Game/AutoBazaarHost`，因为 BazaarAgent host 已是独立 BepInEx 插件(按需安装)；底层 live container/selection read helper 放在 `GameInterop/LiveCards/`，LiveBuildPanel 消费这个 adapter。AutoBazaar 是否迁到同一 adapter 可以作为同 PR 的 opportunistic cleanup，不能让 LiveBuildPanel import AutoBazaar。

### HistoryPanel

`HistoryPanelPreviewSource.Build(...)` 继续负责选择 preview source：battle mode 选 active battle，run mode 选该 run 最新 battle，ghost battle 用 ghost payload store 还原 snapshot。Evidence: [`HistoryPanelPreviewSource.cs:21-49`](../../../Game/HistoryPanel/HistoryPanelPreviewSource.cs#L21-L49), [`HistoryPanelPreviewSource.cs:52-75`](../../../Game/HistoryPanel/HistoryPanelPreviewSource.cs#L52-L75), [`HistoryPanelPreviewSource.cs:77-89`](../../../Game/HistoryPanel/HistoryPanelPreviewSource.cs#L77-L89).

`HistoryBattlePreviewProjection` 改为产出 `BppItemBoard(Id=Historical, Type=Reference)`，card 字段来自 `PvpBattleCardSnapshot`：`TemplateId`、`Tier`、`Enchant`、`Socket`、`Size` 和 socket-effect attributes。现有 projection 已经负责过滤非 item、解析 template id、验证 static template、合并 socket effect attribute。Evidence: [`HistoryBattlePreviewProjection.cs:100-146`](../../../Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs#L100-L146), [`HistoryBattlePreviewProjection.cs:174-234`](../../../Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs#L174-L234).

### Final Build

final-build recommendation 数据和匹配逻辑迁到 `Game/BuildRecommendations/`。当前 repository 会把 final-build player cards 投影为 `ItemBoardItemSpec`，其中包含 `TemplateId`、`Tier`、`SocketId`、`EnchantmentType` 等渲染所需字段。Evidence: [`CardSetBuildDataRepository.cs:539-558`](../../../Game/CardSetPreview/CardSetBuildDataRepository.cs#L539-L558), [`CardSetBuildDataRepository.cs:558-589`](../../../Game/CardSetPreview/CardSetBuildDataRepository.cs#L558-L589), [`ItemBoardItemSpec.cs:9-35`](../../../Game/CardSetPreview/ItemBoardItemSpec.cs#L9-L35).

迁移后的 repository 返回 `BuildRecommendation`，其 board 字段是 `BppItemBoard(Id=FinalBuild, Type=Reference)`。`HistoryPanelDataService` 的 remote refresh 入口同步改引用，不保留 `BazaarPlusPlus.Game.CardSetPreview` namespace shim。

## 候选状态

候选集合是 `HashSet<Guid>`，元素是 item `TemplateId`。

行为：

1. 左键点击 shop/board/stash row 的 selectable item；
2. `LiveBuildCandidateState` toggle 该 item 的 `TemplateId`；
3. 所有 row 根据 `TemplateId` 重新计算 `IsCandidate`；
4. marker overlay 刷新；
5. final-build recommendation 根据候选 `TemplateId` 集合刷新。

旧 `CardSetPreviewRuntime.GetActiveRecommendations(...)` 也是从 selected items 提取 template ids 后调用 final recommendation lookup。Evidence: [`CardSetPreviewRuntime.cs:360-370`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L360-L370).

候选状态是面板会话状态：关闭 LiveBuildPanel 时清空；scene/run/combat 状态变化导致面板关闭并清空；同一次打开内刷新 snapshot 时，只保留仍出现在 selectable rows 的 template candidates。旧 runtime 在关闭 mode 时也清空 `_selectedCards`。Evidence: [`CardSetPreviewRuntime.cs:156-176`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L156-L176).

## 面板生命周期

LiveBuildPanel 是模态 overlay，不是与原生商店同时可操作的 HUD。面板打开时，底层商店/棋盘不应接收点击；所有候选点击由 UITK hit targets 处理。

触发：

- Caps toggle `LiveBuildPanel` open/closed；
- 旧 `ComponentMount<CardSetPreviewRuntime>` 被替换为新 panel mount；
- 不提供 settings dock entry；Caps 是入口。

打开：

1. 如果正在战斗中，阻止打开。`CollectionPanel` 和 `HistoryPanel` 都已经在 combat 时关闭/阻止。Evidence: [`CollectionPanel.cs:226-232`](../../../Game/CollectionPanel/CollectionPanel.cs#L226-L232), [`CollectionPanel.cs:322-325`](../../../Game/CollectionPanel/CollectionPanel.cs#L322-L325), [`HistoryPanel.cs:166-170`](../../../Game/HistoryPanel/HistoryPanel.cs#L166-L170).
2. 通过 `BppOverlayPanelMutex` 关闭同 sorting band 的其他 overlay panel。
3. 读取 live snapshot。
4. 计算 `BppItemBoard` slot plan 和 candidate state。
5. 渲染四个 rows 和第一条匹配 final build。

更新：

- Escape 关闭面板；
- 左键 toggle candidates；
- 右侧 previous/next buttons 导航推荐；
- scene/run/combat 状态变化时关闭并清空；重新打开时读新 snapshot。CollectionPanel 已经在 scene change 时关闭并 dispose Unity runtime；HistoryPanel scene change 会 dispose preview renderer。Evidence: [`CollectionPanel.cs:389-397`](../../../Game/CollectionPanel/CollectionPanel.cs#L389-L397), [`HistoryPanel.cs:404-418`](../../../Game/HistoryPanel/HistoryPanel.cs#L404-L418).

错误和空态：

- no run：显示空态，不抛异常；
- static data 不可用：显示状态，reopen 时重试；
- no shop item options：shop row 保持 10-slot 空行；
- no matching final build：保留候选 marker，显示 no-match；
- display span 超过 10：渲染能放下的卡，跳过其余并写 warning；
- unsupported selection kind：忽略，除非是 `ItemCard`。

## 旧逻辑删除与迁移

### 删除目标

- `Patches/Tooltips/CardSetPreviewClickPatch.cs`: 新 panel 替换旧 click flow 后删除整个文件。该文件里的两个 patch 都只调用 `CardSetPreviewRuntime.Instance`。Evidence: [`CardSetPreviewClickPatch.cs:11-34`](../../../Patches/Tooltips/CardSetPreviewClickPatch.cs#L11-L34).
- `CardSetPreviewRuntime.cs`: 被新 `LiveBuildPanel` mount 替换。当前 composition 仍直接 mount 旧 runtime。Evidence: [`BppComposition.cs:97-101`](../../../BppComposition.cs#L97-L101).
- `CardSetPreviewHotkeys.cs`, `CardSetBuildRecommendationMode.cs`, `CardSetBuildRecommendationModeFlow.cs`: 新 panel 不再有 display-mode state 和 WASD navigation。
- `CardSetPreviewModeStatusText.cs`, `CardSetPreviewModeIndicator.cs`: 新 panel 是可见操作面，不再依赖旧教学 overlay。旧 runtime 当前在 `ShowModeIndicator()` 调用 status text。Evidence: [`CardSetPreviewRuntime.cs:397-410`](../../../Game/CardSetPreview/CardSetPreviewRuntime.cs#L397-L410).
- `ItemBoardService.cs`, `ItemBoardTemplateSetRequest.cs`, `SponsorPanelRenderer.cs`, `CardSetPreviewSponsorSelection.cs`, `ItemBoardTextHelpers.cs`: old CardSetPreview chrome/preview host 被 `BppItemBoardPreview` 和 panel chrome 替代后删除。
- `BattleBoardPreview.cs`, `HistoryItemSpec.cs`, `HistoryPanelPreviewTextureGeometry.cs`: HistoryPanel 改为 `BppItemBoard` + `BppItemBoardPreview` 后删除或折叠到 shared model；auto-fit 使用 `ItemBoardSocketLayout.NativeBoardWidth/Height`，避免继续维护重复 geometry 常量。当前注释还写着 2400x600，但常量是 2600x600，说明该局部已经漂移。Evidence: [`HistoryPanel.cs:356-374`](../../../Game/HistoryPanel/HistoryPanel.cs#L356-L374), [`HistoryPanelPreviewTextureGeometry.cs:5-11`](../../../Game/HistoryPanel/Preview/HistoryPanelPreviewTextureGeometry.cs#L5-L11).

### 迁移目标

- `CardSetBuildDataRepository.cs`, `CardSetBuildRecommendation.cs`, `ItemBoardItemSpec.cs` 迁到 `Game/BuildRecommendations/` 并重命名为 final-build/build-recommendation 语义；`ItemBoardItemSpec` 被 `BppItemBoardCard` 取代。
- `HistoryPanelDataService.cs` 改引用新 repository；`AllowedFeaturePreviewBoundaryFiles` 里的旧 HistoryPanel -> CardSetPreview exception 删除。
- `HistoryPanelPreviewSource` / `HistoryBattlePreviewProjection` 改返回 `HistoryBattlePreviewData(Board: BppItemBoard, Signature)` 或直接由 data 承载 board signature。
- `CollectionPanel` 和 `HistoryPanel` 当前 direct mutex 替换为 `BppOverlayPanelMutex`。

### 测试迁移

`MonsterPreviewResilience.Tests` 当前仍直接编译并断言旧 mode enum、mode flow、hotkey helper 和 mode status text。Evidence: [`MonsterPreviewResilience.Tests.csproj:10-26`](../../../tests/MonsterPreviewResilience.Tests/MonsterPreviewResilience.Tests.csproj#L10-L26), [`MonsterPreviewResilience.Tests/Program.cs:14-68`](../../../tests/MonsterPreviewResilience.Tests/Program.cs#L14-L68). 这些旧断言应删除。该文件里的 supporter attribution helper 断言不需要搬家，因为 `Supporters.Tests` 已经覆盖相同 helper。Evidence: [`Supporters.Tests/Program.cs:18-40`](../../../tests/Supporters.Tests/Program.cs#L18-L40), [`Supporters.Tests.csproj:14-33`](../../../tests/Supporters.Tests/Supporters.Tests.csproj#L14-L33).

`CardSetBuildRecommendationTier.Tests` 不能漏。它通过 reflection 找旧 `BazaarPlusPlus.Game.CardSetPreview.CardSetBuildDataRepository`，并覆盖 tier mapping、cache path 和 remote-refresh 行为。Evidence: [`CardSetBuildRecommendationTier.Tests/Program.cs:25-43`](../../../tests/CardSetBuildRecommendationTier.Tests/Program.cs#L25-L43), [`CardSetBuildRecommendationTier.Tests/Program.cs:45-103`](../../../tests/CardSetBuildRecommendationTier.Tests/Program.cs#L45-L103). 迁移 repository 时必须同步更新类型名和测试断言。

`CoreLayeringTests.HistoryPanel_and_CardSetPreview_do_not_depend_on_each_others_preview_internals` 不能只改 allowlist。它现在 assert `Game/CardSetPreview` directory exists；旧目录删除后这条规则本身会失败。Evidence: [`CoreLayeringTests.cs:149-205`](../../../tests/Architecture.Tests/CoreLayeringTests.cs#L149-L205). 新规则应改成：`Game/HistoryPanel` 与 `Game/LiveBuildPanel` 不互相 import，二者通过 `GameInterop.ItemBoardPreview` 共享；`Game/CardSetPreview` 不存在。

## 测试计划

新增或更新测试：

1. `BppItemBoardSlotPlanner.Tests`
   - `Reference` 使用 source socket；缺失 source socket 时按 order 做 span-aware fallback 并记录可观测 warning；
   - `SelectableContainer` 使用 source socket，invalid/overflow 跳过；
   - `SelectableShop` 按总 span 在 10-slot board 上居中；
   - small/medium/large 无重叠 placement；
   - cursor 按 span 前进，不按 item count；
   - 超出 10 slots 的 item 被跳过；
   - output socket 不超过 0..9。

2. `BppItemBoardPreview.Tests` / `HistoryPanelPreview.Tests`
   - `BppItemBoardCard` 映射到 `NativeCardPreviewSpec`；
   - HistoryPanel board 使用 `ItemBoardPreviewLayoutMode.Socketed`；
   - `HistoryBattlePreviewProjection` 保留 snapshot socket/size/enchant/attributes；
   - run/battle/ghost selection 仍产出正确 signature；
   - auto-fit 改用 `ItemBoardSocketLayout.NativeBoardWidth/Height`。

3. `LiveItemBoardRowVm.Tests`
   - final-build / historical row read-only；
   - shop/board/stash rows 暴露 candidate hit targets；
   - candidate marker state 来自 `TemplateId`；
   - row layout 始终 10 slots。

4. `LiveBuildCandidateState.Tests`
   - toggle add/remove by `TemplateId`；
   - duplicate `TemplateId` 共享候选状态；
   - `InstanceId` 不创建独立候选 membership；
   - close/run-change 清空候选。

5. `LiveCardSnapshotReader.Tests` 或 executable harness
   - shop selection 只保留 item；
   - skill/encounter selection 被忽略；
   - board/stash snapshot 保留 socket id。

6. `BuildRecommendations.Tests`
   - final-build repository namespace/type migration；
   - tier mapping；
   - cache path；
   - stale cache + queued remote refresh；
   - candidate template matching returns `BppItemBoard(Id=FinalBuild, Type=Reference)`。

7. Architecture tests
   - `Game/CardSetPreview` directory is gone;
   - `Game/LiveBuildPanel` 不依赖 `Game/HistoryPanel`；
   - `Game/HistoryPanel` 不依赖 `Game/LiveBuildPanel`；
   - shared native preview/model 通过 `GameInterop.ItemBoardPreview` 使用；
   - overlay panel mutex 不引入 Core -> Game dependency。

## 实施顺序

1. 新增 pure data tests：`BppItemBoardSlotPlanner`、HistoryPanel projection、candidate state。
2. 新增 `BppItemBoard`、`BppItemBoardCard`、`BppItemBoardId`、`BppItemBoardType`、`BppItemBoardSlotPlanner`、`BppItemBoardPreview`。
3. 迁移 HistoryPanel preview：`HistoryBattlePreviewProjection` 产出 `BppItemBoard`，HistoryPanel 使用 shared preview，删除 `BattleBoardPreview` / `HistoryItemSpec` / duplicate geometry。
4. 迁移 final-build repository 到 `Game/BuildRecommendations/`，更新 `HistoryPanelDataService` 和 `CardSetBuildRecommendationTier.Tests`。
5. 新增 `BppOverlayPanelMutex`，把 CollectionPanel / HistoryPanel / LiveBuildPanel 的互斥集中到共享 registry。
6. 新增 `GameInterop/LiveCards/LiveCardSnapshotReader`。
7. 新增 LiveBuildPanel panel shell 和 Caps toggle；不新增 settings dock entry。
8. 新增 `ItemBoardRow` UITK view、hit targets、candidate markers、supporter attribution。
9. 新增 `LiveItemBoardRowPreview` 和 `LiveBuildPreviewRenderer`，连接 final-build recommendation。
10. 删除旧 `Game/CardSetPreview` runtime/patch/hotkey/mode/chrome/tests，更新 architecture tests。
11. `git diff --check`、focused tests、mod build。
12. Steam runtime verification：LiveBuildPanel + HistoryPanel run/battle/ghost + panel mutex + performance.

## 文档自检

- 没有实现代码包含在本文档内。
- 没有 URL-opening flow。
- shop row 只读取当前商店 item options。
- candidate matching 只基于 `TemplateId`。
- 四行统一使用 10-slot row contract。
- HistoryPanel preview 是第一期强制迁移项，不是后续 cleanup。
- Caps 明确切到新的 `LiveBuildPanel`，旧 `CardSetPreview` Caps mode 不并行保留。
- final-build repository 先迁到中性模块，再删除旧 `Game/CardSetPreview`。
- Packed -> Socketed 的依据是 decompiled/native socket contract 和当前 packed relayout 代码，不是未证实猜测。
