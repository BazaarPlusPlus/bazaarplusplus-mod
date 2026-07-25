# Combat Report Viewer：固定帧轴与悬停轴交互恢复

## 背景

时间轴当前同时表达两个不同状态：

- **已选帧（pinned frame）**：用户单击后固定，作为阅读事件、状态曲线和录像的稳定参照。
- **指针预览（hover preview）**：只跟随当前鼠标位置，用于在提交选择前快速扫视时间。

此前连续两轮修改都把视觉降噪当成交互定义：先让悬停替换已选帧，随后又把悬停缩成 ruler 上的短 skimmer。第三轮虽然恢复了两根不同样式的轴，但用户实测仍看到两根轴一起移动，说明绘制层之外仍存在状态所有权或事件传播错误。

## 当前问题

用户在 `__codex-tailwind-theme-review.html` 中移动鼠标时，实轴与虚轴同时移动。正确行为必须是：

| 输入 | 固定实轴 | 悬停虚轴 |
| --- | --- | --- |
| 初始进入 | 报告的当前/默认帧 | 无 |
| `pointermove(t)` | 保持原位置 | 移到 `t` |
| `click(t)` | 固定到 `t` | 与实轴重合时隐藏 |
| 点击后 `pointermove(u)` | 保持在 `t` | 移到 `u` |
| `pointerleave` | 保持在 `t` | 清除 |
| 录像播放推进 | 只有产品明确进入“跟随播放”模式时才更新 | 不变 |

视觉契约：

- 实轴：黄色实线，贯穿状态带、时间标尺和事件泳道。
- 虚轴：绿色虚线，贯穿相同三个区域，不绘制第二个实心 head。
- 两者在 3px 内重合时只绘制实轴。

## 待验证原因

1. `pointermove` 间接派发了选择事件，导致已选帧和预览帧同时更新。
2. 固定轴的 renderer 输入误用了 preview 时间，或两条轴最终读取了同一 mutable ref。
3. **已确认：**悬停触发暂停录像的 preview seek，而录像 `timeupdate` 又调用 `setExternalPlayhead()`，使固定轴跟随移动。
4. Canvas 的 imperative redraw 在一帧中混用了旧/新输入，造成看似双轴同步的残影。
5. Review 页面仍引用了旧 bundle；需要用内容 hash 与运行时行为同时排除缓存/版本错配。

## 候选修复

### A. 明确分离 reducer 状态（优先）

- `selectedCombatMs` 只允许由 click、事件选择或显式播放跟随动作修改。
- `hoverCombatMs` 只允许由 pointer move/leave 修改。
- renderer 只接收不可变快照，不从共享 ref 推导另一条轴。

### B. 保留 imperative hover，但加写入防线

- React/reducer 继续持有 fixed selection。
- Hover 仅写 renderer-local preview ref。
- 在类型与测试层禁止 hover handler 调用 selection/video commit。

### C. 只修绘制参数

若状态观测证明两份时间值本身正确，仅修 renderer 的参数绑定或 canvas 清屏顺序。没有状态证据前不采用。

## 已采用修复

- `RecordingWindow` 的时间回调改为只表达**正在播放的录像时间**；暂停录像因 preview seek 产生的 `timeupdate` 只更新播放器读数，不再发布给时间轴。
- Timeline 的 selection 与 hover renderer 状态保持原有独立所有权；不再为录像反馈环增加第三套时间状态。
- 新增带精确同步录像的浏览器 fixture，并 mock 暂停媒体的 `currentTime → timeupdate` 行为，确保真实复现此前无录像 fixture 漏掉的路径。

## 验收方法

必须用真实鼠标输入按以下序列验收，不能只调用内部方法：

1. 记录初始实轴像素位置 `p0`。
2. Hover 到 25%：虚轴到 `h1`，实轴仍为 `p0`。
3. Click 40%：实轴到 `p1`，重合虚轴不可见。
4. Hover 到 75%：虚轴到 `h2`，实轴仍为 `p1`。
5. 连续移动时：每帧实轴方差为 0，虚轴跟随指针。
6. Pointer leave：虚轴像素清除，实轴仍为 `p1`。
7. 分别在 Chromium 与 WebKit、DPR 1/2 验证状态带、ruler、泳道三块 canvas。
8. 运行时核对安装 bundle 的 SHA-256，排除 review 页面加载旧实现。

回归测试读取两条轴的独立像素位置，并在暂停录像 preview seek 已真实触发 `timeupdate` 后再次断言；仅验证“画面上存在两种颜色”不算通过。
