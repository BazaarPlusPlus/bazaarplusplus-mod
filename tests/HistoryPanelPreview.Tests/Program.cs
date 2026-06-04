using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

TestSignatureGate_NullAggregate_DoesNotCache();
TestSignatureGate_IncompleteAggregate_DoesNotCache();
TestSignatureGate_FaultedAggregate_DoesNotCache();
TestSignatureGate_CanceledAggregate_DoesNotCache();
TestSignatureGate_CompletedSuccessfully_Caches();
TestGenerationGuard_FreshSnapshotIsCurrent();
TestGenerationGuard_BumpInvalidatesPriorSnapshot();
TestGenerationGuard_ParallelBumpsAreSerialised();
TestSocketResolver_HonoursRequestedIndex();
TestSocketResolver_FallsBackWhenNoRequest();
TestSocketResolver_ClampsIntoRange();
TestSocketResolver_ReturnsMinusOneWhenSpanCannotFit();
TestSocketResolver_ReturnsMinusOneForEmptyBoard();
TestBoardSpan_MapsNativeItemSizes();
TestSlotPlanner_ReferencePreservesSourceSockets();
TestSlotPlanner_ReferenceFallsBackWhenSourceMissing();
TestSlotPlanner_SelectableContainerSkipsInvalidSourceSockets();
TestSlotPlanner_SelectableShopCentersByTotalSpan();
TestSlotPlanner_SelectableShopSkipsOverflowRemainder();
TestPreviewMapper_UsesDisplaySocketAndBoardPrefix();

Console.WriteLine("HistoryPanelPreview checks passed.");

static void TestSignatureGate_NullAggregate_DoesNotCache()
{
    Assert(
        !ItemBoardPreviewSignatureGate.ShouldCache(null),
        "Null aggregate must not cache the signature."
    );
}

static void TestSignatureGate_IncompleteAggregate_DoesNotCache()
{
    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    Assert(
        !ItemBoardPreviewSignatureGate.ShouldCache(tcs.Task),
        "An incomplete aggregate must not cache the signature (caller should still be waiting)."
    );
}

static void TestSignatureGate_FaultedAggregate_DoesNotCache()
{
    var tcs = new TaskCompletionSource<bool>();
    tcs.SetException(new InvalidOperationException("simulated SetUp failure"));
    Assert(
        !ItemBoardPreviewSignatureGate.ShouldCache(tcs.Task),
        "A faulted aggregate must not cache; next selection should retry."
    );
}

static void TestSignatureGate_CanceledAggregate_DoesNotCache()
{
    var tcs = new TaskCompletionSource<bool>();
    tcs.SetCanceled();
    Assert(
        !ItemBoardPreviewSignatureGate.ShouldCache(tcs.Task),
        "A canceled aggregate must not cache; the frame is incomplete."
    );
}

static void TestSignatureGate_CompletedSuccessfully_Caches()
{
    var tcs = new TaskCompletionSource<bool>();
    tcs.SetResult(true);
    Assert(
        ItemBoardPreviewSignatureGate.ShouldCache(tcs.Task),
        "A completed-OK aggregate must cache so identical selections short-circuit."
    );
}

static void TestGenerationGuard_FreshSnapshotIsCurrent()
{
    var guard = new ItemBoardPreviewGenerationGuard();
    var snapshot = guard.Bump();
    Assert(
        guard.IsCurrent(snapshot),
        "A snapshot taken at Bump() time must remain current until the next Bump()."
    );
}

static void TestGenerationGuard_BumpInvalidatesPriorSnapshot()
{
    var guard = new ItemBoardPreviewGenerationGuard();
    var firstSnapshot = guard.Bump();
    guard.Bump();
    Assert(
        !guard.IsCurrent(firstSnapshot),
        "A new Bump() must invalidate the previous snapshot so the wait loop exits."
    );
}

static void TestGenerationGuard_ParallelBumpsAreSerialised()
{
    var guard = new ItemBoardPreviewGenerationGuard();
    var first = guard.Bump();
    var second = guard.Bump();
    var third = guard.Bump();
    Assert(
        first != second && second != third && first != third,
        "Sequential Bumps must produce distinct snapshots."
    );
    Assert(guard.IsCurrent(third), "Latest snapshot must be the current generation.");
}

static void TestSocketResolver_HonoursRequestedIndex()
{
    Assert(
        ItemBoardSocketResolver.ResolveIndex(10, 3, 0, 1) == 3,
        "A requested socket index that fits must be used verbatim."
    );
}

static void TestSocketResolver_FallsBackWhenNoRequest()
{
    Assert(
        ItemBoardSocketResolver.ResolveIndex(10, null, 4, 1) == 4,
        "With no requested index, the fallback index is used."
    );
}

static void TestSocketResolver_ClampsIntoRange()
{
    // 10 sockets, span 3 → last valid start is 7; a requested 9 clamps to 7.
    Assert(
        ItemBoardSocketResolver.ResolveIndex(10, 9, 0, 3) == 7,
        "A requested start beyond the last valid start clamps to it."
    );
    Assert(
        ItemBoardSocketResolver.ResolveIndex(10, null, -2, 1) == 0,
        "A negative fallback index clamps to 0."
    );
}

static void TestSocketResolver_ReturnsMinusOneWhenSpanCannotFit()
{
    Assert(
        ItemBoardSocketResolver.ResolveIndex(2, 0, 0, 3) == -1,
        "A card span larger than the socket count cannot fit."
    );
}

static void TestSocketResolver_ReturnsMinusOneForEmptyBoard()
{
    Assert(
        ItemBoardSocketResolver.ResolveIndex(0, 0, 0, 1) == -1,
        "Zero sockets cannot host a card."
    );
}

static void TestBoardSpan_MapsNativeItemSizes()
{
    Assert(BppItemBoardSpan.Resolve(ECardSize.Small) == 1, "Small item should span one slot.");
    Assert(BppItemBoardSpan.Resolve(ECardSize.Medium) == 2, "Medium item should span two slots.");
    Assert(BppItemBoardSpan.Resolve(ECardSize.Large) == 3, "Large item should span three slots.");
    Assert(
        BppItemBoardSpan.Resolve(ECardSize.Small, explicitSpan: 4) == 4,
        "Explicit positive span should override size-derived span."
    );
}

static void TestSlotPlanner_ReferencePreservesSourceSockets()
{
    var board = Board(
        BppItemBoardId.Historical,
        BppItemBoardType.Reference,
        Card(0, ECardSize.Large, source: EContainerSocketId.Socket_4)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board);

    Assert(
        planned.Cards[0].DisplaySocketId == EContainerSocketId.Socket_4,
        "Reference board should preserve a source socket that can fit the card span."
    );
}

static void TestSlotPlanner_ReferenceFallsBackWhenSourceMissing()
{
    var warnings = new List<string>();
    var board = Board(
        BppItemBoardId.FinalBuild,
        BppItemBoardType.Reference,
        Card(0, ECardSize.Medium),
        Card(1, ECardSize.Large)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board, warnings.Add);

    Assert(
        planned.Cards[0].DisplaySocketId == EContainerSocketId.Socket_0,
        "First source-less reference card should fall back to slot 0."
    );
    Assert(
        planned.Cards[1].DisplaySocketId == EContainerSocketId.Socket_2,
        "Fallback cursor should advance by the prior card span, not by item count."
    );
    Assert(warnings.Count == 2, "Each source-less reference card should emit a warning.");
}

static void TestSlotPlanner_SelectableContainerSkipsInvalidSourceSockets()
{
    var warnings = new List<string>();
    var board = Board(
        BppItemBoardId.LiveBoard,
        BppItemBoardType.SelectableContainer,
        Card(0, ECardSize.Large, source: EContainerSocketId.Socket_8),
        Card(1, ECardSize.Small, source: EContainerSocketId.Socket_2),
        Card(2, ECardSize.Small)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board, warnings.Add);

    Assert(planned.Cards.Count == 1, "Selectable container should keep only valid source sockets.");
    Assert(
        planned.Cards[0].DisplaySocketId == EContainerSocketId.Socket_2,
        "Valid source socket should be preserved for selectable containers."
    );
    Assert(warnings.Count == 2, "Overflow and missing sockets should both be warned.");
}

static void TestSlotPlanner_SelectableShopCentersByTotalSpan()
{
    var board = Board(
        BppItemBoardId.LiveShop,
        BppItemBoardType.SelectableShop,
        Card(0, ECardSize.Small),
        Card(1, ECardSize.Medium),
        Card(2, ECardSize.Large)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board);

    Assert(
        planned.Cards[0].DisplaySocketId == EContainerSocketId.Socket_2,
        "Total span 6 should center at slot floor((10 - 6) / 2) = 2."
    );
    Assert(
        planned.Cards[1].DisplaySocketId == EContainerSocketId.Socket_3,
        "Shop cursor should advance by small card span."
    );
    Assert(
        planned.Cards[2].DisplaySocketId == EContainerSocketId.Socket_5,
        "Shop cursor should advance by medium card span."
    );
}

static void TestSlotPlanner_SelectableShopSkipsOverflowRemainder()
{
    var warnings = new List<string>();
    var board = Board(
        BppItemBoardId.LiveShop,
        BppItemBoardType.SelectableShop,
        Card(0, ECardSize.Large),
        Card(1, ECardSize.Large),
        Card(2, ECardSize.Large),
        Card(3, ECardSize.Medium)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board, warnings.Add);

    Assert(planned.Cards.Count == 3, "Overflowing shop item should be skipped.");
    Assert(
        planned.Cards[2].DisplaySocketId == EContainerSocketId.Socket_6,
        "Third large should be the last card that fits on the 10-slot row."
    );
    Assert(warnings.Count == 1, "Overflow should emit one warning.");
}

static void TestPreviewMapper_UsesDisplaySocketAndBoardPrefix()
{
    var board = Board(
        BppItemBoardId.LiveStash,
        BppItemBoardType.SelectableContainer,
        Card(
            0,
            ECardSize.Small,
            source: EContainerSocketId.Socket_1,
            display: EContainerSocketId.Socket_3
        )
    );

    var spec = BppItemBoardPreviewMapper.Map(board).Single();

    Assert(spec.SocketId == EContainerSocketId.Socket_3, "Mapper should prefer display socket.");
    Assert(
        spec.InstanceIdPrefix == "bpp-livestash",
        "Mapper should use the board id as a stable preview instance prefix."
    );
}

static BppItemBoard Board(
    BppItemBoardId id,
    BppItemBoardType type,
    params BppItemBoardCard[] cards
) => new(id, type, cards, $"{id}:{cards.Length}");

static BppItemBoardCard Card(
    int order,
    ECardSize size,
    EContainerSocketId? source = null,
    EContainerSocketId? display = null
) =>
    new()
    {
        TemplateId = Guid.Parse($"00000000-0000-0000-0000-{order + 1:000000000000}"),
        InstanceId = $"instance-{order}",
        Order = order,
        Tier = ETier.Silver,
        Size = size,
        EnchantmentType = EEnchantmentType.Fiery,
        Attributes = new Dictionary<ECardAttributeType, int>
        {
            [ECardAttributeType.BurnApplyAmount] = order + 1,
        },
        SourceSocketId = source,
        DisplaySocketId = display,
    };

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
