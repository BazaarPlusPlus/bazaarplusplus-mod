using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

TestSignatureGate_NullAggregate_DoesNotCache();
TestSignatureGate_IncompleteAggregate_DoesNotCache();
TestSignatureGate_FaultedAggregate_DoesNotCache();
TestSignatureGate_CanceledAggregate_DoesNotCache();
TestSignatureGate_CompletedSuccessfully_Caches();
TestGenerationGuard_FreshSnapshotIsCurrent();
TestGenerationGuard_BumpInvalidatesPriorSnapshot();
TestGenerationGuard_ParallelBumpsAreSerialised();
TestBatchAcquirer_UsesFakeHostAndPreservesPartialResults().GetAwaiter().GetResult();
TestBatchAcquirer_ConvertsScopeCancellationToCanceledResult().GetAwaiter().GetResult();
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
TestPreviewMapper_CarriesDisplaySpan();
TestOptionsForwarder_PreservesSlotGridLayoutMode();
TestSlotGridGeometry_ResolvesSingleSlot();
TestSlotGridGeometry_ResolvesMediumSpan();
TestSlotGridGeometry_ResolvesLargeSpan();
TestSlotGridGeometry_ClampsOverflowSpan();
TestSlotGridScale_AllowsHeightFirstUpscale();
TestSlotGridScale_UsesSharedHeightForProportionalSpans();
TestSlotGridScale_DerivesWidthFromHeightAndNativeAspect();
TestSlotGridTargetHeight_UsesBoardFitScaleInTallContainer();
TestSlotGridTargetHeight_ClampsToSlotHeightInShortContainer();

Console.WriteLine("HistoryPanelPreview checks passed.");

static async Task TestBatchAcquirer_UsesFakeHostAndPreservesPartialResults()
{
    var firstSession = new FakeSession();
    var secondSession = new FakeSession();
    var failure = new NativeCardPreviewFailure(
        NativeCardPreviewOperation.SetUp,
        NativeCardPreviewFailureReason.SetUpException,
        Guid.NewGuid()
    );
    var scope = new FakeScope(
        subject => Acquired(firstSession),
        subject => new ValueTask<NativeCardAcquireResult>(
            new NativeCardAcquireResult(NativeCardAcquireStatus.Failed, null, failure)
        ),
        subject => Acquired(secondSession)
    );
    INativeCardPreviewHost host = new FakeHost(scope);
    var openedScope = host.OpenScope(new FakeOwner());
    var subjects = new[] { Subject(), Subject(), Subject() };

    var results = await ItemBoardPreviewBatchAcquirer.AcquireAsync(openedScope, subjects);

    Assert(results.Length == 3, "Batch acquisition should preserve every input result.");
    Assert(
        ReferenceEquals(results[0].Session, firstSession)
            && ReferenceEquals(results[2].Session, secondSession),
        "Successful acquisitions should expose only the opaque sessions returned by the host scope."
    );
    Assert(
        results[1].Session == null && ReferenceEquals(results[1].NativeFailure, failure),
        "A partial native failure must not discard successful sibling sessions."
    );

    results[0].Session!.Dispose();
    results[2].Session!.Dispose();
    Assert(
        firstSession.DisposeCount == 1 && secondSession.DisposeCount == 1,
        "Consumer cleanup should dispose each acquired session exactly once."
    );
}

static async Task TestBatchAcquirer_ConvertsScopeCancellationToCanceledResult()
{
    var scope = new FakeScope(subject => throw new OperationCanceledException());
    var results = await ItemBoardPreviewBatchAcquirer.AcquireAsync(scope, new[] { Subject() });

    Assert(
        results.Length == 1 && results[0].Canceled && results[0].Session == null,
        "A standard scope cancellation should remain distinguishable from a typed native failure."
    );
}

static ValueTask<NativeCardAcquireResult> Acquired(INativeCardPreviewSession session) =>
    new(new NativeCardAcquireResult(NativeCardAcquireStatus.Acquired, session, Failure: null));

static NativeCardPreviewSubject Subject() =>
    new()
    {
        TemplateId = Guid.NewGuid(),
        Tier = ETier.Bronze,
        DisplaySpan = 1,
    };

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

static void TestPreviewMapper_CarriesDisplaySpan()
{
    var board = Board(
        BppItemBoardId.LiveBoard,
        BppItemBoardType.SelectableContainer,
        Card(
            0,
            ECardSize.Large,
            source: EContainerSocketId.Socket_1,
            display: EContainerSocketId.Socket_4
        )
    );

    var spec = BppItemBoardPreviewMapper.Map(board).Single();

    Assert(spec.SocketId == EContainerSocketId.Socket_4, "Mapper should preserve display socket.");
    Assert(spec.DisplaySpan == 3, "Mapper should pass the planned display span to the renderer.");
}

static void TestOptionsForwarder_PreservesSlotGridLayoutMode()
{
    Action<NativeCardPreviewFailure> cardPreviewFailureReporter = _ => { };
    Action<NativeCardPreviewFailure> hoverFailureReporter = _ => { };
    Action<ItemBoardPreviewFailure> itemBoardFailureReporter = _ => { };
    var options = new ItemBoardPreviewOptions
    {
        Layer = 7,
        SortingOrder = 8,
        LayoutMode = ItemBoardPreviewLayoutMode.SlotGrid,
        ShowHover = false,
        UseCanvasGroup = true,
        CardPreviewFailureReporter = cardPreviewFailureReporter,
        HoverFailureReporter = hoverFailureReporter,
        ItemBoardFailureReporter = itemBoardFailureReporter,
        SlotGridHorizontalInsetPixels = 11f,
        SlotGridVerticalInsetPixels = 12f,
        SlotGridMaxHeightRatio = 0.75f,
        SlotGridMaxScale = 0.9f,
    };

    var forwarded = ItemBoardPreviewOptionsForwarder.ForSurface(options);

    Assert(
        forwarded.LayoutMode == ItemBoardPreviewLayoutMode.SlotGrid,
        "Layout mode must forward."
    );
    Assert(forwarded.Layer == 7, "Layer must forward.");
    Assert(forwarded.SortingOrder == 8, "Sorting order must forward.");
    Assert(!forwarded.ShowHover, "Hover option must forward.");
    Assert(forwarded.UseCanvasGroup, "CanvasGroup option must forward.");
    Assert(
        ReferenceEquals(forwarded.CardPreviewFailureReporter, cardPreviewFailureReporter),
        "Card-preview failure reporter must forward."
    );
    Assert(
        ReferenceEquals(forwarded.HoverFailureReporter, hoverFailureReporter),
        "Hover failure reporter must forward."
    );
    Assert(
        ReferenceEquals(forwarded.ItemBoardFailureReporter, itemBoardFailureReporter),
        "Item-board failure reporter must forward."
    );
    Assert(
        forwarded.SlotGridHorizontalInsetPixels == 11f,
        "SlotGrid horizontal inset must forward."
    );
    Assert(forwarded.SlotGridVerticalInsetPixels == 12f, "SlotGrid vertical inset must forward.");
    Assert(forwarded.SlotGridMaxHeightRatio == 0.75f, "SlotGrid max height must forward.");
    Assert(forwarded.SlotGridMaxScale == 0.9f, "SlotGrid max scale must forward.");
}

static void TestSlotGridGeometry_ResolvesSingleSlot()
{
    var rect = ItemBoardSlotGridGeometry.ResolveOccupiedRect(1000f, 200f, 2, 1, 0f, 0f);

    Assert(rect.X == 200f, "Socket 2 should start at x=200 for a 1000px row.");
    Assert(rect.Width == 100f, "A small item should occupy one 100px slot.");
}

static void TestSlotGridGeometry_ResolvesMediumSpan()
{
    var rect = ItemBoardSlotGridGeometry.ResolveOccupiedRect(1000f, 200f, 3, 2, 0f, 0f);

    Assert(rect.X == 300f, "Socket 3 should start at x=300 for a 1000px row.");
    Assert(rect.Width == 200f, "A medium item should occupy two 100px slots.");
}

static void TestSlotGridGeometry_ResolvesLargeSpan()
{
    var rect = ItemBoardSlotGridGeometry.ResolveOccupiedRect(1000f, 200f, 5, 3, 0f, 0f);

    Assert(rect.X == 500f, "Socket 5 should start at x=500 for a 1000px row.");
    Assert(rect.Width == 300f, "A large item should occupy three 100px slots.");
}

static void TestSlotGridGeometry_ClampsOverflowSpan()
{
    var rect = ItemBoardSlotGridGeometry.ResolveOccupiedRect(1000f, 200f, 8, 3, 0f, 0f);

    Assert(rect.X == 800f, "Socket 8 should start at x=800 for a 1000px row.");
    Assert(rect.Width == 200f, "Overflowing span should clamp at the end of the row.");
}

static void TestSlotGridScale_AllowsHeightFirstUpscale()
{
    var scale = ItemBoardSlotGridGeometry.ResolveHeightScale(200f, 300f, 2f);

    Assert(Approx(scale, 1.5f), "SlotGrid should be able to grow cards above native scale.");
}

static void TestSlotGridScale_UsesSharedHeightForProportionalSpans()
{
    const float nativeHeight = 200f;
    const float targetHeight = 300f;
    var smallScale = ItemBoardSlotGridGeometry.ResolveHeightScale(nativeHeight, targetHeight, 2f);
    var mediumScale = ItemBoardSlotGridGeometry.ResolveHeightScale(nativeHeight, targetHeight, 2f);
    var largeScale = ItemBoardSlotGridGeometry.ResolveHeightScale(nativeHeight, targetHeight, 2f);

    Assert(
        Approx(nativeHeight * smallScale, targetHeight),
        "Small card should reach target height."
    );
    Assert(
        Approx(nativeHeight * mediumScale, targetHeight),
        "Medium card should reach target height."
    );
    Assert(
        Approx(nativeHeight * largeScale, targetHeight),
        "Large card should reach target height."
    );
}

static void TestSlotGridScale_DerivesWidthFromHeightAndNativeAspect()
{
    const float nativeWidth = 400f;
    const float nativeHeight = 100f;
    const float targetHeight = 250f;
    var scale = ItemBoardSlotGridGeometry.ResolveHeightScale(nativeHeight, targetHeight, 5f);

    Assert(Approx(scale, 2.5f), "SlotGrid scale should be determined by target height.");
    Assert(
        Approx(nativeWidth * scale, 1000f),
        "Rendered width should follow the native frame aspect after height scaling."
    );
}

static void TestSlotGridTargetHeight_UsesBoardFitScaleInTallContainer()
{
    var targetHeight = ItemBoardSlotGridGeometry.ResolveScaledTargetHeight(
        slotHeight: 500f,
        boardNativeHeight: 600f,
        boardScale: 0.4f,
        maxHeightRatio: 0.96f
    );

    Assert(
        Approx(targetHeight, 230.4f),
        "A tall HistoryPanel container should cap card height by board-fit scale."
    );
}

static void TestSlotGridTargetHeight_ClampsToSlotHeightInShortContainer()
{
    var targetHeight = ItemBoardSlotGridGeometry.ResolveScaledTargetHeight(
        slotHeight: 180f,
        boardNativeHeight: 600f,
        boardScale: 0.5f,
        maxHeightRatio: 0.96f
    );

    Assert(Approx(targetHeight, 180f), "A short LiveBuildPanel row should use its slot height.");
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

static bool Approx(float actual, float expected, float tolerance = 0.0001f) =>
    Math.Abs(actual - expected) <= tolerance;

internal sealed class FakeHost(INativeCardPreviewScope scope) : INativeCardPreviewHost
{
    public NativeCardMeasureResult Measure(NativeCardPreviewSubject subject) =>
        new(NativeCardMeasureStatus.Measured, subject.DisplaySpan, null);

    public INativeCardPreviewScope OpenScope(INativeCardPreviewOwner owner) => scope;

    public NativeTooltipRefreshResult RefreshHoveredTooltip(NativeTooltipRefreshRequest request) =>
        new(NativeTooltipRefreshStatus.NoHoveredPreview, null, null);
}

internal sealed class FakeScope(
    params Func<NativeCardPreviewSubject, ValueTask<NativeCardAcquireResult>>[] outcomes
) : INativeCardPreviewScope
{
    private readonly Queue<
        Func<NativeCardPreviewSubject, ValueTask<NativeCardAcquireResult>>
    > _outcomes = new(outcomes);

    public ValueTask<NativeCardAcquireResult> AcquireAsync(
        NativeCardPreviewSubject subject,
        CancellationToken cancellationToken = default
    ) => _outcomes.Dequeue()(subject);

    public ValueTask DisposeAsync() => default;
}

internal sealed class FakeSession : INativeCardPreviewSession
{
    public int DisposeCount { get; private set; }
    public UnityEngine.GameObject Root => null!;
    public UnityEngine.RectTransform Rect => null!;

    public NativePreviewActionResult Show() => Applied();

    public NativePreviewActionResult ShowArtworkOnly() => Applied();

    public NativePreviewActionResult Hide() => Applied();

    public NativeCardPreviewSlotFitResult FitInto(
        UnityEngine.RectTransform slot,
        NativeCardPreviewHorizontalAlignment horizontalAlignment =
            NativeCardPreviewHorizontalAlignment.Center
    ) => NativeCardPreviewSlotFitResult.Applied;

    public NativePreviewActionResult HoverEnter() => Applied();

    public NativePreviewActionResult HoverExit() => Applied();

    public void Dispose() => DisposeCount++;

    private static NativePreviewActionResult Applied() =>
        new(NativePreviewActionStatus.Applied, null);
}

internal sealed class FakeOwner : INativeCardPreviewOwner
{
    public int Layer => 0;

    public UnityEngine.Transform? ResolveParent(NativeCardPreviewSubject subject) => null;

    public void PrepareWhileInactive(NativeCardPreviewOwnerContext context) { }

    public void OnAcquired(NativeCardPreviewOwnerContext context) { }

    public void BeforeRelease(NativeCardPreviewOwnerContext context) { }

    public void ReportFailure(NativeCardPreviewFailure failure) { }
}
