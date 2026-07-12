using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Game.Lobby.RandomHeroPool;
using BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

RandomUserClickEditsAndPersistsPool();
NativeRoutesNeverEditOrPersistPool();
PatchRoutingDistinguishesProgrammaticCallsAndPoolEdits();
ModeSelectsTheVisualSource();
LastMemberRemovalIsHandledWithoutPersistence();
PersistedCollectiblePoolRoundTrips();

Console.WriteLine("Native random-pool interaction checks passed.");

static void RandomUserClickEditsAndPersistsPool()
{
    var persisted = new List<IReadOnlyCollection<string>>();
    var coordinator = CreateHeroCoordinator(new[] { "Vanessa" }, persisted);

    var route = coordinator.HandleClick(
        poolModeEnabled: true,
        eligibleForPool: true,
        NativePoolInteractionOrigin.User,
        "Mak"
    );

    Assert(route == NativePoolInteractionRoute.PoolEdit, "Random user click must edit the pool.");
    Assert(coordinator.State.IsSelected("Mak"), "Random user click must toggle membership.");
    Assert(persisted.Count == 1, "Random user click must persist exactly once.");
}

static void NativeRoutesNeverEditOrPersistPool()
{
    var persisted = new List<IReadOnlyCollection<string>>();
    var coordinator = CreateHeroCoordinator(new[] { "Vanessa" }, persisted);

    var normalRoute = coordinator.HandleClick(
        poolModeEnabled: false,
        eligibleForPool: true,
        NativePoolInteractionOrigin.User,
        "Mak"
    );
    var programmaticRoute = coordinator.HandleClick(
        poolModeEnabled: true,
        eligibleForPool: true,
        NativePoolInteractionOrigin.Programmatic,
        "Mak"
    );
    var lockedRoute = coordinator.HandleClick(
        poolModeEnabled: true,
        eligibleForPool: false,
        NativePoolInteractionOrigin.User,
        "Mak"
    );

    Assert(
        normalRoute == NativePoolInteractionRoute.NativeAction,
        "Normal click must stay native."
    );
    Assert(
        programmaticRoute == NativePoolInteractionRoute.NativeAction,
        "Programmatic selection must stay native."
    );
    Assert(
        lockedRoute == NativePoolInteractionRoute.NativeAction,
        "Locked hero click must stay native."
    );
    Assert(!coordinator.State.IsSelected("Mak"), "Native routes must not edit the pool.");
    Assert(persisted.Count == 0, "Native routes must not persist the pool.");
}

static void PatchRoutingDistinguishesProgrammaticCallsAndPoolEdits()
{
    Assert(
        NativePoolInteractionRouting.ResolveOrigin(
            programmaticScopeActive: false,
            nativeProgrammaticSelection: false
        ) == NativePoolInteractionOrigin.User,
        "An unscoped native-card callback must be treated as a user click."
    );
    Assert(
        NativePoolInteractionRouting.ResolveOrigin(
            programmaticScopeActive: true,
            nativeProgrammaticSelection: false
        ) == NativePoolInteractionOrigin.Programmatic,
        "A purchase selection scope must stay on the native route."
    );
    Assert(
        NativePoolInteractionRouting.ResolveOrigin(
            programmaticScopeActive: false,
            nativeProgrammaticSelection: true
        ) == NativePoolInteractionOrigin.Programmatic,
        "The game's final random selection flag must stay on the native route."
    );
    Assert(
        !NativePoolInteractionRouting.ShouldRunNativeAction(
            NativePoolInteractionRoute.PoolEdit
        ),
        "A random-pool card edit must suppress the native select/equip action."
    );
    Assert(
        NativePoolInteractionRouting.ShouldRunNativeAction(
            NativePoolInteractionRoute.NativeAction
        ),
        "Normal and programmatic callbacks must continue into the native action."
    );
}

static void ModeSelectsTheVisualSource()
{
    var persisted = new List<IReadOnlyCollection<string>>();
    var coordinator = CreateHeroCoordinator(new[] { "Mak" }, persisted);

    Assert(
        coordinator.IsVisuallySelected(poolModeEnabled: true, "Mak", nativeSelected: false),
        "Random mode must render pool membership."
    );
    Assert(
        !coordinator.IsVisuallySelected(poolModeEnabled: true, "Vanessa", nativeSelected: true),
        "Random mode must ignore the actual native selection."
    );
    Assert(
        coordinator.IsVisuallySelected(poolModeEnabled: false, "Vanessa", nativeSelected: true),
        "Normal mode must restore the actual native selection."
    );
    Assert(
        !coordinator.IsVisuallySelected(poolModeEnabled: false, "Mak", nativeSelected: false),
        "Normal mode must stop rendering pool membership."
    );
}

static void LastMemberRemovalIsHandledWithoutPersistence()
{
    var persisted = new List<IReadOnlyCollection<string>>();
    var coordinator = CreateHeroCoordinator(new[] { "Vanessa" }, persisted);

    var route = coordinator.HandleClick(
        poolModeEnabled: true,
        eligibleForPool: true,
        NativePoolInteractionOrigin.User,
        "Vanessa"
    );

    Assert(route == NativePoolInteractionRoute.PoolEdit, "Last-member click is still a pool edit.");
    Assert(coordinator.State.IsSelected("Vanessa"), "Last pool member must remain selected.");
    Assert(persisted.Count == 0, "Blocked last-member removal must not persist a fake change.");
}

static void PersistedCollectiblePoolRoundTrips()
{
    IReadOnlyCollection<string>? saved = null;
    var state = RandomHeroSkinPoolStateFactory.Create(
        new[] { "default", "skin-a", "skin-b" },
        savedSelectedSkinIds: new[] { "default", "skin-a" }
    );
    var coordinator = new NativePoolInteractionCoordinator<RandomHeroSkinPoolState>(
        state,
        (current, id) => current.IsSelected(id),
        (current, id, isSelected) => current.SetSelected(id, isSelected),
        current => saved = current.SelectedSkinIds
    );

    coordinator.HandleClick(
        poolModeEnabled: true,
        eligibleForPool: true,
        NativePoolInteractionOrigin.User,
        "skin-b"
    );
    coordinator.HandleClick(
        poolModeEnabled: true,
        eligibleForPool: true,
        NativePoolInteractionOrigin.User,
        "default"
    );

    Assert(saved != null, "Collectible pool edit must persist a selected subset.");
    var restored = RandomHeroSkinPoolStateFactory.Create(
        new[] { "default", "skin-a", "skin-b", "new-skin" },
        saved
    );
    Assert(!restored.IsSelected("default"), "Removed collectible must stay removed after reload.");
    Assert(restored.IsSelected("skin-a"), "Existing selected collectible must survive reload.");
    Assert(restored.IsSelected("skin-b"), "Added collectible must survive reload.");
    Assert(!restored.IsSelected("new-skin"), "New collectible must not join a curated pool.");
}

static NativePoolInteractionCoordinator<RandomHeroPoolState> CreateHeroCoordinator(
    IEnumerable<string> selected,
    ICollection<IReadOnlyCollection<string>> persisted
)
{
    var state = RandomHeroPoolStateFactory.Create(
        new[] { "Vanessa", "Mak" },
        savedPoolHeroIds: selected
    );
    return new NativePoolInteractionCoordinator<RandomHeroPoolState>(
        state,
        (current, id) => current.IsSelected(id),
        (current, id, isSelected) => current.SetSelected(id, isSelected),
        current => persisted.Add(current.SelectedHeroIds)
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
