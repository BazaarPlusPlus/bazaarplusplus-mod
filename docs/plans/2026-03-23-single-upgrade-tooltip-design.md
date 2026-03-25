# Single Upgrade Tooltip Design

**Problem:** The current Bazaar++ upgrade preview reuses the native secondary tooltip flow, which shows a two-card comparison. For hover-based preview, the desired UX is to keep a single tooltip visible while still rendering upgraded values through the native card tooltip pipeline.

**Decision:** Keep the native primary tooltip creation path, but stop spawning the native secondary upgrade tooltip from Bazaar++. Instead, enter the card's upgrade-preview state and refresh the primary tooltip so its content renders through the existing `CanFuse()`-driven native formatting.

**Why this approach:**
- It preserves the native upgraded-value rendering path.
- It avoids reimplementing card tooltip layout and formatting.
- It removes the secondary tooltip dependency, which is tightly coupled to primary-tooltip positioning and animation logic.

**Scope:**
- Item cards only
- Hover upgrade preview only
- Existing hotkey-driven live refresh behavior remains

**Implementation notes:**
- Replace the current delayed `DisplayUpgradeTooltips(...)` call with a delayed primary-tooltip refresh.
- Wait for the primary tooltip controller to exist before refreshing, matching the existing asynchronous lifecycle assumption.
- Hide the current primary tooltip, enter upgrade preview on the card controller, then show the primary tooltip again with the same `CardTooltipData`.
- Let normal tooltip hide/release paths exit upgrade preview and restore the default state.

**Validation:**
- Add a regression assertion that the Bazaar++ patch no longer calls `DisplayUpgradeTooltips(...)`.
- Add a regression assertion that the patch explicitly enters upgrade preview and refreshes the primary tooltip.
