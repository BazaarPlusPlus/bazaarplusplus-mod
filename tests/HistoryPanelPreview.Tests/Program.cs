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

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
