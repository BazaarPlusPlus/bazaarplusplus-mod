# BazaarAgent 对局测试记录

## 2026-07-31 — Day 3，升级基座完成后的操作时序

- 场景：Day 3，`B1&B2` 升级基座中选择 `哈库维发射器#00050`。升级动作已被 host 确认，发射器由青铜升至白银（104 → 204 伤害）。
- 观察：确认响应的 context（revision 282）仍在 `Pedestal` 状态并列出 `operations: ["exit", "move", "select", "sell"]`。紧接着对 `exit` 发动作，host 返回 `invalid-action`，并附带 revision 283 的 `Choice` context；此时 `exit` 已不在 operations 中。
- 影响：对局未中断，后续 context 已正确推进至下一小时的选择界面；客户端应以错误响应中的最新 context 继续，不应重放该动作。
- 待查：升级基座的成功动作是否应当直接返回推进后的 Choice context，或在 host 自动推进时避免在可操作 context 中短暂暴露 `exit`，以消除调用方的竞态窗口。
