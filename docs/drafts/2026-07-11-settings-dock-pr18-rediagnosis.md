# Settings dock PR #18 重新诊断

## 背景

- Issue [#19](https://github.com/cauyxy/bazaarplusplus-mod/issues/19) 报告两个独立但相关的 settings dock 问题：BPP 按钮没有正确避让原生按钮；打开 BPP 设置菜单后，鼠标移走仍会残留异常的蓝色高亮样式。
- PR [#18](https://github.com/cauyxy/bazaarplusplus-mod/pull/18) 只调整了 `CanApply=false` 时的位置 fallback：controller 仍写入基础 `desired`，并补了 solver 契约测试。
- 用户随后澄清：本地加载 PR #18 后 icon 位置是正确的。Issue 截图不能再当成 PR #18 之后的位置复现；本轮冻结 placement/avoidance，只处理第二项蓝色视觉异常。

## 当前状态

### 1. 位置：本地已通过

- PR #18 的 `CanApply=false` fallback 已在用户本地得到正确 icon 位置。
- 不再以缺少构建来源的 Issue 截图反推 PR #18 的位置实现，也不重写 solver、scene override 或坐标转换。
- 最终只做一次位置无回归确认，不把它当作本轮待修问题。

### 2. 剩余问题：菜单展开后蓝色方块

- 修复前，点击打开菜单后，controller 把 `expanded=true` 传给 `BppDockButtonExpandedVisualState`。
- 当前实现把 expanded 映射为原生 `selected/ClickedImage` 基线。Unity `SpriteSwap` 在 hover 时用 `Image.overrideSprite` 覆盖它；光标移出后 override 被清除，底下的蓝色 `ClickedImage` 才暴露，所以看起来像“移出后突然残留”。
- 这不是 EventSystem 未 deselect：clone 已禁用 navigation，BPP 也会清 current selected object；清完后代码仍主动重写 selected 基线。
- 反编译的原生 `SettingDialogsView` 打开设置只调用 ShowSettings，并不会把普通 settings popover 按钮设成持久 selected。持久选择只用于 playmode、tab 等真正的选择控件。

## 最终方案

- 保留原生 hover / pressed / normal transition。
- 菜单 expanded/collapsed 只清 EventSystem selection 并恢复原生 normal baseline，不再把 expanded 映射到 `ClickedImage`、selected color 或 selected animation trigger。
- 移除只服务于 expanded-selected 的状态字段/分支，避免 `Image.sprite` 与原生 `SpriteSwap overrideSprite` 双写。
- 不修改 PR #18 的位置 fallback。

## 反馈回路与验证方法

- [x] 先把现有“expanded normal = selected”测试改成失败断言：修复前 2 个用例按预期失败（normal color / normal animation trigger 被 selected 值替换）。
- [x] 移除 expanded-selected 分支，并增加 SpriteSwap normal base sprite 回归用例；settings dock 定向测试 66/66 通过。真实 pointer event 顺序仍保留实机 gate。
- [x] 以不触发游戏目录部署的 `CompatCheck` 配置验证：主项目 build 成功（0 warning / 0 error），Architecture.Tests 29/29 通过。
- [ ] 用户实机复核：打开设置、鼠标从按钮移到菜单内、再移到菜单外，按钮都恢复原生 normal；同时确认 icon 位置仍正确。

## 完成条件

- PR #18 的位置行为不回归。
- 设置菜单打开后移开鼠标，按钮不会露出蓝色 `ClickedImage` / selected 基线。
- 临时诊断代码清理完毕，回归测试能在错误实现上失败、在最终实现上通过。
