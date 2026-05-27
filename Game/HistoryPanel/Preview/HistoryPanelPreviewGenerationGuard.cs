#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

// Cooperative cancellation counter for the renderer's RenderPreview coroutine. Bumping
// the generation invalidates any in-flight wait loop without touching DOTween or any
// other shared state.
internal sealed class HistoryPanelPreviewGenerationGuard
{
    private int _generation;

    public int Current => _generation;

    public int Bump()
    {
        return ++_generation;
    }

    public bool IsCurrent(int snapshot)
    {
        return snapshot == _generation;
    }
}
