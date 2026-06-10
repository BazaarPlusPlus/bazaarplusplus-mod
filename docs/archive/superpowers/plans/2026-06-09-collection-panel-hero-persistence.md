---
status: implemented
archived: 2026-06-10
superseded-by: docs/ARCHITECTURE.md
---

# Collection Panel Hero Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remember the user's explicit CollectionPanel hero chip selection across panel closes and game restarts, while preserving current-run hero/source auto-selection when the panel opens during a run.

**Architecture:** Keep the persisted value small and user-facing, so use Unity `PlayerPrefs`, not BepInEx config or SQLite. Add a pure codec under `Game/CollectionPanel/Data` for key/value validation and tests, then add a narrow Unity-backed store at the CollectionPanel feature edge. `CollectionPanelOpenSelectionResolver` accepts an optional remembered hero for out-of-run opens only; in-run opens continue to trust the current game hero and encounter source.

**Tech Stack:** C# 12 / netstandard2.1 main mod, Unity `PlayerPrefs`, existing exe-runner tests (`dotnet run --project ...`), existing CollectionPanel source/filter test projects.

---

## Current Code Evidence

- `CollectionPanel.OpenFromDockButton()` opens with `ResolveOpenSelection()`, and `GetCurrentSelectionState()` only returns the in-memory `_filter` state while the singleton exists; there is no persisted selection read path today (`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:130-141`).
- `ResolveOpenSelection()` reads run state, current run/selected hero, encounter ids, and delegates to `CollectionPanelOpenSelectionResolver.Resolve(...)` (`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:145-173`).
- `TryReadCurrentHero()` prefers `TheBazaar.Data.Run?.Player?.Hero`, then `TheBazaar.Data.SelectedHero`, and only accepts concrete heroes through `CollectionPanelOpenSelectionResolver.IsConcreteHero(...)` (`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:198-215`).
- User clicks are routed through `toggleHero`, which currently only calls `_filter.ToggleHero(hero)`, prunes source selections, reapplies filters, and refreshes the view (`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs:491-498`).
- `CollectionFilterState.ToggleHero(...)` already enforces a required single-select hero group: clicking the only selected hero keeps it selected; choosing another clears the old hero and adds the new one (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionFilterState.cs:80-87`).
- `CollectionPanelOpenSelectionResolver.Resolve(...)` currently returns `CollectionPanelSelectionState.Default` whenever the panel is outside a run or lacks a concrete current hero; only in-run concrete heroes drive source matching (`src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelOpenSelectionResolver.cs:12-38`).
- Default state is fixed at Vanessa + Jay Jay (`src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionPanelSelectionState.cs:10-14`).
- Existing PlayerPrefs prior art stores tiny account-scoped UI preferences as JSON/string values and calls `PlayerPrefs.Save()` explicitly (`src/BazaarPlusPlus/Game/Lobby/RandomPoolPrefsHelpers.cs:16-45`).

## Behavioral Decisions

1. Persist only explicit hero chip input. The store writes when the user clicks a hero chip, not when an in-run open auto-selects the current run hero.
2. In-run open behavior remains authoritative. When `isInGameRun == true` and a concrete current hero is available, the resolver ignores the remembered hero and keeps encounter/source auto-selection intact.
3. Out-of-run open uses the remembered hero if it is one of the eight supported panel heroes, including `Common`; otherwise it falls back to the current default Vanessa + Jay Jay.
4. The remembered hero is account-scoped when profile account id or username can be read, matching the RandomHeroPool pattern. If profile lookup fails, it uses an `anonymous` scope.
5. Do not persist selected source in this change. Source identity is tied to tab/source visibility and current encounters; persisting it would widen the feature and risk stale source keys.
6. Do not add a settings toggle. This is a tiny UX preference; disabling it would add config surface without a clear user workflow.

## File Structure

- Create `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionPanelHeroPreference.cs`
  - Pure codec and validation for supported heroes, serialized value, and account-scoped PlayerPrefs key.
  - No Unity dependency so exe-runner tests can compile it.
- Create `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelHeroPreferenceStore.cs`
  - Unity/feature-edge adapter: reads/writes `PlayerPrefs`, resolves account scope through `BppClientCacheBridge`, logs corrupt values, and deletes invalid keys.
- Modify `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelOpenSelectionResolver.cs`
  - Add an optional `EHero? rememberedHero = null` parameter.
  - Use remembered hero only for out-of-run opens.
- Modify `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs`
  - Add a preference-store field.
  - Load remembered hero before resolving out-of-run open selection.
  - Save hero after explicit hero chip clicks.
- Modify `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
  - Compile-link the pure `CollectionPanelHeroPreference.cs`.
- Modify `tests/CollectionFilterEngine.Tests/Program.cs`
  - Add pure codec/key validation tests.
- Modify `tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj`
  - Compile-link the pure `CollectionPanelHeroPreference.cs`, because the open-selection resolver will reference it.
- Modify `tests/CollectionSourceFiltering.Tests/Program.cs`
  - Add resolver tests for out-of-run remembered hero and in-run remembered-hero ignore.
- Modify `docs/features/collection-panel.md`
  - Update as-shipped behavior once implementation is complete.

---

### Task 1: Add Pure Hero Preference Codec

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionPanelHeroPreference.cs`
- Modify: `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`
- Modify: `tests/CollectionFilterEngine.Tests/Program.cs`

- [ ] **Step 1: Compile-link the new pure file in the test project**

Add this item after the existing `CollectionPanelSelectionState.cs` link in `tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj`:

```xml
    <Compile
      Include="..\..\src\BazaarPlusPlus\Game\CollectionPanel\Data\CollectionPanelHeroPreference.cs"
      Link="CollectionPanelHeroPreference.cs"
    />
```

- [ ] **Step 2: Write failing codec tests**

Insert this block in `tests/CollectionFilterEngine.Tests/Program.cs` after the existing default selection assertions:

```csharp
AssertEqual(
    "BPP.CollectionPanel.SelectedHero.anonymous",
    CollectionPanelHeroPreference.BuildPrefsKey(null),
    "CollectionPanel hero preference should use an anonymous scope when no account scope is available."
);
AssertEqual(
    "BPP.CollectionPanel.SelectedHero.account%2Fone",
    CollectionPanelHeroPreference.BuildPrefsKey("account/one"),
    "CollectionPanel hero preference key should URI-escape account scopes."
);
AssertEqual(
    "Dooley",
    CollectionPanelHeroPreference.Serialize(EHero.Dooley),
    "CollectionPanel hero preference should serialize enum names."
);
AssertTrue(
    CollectionPanelHeroPreference.TryParse("Dooley", out var parsedDooley)
        && parsedDooley == EHero.Dooley,
    "CollectionPanel hero preference should parse a supported concrete hero."
);
AssertTrue(
    CollectionPanelHeroPreference.TryParse("Common", out var parsedCommon)
        && parsedCommon == EHero.Common,
    "CollectionPanel hero preference should preserve Common as a real panel hero."
);
AssertFalse(
    CollectionPanelHeroPreference.TryParse("NotARealHero", out _),
    "CollectionPanel hero preference should reject unknown hero strings."
);
AssertFalse(
    CollectionPanelHeroPreference.TryParse("", out _),
    "CollectionPanel hero preference should reject empty values."
);
```

- [ ] **Step 3: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: FAIL at compile time because `CollectionPanelHeroPreference` does not exist yet.

- [ ] **Step 4: Add the pure codec**

Create `src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionPanelHeroPreference.cs`:

```csharp
#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionPanelHeroPreference
{
    private const string AnonymousAccountScope = "anonymous";
    private const string PrefsKeyPrefix = "BPP.CollectionPanel.SelectedHero";

    public static string BuildPrefsKey(string? accountScope)
    {
        var scope = string.IsNullOrWhiteSpace(accountScope)
            ? AnonymousAccountScope
            : Uri.EscapeDataString(accountScope);
        return $"{PrefsKeyPrefix}.{scope}";
    }

    public static string Serialize(EHero hero) => hero.ToString();

    public static bool TryParse(string? raw, out EHero hero)
    {
        hero = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!Enum.TryParse(raw.Trim(), ignoreCase: false, out EHero parsed))
            return false;

        if (!IsSupportedHero(parsed))
            return false;

        hero = parsed;
        return true;
    }

    public static bool IsSupportedHero(EHero hero)
    {
        return hero
            is EHero.Common
                or EHero.Vanessa
                or EHero.Dooley
                or EHero.Pygmalien
                or EHero.Karnok
                or EHero.Mak
                or EHero.Stelle
                or EHero.Jules;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected: PASS and output `CollectionFilterEngine checks passed.`

- [ ] **Step 6: Commit**

```bash
git add src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionPanelHeroPreference.cs tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj tests/CollectionFilterEngine.Tests/Program.cs
git commit -m "test: cover collection panel hero preference codec"
```

---

### Task 2: Teach Open Selection About Remembered Heroes

**Files:**
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelOpenSelectionResolver.cs`
- Modify: `tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj`
- Modify: `tests/CollectionSourceFiltering.Tests/Program.cs`

- [ ] **Step 1: Compile-link the pure preference file in source-filtering tests**

Add this item after the existing `CollectionPanelSelectionState.cs` link in `tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj`:

```xml
    <Compile
      Include="..\..\src\BazaarPlusPlus\Game\CollectionPanel\Data\CollectionPanelHeroPreference.cs"
      Link="CollectionPanelHeroPreference.cs"
    />
```

- [ ] **Step 2: Write failing resolver tests**

Insert this block in `tests/CollectionSourceFiltering.Tests/Program.cs` immediately after the existing `openOutsideRun` assertion:

```csharp
var openOutsideRunWithRememberedHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: EHero.Dooley,
    currentEncounterTemplateId: vanessaAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedHero: EHero.Mak
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Mak,
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openOutsideRunWithRememberedHero,
    "Opening outside a run should use the remembered explicit CollectionPanel hero."
);

var openOutsideRunWithRememberedCommon = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: EHero.Dooley,
    currentEncounterTemplateId: vanessaAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedHero: EHero.Common
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Common,
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openOutsideRunWithRememberedCommon,
    "Opening outside a run should preserve Common as a remembered hero."
);
```

Insert this block immediately after the existing `openOnCurrentMerchant` assertion:

```csharp
var openInRunIgnoresRememberedHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: EHero.Dooley,
    currentEncounterTemplateId: dooleyAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedHero: EHero.Mak
);
AssertEqual(
    new CollectionPanelSelectionState(
        EHero.Dooley,
        dooleyAila.SourceKey,
        CollectionSourceKind.Merchant
    ),
    openInRunIgnoresRememberedHero,
    "Opening during a run should keep the current run hero and source over a remembered hero."
);
```

- [ ] **Step 3: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
```

Expected: FAIL at compile time because `Resolve(...)` does not accept `rememberedHero`.

- [ ] **Step 4: Update the resolver**

Change `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelOpenSelectionResolver.cs` so the `Resolve` signature and early branch look like this:

```csharp
    public static CollectionPanelSelectionState Resolve(
        bool isInGameRun,
        EHero? currentHero,
        Guid? currentEncounterTemplateId,
        IReadOnlyCollection<Guid>? choiceSelectionTemplateIds,
        IEnumerable<CollectionSourceEntry> entries,
        EHero? rememberedHero = null
    )
    {
        if (!isInGameRun)
            return ResolveOutOfRunSelection(rememberedHero);

        if (!IsConcreteHero(currentHero))
            return CollectionPanelSelectionState.Default;

        var hero = currentHero!.Value;
        var source = ResolveSource(
            hero,
            currentEncounterTemplateId,
            choiceSelectionTemplateIds,
            entries
        );

        return source == null
            ? new CollectionPanelSelectionState(
                hero,
                CollectionPanelSelectionState.DefaultMerchantSourceKey,
                CollectionSourceKind.Merchant
            )
            : new CollectionPanelSelectionState(hero, source.SourceKey, source.Kind);
    }

    private static CollectionPanelSelectionState ResolveOutOfRunSelection(EHero? rememberedHero)
    {
        var hero =
            rememberedHero.HasValue && CollectionPanelHeroPreference.IsSupportedHero(rememberedHero.Value)
                ? rememberedHero.Value
                : CollectionPanelSelectionState.DefaultHero;

        return new CollectionPanelSelectionState(
            hero,
            CollectionPanelSelectionState.DefaultMerchantSourceKey,
            CollectionSourceKind.Merchant
        );
    }
```

Keep this existing method unchanged:

```csharp
    internal static bool IsConcreteHero(EHero? hero) => hero.HasValue && hero.Value != EHero.Common;
```

- [ ] **Step 5: Run tests to verify pass**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
```

Expected:
- `CollectionSourceFiltering checks passed.`
- `CollectionFilterEngine checks passed.`

- [ ] **Step 6: Commit**

```bash
git add src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelOpenSelectionResolver.cs tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj tests/CollectionSourceFiltering.Tests/Program.cs
git commit -m "feat: let collection panel open with remembered hero"
```

---

### Task 3: Add Unity PlayerPrefs Store

**Files:**
- Create: `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelHeroPreferenceStore.cs`

- [ ] **Step 1: Add the store**

Create `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelHeroPreferenceStore.cs`:

```csharp
#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal interface ICollectionPanelHeroPreferenceStore
{
    EHero? Load();

    void Save(EHero hero);
}

internal sealed class CollectionPanelHeroPreferenceStore : ICollectionPanelHeroPreferenceStore
{
    private const string LogScope = "CollectionPanelHeroPrefs";

    public EHero? Load()
    {
        var key = BuildScopedPrefsKey();
        if (!PlayerPrefs.HasKey(key))
            return null;

        var raw = PlayerPrefs.GetString(key, string.Empty);
        if (CollectionPanelHeroPreference.TryParse(raw, out var hero))
            return hero;

        BppLog.Warn(LogScope, $"Ignoring invalid saved hero '{raw}' for key '{key}'.");
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        return null;
    }

    public void Save(EHero hero)
    {
        if (!CollectionPanelHeroPreference.IsSupportedHero(hero))
        {
            BppLog.Warn(LogScope, $"Ignoring unsupported hero '{hero}'.");
            return;
        }

        PlayerPrefs.SetString(BuildScopedPrefsKey(), CollectionPanelHeroPreference.Serialize(hero));
        PlayerPrefs.Save();
    }

    private static string BuildScopedPrefsKey()
    {
        return CollectionPanelHeroPreference.BuildPrefsKey(ResolveAccountScopeForPrefs());
    }

    private static string? ResolveAccountScopeForPrefs()
    {
        try
        {
            var accountId = BppClientCacheBridge.TryGetProfileAccountId();
            if (!string.IsNullOrWhiteSpace(accountId))
                return accountId;

            var username = BppClientCacheBridge.TryGetProfileUsername();
            if (!string.IsNullOrWhiteSpace(username))
                return username;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogScope,
                $"Failed to resolve account scope; using anonymous CollectionPanel hero preference: {ex.Message}"
            );
        }

        return null;
    }
}
```

- [ ] **Step 2: Run build to verify compile**

Run:

```bash
./run.sh build
```

Expected: Build succeeds. If it fails because `BppClientCacheBridge` methods are unavailable or renamed, inspect `src/BazaarPlusPlus/GameInterop/BppClientCacheBridge.cs` and align the method names with the same calls used by `RandomPoolPrefsHelpers`.

- [ ] **Step 3: Commit**

```bash
git add src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanelHeroPreferenceStore.cs
git commit -m "feat: add collection panel hero preference store"
```

---

### Task 4: Wire Store Into CollectionPanel

**Files:**
- Modify: `src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs`

- [ ] **Step 1: Add the preference store field**

Add this field near the existing `_filter` field:

```csharp
    private readonly ICollectionPanelHeroPreferenceStore _heroPreferenceStore =
        new CollectionPanelHeroPreferenceStore();
```

The field should sit next to:

```csharp
    private readonly CollectionCatalog _catalog = new();
    private readonly CollectionFilterState _filter = new();
    private readonly CollectionSourceOfferPoolCache _offerPoolCache = new();
```

- [ ] **Step 2: Pass remembered hero to open selection outside runs**

Change `ResolveOpenSelection()` from:

```csharp
        var isInGameRun = IsInGameRunForOpen();
        var hero = isInGameRun ? TryReadCurrentHero() : null;
```

to:

```csharp
        var isInGameRun = IsInGameRunForOpen();
        var rememberedHero = isInGameRun ? null : _heroPreferenceStore.Load();
        var hero = isInGameRun ? TryReadCurrentHero() : null;
```

Change the resolver call from:

```csharp
        var selection = CollectionPanelOpenSelectionResolver.Resolve(
            isInGameRun,
            hero,
            encounterIds.CurrentEncounterTemplateId,
            encounterIds.ChoiceSelectionTemplateIds,
            CollectionSourceCatalog.Entries
        );
```

to:

```csharp
        var selection = CollectionPanelOpenSelectionResolver.Resolve(
            isInGameRun,
            hero,
            encounterIds.CurrentEncounterTemplateId,
            encounterIds.ChoiceSelectionTemplateIds,
            CollectionSourceCatalog.Entries,
            rememberedHero
        );
```

Extend the debug log line with the remembered hero:

```csharp
                + $"rememberedHero={rememberedHero?.ToString() ?? "none"} "
```

Place it after the existing `hero=...` segment so runtime logs show whether the saved value participated.

- [ ] **Step 3: Save explicit hero chip clicks**

Change the hero toggle callback from:

```csharp
            toggleHero: hero =>
            {
                _filter.ToggleHero(hero);
                PruneInvisibleSourceSelections();
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
```

to:

```csharp
            toggleHero: hero =>
            {
                _filter.ToggleHero(hero);
                _heroPreferenceStore.Save(hero);
                PruneInvisibleSourceSelections();
                _scrollY = 0f;
                ApplyFilters();
                RefreshView();
            },
```

This records explicit user input only. Do not call `Save(...)` from `ApplyOpenSelection(...)`; that would make automatic in-run hero detection overwrite the user's remembered out-of-run preference.

- [ ] **Step 4: Run focused tests and build**

Run:

```bash
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
./run.sh build
```

Expected:
- `CollectionSourceFiltering checks passed.`
- `CollectionFilterEngine checks passed.`
- `./run.sh build` succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/BazaarPlusPlus/Game/CollectionPanel/CollectionPanel.cs
git commit -m "feat: remember collection panel hero selection"
```

---

### Task 5: Document As-Shipped Behavior

**Files:**
- Modify: `docs/features/collection-panel.md`

- [ ] **Step 1: Update feature docs**

In `docs/features/collection-panel.md`, update the "入口与生命周期" open-selection bullet so it says:

```markdown
- **打开选择**：`Open` 经 `CollectionPanelOpenSelectionResolver.Resolve` (`CollectionPanelOpenSelectionResolver.cs:12`) 把当前 run 的英雄 + 当前 encounter / 选择项 template id 映射到一个来源条目作为初始过滤；run 内当前英雄优先。run 外则使用用户上次在 CollectionPanel 明确点击的英雄 chip（`PlayerPrefs`，账号 scope，`CollectionPanelHeroPreferenceStore`），无记录或记录无效时回退到默认 Vanessa + Jay Jay。
```

In the "过滤维度" `Heroes` bullet, replace it with:

```markdown
- **Heroes**：单选英雄（`ToggleHero` 始终归一到一个英雄，`CollectionFilterState.cs:82-89`）。`SelectedHero` 仅当恰好选 1 个英雄时有值。用户明确点击英雄 chip 后，当前英雄写入 `PlayerPrefs`，用于下一次局外打开面板；run 内打开仍由当前 run 英雄和 encounter source 决定。
```

- [ ] **Step 2: Validate docs formatting**

Run:

```bash
git diff --check
```

Expected:
- `git diff --check` exits 0.

- [ ] **Step 3: Commit**

```bash
git add docs/features/collection-panel.md
git commit -m "docs: describe collection panel hero persistence"
```

---

### Task 6: Final Verification

**Files:**
- No new files.

- [ ] **Step 1: Run focused test matrix**

Run:

```bash
dotnet run --project tests/CollectionFilterEngine.Tests/CollectionFilterEngine.Tests.csproj
dotnet run --project tests/CollectionSourceFiltering.Tests/CollectionSourceFiltering.Tests.csproj
./run.sh build
git diff --check
```

Expected:
- `CollectionFilterEngine checks passed.`
- `CollectionSourceFiltering checks passed.`
- Debug build succeeds.
- `git diff --check` exits 0.

- [ ] **Step 2: Optional runtime smoke through Steam**

Run:

```bash
open "steam://run/1617400"
```

Manual verification:
- Outside a run, open CollectionPanel, choose Mak, close the panel, reopen it, and confirm Mak remains selected.
- Restart the game, open CollectionPanel outside a run, and confirm Mak remains selected.
- Start or enter a run with another concrete hero, open CollectionPanel, and confirm the run hero/source auto-selection wins over the remembered out-of-run hero.
- Choose another hero manually during the panel session, leave the run, reopen outside a run, and confirm the explicitly clicked hero is now remembered.

- [ ] **Step 3: Review diff**

Run:

```bash
git diff -- src/BazaarPlusPlus/Game/CollectionPanel src/BazaarPlusPlus/Game/CollectionPanel/Data tests/CollectionFilterEngine.Tests tests/CollectionSourceFiltering.Tests docs/features/collection-panel.md
```

Check:
- No SQLite/config changes were added.
- PlayerPrefs writes happen only in the explicit hero-click path.
- Resolver still ignores remembered hero for in-run concrete heroes.
- `Common` remains a supported persisted hero.
- Invalid saved values are deleted and do not crash the panel.

- [ ] **Step 4: Commit or amend final state**

If the previous task commits were made one by one and the team wants a single feature commit, squash locally before merge. Otherwise keep the small commits.

```bash
git status --short
```

Expected: only intended tracked files changed, or the worktree is clean after commits.

---

## Self-Review

- Spec coverage: The plan records the current CollectionPanel input hero via explicit hero chip clicks, restores it on out-of-run opens, preserves current-run hero/source behavior, and documents the behavior.
- Placeholder scan: This plan intentionally contains no open-ended implementation placeholders; every code-changing step includes exact snippets and commands.
- Type consistency: `CollectionPanelHeroPreference` is the pure data helper, `CollectionPanelHeroPreferenceStore` is the Unity/PlayerPrefs adapter, and `ICollectionPanelHeroPreferenceStore` is consumed only by `CollectionPanel`.

## Suggested Rule Additions

None. This plan follows existing CollectionPanel and PlayerPrefs patterns; it does not expose a new repeated trap that belongs in repo rules yet.
