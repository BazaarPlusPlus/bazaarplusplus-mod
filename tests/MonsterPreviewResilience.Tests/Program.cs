using BazaarPlusPlus;

TestPreviewCardSpecFilter();
TestPreviewRenderGenerationGate();
TestMonsterPreviewBoardSupportsOverflowSkills();
TestRecoverableLocalCatalogCache();
TestRecoverableItemAttrCache();
TestPreviewFactoriesCatchAsyncInitializationFailures();
TestMonsterPreviewWarmupIsMounted();

Console.WriteLine("MonsterPreviewResilience checks passed.");

static void TestPreviewCardSpecFilter()
{
    var knownTemplate = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var specs = new List<PreviewCardSpec>
    {
        new() { TemplateId = knownTemplate.ToString(), Tier = 2, Size = 2, SourceName = "Known" },
        new() { TemplateId = "not-a-guid", Tier = 1, Size = 1, SourceName = "BadGuid" },
        new() { TemplateId = "22222222-2222-2222-2222-222222222222", Tier = 3, Size = 3, SourceName = "Missing" },
    };

    var filtered = PreviewCardSpecFilter.Filter(specs, templateId => templateId == knownTemplate);

    Assert(filtered.Count == 1, "Only locally renderable preview specs should remain.");
    Assert(filtered[0].TemplateId == knownTemplate.ToString(), "Known template should be preserved.");
    Assert(filtered[0].SourceName == "Known", "Known spec data should be preserved.");
}

static void TestPreviewRenderGenerationGate()
{
    var gate = new PreviewRenderGenerationGate();

    var firstGeneration = gate.BeginRender(visible: true);
    Assert(!gate.ShouldCancel(firstGeneration), "Active visible render should remain valid.");

    var secondGeneration = gate.BeginRender(visible: true);
    Assert(gate.ShouldCancel(firstGeneration), "Older render generation should be cancelled by a newer render.");
    Assert(!gate.ShouldCancel(secondGeneration), "Newest visible render should remain valid.");

    gate.InvalidateForHide();
    Assert(gate.ShouldCancel(secondGeneration), "Hide should cancel the active render generation.");

    var thirdGeneration = gate.BeginRender(visible: true);
    Assert(!gate.ShouldCancel(thirdGeneration), "A new visible render after hide should become valid again.");

    gate.MarkDisposed();
    Assert(gate.ShouldCancel(thirdGeneration), "Dispose should cancel all generations.");
}

static void TestMonsterPreviewBoardSupportsOverflowSkills()
{
    var sourcePath = Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Game/MonsterPreview/GameObjectFactory/MonsterPreviewBoard.cs"
        )
    );
    var source = File.ReadAllText(sourcePath);

    Assert(
        source.Contains("private const int DefaultSkillSlotCount = 3;", StringComparison.Ordinal),
        "MonsterPreviewBoard should keep a minimum skill slot baseline while allowing expansion."
    );
    Assert(
        source.Contains("EnsureSkillSlots(skillCards.Count);", StringComparison.Ordinal),
        "MonsterPreviewBoard should expand skill slots before rebuilding preview skills."
    );
    Assert(
        source.Contains("private int _activeSkillSlotCount = DefaultSkillSlotCount;", StringComparison.Ordinal),
        "MonsterPreviewBoard should track the active skill slot count separately from allocated slot objects."
    );
    Assert(
        source.Contains("_activeSkillSlotCount = Mathf.Max(DefaultSkillSlotCount, skillCards.Count);", StringComparison.Ordinal),
        "MonsterPreviewBoard should reset active skill slots to the current preview size."
    );
    Assert(
        source.Contains("var slotCount = Mathf.Max(DefaultSkillSlotCount, _activeSkillSlotCount);", StringComparison.Ordinal),
        "MonsterPreviewBoard should size skill layout from the active slot count instead of all allocated slots."
    );
}

static void TestRecoverableLocalCatalogCache()
{
    var source = ReadRepoFile("Data/LocalCardTemplateCatalog.cs");

    Assert(
        !source.Contains("Lazy<HashSet<Guid>>", StringComparison.Ordinal),
        "LocalCardTemplateCatalog should not permanently cache initialization state via Lazy<HashSet<Guid>>."
    );
    Assert(
        source.Contains("internal static bool Warm()", StringComparison.Ordinal),
        "LocalCardTemplateCatalog should expose a warmup entry point."
    );
    Assert(
        source.Contains("internal static void ResetForTests()", StringComparison.Ordinal),
        "LocalCardTemplateCatalog should support explicit cache reset for resilience tests."
    );
    Assert(
        source.Contains("private static bool EnsureLoaded()", StringComparison.Ordinal),
        "LocalCardTemplateCatalog should gate access through a reloadable EnsureLoaded path."
    );
    Assert(
        source.Contains("catch (Exception ex)", StringComparison.Ordinal),
        "LocalCardTemplateCatalog should catch file and parse failures instead of faulting permanently."
    );
}

static void TestRecoverableItemAttrCache()
{
    var source = ReadRepoFile("Data/ItemAttr.cs");

    Assert(
        !source.Contains("Lazy<IReadOnlyDictionary<Guid, CardAttributes>>", StringComparison.Ordinal),
        "ItemAttr should not permanently cache initialization state via Lazy<T>."
    );
    Assert(
        source.Contains("internal static bool Warm()", StringComparison.Ordinal),
        "ItemAttr should expose a warmup entry point."
    );
    Assert(
        source.Contains("internal static void ResetForTests()", StringComparison.Ordinal),
        "ItemAttr should support explicit cache reset for resilience tests."
    );
    Assert(
        source.Contains("private static bool EnsureLoaded()", StringComparison.Ordinal),
        "ItemAttr should gate access through a reloadable EnsureLoaded path."
    );
    Assert(
        source.Contains("catch (Exception ex)", StringComparison.Ordinal),
        "ItemAttr should catch file and parse failures instead of faulting permanently."
    );
}

static void TestPreviewFactoriesCatchAsyncInitializationFailures()
{
    var itemFactorySource = ReadRepoFile("Game/MonsterPreview/GameObjectFactory/MonsterPreviewItemCardFactory.cs");
    var skillFactorySource = ReadRepoFile("Game/MonsterPreview/GameObjectFactory/MonsterPreviewSkillCardFactory.cs");

    Assert(
        itemFactorySource.Contains("catch (Exception ex)", StringComparison.Ordinal)
            && itemFactorySource.Contains("CreateCardAsync failed", StringComparison.Ordinal),
        "MonsterPreviewItemCardFactory should catch async initialization failures and log them."
    );
    Assert(
        skillFactorySource.Contains("catch (Exception ex)", StringComparison.Ordinal)
            && skillFactorySource.Contains("CreateCardAsync failed", StringComparison.Ordinal),
        "MonsterPreviewSkillCardFactory should catch async initialization failures and log them."
    );
}

static void TestMonsterPreviewWarmupIsMounted()
{
    var pluginSource = ReadRepoFile("Plugin.cs");
    var warmupSource = ReadRepoFile("Game/MonsterPreview/MonsterPreviewWarmupController.cs");

    Assert(
        pluginSource.Contains("gameObject.AddComponent<MonsterPreviewWarmupController>();", StringComparison.Ordinal),
        "Plugin should mount MonsterPreviewWarmupController so first-open work can be prewarmed."
    );
    Assert(
        warmupSource.Contains("LocalCardTemplateCatalog.Warm()", StringComparison.Ordinal)
            && warmupSource.Contains("ItemAttr.Warm()", StringComparison.Ordinal)
            && warmupSource.Contains("Data.GetStatic()", StringComparison.Ordinal),
        "MonsterPreviewWarmupController should warm the local template catalog, attribute cache, and static data."
    );
}

static string ReadRepoFile(string relativePath)
{
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
    return File.ReadAllText(path);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
