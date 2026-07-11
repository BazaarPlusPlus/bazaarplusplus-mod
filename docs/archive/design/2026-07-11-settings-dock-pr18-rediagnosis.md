# Settings dock PR #18 重新诊断

## 背景

- Issue [#19](https://github.com/cauyxy/bazaarplusplus-mod/issues/19) 报告两个独立但相关的 settings dock 问题：BPP 按钮没有正确避让原生按钮；打开 BPP 设置菜单后，鼠标移走仍会残留异常的蓝色高亮样式。
- PR [#18](https://github.com/cauyxy/bazaarplusplus-mod/pull/18) 只调整了 `CanApply=false` 时的位置 fallback：controller 仍写入基础 `desired`，并补了 solver 契约测试。
- 用户随后先说明本地主界面的 icon 位置看起来正确，但进一步提供了战斗与商城场景：战斗等场景会被排成竖列；商城中 BPP settings/sliders 与原生 info 按钮重叠。因此“位置正确”只适用于部分场景，placement/avoidance 仍未完成。

## 当前状态

### 1. 位置：根因已定位，等待实机 gate

- 主菜单的局部结果不能代表战斗、商城和宝箱场景。
- 战斗场景的竖排并不是自动避让结果，而是 `BppSettingsDockPatch` 对 `FightMenu` 直接使用 `AboveSettingButton(... siblingStepCount: 2)` 的硬编码结果。
- 商城截图中 settings/sliders 与原生 info 按钮重叠。当前实现同时存在两套策略：按场景名预设纵向位置，以及运行时扫描 blocker 后避让；两套策略会互相绕过。
- avoidance 把合法区域限制为 native settings parent 与 root canvas 的交集。该 parent 只是克隆来源，不是 BPP dock 的真实可用边界；当目标位置落在 parent 外时 solver 会返回 `CanApply=false`，PR #18 的 fallback 随后写回未经避让的 `desired`，因此仍可能覆盖原生 info。
- scene override 会先尝试由 `SceneLoader.CatalogSO` 解析真实 scene 名，不应断言 `MarketplaceScene` 必然漏判；删除它的根本理由是产品规则应由当前可见 blocker 决定，而不是由 scene 名决定。
- Unity `Player.log` 还揭示了更直接的首要故障：`BppSettingsDockController.LateUpdate` 每帧抛 `MissingMethodException: Scene.get_handle()`。游戏实际 Player runtime 没有该 getter，导致商店 info 异步出现后，0.25/0.5/1/2/4/8 秒与稳态 probe 从未执行。

### 2. 剩余问题：菜单展开后蓝色方块

- 修复前，点击打开菜单后，controller 把 `expanded=true` 传给 `BppDockButtonExpandedVisualState`。
- 当前实现把 expanded 映射为原生 `selected/ClickedImage` 基线。Unity `SpriteSwap` 在 hover 时用 `Image.overrideSprite` 覆盖它；光标移出后 override 被清除，底下的蓝色 `ClickedImage` 才暴露，所以看起来像“移出后突然残留”。
- 这不是 EventSystem 未 deselect：clone 已禁用 navigation，BPP 也会清 current selected object；清完后代码仍主动重写 selected 基线。
- 反编译的原生 `SettingDialogsView` 打开设置只调用 ShowSettings，并不会把普通 settings popover 按钮设成持久 selected。持久选择只用于 playmode、tab 等真正的选择控件。

### 3. 实机复测新增：三枚按钮的金色外框过紧

- 新 planner 首版只用 `Button.targetGraphic.rectTransform` 作为 footprint。该矩形是 Selectable 的 transition/raycast 主 Graphic，不保证覆盖原生按钮的所有装饰子 Graphic。
- 实机截图中圆形主体没有重叠，但左右/上下金色尖角已经接触；说明 planner 的数值 gap 被 target rect 之外的装饰 overhang 吃掉。
- 修复口径不是盲目增大常量，而是把按钮自身所有有效 Graphic 投影合并成 composite visual footprint；settings panel 与 nested button 必须排除，避免面板展开后反过来改变 dock 尺寸。

## 已完成的视觉修复

- 保留原生 hover / pressed / normal transition。
- 菜单 expanded/collapsed 只清 EventSystem selection 并恢复原生 normal baseline，不再把 expanded 映射到 `ClickedImage`、selected color 或 selected animation trigger。
- 移除只服务于 expanded-selected 的状态字段/分支，避免 `Image.sprite` 与原生 `SpriteSwap overrideSprite` 双写。
- 该修复已经提交并推送；它不解决跨场景位置问题。

## 已实现的统一定位方案

- 同一次 planner 调用原子计算 Collection/book 与 Settings/sliders，不能让 settings 读取上一帧的实际 book 位置。
- Collection/book 的第一候选固定在原生 gear 上方；Settings/sliders 的第一候选固定在 gear 左侧。不再因为 `FightMenu`、store 或 chest 场景名直接改成竖排。
- 只有 settings 的 gear-left 可见矩形与当前真正可见的原生按钮（例如商城 info）相交时，才尝试 book 上方候选；第二候选也必须通过 blocker 与 viewport/safe-bounds 校验。
- planner 使用统一的可见投影空间：优先 screen/display 空间；只有运行时证明 anchor、clone 与 blocker 属于同一个 root canvas 时才能使用 root-local。跨 canvas 的 blocker 也要投影到同一空间。
- 所有尺寸与候选关系均基于按钮自身的 composite visual footprint：以 `targetGraphic` 为基线，再合并有效装饰子 Graphic；不把 settings panel 或 nested button 算入 dock 外框。apply 阶段以“目标 composite 中心 - 当前 composite 中心”的位移平移 clone root，并一次转换成 parent 的完整 local XYZ。
- blocker 的“可见”不能只看 `activeInHierarchy`：还需检查 Graphic enabled/cull、有效 CanvasGroup alpha 和 viewport 相交；不可交互但仍可见的按钮仍然是 blocker。
- 删除 `FightMenu`、store、chest 的硬编码纵向 override 和旧的场景双轨 fallback；最终只保留一条 blocker-driven 定位链。保留 FightMenu 的 attach patch，只删除其 `Above step2` 定位策略。
- 如果 book 上方也被占用或越界，继续按 viewport 向上查找第一个合法槽位；没有合法槽时隐藏两枚 clone 并继续 probe，绝不再回退到无效 `desired`。
- 删除旧 avoidance、scene context、FightMenu/store/chest 纵向 override 与 Collection 的第二个位置 writer；两个 clone 只由 screen-space planner 原子定位。
- scene tracker 改用运行时已验证存在的 active scene name，不再调用 `Scene.handle`；保留异步/稳态重算以覆盖商店内容的延迟创建。

## 反馈回路与验证方法

- [x] 先把现有“expanded normal = selected”测试改成失败断言：修复前 2 个用例按预期失败（normal color / normal animation trigger 被 selected 值替换）。
- [x] 移除 expanded-selected 分支，并增加 SpriteSwap normal base sprite 回归用例；settings dock 定向测试 66/66 通过。真实 pointer event 顺序仍保留实机 gate。
- [x] 以不触发游戏目录部署的 `CompatCheck` 配置验证：主项目 build 成功（0 warning / 0 error），Architecture.Tests 29/29 通过。
- [x] 新 planner red：初始 seam 保持 gear-left 时，商城 info blocker 与第二候选占用两个用例按预期失败。
- [x] 纯逻辑回归：无 blocker 时 book-above-gear + settings-left-of-gear；商城 info 覆盖 left 时选择 above-planned-book；首个 stacked slot 被占时继续向上；无槽时返回不可应用而不是重叠 desired。
- [x] 最终顺序验证：rebase 到最新 `origin/master` 后，SettingsDockRegistry.Tests 87/87、Architecture.Tests 33/33、主项目 CompatCheck build 0 warning / 0 error。
- [x] 装饰 overhang 回归：构造 target rect 120px、四向尖角外伸到 144px 的 composite footprint，断言 book/gear 与 settings/gear 的最终视觉外框仍保留 18px gap；同时覆盖 inactive owner 重试语义、panel/nested-button 排除与非对称视觉中心。
- [x] 用户使用部署 DLL `0b7725c2…` 实机复测本次间距并确认“看着还行”；该结论只覆盖本次观察到的按钮组，不外推为全部场景矩阵已完成。
- [ ] blocker 回归：inactive、alpha=0、culled 与无关 blocker 不触发；可见但 non-interactable 的 blocker 触发；另一 root canvas 的可见 blocker 仍可命中；BPP book/panel 不被当作 native blocker。
- [ ] 坐标回归：覆盖旋转/3D 父级、非零 visual child offset 与不同按钮 footprint；round-trip 后断言最终 `targetGraphic` 可见中心关系和完整 XYZ，而不是只断言 local X/Y 标量。
- [x] 生命周期纯逻辑：scene name 变化重启即时同步窗口；异步与稳态 probe 持续执行；架构测试禁止恢复 `Scene.handle`、Collection 第二 writer 或 scene step2。
- [ ] 用户实机场景矩阵：主菜单、英雄选择、战斗、商城、Collection+宝箱、Prize Pass 与分辨率变化分别截图确认；同时复核打开设置并移开鼠标后按钮恢复原生 normal、panel 不出屏。

## 完成条件

- 主菜单和战斗在左侧无原生 blocker 时不应无条件竖排。
- 商城等左侧已有原生按钮的场景不得重叠，并按用户确认的候选顺序避让。
- 三枚按钮以完整金色外框计距，尖角之间保留明确空隙，不再只保证 target rect 的数学间距。
- 设置菜单打开后移开鼠标，按钮不会露出蓝色 `ClickedImage` / selected 基线。
- 临时诊断代码清理完毕，回归测试能在错误实现上失败、在最终实现上通过。
