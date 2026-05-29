# AutoBazaar（parked）

AutoBazaar 是一个本地回环 HTTP 决策表面：把当前游戏状态发布为版本化快照（`GET /v1/context`），并接受外部 agent 提交的单个动作（`POST /v1/actions`）。mod 只做传输与校验，所有策略决策属于外部工具（决策 agent 现住在独立仓库 `bazaarplusplus-agent`）。

> **状态：parked。** mount 在 `BppComposition.cs:120` 被注释（`// _mountables.Register(new AutoBazaarMount());`），loopback HTTP server 当前**不启动**。整套 `Game/AutoBazaar/` 源码与测试保留，取消注释即可重新启用——这是有意的「parked，不是 dead」状态，清理时勿删。

## 文档

- **wire 契约**（端点、字段、动作集）：[../reference/auto-bazaar-http-api-v1.md](../reference/auto-bazaar-http-api-v1.md)。字段名是稳定契约，勿改名。
- **字段推导**（`AutoBazaarContextBuilder` 如何从游戏状态填充每个字段）：[../reference/auto-bazaar-decision-surface.md](../reference/auto-bazaar-decision-surface.md)。
- **历史设计**（归档，均未实现 / 已实现历史）：[../design/archive/](../design/archive/) 下的 `2026-05-17-autobazaar-agent-design.md`（aspirational，决策 brain 现属 `bazaarplusplus-agent`）、`2026-05-17-bypass-http-rate-limit-design.md`（aspirational）、`2026-05-22-autobazaar-mountable-and-encounter-decoupling-design.md`（已落地，as-built 泛化为 `ComponentMount<T>`，见 [ADR-0002](../adr/0002-mountable-feature-registry.md)）。
