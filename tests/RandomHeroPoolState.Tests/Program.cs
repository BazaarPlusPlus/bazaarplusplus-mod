using System.Collections;
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
AssertSet(unchangedState.SelectedHeroIds, "Pygmalien");

var clearToZeroGuardState = RandomHeroPoolStateFactory.Create(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "Vanessa", "Pygmalien" }
);
var clearToZeroBlockedState = clearToZeroGuardState.SetSelected("Vanessa", isSelected: false)
    .SetSelected("Pygmalien", isSelected: false);
AssertSet(clearToZeroBlockedState.SelectedHeroIds, "Pygmalien");

var sanitizedUnlockedState = RandomHeroPoolStateFactory.Create(
    unlockedHeroIds: new[] { "Vanessa", "", null!, "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "", null!, "Unknown", "Pygmalien" }
);
AssertSet(sanitizedUnlockedState.UnlockedHeroIds, "Vanessa", "Pygmalien");
AssertSet(sanitizedUnlockedState.SelectedHeroIds, "Pygmalien");

var noMutationThroughUnlockedView = RandomHeroPoolStateFactory.Create(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "Vanessa" }
);
var unlockedViewArray = noMutationThroughUnlockedView.UnlockedHeroIds as string[];
if (unlockedViewArray is not null)
{
    unlockedViewArray[0] = "Mutated";
}
AssertSet(noMutationThroughUnlockedView.UnlockedHeroIds, "Vanessa", "Pygmalien");

var noMutationThroughSelectedView = RandomHeroPoolStateFactory.Create(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "Vanessa" }
);
var selectedViewSet = noMutationThroughSelectedView.SelectedHeroIds as HashSet<string>;
if (selectedViewSet is not null)
{
    selectedViewSet.Add("Pygmalien");
}
AssertSet(noMutationThroughSelectedView.SelectedHeroIds, "Vanessa");

var guardedToggleState = RandomHeroPoolStateFactory.Create(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "Vanessa" }
);
var afterUnknownSelect = guardedToggleState.SetSelected("Unknown", isSelected: true);
AssertSet(afterUnknownSelect.SelectedHeroIds, "Vanessa");
var afterEmptySelect = guardedToggleState.SetSelected(string.Empty, isSelected: true);
AssertSet(afterEmptySelect.SelectedHeroIds, "Vanessa");
var afterNullSelect = guardedToggleState.SetSelected(null!, isSelected: true);
AssertSet(afterNullSelect.SelectedHeroIds, "Vanessa");

AssertThrows<ArgumentException>(
    () => RandomHeroPoolStateFactory.Create(Array.Empty<string>(), savedPoolHeroIds: null),
    "at least one unlocked hero"
);

var singlePassUnlocked = new SinglePassEnumerable("Vanessa", "Pygmalien");
var singlePassState = RandomHeroPoolStateFactory.Create(singlePassUnlocked, savedPoolHeroIds: null);
AssertSet(singlePassState.SelectedHeroIds, "Vanessa", "Pygmalien");

var singlePassSavedPool = new SinglePassEnumerable("Mak");
var singlePassSavedState = RandomHeroPoolStateFactory.Create(unlocked, singlePassSavedPool);
AssertSet(singlePassSavedState.SelectedHeroIds, "Mak");

var persisted = RandomHeroPoolPreferences.MergeUnlockedHeroIds(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "Vanessa" },
    newlyUnlockedHeroIds: new[] { "Mak" }
);
AssertSet(persisted, "Vanessa", "Mak");

var singlePassNewlyUnlocked = new SinglePassEnumerable("Mak");
var mergedWithSinglePassNewlyUnlocked = RandomHeroPoolPreferences.MergeUnlockedHeroIds(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "Vanessa" },
    newlyUnlockedHeroIds: singlePassNewlyUnlocked
);
AssertSet(mergedWithSinglePassNewlyUnlocked, "Vanessa", "Mak");

var sanitized = RandomHeroPoolPreferences.Sanitize(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "MissingHero" }
);
AssertSet(sanitized, "Vanessa", "Pygmalien");

var mergedWithKnownUnlocked = RandomHeroPoolPreferences.MergeWithKnownUnlockedHeroIds(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien", "Mak" },
    savedPoolHeroIds: new[] { "Vanessa" },
    knownUnlockedHeroIds: new[] { "Vanessa", "Pygmalien" }
);
AssertSet(mergedWithKnownUnlocked, "Vanessa", "Mak");

var sanitizedSnapshot = RandomHeroPoolPreferences.Sanitize(
    unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
    savedPoolHeroIds: new[] { "Vanessa" }
);
MutateSnapshotOrThrow(sanitizedSnapshot, "Mutated");
AssertSet(
    RandomHeroPoolPreferences.Sanitize(
        unlockedHeroIds: new[] { "Vanessa", "Pygmalien" },
        savedPoolHeroIds: new[] { "Vanessa" }
    ),
    "Vanessa"
);

var selector = new RandomHeroPoolSelector();
var picked = selector.SelectHero(candidateHeroIds: new[] { "Vanessa", "Mak" }, randomIndex: 1);
AssertEqual("Mak", picked);

Console.WriteLine("RandomHeroPoolState checks passed.");

static void AssertSet(IEnumerable<string> actual, params string[] expected)
{
    var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
    var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
    if (actualSet.SetEquals(expectedSet))
    {
        return;
    }

    var actualText = string.Join(", ", actualSet.OrderBy(x => x, StringComparer.Ordinal));
    var expectedText = string.Join(", ", expectedSet.OrderBy(x => x, StringComparer.Ordinal));
    throw new InvalidOperationException(
        $"Set assertion failed. Expected: [{expectedText}] Actual: [{actualText}]"
    );
}

static void AssertThrows<TException>(Action action, string expectedMessageFragment)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException ex)
    {
        Assert(
            ex.Message.Contains(expectedMessageFragment, StringComparison.OrdinalIgnoreCase),
            $"Expected {typeof(TException).Name} message to contain '{expectedMessageFragment}', but was '{ex.Message}'."
        );
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name} but no exception was thrown."
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual(string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Value assertion failed. Expected: '{expected}' Actual: '{actual}'"
        );
    }
}

static void MutateSnapshotOrThrow(IReadOnlyCollection<string> snapshot, string replacementValue)
{
    if (snapshot is not string[] snapshotArray)
    {
        throw new InvalidOperationException(
            $"Expected snapshot collection to be backed by string[] but was {snapshot.GetType().FullName}."
        );
    }

    if (snapshotArray.Length == 0)
    {
        throw new InvalidOperationException("Expected snapshot collection to contain at least one item.");
    }

    snapshotArray[0] = replacementValue;
}

file sealed class SinglePassEnumerable : IEnumerable<string>
{
    private readonly string[] _values;
    private bool _enumerated;

    public SinglePassEnumerable(params string[] values)
    {
        _values = values;
    }

    public IEnumerator<string> GetEnumerator()
    {
        if (_enumerated)
        {
            throw new InvalidOperationException("SinglePassEnumerable can only be enumerated once.");
        }

        _enumerated = true;
        return ((IEnumerable<string>)_values).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
