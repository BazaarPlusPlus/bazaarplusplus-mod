> **Status: SUPERSEDED - the offscreen-RT bet was reverted to ScreenSpaceOverlay under URP; do not read as current.** Decision: [ADR-0003](../../adr/0003-history-panel-preview-overlay.md); current state: [history-panel.md](../../features/history-panel.md).

# HistoryPanel Native Rendering Migration

## Scope

Replace HistoryPanel 的自绘卡板渲染管线，让卡面渲染走游戏原生的 `CardPreviewBase.SetUp` 路径。
彻底删除 `Game/PreviewSurface/` 整个文件夹和 `Game/MonsterPreview/Architecture/` 下的孤儿支持代码。

本方案经过两轮独立对抗审阅打磨，最终选定 "**clone fewer levels**" 策略——HistoryPanel 自己拥有
host Canvas + socket 布局，只复用游戏的 `CardPreviewBase` prefab，不使用 `ItemBoardOverlay`。

## Constraints

产品决策，本方案不挑战：

- **单板**——HistoryPanel preview 只渲染一块板
- **Items only**——不渲染 skill
- **不显示血量上限**——`HealthMax` 永远为 0

## Design Rationale

### 为什么不走 `ItemBoardOverlay`

直觉上 `ItemBoardOverlay` (`Game/ItemBoard/ItemBoardOverlay.cs`) 已经在 MonsterPreview / CardSetPreview
两处反射调用 `MonsterBoardTooltip`，HistoryPanel 似乎应该复用。但深度审阅发现这条路有不可越过的障碍：

1. **它绑定 game canvas**：`Ensure()` 把 `_overlayRoot.SetParent(controller.RootCanvas)`，
   `Render()` 还会 `SetAsLastSibling()` 把自己提到游戏 tooltip canvas 顶层。HistoryPanel 想做的是
   离屏 RT 抓帧，这两个行为根本对不上。

2. **`MonsterBoardTooltip` 自带 DOTween 入场/退场**：`Show()` 起 `_carpetImage.DOAnchorPosY(-200f).From()`，
   `UIVisibilityControl.HandleFade(true, 0)` 即使 0 时长也要一帧 DOTween tick；
   离屏抓帧时机难以稳定捕获 alpha=1 状态。

3. **Reveal 协程是错误信号源**：`RevealCardsAfterFrames` 跨 +1/+2/+3 帧分阶段
   `SetActive(_cardImage/_frameContainer)`，跟 art 加载（`CardPreviewBase.SetUp` 内
   `Addressables.LoadAssetAsync<CardAssetDataSO>` Task）完全是两回事；
   等 reveal 完成 ≠ 等 art 完成。

4. **`IsArtReady` 池复用假阳性**：`CardPreviewBase._cardImage.material` 在 F8 重开时本来就 non-null
   （上次留下的），新 art 还在加载时立刻返回 true。

5. **`Render()` 是单方法**：给它加 offscreen mode 分支等于在共享方法里塞 hidden coupling；
   live-canvas 消费者随时回归风险。

### 解法

HistoryPanel 自己拥有 host Canvas + socket 布局，**只复用 `CardPreviewBase` prefab**
（通过反射从任一 `MonsterBoardTooltip` 实例的 `_smallItemReference` / `_mediumItemReference` /
`_largeItemReference` 字段拿到）。

- `ItemBoardOverlay` / `ItemBoardService` 零改动
- `MonsterBoardTooltip` 只是 prefab 引用源，不实例化、不调方法
- 卡面渲染 100% 走游戏自己的 `CardPreviewBase.SetUp` → Addressables 路径
- "等 art 加载完成" 直接 `await Task.WhenAll(SetUp tasks)`——真信号，零假阳性

## Architecture

```
HistoryPanel
├── HistoryBattlePreviewProjection (重写, ~430→~150 行)
│      PvpBattleCardSnapshot[] → List<ItemBoardItemSpec>
│      保留: 模板存在性校验、socket-effect 属性注入、item 类型过滤、socket 排序
│      删除: skill 分支、opponent 分支
│
├── HistoryBattlePreviewData
│      IReadOnlyList<ItemBoardItemSpec> + Signature (battle id 即可)
│
└── HistoryPanelPreviewRenderer (重写, ~700→~250 行, 不经 ItemBoardOverlay)
    Owns:
      GameObject _root (on _privateLayer)
      Canvas _canvas (renderMode=ScreenSpaceCamera, worldCamera=_camera)
      Camera _camera (orthographic, cullingMask = 1 << _privateLayer, targetTexture = _rt)
      RenderTexture _rt
      RectTransform[] _socketAnchors  (10 socket, 固定 pixel 布局)
      Dictionary<ECardSize, Queue<CardPreviewBase>> _pool
      Dictionary<ECardSize, CardPreviewBase> _prefabRefs  (反射一次性缓存, 跨 scene 持久)
      List<CardPreviewBase> _active
      List<Task> _activeSetUpTasks
```

### `EnsureInitialized()`

```
1. 建 root GameObject, 放在 _privateLayer
2. 建 Canvas (renderMode=ScreenSpaceCamera, worldCamera=_camera)
3. 建 Camera (ortho, cullingMask = 1 << _privateLayer, targetTexture=_rt)
4. 在 Canvas 下建 10 个 RectTransform socket, 固定 pixel 布局
   (参考真 MonsterBoardTooltip._sockets 的相对位置作为起点; 后续按容器尺寸 anchor 缩放)
5. AcquirePrefabRefs() 反射一次:
   - Resources.FindObjectsOfTypeAll<MonsterBoardTooltip>() 找任一实例
     (注意: FindObjectsOfTypeAll 包含 inactive 对象, 不依赖场景里有活跃 CardTooltipController)
   - 反射读 _smallItemReference / _mediumItemReference / _largeItemReference
   - 缓存到 _prefabRefs (全局静态, 跨 scene 持久)
6. 任一 prefab 为空 → 永久 status "preview unavailable in this scene"
```

### `RenderPreview(data)` 协程

```csharp
CancelPending();                               // 仅 bump _generation, 不需要碰任何 DOTween
var gen = _generation;

if (!data.HasRenderableCards) { status + Hide; return; }
if (!EnsureInitialized())    { status; return; }
if (_renderedSignature == data.Signature) return;     // 已渲染过同一场, 跳过

status("loading");
ReturnActiveCardsToPool();                     // 上次的 _active 全部 SetActive(false) 回池
_activeSetUpTasks.Clear();

foreach (var item in data.Items) {
    var card = TakeOrInstantiate(item.Size);
    card.gameObject.SetActive(true);
    ApplyLayerRecursive(card.gameObject, _privateLayer);   // 必须, cullingMask 要求
    card.transform.SetParent(_socketAnchors[(int)item.SocketId], false);
    card.Resize();
    var instance = BuildSyntheticTCardInstanceItem(item);  // 复用 ItemBoardOverlay.BuildSyntheticMonster 字段集
    var task = WrapWithLogging(
        card.SetUp(GetCardData(item.TemplateId), isPremium: false, instance)
    );                                          // 单卡失败不污染整体
    _activeSetUpTasks.Add(task);
    _active.Add(card);
}

// 真信号: SetUp Task 完成 = art 已加载
var aggregate = Task.WhenAll(_activeSetUpTasks);
while (!aggregate.IsCompleted && gen == _generation)
    yield return null;
if (gen != _generation) yield break;

yield return new WaitForEndOfFrame();           // RectTransform 布局 settle 一帧
if (gen != _generation) yield break;

_camera.Render();

// 只在全部成功时记 signature; 失败则下次还会重试, 不缓存错误帧
if (!aggregate.IsFaulted)
    _renderedSignature = data.Signature;

status(null);
onRendered?.Invoke();
```

### `CancelPending()`

```csharp
_generation++;
// 没有 DOTween 状态, 没有 ItemBoardOverlay state, 没有 Hide 需要调
```

### `Hide()` / `Dispose()`

```csharp
Hide()    => _root?.SetActive(false);   // 保留池, F8 反复开关不掉性能
Dispose() => Destroy 所有 card + RT + Camera + root;  // 仅 scene change / panel destroy
```

## v2 BLOCKER 在 v3 中如何消失

| v2 BLOCKER | v3 处理 |
|---|---|
| `OnRevealComplete` 是错误信号 | 不存在: 直接 `await Task.WhenAll(SetUp tasks)` |
| `IsArtReady` 池复用假阳性 | 不存在: SetUp Task 本身就是真信号 |
| 反射 null `_canvas` 无效/无害 | 不存在: 卡 spawn 时就在我们 Canvas 下, `SetUp` 第一行 `GetComponentInParent<Canvas>()` 自然解析正确 |
| 60 帧预算凭空猜 | 不存在: await 真 Task, 无超时 |
| DOTween race | 不存在: 不调用 `MonsterBoardTooltip.Show()` |
| `Render()` 模式分支 hidden coupling | 不存在: 不进 `ItemBoardOverlay.Render` |
| `CancelPending+Hide` race | 不存在: `CancelPending` 仅 bump generation |
| 主菜单无 `CardTooltipController` | 不存在: 不依赖 controller; prefab refs 经 `FindObjectsOfTypeAll<MonsterBoardTooltip>()` 获取 |
| 错误帧被缓存 | `_renderedSignature` 仅在 `!aggregate.IsFaulted` 时记录 |

## Spikes (必须先做, 任一失败则方案重评估)

### S0.a — Canvas-mode 渲染 spike

- 临时 GameObject + Canvas (ScreenSpaceCamera) + Camera + RT
- 反射拿一个 `_smallItemReference` prefab
- 在 Canvas 下 Instantiate 一张
- 用硬编码 `TCardBase` + 合成 `TCardInstanceItem` 调 `SetUp`
- **验收**: RT 里能看到完整卡牌 (art + frame + tier)
- **失败的话**: 整个方案不成立, 退回保留自绘渲染, 转做 USS / 字体 / sprite badge 视觉打磨

### S0.b — Addressables 冷/热加载时序测量

- 测量从 `SetUp(...)` 调用到 `_cardImage.material != null` 的时长
- 10 个不同模板 × 5 次, 区分 warm/cold cache (跑前清 Addressables cache)
- **目的**: 确认 Task.WhenAll 等待时长是否在产品可接受范围; 如冷加载超过 5s 需考虑预热或占位帧

### S0.c — Heated/Chilled attribute 视觉保留

- 合成 `TCardInstanceItem` 时 `Attributes[Heated] = 1`
- `SetUp` 后**视觉**上是否仍显示 Heated 指示
- **失败的话**: 用反射在 SetUp Task 完成后写 `_clientCard.Attributes` 强制注入
- 相关参考代码: `Game/HistoryPanel/HistoryBattlePreviewProjection.cs` 当前的
  `ApplySocketEffectAttributes` 逻辑必须保留语义

## 步骤

| Step | 内容 |
|---|---|
| **S0** | 三个 spike (上节), 任一失败 → 重评估 |
| **S1** | `HistoryPanelPreviewCardPool` (~80 行): 池、prefab 获取、layer 设置、回池/取卡 |
| **S2** | `HistoryPanelPreviewLayout` (~50 行): 10 个 socket RectTransform, 固定 pixel 布局 |
| **S3** | 重写 `HistoryBattlePreviewProjection` → `List<ItemBoardItemSpec>` (保留 socket-effect 注入) |
| **S4** | 重塑 `HistoryBattlePreviewData` |
| **S5** | 重写 `HistoryPanelPreviewRenderer`, Task-await 主流程 |
| **S6** | 新测试替代 `tests/PreviewSurfaceHost.Tests/`: <br>- generation 取消 <br>- signature 不缓存失败帧 <br>- 池上限淘汰 <br>- prefab-refs-unavailable 路径 |
| **S7** | 删 `PreviewSurface/` + `MonsterPreview/Architecture/*` + `PreviewBoardSurfaceMarker.cs` + `Patches/Showcase/PreviewBoardSurfacePatches.cs` + `tests/PreviewSurfaceHost.Tests/` + csproj 引用清理 |
| **S8** | 顺手解掉 [history-panel-known-issues.md](../../features/history-panel.md) 的宽屏问题: RT 跟 UI Toolkit `GeometryChangedEvent` 重建, socket 布局 anchor 跟随容器缩放 |

## 删除清单

```
Game/PreviewSurface/                                         整文件夹 (10 文件)
Game/MonsterPreview/Architecture/BoardPose.cs
Game/MonsterPreview/Architecture/BoardRenderModel.cs
Game/MonsterPreview/Architecture/IBoardRenderTarget.cs
Game/MonsterPreview/Architecture/IPreviewDataSource.cs       已是孤儿接口
Game/MonsterPreview/Architecture/MonsterPreviewDefaults.cs
Game/MonsterPreview/Architecture/PreviewBoardDebugOptions.cs
Game/MonsterPreview/Architecture/PreviewBoardSignature.cs
Game/MonsterPreview/View/PreviewBoardSurfaceMarker.cs
Patches/Showcase/PreviewBoardSurfacePatches.cs               依赖被删的 marker
tests/PreviewSurfaceHost.Tests/                              整测试项目
```

`BazaarPlusPlus.csproj` 里相关引用同步清理。

## 残余风险

| 风险 | 缓解 |
|---|---|
| Prefab refs 极早期启动找不到 | `FindObjectsOfTypeAll` 包含 inactive; 首次成功后全局缓存, 跨 scene 持久 |
| `CardPreviewBase.SetUp` 在 ScreenSpaceCamera-only Canvas 下未必工作 | S0.a spike 必通过, 否则方案推翻 |
| 单卡 SetUp 抛异常污染 `Task.WhenAll` | 每个 task 包 try/catch wrapper |
| 池无限增长 | LRU 上限 30 张, 超额 Destroy |
| 合成 `TCardInstanceItem` 漏字段 | 复用 `Game/ItemBoard/ItemBoardOverlay.cs:954-995` 的 `BuildSyntheticMonster` 已验证字段集 |
| Heated/Chilled 仍可能被 tier resolver 覆盖 | S0.c 验证, 否则反射写 `_clientCard.Attributes` after SetUp |
| Layer 必须递归 | 写在 `TakeOrInstantiate` 末尾的 `ApplyLayerRecursive` |

## 关键不变量

1. **`ItemBoardOverlay` / `ItemBoardService` / `MonsterPreviewItemBoardRuntime` / `CardSetPreviewRuntime`
   零改动**——它们的 live-canvas 路径完全不受影响
2. **`MonsterBoardTooltip` 仅作为 prefab 引用源**——不实例化、不调用其方法
3. **卡面渲染 100% 走游戏 `CardPreviewBase.SetUp` → Addressables**——和现有
   `MonsterBoardTooltip.AddCard` 跑的是同一条加载链路, 资源/着色器/品质边框完全一致

## 工作量估算

| 项 | 工作量 |
|---|---|
| S0 三个 spike | 1 天 |
| S1–S2 池 + 布局 | 1 天 |
| S3–S5 projection / data / renderer 重写 | 1.5 天 |
| S6 测试 | 0.5 天 |
| S7 删代码 | 0.5 天 |
| S8 宽屏 | 0.5 天 |
| **合计** | **~5 天** |

## 不在本 spec 范围内 (后续单独成 spec)

- 接入游戏字体替换 `LegacyRuntime.ttf`
- 引入 USS stylesheet 替换 580 行内联样式
- 英雄 badge 改用游戏立绘 sprite
