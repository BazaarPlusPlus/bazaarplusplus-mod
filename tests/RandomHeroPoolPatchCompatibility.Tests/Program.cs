using System.Reflection;

var dependencyDirectories = ResolveDependencyDirectories();
AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    var assemblyName = new AssemblyName(args.Name).Name + ".dll";
    foreach (var directory in dependencyDirectories)
    {
        var candidatePath = Path.Combine(directory, assemblyName);
        if (File.Exists(candidatePath))
            return Assembly.LoadFrom(candidatePath);
    }

    return null;
};

var pluginPath = Path.Combine(AppContext.BaseDirectory, "BazaarPlusPlus.dll");
Assert(File.Exists(pluginPath), $"Expected plugin assembly at {pluginPath}.");

var assembly = Assembly.LoadFrom(pluginPath);
var patchType = assembly.GetType(
    "BazaarPlusPlus.Patches.Lobby.RandomHeroPoolRefreshButtonsPatch",
    throwOnError: true
)!;
var postfix = patchType.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
Assert(postfix != null, "Random hero pool RefreshButtons postfix was not found.");

var parameters = postfix!.GetParameters();
Assert(
    parameters.All(parameter => parameter.Name != "__result"),
    "RefreshButtons postfix must not request __result; current game RefreshButtons returns void."
);

AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroPoolHeroItemStartPatch",
    "Postfix"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroPoolHeroItemUpdateViewPatch",
    "Prefix"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroPoolHeroItemSelectedPatch",
    "Prefix"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroPoolOnHeroPurchasedPatch",
    "Prefix"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroPoolOnHeroPurchasedPatch",
    "Finalizer"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroPoolSelectRandomHeroImmediatePatch",
    "Prefix"
);
AssertPatchMethod(assembly, "BazaarPlusPlus.Patches.Lobby.RandomHeroSkinPoolFetchPatch", "Prefix");
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroSkinPoolSetDataPatch",
    "Postfix"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroSkinPoolSetEquipStatePatch",
    "Prefix"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroSkinPoolEquipItemPatch",
    "Prefix"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroSkinPoolEquipItemPatch",
    "Postfix"
);
AssertPatchMethod(
    assembly,
    "BazaarPlusPlus.Patches.Lobby.RandomHeroSkinPoolTogglePatch",
    "Postfix"
);

var programmaticScopeType = assembly.GetType(
    "BazaarPlusPlus.Game.Lobby.RandomHeroPool.HeroProgrammaticSelectionScope",
    throwOnError: true
)!;
foreach (var methodName in new[] { "Enter", "Restore", "IsActive" })
{
    Assert(
        programmaticScopeType.GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static
        ) != null,
        $"Hero programmatic-selection scope must expose {methodName}."
    );
}

Assert(
    assembly.GetType("BazaarPlusPlus.Game.Lobby.RandomHeroPool.RandomHeroPoolPanelController")
        == null,
    "Legacy random hero panel controller must be removed."
);
Assert(
    assembly.GetType(
        "BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool.RandomHeroSkinPoolPanelController"
    ) == null,
    "Legacy random collectible panel controller must be removed."
);

var runtimeType = assembly.GetType(
    "BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool.RandomHeroSkinPoolRuntime",
    throwOnError: true
)!;
var isSupported = runtimeType.GetMethod(
    "IsSupported",
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
)!;
var collectionType = isSupported.GetParameters()[0].ParameterType;
foreach (
    var name in new[]
    {
        "HeroSkins",
        "Toys",
        "Boards",
        "Carpets",
        "CardBacks",
        "Album",
        "Stash",
        "Bank",
    }
)
{
    var value = Enum.Parse(collectionType, name);
    Assert(
        isSupported.Invoke(null, new[] { value }) is true,
        $"Random collectible pool must continue supporting {name}."
    );
}

Console.WriteLine("RandomHeroPool patch compatibility checks passed.");

static void AssertPatchMethod(Assembly assembly, string typeName, string methodName)
{
    var type = assembly.GetType(typeName, throwOnError: true)!;
    var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
    Assert(method != null, $"Expected {typeName}.{methodName} patch method.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static string[] ResolveDependencyDirectories()
{
    var gameRootCandidates = new[]
    {
        @"C:\Program Files (x86)\Steam\steamapps\common\The Bazaar",
        @"D:\Program Files (x86)\Steam\steamapps\common\The Bazaar",
        @"E:\Program Files (x86)\Steam\steamapps\common\The Bazaar",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Application Support/Steam/steamapps/common/The Bazaar"
        ),
    };

    return gameRootCandidates
        .Where(Directory.Exists)
        .SelectMany(gameRoot =>
            new[]
            {
                AppContext.BaseDirectory,
                Path.Combine(gameRoot, "TheBazaar_Data", "Managed"),
                Path.Combine(gameRoot, "TheBazaar.app", "Contents", "Resources", "Data", "Managed"),
                Path.Combine(gameRoot, "BepInEx", "core"),
                Path.Combine(gameRoot, "BepInEx", "plugins"),
            }
        )
        .Prepend(AppContext.BaseDirectory)
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
