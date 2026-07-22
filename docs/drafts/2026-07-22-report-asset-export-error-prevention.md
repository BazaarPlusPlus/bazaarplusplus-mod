# Report asset export 错误复盘（2026-07-22）

## 错了什么 / 影响范围

物品报告素材先后尝试了 Screen Space Camera 与 World Space Canvas 离屏渲染。两者都无法把 uGUI 写入当前 Unity 6/URP 的 RenderTexture；多轮实机只得到透明 PNG，延误了真正的原生素材导出。更早的 Overlay + screen capture 路径还会在玩家画面中心轮流闪现巨大图标。

## 根因

把“游戏原生 CardPreview 能显示”误当成“它能由私有 Camera 写入 RenderTexture”。项目 ADR-0003 已明确记录当前 URP build 不能把 uGUI 渲染到 RT，但在制定报告素材方案时没有先检查这项既有约束。与此同时，没有优先复用游戏 Collection/Inspect 已经存在的普通 `Renderer` 卡面路径。

## 为什么当时没发现

初始门禁只验证了主画面不闪和 Camera/RT 可创建，没有先放一个最小 uGUI sentinel 验证 Canvas 是否真的进入 RT，也没有在设计 preflight 中搜索 `MEMORY.md`/ADR 的 RenderTexture 结论。因此失败被误归因到 CardPreview 尺寸、激活时序和 URP camera data。

## 当前修复

1. 删除报告物品导出中的全部 uGUI Canvas/CardPreview 路径，不保留 fallback。
2. 技能、头像、状态继续直接 GPU copy 原生 `Texture/Sprite`。
3. 物品改用游戏 `AssetLoader.ConstructAndInstantiateCardVisuals` / `ItemVisualsController` 的普通 Renderer，在专用 Layer 上由始终 disabled 的 Base Camera 通过 URP `SingleCameraRequest` 显式渲染到私有 RenderTexture；不再依赖临时 enable Camera 的帧调度时序。
4. 以 Renderer 数量、compound bounds、RT alpha 与第二份相同阵容 Unity materializer 调用数为硬门禁。

## 防复发检查

以后新增 Unity 渲染/截图链路，在写实现前必须完成三项 preflight：

1. `rg` 搜索 `docs/MEMORY.md`、`docs/adr/` 与 `decompiled/` 中已有的渲染管线限制和原生 prior art。
2. 先在主路径加入一个最小类型 sentinel，分别证明 Camera clear、目标渲染类型与 GPU readback，而不是用复杂业务 prefab 猜失败层级。
3. 验收同时包含“输出像素正确”和“主画面零污染”；任一失败都不得通过调整显示坐标、透明度或截图裁剪继续修补。
