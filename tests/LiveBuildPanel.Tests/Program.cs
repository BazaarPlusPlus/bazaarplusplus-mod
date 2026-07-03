using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Localization;

TestOverlaySortingLayersKeepNativeCardsBetweenPanelAndForeground();
TestSupporterAttributionCountWithinRailCap();
TestCandidateToggleUsesTemplateId();
TestCandidatePruneKeepsSelectableRowsOnly();
TestRowVmTogglePolicyComesFromBoardType();
TestSlotChromeGeometryMatchesTenSlotContract();
TestRefreshFinalBuildsTextsAreAtlasWarmed();
TestNoRunRowsSuppressEmptyText();
TestActiveRunRowsSuppressVisibleEmptyTextAndKeepSpecificTooltips();

Console.WriteLine("LiveBuildPanel checks passed.");

static void TestOverlaySortingLayersKeepNativeCardsBetweenPanelAndForeground()
{
    Assert(
        BppOverlaySorting.PanelUiToolkit < BppOverlaySorting.NativeCardPreview,
        "Native cards must render above the main UI Toolkit panel."
    );
    Assert(
        BppOverlaySorting.NativeCardPreview < BppOverlaySorting.PanelForeground,
        "Foreground markers must render above native cards."
    );
}

static void TestSupporterAttributionCountWithinRailCap()
{
    RegisterPluginReflectionAssemblyResolution();
    var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "BazaarPlusPlus.dll"));
    var panelType = assembly.GetType("BazaarPlusPlus.Game.LiveBuildPanel.LiveBuildPanel");
    Assert(
        panelType != null,
        $"Plugin assembly should contain LiveBuildPanel: {assembly.FullName}."
    );

    var field = panelType!.GetField(
        "SupporterAttributionCount",
        BindingFlags.NonPublic | BindingFlags.Static
    );
    Assert(field != null, "LiveBuildPanel should keep supporter attribution count named.");
    var count = (int)field!.GetRawConstantValue()!;
    Assert(
        count >= 1 && count <= 4,
        $"LiveBuildPanel should request 1-4 supporters (within the attribution row cap), got {count}."
    );
}

static void RegisterPluginReflectionAssemblyResolution()
{
    AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
    {
        var name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var root in ManagedAssemblySearchRoots())
        {
            var candidate = Path.Combine(root, $"{name}.dll");
            if (File.Exists(candidate))
                return Assembly.LoadFrom(candidate);
        }

        return null;
    };
}

static IEnumerable<string> ManagedAssemblySearchRoots()
{
    yield return AppContext.BaseDirectory;
    yield return @"C:\Program Files (x86)\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed";
    yield return @"C:\Program Files\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed";
    yield return @"D:\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed";
    yield return @"E:\Steam\steamapps\common\The Bazaar\TheBazaar_Data\Managed";
    yield return MacSteamManagedPath();
}

static string MacSteamManagedPath() =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"
    );

static void TestCandidateToggleUsesTemplateId()
{
    var state = new LiveBuildCandidateState();
    var templateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    Assert(state.Toggle(templateId), "First toggle should change state.");
    Assert(state.Contains(templateId), "Template id should be selected after first toggle.");
    Assert(state.Toggle(templateId), "Second toggle should change state.");
    Assert(!state.Contains(templateId), "Template id should be removed after second toggle.");
}

static void TestCandidatePruneKeepsSelectableRowsOnly()
{
    var state = new LiveBuildCandidateState();
    var selectableTemplate = Guid.Parse("22222222-2222-2222-2222-222222222222");
    var referenceTemplate = Guid.Parse("33333333-3333-3333-3333-333333333333");
    state.Toggle(selectableTemplate);
    state.Toggle(referenceTemplate);

    state.PruneToSelectableRows([
        Board(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer, selectableTemplate),
        Board(BppItemBoardId.FinalBuild, BppItemBoardType.Reference, referenceTemplate),
    ]);

    Assert(state.Contains(selectableTemplate), "Selectable row candidate should survive prune.");
    Assert(!state.Contains(referenceTemplate), "Reference row candidate should be pruned.");
}

static void TestRowVmTogglePolicyComesFromBoardType()
{
    Assert(
        new LiveItemBoardRowVm(
            Board(BppItemBoardId.FinalBuild, BppItemBoardType.Reference, Guid.NewGuid()),
            "final",
            "empty"
        ).CanToggleCandidates == false,
        "Final build row should not expose candidate hit targets."
    );
    Assert(
        new LiveItemBoardRowVm(
            Board(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop, Guid.NewGuid()),
            "shop",
            "empty"
        ).CanToggleCandidates,
        "Shop row should expose candidate hit targets."
    );
}

static void TestSlotChromeGeometryMatchesTenSlotContract()
{
    var hitPercent = ItemBoardSlotGridGeometry.ResolveOccupiedRect(100f, 100f, 3, 2, 0f, 0f);
    var markerPixels = ItemBoardSlotGridGeometry.ResolveOccupiedRect(1000f, 180f, 3, 2, 0f, 4f);

    Assert(hitPercent.X == 30f, "Hit target should start at the socket's 10-slot percent.");
    Assert(hitPercent.Width == 20f, "Hit target should span the card's display slots.");
    Assert(markerPixels.X == 300f, "Marker should use the same left socket in pixels.");
    Assert(markerPixels.Width == 200f, "Marker should use the same occupied span in pixels.");
    Assert(markerPixels.Y == 4f, "Marker should apply only its vertical chrome inset.");
    Assert(markerPixels.Height == 172f, "Marker height should preserve the vertical chrome inset.");
}

// CJK glyphs only render if they were in the font-atlas warm-up sample on first open; every
// refresh-flow string the rail can show must therefore be part of FontAtlasSample().
static void TestRefreshFinalBuildsTextsAreAtlasWarmed()
{
    L.Install(
        new FixedLanguageProvider("zh-CN"),
        new FixedLocaleModeProvider(BppChineseLocaleMode.Mainland)
    );

    Assert(
        LiveBuildPanelText.RefreshFinalBuilds() == "拉取阵容",
        "zh-CN pull-builds button copy should be 拉取阵容."
    );

    var sampleSummary = new TenWinCorpusSummary(
        new DateTimeOffset(2034, 5, 16, 7, 28, 9, TimeSpan.Zero),
        1234567890,
        1234567890,
        [
            new TenWinHeroBuildCount("Vanessa", 1234567890),
            new TenWinHeroBuildCount("Dooley", 987654321),
        ]
    );
    var sample = LiveBuildPanelText.FontAtlasSample();
    foreach (
        var text in new[]
        {
            LiveBuildPanelText.CorpusCardTitle(),
            LiveBuildPanelText.ResultCardTitle(),
            LiveBuildPanelText.RefreshFinalBuilds(),
            LiveBuildPanelText.Working(),
            LiveBuildPanelText.RefreshingFinalBuilds(),
            LiveBuildPanelText.CorpusEmpty(),
            "✓",
            "VAN",
            "更新于",
            LiveBuildPanelText.CorpusSummaryTooltip(sampleSummary),
            LiveBuildPanelText.FinalBuildRefreshFailed(LiveBuildPanelText.Unknown()),
        }
    )
    {
        Assert(
            sample.Contains(text, StringComparison.Ordinal),
            $"FontAtlasSample must include refresh copy '{text}' for CJK glyph warm-up."
        );
    }

    var twoHoursOld = new TenWinCorpusSummary(
        new DateTimeOffset(2034, 5, 16, 5, 28, 9, TimeSpan.Zero),
        1240,
        7
    );
    var freshness = LiveBuildPanelText.CorpusFreshnessLine(
        twoHoursOld,
        new DateTimeOffset(2034, 5, 16, 7, 28, 9, TimeSpan.Zero)
    );
    Assert(
        freshness.Contains("2 小时前", StringComparison.Ordinal),
        $"zh-CN freshness line should bucket a 2h-old corpus as '2 小时前', got '{freshness}'."
    );
}

static void TestNoRunRowsSuppressEmptyText()
{
    L.Install(
        new FixedLanguageProvider("en"),
        new FixedLocaleModeProvider(BppChineseLocaleMode.Mainland)
    );

    var rows = RowsById(
        new LiveBuildPanelSnapshot
        {
            Shop = EmptyBoard(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop),
            Board = EmptyBoard(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer),
            Stash = EmptyBoard(BppItemBoardId.LiveStash, BppItemBoardType.SelectableContainer),
        }
    );

    foreach (
        var id in new[]
        {
            BppItemBoardId.LiveShop,
            BppItemBoardId.LiveBoard,
            BppItemBoardId.LiveStash,
        }
    )
    {
        Assert(
            string.IsNullOrEmpty(rows[id].EmptyText),
            $"{id} no-run empty text should be suppressed."
        );
        Assert(
            string.IsNullOrEmpty(rows[id].EmptyTooltip),
            $"{id} no-run empty tooltip should be suppressed."
        );
    }
}

static void TestActiveRunRowsSuppressVisibleEmptyTextAndKeepSpecificTooltips()
{
    L.Install(
        new FixedLanguageProvider("en"),
        new FixedLocaleModeProvider(BppChineseLocaleMode.Mainland)
    );

    var rows = RowsById(
        new LiveBuildPanelSnapshot
        {
            Hero = EHero.Vanessa,
            Shop = EmptyBoard(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop),
            Board = EmptyBoard(BppItemBoardId.LiveBoard, BppItemBoardType.SelectableContainer),
            Stash = EmptyBoard(BppItemBoardId.LiveStash, BppItemBoardType.SelectableContainer),
        }
    );

    Assert(
        string.IsNullOrEmpty(rows[BppItemBoardId.LiveShop].EmptyText),
        "Active empty shop should not render visible empty text."
    );
    Assert(
        string.IsNullOrEmpty(rows[BppItemBoardId.LiveBoard].EmptyText),
        "Active empty board should not render visible empty text."
    );
    Assert(
        string.IsNullOrEmpty(rows[BppItemBoardId.LiveStash].EmptyText),
        "Active empty stash should not render visible empty text."
    );
    Assert(
        rows[BppItemBoardId.LiveShop].EmptyTooltip == LiveBuildPanelText.EmptyShop(),
        "Active empty shop tooltip should preserve the shop-specific detail."
    );
    Assert(
        rows[BppItemBoardId.LiveBoard].EmptyTooltip == LiveBuildPanelText.EmptyBoard(),
        "Active empty board tooltip should preserve the board-specific detail."
    );
    Assert(
        rows[BppItemBoardId.LiveStash].EmptyTooltip == LiveBuildPanelText.EmptyStash(),
        "Active empty stash tooltip should preserve the stash-specific detail."
    );
}

static BppItemBoard Board(BppItemBoardId id, BppItemBoardType type, Guid templateId) =>
    new(id, type, [new BppItemBoardCard { TemplateId = templateId, Size = ECardSize.Small }]);

static BppItemBoard EmptyBoard(BppItemBoardId id, BppItemBoardType type) =>
    new(id, type, Array.Empty<BppItemBoardCard>());

static Dictionary<BppItemBoardId, LiveItemBoardRowVm> RowsById(LiveBuildPanelSnapshot snapshot) =>
    snapshot.Rows.ToDictionary(row => row.Board.Id);

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class FixedLanguageProvider(string languageCode) : ILanguageProvider
{
    public string CurrentLanguageCode => languageCode;
}

internal sealed class FixedLocaleModeProvider(BppChineseLocaleMode mode) : ILocaleModeProvider
{
    public BppChineseLocaleMode CurrentMode => mode;
}
