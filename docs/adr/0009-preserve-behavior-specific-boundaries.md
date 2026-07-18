# ADR-0009: Preserve behavior-specific boundaries; reject cosmetic unification

Status: Accepted decision register, consolidated 2026-07-19

## Decision

Do not pursue the following architecture-review proposals unless their underlying behavior changes:

- Do not batch-retire `IGameStateProbe`, `IRunContext`, and `IBppConfig`. They are active seams, and `IGameStateProbe` has a live test adapter used to drive CombatStatusBar behavior ([test seam](../../tests/CombatStatusBarState.Tests/TestCombatStatusBarShims.cs#L17-L38), [test use](../../tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs#L208-L218)).
- Do not wrap HistoryPanel’s four async operations in one configurable `RunGuardedAsync`; replay, health, account-link, and ghost-sync handlers have different ownership, cancellation, rollback, and status semantics ([coordinator](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs#L330), entry points at lines 330, 574, 697, and 932).
- Do not introduce a “two-writer” HistoryPanel rewrite: the coordinator already owns mutable workflow state. `OnPanelShown` intentionally resets replay/account-link state while preserving in-flight ghost sync and health probes ([coordinator](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs#L59-L77), [behavior pin](../../tests/GhostBattleSync.Tests/Program.cs#L165-L193)).
- Do not unify enchant-section line-ending normalization with quest-reward inline normalization. One preserves multiline content; the other trims and joins lines with spaces ([enchant](../../src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs#L46-L61), [quest](../../src/BazaarPlusPlus/Patches/Tooltips/QuestRewardPreviewTooltipPatch.cs#L272-L301)).
- Keep collection facet availability recomputation beside catalog publication, not on the catalog result or per-refresh path ([`SetCatalogCards`](../../src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs#L1119-L1125)).
- Do not add an interop-before-game registration rule: only explicit dependency order is load-bearing; registry order and teardown behavior are already pinned ([feature registry tests](../../tests/CompositionRuntime.Tests/BppFeatureRegistryTests.cs#L30-L67)).

These proposals reduced visible repetition but either encoded divergent behavior as configuration or added a boundary without a consumer. Reopen a specific item only with new code evidence that the divergence or ownership condition no longer exists.
