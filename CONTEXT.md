# BazaarPlusPlus Mod

In-game mod for *The Bazaar*. This glossary captures the project-specific vocabulary that recurs across features. General programming concepts are excluded.

## Run / encounters

**Encounter**:
A single stop on a run — a combat, PvP combat, event, shop, pedestal, loot, or level-up — that the player reaches and enters.
_Avoid_: node, map node

**Pedestal**:
An encounter that upgrades or enchants one of the player's existing items, rather than granting a new one. Whether the current Choice screen offers an upgrade pedestal, enchant pedestal, or neither is what drives the upgrade/enchant preview's "smart" mode.

**Encounter status probe**:
The on-demand, pull-based read of the player's *current* run/encounter state (`IEncounterStateProbe.GetEncounterIds()`, `GetChoicePedestal()`, `GetTargetingState()`). The project's chosen way to expose "where is the player in the run right now" — as status queries, not a recorded timeline.
_Avoid_: encounter tracker, run timeline (deliberately not built — see [ADR-0001](docs/adr/0001-encounter-status-probe-not-timeline-tracker.md))

**Run Snapshot Probe（运行快照探针）**:
对「当前 run 的可记录事实」（天数/小时/胜负/英雄/模式、玩家五属性、段位、排行榜名次）的按需拉取读取，由 `IRunSnapshotProbe`（Core）+ `GameInterop/RunSnapshot` 适配器承载，按读取成本分方法。RunLogging 与 Screenshots 的记录构造是消费快照的纯映射器，不再直读游戏全局。

## Overlay panels

**Main Overlay Panel**:
A full-screen mod overlay — Collection Panel, History Review, or Live Build Panel. At most one is open at a time; the Overlay Panel Host enforces the exclusivity.
_Avoid_: popup, window

**Overlay Panel Host**:
The single module that owns main-overlay-panel lifecycle: mutual exclusion, scene-change policy, combat gating, hotkey and escape routing, and the per-frame tick. Panels register content callbacks with the host instead of re-implementing the lifecycle.
_Avoid_: panel mutex (the deleted `BppOverlayPanelMutex` predecessor)

## Settings dock

**Cycling Settings Dock Entry（循环设置项）**:
settings dock 中「点击在有序值阶梯上循环、越省缺即高亮、渲染本地化状态」的统一概念，由 `CyclingSettingsDockEntry<T>` 承载；功能侧只贡献数据（阶梯 + 读写 + 文案 + 可选 nextOverride/onChanged）。bool 开关是 `Toggle` 工厂承载的二值特例。动作按钮与锁定开关（局末截图的强制锁开策略）不属于此概念。

## Collection sources

**Collection Source Catalog**:
The versioned in-mod catalog that defines merchant and trainer source filters for the Collection Panel. It is built from `collection-sources.json`, validated by `CollectionSourceCatalog.ExpectedSchemaVersion`, and keyed by stable source keys plus source template ids. Current code expects schema version 4 and source `offerSegments`.

**Offer Pool**:
The set of card templates a source can offer, expressed through structured rules rather than runtime encounter fallback heuristics. Collection source filtering resolves cards against `CollectionSourceEntry.OfferSegments`; the result keeps both the union of offered card ids and per-card source matches.

**Collection Source Kind**:
The source category used by CollectionPanel source chips. `CollectionSourceKind.Merchant` maps to Item sources and `CollectionSourceKind.Trainer` maps to Skill sources; this replaced the older tag-like merchant-kind filtering path.
