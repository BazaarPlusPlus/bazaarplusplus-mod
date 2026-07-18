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
对「当前 run 的可记录事实」（天数/小时/胜负/英雄/模式、玩家五属性、段位、排行榜名次）的按需拉取读取。消费方把快照映射成自己的记录，不直读游戏全局。
_Avoid_: run tracker、直读全局状态

**Game Build Channel**:
The classification of the running client as `Online`, `Ptr`, or `Unknown` (resolved once at startup by `GameBuildInfoResolver`; disagreement between signals resolves to `Ptr`). It gates uploads (`channel != Ptr`) and is stamped on recorded runs, isolating PTR data from the production dataset.
_Avoid_: environment, server flag

**Run Logging Intake（对局日志入口）**:
The pure `IBppFeature` that owns run-log subscriptions, session transitions, persistence/checkpoint ordering, and deferred completion. It consumes event-bus inputs and the shared PvP battle catalog; it is not a mounted Unity controller and does not own a run timeline.
_Avoid_: RunLoggingController, run logger MonoBehaviour

**Encounter Preview Module（事件预览模块）**:
The feature-owned query boundary for event cards, encounter-step rewards, and hero level rewards. Callers supply a typed query containing only a stable template id/current level and native text they already hold; the module owns static-plan generation, cache/compile/publication, live hero/day/inventory reads, and final presentation.
_Avoid_: plan runtime facade, Collection encounter helper, patch-built run snapshot

**Ghost Battle**:
A PvP battle fetched from the mod backend in which the local player's uploaded build fought inside another player's run (the game's PvP is asynchronous — opponents are ghosts). `GhostBattleSyncService` imports these battles, flips them into local-player perspective, and HistoryPanel's Ghosts tab lists and replays them.
_Avoid_: remote battle, opponent battle

## Overlay panels

**Main Overlay Panel**:
A full-screen mod overlay — Collection Panel, History Panel, or Live Build Panel. At most one is open at a time; the Overlay Panel Host enforces the exclusivity.
_Avoid_: popup, window

**Overlay Panel Host**:
The single module that owns main-overlay-panel lifecycle: mutual exclusion, scene-change policy, combat gating, hotkey and escape routing, and the per-frame tick. Panels register content callbacks with the host instead of re-implementing the lifecycle.
_Avoid_: panel mutex (the deleted `BppOverlayPanelMutex` predecessor)

**Native Card Preview Host（原生卡牌预览宿主）**:
`GameInterop/CardPreview` 对游戏原生卡牌 prefab、setup/resize、完整可见性（含 native Show 未覆盖的 item gem）、reflection、hover、tooltip 替换、pool 与销毁的唯一 owning module。Collection 与 ItemBoard 只通过 host 打开各自 scope，不直接持有 runtime/reflection/pool。
_Avoid_: card factory facade, global preview pool

**Native Card Preview Scope / Session**:
Scope 是一个真实 UI owner 的资源边界，拥有独立 pool 并在关闭时 cancel/settle acquisition、销毁 active 与 idle 对象。Session 是单张已完成 setup 的 opaque lease，只暴露布局 root/rect 和 show/hide/hover intent；caller 不保存 native component、kind 或 setup task。
_Avoid_: preview handle, setup-task lease, cross-owner session

## Fonts

**Native Game Typography**:
The single adapter (`GameInterop/Fonts/NativeGameTypography`) through which every BPP surface gets text rendering: it reuses the game's own font assets and zh-CN fallback chains and never exposes a raw `Font`/`TMP_FontAsset`. BPP embeds no fonts of its own; CJK tofu is fixed by routing through this adapter, never by editing copy.
_Avoid_: custom font, font selector

## Settings dock

**Cycling Settings Dock Entry（循环设置项）**:
settings dock 中「点击在有序值阶梯上循环、偏离默认值即高亮、渲染本地化状态」的设置项统一概念；功能侧只贡献数据，不写行为类。bool 开关是它的二值特例。动作按钮与锁定开关（局末截图的强制锁开策略）不属于此概念。

## Collection sources

**Collection Source Catalog**:
The versioned in-mod catalog that defines merchant and trainer source filters for the Collection Panel. It is built from `collection-sources.json`, validated by `CollectionSourceCatalog.ExpectedSchemaVersion`, and keyed by stable source keys plus source template ids. Current code expects schema version 4 and source `offerSegments`.

**Offer Pool**:
The set of card templates a source can offer, expressed through structured rules rather than runtime encounter fallback heuristics. Collection source filtering resolves cards against `CollectionSourceEntry.OfferSegments`; the result keeps both the union of offered card ids and per-card source matches.

**Collection Source Kind**:
The source category used by CollectionPanel source chips. `CollectionSourceKind.Merchant` maps to Item sources and `CollectionSourceKind.Trainer` maps to Skill sources; this replaced the older tag-like merchant-kind filtering path.

## Remote embedded data

**Remote Embedded Catalog**:
The shared runtime lifecycle for data shipped as an embedded seed, cached under `<GameRoot>/BazaarPlusPlusV4/`, and refreshed from a remote source. `IRemoteEmbeddedCatalog<T>` exposes only current-snapshot lookup, warm-up, explicit refresh, and disposal; feature modules keep their own parser, logging, and user-facing refresh policy. Warm-up resolves fresh cache first, otherwise publishes stale cache or the embedded seed immediately and refreshes in the background.
_Avoid_: feature repository loader, dual catalog state machine
