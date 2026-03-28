# Random Hero Pool Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a native-feeling Hero Select popup that lets players restrict random-hero mode to a chosen subset of unlocked heroes, while preserving the current default behavior of "all unlocked heroes".

**Architecture:** Keep the random-pool rules in pure C# classes under `Game/Lobby` so they can be unit tested without Unity scene setup. Patch the existing Hero Select UI to inject a small config button beside the native random toggle, then drive a lightweight popup controller that reads and writes `PlayerPreferences.Data` and hands an effective candidate pool back to the existing random-start flow.

**Tech Stack:** BepInEx, Harmony, Unity UI (`Button`, `Toggle`, `RectTransform`, `LayoutGroup`), game-native `PlayerPreferences`, focused `net10.0` console-style test project.

---

### Task 1: Add a testable random-pool state model

**Files:**
- Create: `Game/Lobby/RandomHeroPool/RandomHeroPoolState.cs`
- Create: `Game/Lobby/RandomHeroPool/RandomHeroPoolStateFactory.cs`
- Create: `tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj`
- Create: `tests/RandomHeroPoolState.Tests/Program.cs`

**Step 1: Write the failing test**

Create `tests/RandomHeroPoolState.Tests/Program.cs` with assertions for:

```csharp
using BazaarPlusPlus.Game.Lobby.RandomHeroPool;

var unlocked = new[] { "Vanessa", "Pygmalien", "Mak" };

var defaultState = RandomHeroPoolStateFactory.Create(unlocked, savedPoolHeroIds: null);
AssertSet(defaultState.SelectedHeroIds, "Vanessa", "Pygmalien", "Mak");

var filteredState = RandomHeroPoolStateFactory.Create(
    unlocked,
    savedPoolHeroIds: new[] { "Mak", "MissingHero" }
);
AssertSet(filteredState.SelectedHeroIds, "Mak");

var fallbackState = RandomHeroPoolStateFactory.Create(
    unlocked,
    savedPoolHeroIds: new[] { "MissingHero" }
);
AssertSet(fallbackState.SelectedHeroIds, "Vanessa", "Pygmalien", "Mak");

var toggledState = filteredState.SetSelected("Pygmalien", isSelected: true);
AssertSet(toggledState.SelectedHeroIds, "Mak", "Pygmalien");

var unchangedState = toggledState.SetSelected("Mak", isSelected: false)
    .SetSelected("Pygmalien", isSelected: false);
AssertSet(unchangedState.SelectedHeroIds, "Mak");
```

**Step 2: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj
```

Expected: build failure because `RandomHeroPoolState` and `RandomHeroPoolStateFactory` do not exist yet.

**Step 3: Write minimal implementation**

Create `Game/Lobby/RandomHeroPool/RandomHeroPoolState.cs`:

```csharp
namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public sealed class RandomHeroPoolState
{
    private readonly string[] _unlockedHeroIds;
    private readonly HashSet<string> _selectedHeroIds;

    public RandomHeroPoolState(IEnumerable<string> unlockedHeroIds, IEnumerable<string> selectedHeroIds)
    {
        _unlockedHeroIds = unlockedHeroIds.Distinct(StringComparer.Ordinal).ToArray();
        _selectedHeroIds = new HashSet<string>(
            selectedHeroIds.Where(id => _unlockedHeroIds.Contains(id, StringComparer.Ordinal)),
            StringComparer.Ordinal
        );
        if (_selectedHeroIds.Count == 0)
            _selectedHeroIds.UnionWith(_unlockedHeroIds);
    }

    public IReadOnlyList<string> UnlockedHeroIds => _unlockedHeroIds;
    public IReadOnlyCollection<string> SelectedHeroIds => _selectedHeroIds;

    public bool IsSelected(string heroId) => _selectedHeroIds.Contains(heroId);

    public RandomHeroPoolState SetSelected(string heroId, bool isSelected)
    {
        var next = new HashSet<string>(_selectedHeroIds, StringComparer.Ordinal);
        if (isSelected)
            next.Add(heroId);
        else if (next.Count > 1)
            next.Remove(heroId);

        return new RandomHeroPoolState(_unlockedHeroIds, next);
    }
}
```

Create `Game/Lobby/RandomHeroPool/RandomHeroPoolStateFactory.cs`:

```csharp
namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public static class RandomHeroPoolStateFactory
{
    public static RandomHeroPoolState Create(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds
    )
    {
        return new RandomHeroPoolState(
            unlockedHeroIds,
            savedPoolHeroIds ?? unlockedHeroIds
        );
    }
}
```

**Step 4: Run test to verify it passes**

Run:

```bash
dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj
```

Expected: PASS with a confirmation line such as `RandomHeroPoolState checks passed.`

**Step 5: Commit**

```bash
git add Game/Lobby/RandomHeroPool/RandomHeroPoolState.cs Game/Lobby/RandomHeroPool/RandomHeroPoolStateFactory.cs tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj tests/RandomHeroPoolState.Tests/Program.cs
git commit -m "Add random hero pool state model"
```

### Task 2: Add preference helpers for the random-hero subset

**Files:**
- Create: `Game/Lobby/RandomHeroPool/RandomHeroPoolPreferences.cs`
- Modify: `Game/Lobby/RandomHeroPool/RandomHeroPoolStateFactory.cs`
- Test: `tests/RandomHeroPoolState.Tests/Program.cs`

**Step 1: Write the failing test**

Extend `tests/RandomHeroPoolState.Tests/Program.cs` with assertions for preference merge rules:

```csharp
var persisted = RandomHeroPoolPreferences.MergeUnlockedHeroIds(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "Vanessa" },
    newlyUnlockedHeroIds: new[] { "Mak" }
);
AssertSet(persisted, "Vanessa", "Mak");

var sanitized = RandomHeroPoolPreferences.Sanitize(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "MissingHero" }
);
AssertSet(sanitized, "Vanessa", "Pygmalien");
```

**Step 2: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj
```

Expected: build failure because `RandomHeroPoolPreferences` does not exist.

**Step 3: Write minimal implementation**

Create `Game/Lobby/RandomHeroPool/RandomHeroPoolPreferences.cs`:

```csharp
namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public static class RandomHeroPoolPreferences
{
    public static IReadOnlyCollection<string> Sanitize(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds
    )
    {
        return RandomHeroPoolStateFactory.Create(unlockedHeroIds, savedPoolHeroIds).SelectedHeroIds;
    }

    public static IReadOnlyCollection<string> MergeUnlockedHeroIds(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds,
        IEnumerable<string> newlyUnlockedHeroIds
    )
    {
        var state = RandomHeroPoolStateFactory.Create(unlockedHeroIds, savedPoolHeroIds);
        var merged = new HashSet<string>(state.SelectedHeroIds, StringComparer.Ordinal);
        merged.UnionWith(newlyUnlockedHeroIds);
        return merged;
    }
}
```

Adjust `RandomHeroPoolStateFactory` only if the tests reveal duplicated filtering logic that should be centralized.

**Step 4: Run test to verify it passes**

Run:

```bash
dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj
```

Expected: PASS with all state and preference checks green.

**Step 5: Commit**

```bash
git add Game/Lobby/RandomHeroPool/RandomHeroPoolPreferences.cs Game/Lobby/RandomHeroPool/RandomHeroPoolStateFactory.cs tests/RandomHeroPoolState.Tests/Program.cs
git commit -m "Add random hero pool preference helpers"
```

### Task 3: Inject a native-style config button and popup container into Hero Select

**Files:**
- Create: `Game/Lobby/RandomHeroPool/RandomHeroPoolPanelController.cs`
- Create: `Patches/Lobby/RandomHeroPoolPatches.cs`
- Modify: `Plugin.cs`

**Step 1: Write the failing test**

There is no reliable pure unit test for scene object injection here. Instead, capture the target layout assumptions in code comments and fail fast with runtime guards:

```csharp
if (randomHeroToggle == null)
{
    BppLog.Warn("RandomHeroPool", "Random hero toggle was unavailable; skipping pool UI injection.");
    return;
}
```

**Step 2: Run the smallest verification before implementation**

Run:

```bash
dotnet build BazaarPlusPlus.csproj
```

Expected: PASS, or if local game assemblies are not auto-detected, rerun with `-p:ManagedPath=/absolute/path/to/TheBazaar_Data/Managed`.

**Step 3: Write minimal implementation**

Create `Patches/Lobby/RandomHeroPoolPatches.cs` with a postfix on `HeroSelectButtonsView.Awake` or `HeroSelectButtonsView.RefreshButtons` that:

```csharp
[HarmonyPatch(typeof(HeroSelectButtonsView), "Awake")]
internal static class RandomHeroPoolUiPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance)
    {
        RandomHeroPoolPanelController.Attach(__instance);
    }
}
```

Create `Game/Lobby/RandomHeroPool/RandomHeroPoolPanelController.cs` to:

```csharp
public sealed class RandomHeroPoolPanelController : MonoBehaviour
{
    public static void Attach(HeroSelectButtonsView view)
    {
        // Find RandomHeroToggle via reflection or hierarchy scan.
        // Clone a nearby native button to serve as the config trigger.
        // Create a hidden popup container under the same canvas.
    }
}
```

If `Plugin.cs` needs an explicit namespace import for the new files, add it there; otherwise do not change plugin startup.

**Step 4: Run build to verify the patch compiles**

Run:

```bash
dotnet build BazaarPlusPlus.csproj
```

Expected: PASS.

**Step 5: Commit**

```bash
git add Game/Lobby/RandomHeroPool/RandomHeroPoolPanelController.cs Patches/Lobby/RandomHeroPoolPatches.cs Plugin.cs
git commit -m "Inject random hero pool config entrypoint"
```

### Task 4: Bind popup interactions to unlocked heroes and preference persistence

**Files:**
- Modify: `Game/Lobby/RandomHeroPool/RandomHeroPoolPanelController.cs`
- Modify: `Patches/Lobby/RandomHeroPoolPatches.cs`
- Modify: `Game/Lobby/RandomHeroPool/RandomHeroPoolPreferences.cs`

**Step 1: Write the failing test**

Add a focused state-driven assertion to `tests/RandomHeroPoolState.Tests/Program.cs` that mirrors the popup rule:

```csharp
var state = RandomHeroPoolStateFactory.Create(
    new[] { "Vanessa", "Pygmalien" },
    new[] { "Vanessa", "Pygmalien" }
);
var blocked = state.SetSelected("Vanessa", isSelected: false)
    .SetSelected("Pygmalien", isSelected: false);
AssertSet(blocked.SelectedHeroIds, "Pygmalien");
```

**Step 2: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj
```

Expected: FAIL if the "last hero cannot be removed" rule is not enforced correctly.

**Step 3: Write minimal implementation**

Update `RandomHeroPoolPanelController.cs` so that it:

```csharp
private void RefreshEntries(IReadOnlyList<HeroItemView> unlockedHeroes)
{
    var heroIds = unlockedHeroes.Select(hero => hero.Hero.ToString());
    _state = RandomHeroPoolStateFactory.Create(heroIds, LoadSavedHeroIds());
}

private void OnHeroToggled(string heroId, bool isSelected)
{
    var nextState = _state.SetSelected(heroId, isSelected);
    if (nextState.SelectedHeroIds.SetEquals(_state.SelectedHeroIds) && !isSelected)
    {
        ShowHint("At least one hero must remain selected.");
        RebindWithoutNotify(heroId, true);
        return;
    }

    _state = nextState;
    SaveSelectedHeroIds(_state.SelectedHeroIds);
}
```

Also add:
- `Select All` to persist all unlocked hero IDs
- `Clear` to reduce to the currently checked fallback hero instead of saving an empty set
- auto-merge of newly unlocked heroes before saving, so new unlocks enter the pool by default

**Step 4: Run tests and build**

Run:

```bash
dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj
dotnet build BazaarPlusPlus.csproj
```

Expected: both PASS.

**Step 5: Commit**

```bash
git add Game/Lobby/RandomHeroPool/RandomHeroPoolPanelController.cs Game/Lobby/RandomHeroPool/RandomHeroPoolPreferences.cs Patches/Lobby/RandomHeroPoolPatches.cs tests/RandomHeroPoolState.Tests/Program.cs
git commit -m "Bind random hero pool popup to preferences"
```

### Task 5: Route random hero selection through the configured pool

**Files:**
- Create: `Game/Lobby/RandomHeroPool/RandomHeroPoolSelector.cs`
- Modify: `Patches/Lobby/RandomHeroPoolPatches.cs`
- Test: `tests/RandomHeroPoolState.Tests/Program.cs`

**Step 1: Write the failing test**

Extend `tests/RandomHeroPoolState.Tests/Program.cs` with selector checks:

```csharp
var selector = new RandomHeroPoolSelector();
var picked = selector.SelectHero(
    candidateHeroIds: new[] { "Vanessa", "Mak" },
    randomIndex: 1
);
AssertEqual("Mak", picked);
```

**Step 2: Run test to verify it fails**

Run:

```bash
dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj
```

Expected: build failure because `RandomHeroPoolSelector` does not exist.

**Step 3: Write minimal implementation**

Create `Game/Lobby/RandomHeroPool/RandomHeroPoolSelector.cs`:

```csharp
namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public sealed class RandomHeroPoolSelector
{
    public string SelectHero(IReadOnlyList<string> candidateHeroIds, int randomIndex)
    {
        if (candidateHeroIds.Count == 0)
            throw new InvalidOperationException("Random hero pool cannot be empty.");

        return candidateHeroIds[randomIndex];
    }
}
```

Patch `HeroSelectButtonsView.SelectRandomHeroImmediate` so it:
- reads the saved pool
- intersects it with the current `_unlockedHeroes`
- falls back to all unlocked heroes if the saved pool is invalid
- maps the selected hero ID back to `HeroItemView`
- preserves the existing `_isProgrammaticSelection` guard

Example shape:

```csharp
var effectivePool = RandomHeroPoolPreferences.Sanitize(unlockedHeroIds, savedPoolHeroIds);
var randomIndex = UnityEngine.Random.Range(0, effectivePool.Count);
var selectedHeroId = _selector.SelectHero(effectivePool.ToArray(), randomIndex);
```

**Step 4: Run tests and the targeted build**

Run:

```bash
dotnet run --project tests/RandomHeroPoolState.Tests/RandomHeroPoolState.Tests.csproj
dotnet build BazaarPlusPlus.csproj
```

Expected: both PASS.

**Step 5: Commit**

```bash
git add Game/Lobby/RandomHeroPool/RandomHeroPoolSelector.cs Patches/Lobby/RandomHeroPoolPatches.cs tests/RandomHeroPoolState.Tests/Program.cs
git commit -m "Honor configured random hero pool when starting a run"
```

### Task 6: Verify end-to-end behavior in game

**Files:**
- Modify: none unless a verification gap is found

**Step 1: Build the mod**

Run:

```bash
dotnet build BazaarPlusPlus.csproj
```

Expected: PASS, or rerun with `ManagedPath` if local assembly discovery fails.

**Step 2: Manual verification in Hero Select**

Check:
- enabling random mode without opening the popup still behaves like all unlocked heroes are eligible
- the config button appears beside the native random toggle and matches the page styling closely enough
- the popup lists only unlocked heroes
- deselecting down to zero is blocked with a light hint
- newly unlocked heroes appear selected by default
- closing and reopening the game preserves the chosen subset

**Step 3: Targeted regression check**

Check:
- manually selecting a hero still disables random mode
- active runs still bypass random-mode UI behavior
- play button still saves the resulting selected hero and the game starts with that hero

**Step 4: Commit any final polish**

```bash
git add -A
git commit -m "Polish random hero pool UX"
```

