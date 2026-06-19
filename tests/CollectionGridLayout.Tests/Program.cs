using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;

// --- Skill grid: SkillColumns-wide square array, ceil(count / SkillColumns) shelves ---
var skills = Make(ECardType.Skill, 19, _ => ECardSize.Medium);
var skillLayout = CollectionGridLayout.Build(skills, CollectionTabKind.Skills);

AssertEqual(19, skillLayout.Count, "Skill layout keeps every card.");
AssertEqual(7, skillLayout.Columns, "Skill grid is 7 columns.");
AssertEqual(1, skillLayout.ShelfHeightUnits, "Skill shelves are one unit tall.");
AssertEqual(3, skillLayout.ShelfCount, "19 skills pack into ceil(19/7) = 3 shelves.");
AssertEqual(3, skillLayout.TotalRowUnits, "Skill total row-units == shelf count.");

AssertCell(skillLayout.CellAt(0), 0, 0, 1, "First skill is col0 / shelf0.");
AssertCell(skillLayout.CellAt(6), 6, 0, 1, "Seventh skill fills the first shelf.");
AssertCell(skillLayout.CellAt(7), 0, 1, 1, "Eighth skill wraps to col0 / shelf1.");
AssertCell(skillLayout.CellAt(18), 4, 2, 1, "Last skill is col4 / shelf2.");

AssertShelf(skillLayout.ShelfAt(0), 0, 6, "Skill shelf 0 covers indices 0..6.");
AssertShelf(skillLayout.ShelfAt(2), 14, 18, "Skill shelf 2 covers the tail 14..18.");

// --- Item grid: span-aware shelf packing (small 1, medium 2, large 3; all 2 units tall) ---
var trio = new[] { Item(ECardSize.Small), Item(ECardSize.Small), Item(ECardSize.Medium) };
var trioLayout = CollectionGridLayout.Build(trio, CollectionTabKind.Items);

AssertEqual(1, trioLayout.ShelfCount, "[S,S,M] fits one shelf (1+1+2 = 4 <= 10 units).");
AssertEqual(2, trioLayout.ShelfHeightUnits, "Item shelves are two units tall.");
AssertEqual(2, trioLayout.TotalRowUnits, "One item shelf == 2 row-units.");
AssertCell(trioLayout.CellAt(0), 0, 0, 1, "Small item spans 1 unit at col0.");
AssertCell(trioLayout.CellAt(1), 1, 0, 1, "Second small item at col1.");
AssertCell(trioLayout.CellAt(2), 2, 0, 2, "Medium item spans 2 units at col2.");

// Wrap: four mediums take 8 of 10 columns, the large (3) can't fit the last 2 so it wraps.
var wrap = new[]
{
    Item(ECardSize.Medium),
    Item(ECardSize.Medium),
    Item(ECardSize.Medium),
    Item(ECardSize.Medium),
    Item(ECardSize.Large),
};
var wrapLayout = CollectionGridLayout.Build(wrap, CollectionTabKind.Items);
AssertEqual(2, wrapLayout.ShelfCount, "Large wraps onto a second shelf.");
AssertCell(wrapLayout.CellAt(3), 6, 0, 2, "Fourth medium ends the first shelf at col6.");
AssertCell(wrapLayout.CellAt(4), 0, 1, 3, "Large wraps to col0 / shelf1 spanning 3.");
AssertShelf(wrapLayout.ShelfAt(0), 0, 3, "Item shelf 0 covers the four mediums.");
AssertShelf(wrapLayout.ShelfAt(1), 4, 4, "Item shelf 1 holds the wrapped large.");

// Exact fit: three larges (3 * 3 = 9 <= 10) share a shelf; a fourth wraps.
var twoLarge = CollectionGridLayout.Build(
    new[] { Item(ECardSize.Large), Item(ECardSize.Large) },
    CollectionTabKind.Items
);
AssertEqual(1, twoLarge.ShelfCount, "Two larges share one shelf (6 <= 10).");
AssertCell(twoLarge.CellAt(1), 3, 0, 3, "Second large sits at col3.");

var threeLarge = CollectionGridLayout.Build(
    new[] { Item(ECardSize.Large), Item(ECardSize.Large), Item(ECardSize.Large) },
    CollectionTabKind.Items
);
AssertEqual(1, threeLarge.ShelfCount, "Three larges share one shelf (9 <= 10).");
AssertCell(threeLarge.CellAt(2), 6, 0, 3, "Third large sits at col6.");

var fourLarge = CollectionGridLayout.Build(
    new[]
    {
        Item(ECardSize.Large),
        Item(ECardSize.Large),
        Item(ECardSize.Large),
        Item(ECardSize.Large),
    },
    CollectionTabKind.Items
);
AssertEqual(2, fourLarge.ShelfCount, "Fourth large wraps (9 + 3 > 10).");
AssertCell(fourLarge.CellAt(3), 0, 1, 3, "Fourth large wraps to col0 / shelf1.");

// --- Pixelization (unit = 100, gap = 10, originX = 18, originY = 18; step = 110) ---
AssertRect(
    skillLayout.ContentRectFor(9, 100f, 10f, 18f, 18f),
    238f,
    128f,
    100f,
    100f,
    "Skill col2/shelf1 → (238,128,100,100)."
);
AssertRect(
    trioLayout.ContentRectFor(2, 100f, 10f, 18f, 18f),
    238f,
    18f,
    210f,
    210f,
    "Item medium col2/shelf0 → (238,18,210,210)."
);
AssertRect(
    wrapLayout.ContentRectFor(4, 100f, 10f, 18f, 18f),
    18f,
    238f,
    320f,
    210f,
    "Item large col0/shelf1 → (18,238,320,210)."
);

AssertApprox(440f, wrapLayout.ContentHeight(100f, 10f), "Two item shelves → 4 * 110 = 440 tall.");
AssertApprox(
    330f,
    skillLayout.ContentHeight(100f, 10f),
    "Three skill shelves → 3 * 110 = 330 tall."
);
AssertApprox(220f, wrapLayout.ShelfPitch(100f, 10f), "Item shelf pitch is 2 * 110.");
AssertApprox(110f, skillLayout.ShelfPitch(100f, 10f), "Skill shelf pitch is 1 * 110.");

// --- Pixelization keeps the grid envelope stable across Item / Skill tabs ---
var wideViewport = 2600f;
var itemPixels = CollectionGridPixelization.ForViewport(
    wideViewport,
    CollectionGridConstants.ItemColumns,
    CollectionGridConstants.ItemMaxUnitWidth
);
var skillPixels = CollectionGridPixelization.ForViewport(
    wideViewport,
    CollectionGridConstants.SkillColumns,
    CollectionGridConstants.SkillMaxUnitWidth
);

AssertApprox(
    itemPixels.GridWidth,
    skillPixels.GridWidth,
    "Item and Skill grids should occupy the same total width in the preview area."
);
AssertApprox(
    itemPixels.OriginX,
    skillPixels.OriginX,
    "Item and Skill grids should share the same horizontal origin in the preview area."
);

// --- Material cache LRU: evicts only unreferenced materials and stays bounded when possible ---
var materialLru = new CollectionCardMaterialLru(capacity: 2);
AssertValues(
    materialLru.Acquire("a"),
    Array.Empty<string>(),
    "First material acquire should not evict."
);
materialLru.Release("a");
materialLru.Acquire("b");
materialLru.Release("b");
AssertValues(
    materialLru.Acquire("c"),
    new[] { "a" },
    "LRU should evict the oldest unreferenced material when capacity is exceeded."
);
AssertFalse(materialLru.Contains("a"), "Evicted material key should leave the LRU.");
AssertTrue(materialLru.Contains("b"), "Newer unreferenced material should stay resident.");
AssertTrue(materialLru.Contains("c"), "Newest material should stay resident.");

var referencedLru = new CollectionCardMaterialLru(capacity: 2);
referencedLru.Acquire("active-a");
referencedLru.Acquire("idle-b");
referencedLru.Release("idle-b");
AssertValues(
    referencedLru.Acquire("active-c"),
    new[] { "idle-b" },
    "LRU should skip referenced entries and evict an idle entry instead."
);
AssertTrue(referencedLru.Contains("active-a"), "Referenced oldest material should not be evicted.");
AssertTrue(referencedLru.Contains("active-c"), "New referenced material should remain tracked.");

var allReferencedLru = new CollectionCardMaterialLru(capacity: 1);
allReferencedLru.Acquire("active-a");
AssertValues(
    allReferencedLru.Acquire("active-b"),
    Array.Empty<string>(),
    "LRU should temporarily exceed capacity rather than evict a referenced material."
);
AssertEqual(
    2,
    allReferencedLru.Count,
    "All-referenced material cache should report the temporary over-capacity state."
);
allReferencedLru.Release("active-a");
AssertValues(
    allReferencedLru.Acquire("active-c"),
    new[] { "active-a" },
    "Once an old material is released, a later acquire should evict it."
);

// --- Degenerate: empty visible set ---
var empty = CollectionGridLayout.Build(
    System.Array.Empty<CollectionCardVm>(),
    CollectionTabKind.Items
);
AssertEqual(0, empty.Count, "Empty layout has no cells.");
AssertEqual(0, empty.ShelfCount, "Empty layout has no shelves.");
AssertApprox(0f, empty.ContentHeight(100f, 10f), "Empty layout is zero tall.");

System.Console.WriteLine("CollectionGridLayout checks passed.");

static CollectionCardVm Item(ECardSize size) => new() { Type = ECardType.Item, Size = size };

static CollectionCardVm[] Make(ECardType type, int count, System.Func<int, ECardSize> size)
{
    var list = new CollectionCardVm[count];
    for (var i = 0; i < count; i++)
        list[i] = new CollectionCardVm { Type = type, Size = size(i) };
    return list;
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
}

static void AssertCell(CollectionGridCell cell, int col, int shelf, int widthSpan, string message)
{
    if (cell.Col != col || cell.Shelf != shelf || cell.WidthSpan != widthSpan)
        throw new InvalidOperationException(
            $"{message} Expected: (col {col}, shelf {shelf}, span {widthSpan}), "
                + $"Actual: (col {cell.Col}, shelf {cell.Shelf}, span {cell.WidthSpan})"
        );
}

static void AssertShelf(CollectionGridShelf shelf, int first, int last, string message)
{
    if (shelf.FirstIndex != first || shelf.LastIndex != last)
        throw new InvalidOperationException(
            $"{message} Expected: [{first}..{last}], Actual: [{shelf.FirstIndex}..{shelf.LastIndex}]"
        );
}

static void AssertRect(
    CollectionGridRect rect,
    float x,
    float y,
    float width,
    float height,
    string message
)
{
    if (
        !Approx(rect.X, x)
        || !Approx(rect.Y, y)
        || !Approx(rect.Width, width)
        || !Approx(rect.Height, height)
    )
        throw new InvalidOperationException(
            $"{message} Expected: ({x},{y},{width},{height}), "
                + $"Actual: ({rect.X},{rect.Y},{rect.Width},{rect.Height})"
        );
}

static void AssertApprox(float expected, float actual, string message)
{
    if (!Approx(expected, actual))
        throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

static void AssertValues<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected, string message)
{
    if (actual.Count != expected.Count)
        throw new InvalidOperationException(
            $"{message} Expected {expected.Count} values, got {actual.Count}."
        );
    for (var i = 0; i < actual.Count; i++)
    {
        if (!EqualityComparer<T>.Default.Equals(actual[i], expected[i]))
            throw new InvalidOperationException(
                $"{message} At {i}: expected {expected[i]}, got {actual[i]}."
            );
    }
}

static bool Approx(float a, float b) => System.Math.Abs(a - b) < 0.001f;
