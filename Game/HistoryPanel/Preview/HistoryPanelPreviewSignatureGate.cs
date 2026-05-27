#nullable enable
using System.Threading.Tasks;

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

// Decides whether the renderer should cache `previewData.Signature` after awaiting all
// per-card SetUp tasks. Any failed or cancelled task means the frame is incomplete: we
// drop the signature so the next selection re-attempts the render instead of locking in
// a half-painted RT.
internal static class HistoryPanelPreviewSignatureGate
{
    public static bool ShouldCache(Task? aggregate)
    {
        if (aggregate == null)
            return false;
        if (!aggregate.IsCompleted)
            return false;
        return !aggregate.IsFaulted && !aggregate.IsCanceled;
    }
}
