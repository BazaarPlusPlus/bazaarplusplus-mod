using BazaarPlusPlus.Game.HistoryPanel.Preview;

TestSignatureGate_NullAggregate_DoesNotCache();
TestSignatureGate_IncompleteAggregate_DoesNotCache();
TestSignatureGate_FaultedAggregate_DoesNotCache();
TestSignatureGate_CanceledAggregate_DoesNotCache();
TestSignatureGate_CompletedSuccessfully_Caches();
TestGenerationGuard_FreshSnapshotIsCurrent();
TestGenerationGuard_BumpInvalidatesPriorSnapshot();
TestGenerationGuard_ParallelBumpsAreSerialised();
TestPreviewTextureGeometry_DoesNotShrinkBelowNativeBoardSize();
TestPreviewOverlayPlacement_FitsNativeBoardInsideContainer();

Console.WriteLine("HistoryPanelPreview checks passed.");

static void TestSignatureGate_NullAggregate_DoesNotCache()
{
    Assert(
        !HistoryPanelPreviewSignatureGate.ShouldCache(null),
        "Null aggregate must not cache the signature."
    );
}

static void TestSignatureGate_IncompleteAggregate_DoesNotCache()
{
    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    Assert(
        !HistoryPanelPreviewSignatureGate.ShouldCache(tcs.Task),
        "An incomplete aggregate must not cache the signature (caller should still be waiting)."
    );
}

static void TestSignatureGate_FaultedAggregate_DoesNotCache()
{
    var tcs = new TaskCompletionSource<bool>();
    tcs.SetException(new InvalidOperationException("simulated SetUp failure"));
    Assert(
        !HistoryPanelPreviewSignatureGate.ShouldCache(tcs.Task),
        "A faulted aggregate must not cache; next selection should retry."
    );
}

static void TestSignatureGate_CanceledAggregate_DoesNotCache()
{
    var tcs = new TaskCompletionSource<bool>();
    tcs.SetCanceled();
    Assert(
        !HistoryPanelPreviewSignatureGate.ShouldCache(tcs.Task),
        "A canceled aggregate must not cache; the frame is incomplete."
    );
}

static void TestSignatureGate_CompletedSuccessfully_Caches()
{
    var tcs = new TaskCompletionSource<bool>();
    tcs.SetResult(true);
    Assert(
        HistoryPanelPreviewSignatureGate.ShouldCache(tcs.Task),
        "A completed-OK aggregate must cache so identical selections short-circuit."
    );
}

static void TestGenerationGuard_FreshSnapshotIsCurrent()
{
    var guard = new HistoryPanelPreviewGenerationGuard();
    var snapshot = guard.Bump();
    Assert(
        guard.IsCurrent(snapshot),
        "A snapshot taken at Bump() time must remain current until the next Bump()."
    );
}

static void TestGenerationGuard_BumpInvalidatesPriorSnapshot()
{
    var guard = new HistoryPanelPreviewGenerationGuard();
    var firstSnapshot = guard.Bump();
    guard.Bump();
    Assert(
        !guard.IsCurrent(firstSnapshot),
        "A new Bump() must invalidate the previous snapshot so the wait loop exits."
    );
}

static void TestGenerationGuard_ParallelBumpsAreSerialised()
{
    var guard = new HistoryPanelPreviewGenerationGuard();
    var first = guard.Bump();
    var second = guard.Bump();
    var third = guard.Bump();
    Assert(first != second && second != third && first != third, "Sequential Bumps must produce distinct snapshots.");
    Assert(guard.IsCurrent(third), "Latest snapshot must be the current generation.");
}

static void TestPreviewTextureGeometry_DoesNotShrinkBelowNativeBoardSize()
{
    var size = HistoryPanelPreviewTextureGeometry.ResolveTextureSize(720, 284);

    Assert(
        size.Width == 2400 && size.Height == 600,
        "Small UI preview containers must not shrink the native-card render board; otherwise CardPreviewBase prefabs overlap or crop."
    );
}

static void TestPreviewOverlayPlacement_FitsNativeBoardInsideContainer()
{
    var placement = HistoryPanelPreviewTextureGeometry.ResolveBoardPlacement(720, 284);

    Assert(placement.Width == 720, "Overlay board width should fill the preview container.");
    Assert(placement.Height == 180, "Overlay board height should preserve the native board aspect ratio.");
    Assert(placement.OffsetX == 0, "Overlay board should be horizontally centered.");
    Assert(placement.OffsetY == 52, "Overlay board should be vertically centered.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
