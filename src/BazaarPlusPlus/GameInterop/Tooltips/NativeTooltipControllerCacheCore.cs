#nullable enable

namespace BazaarPlusPlus.GameInterop.Tooltips;

/// <summary>Keeps one native-controller snapshot until topology or object lifetime invalidates it.</summary>
internal sealed class NativeTooltipControllerCacheCore<TController>
{
    private TController[] _controllers = [];
    private int _generation = int.MinValue;

    internal IReadOnlyList<TController> GetOrRefresh(
        int generation,
        Func<TController, bool> isAlive,
        Func<TController[]> scan
    )
    {
        if (isAlive == null)
            throw new ArgumentNullException(nameof(isAlive));
        if (scan == null)
            throw new ArgumentNullException(nameof(scan));

        if (_generation != generation || _controllers.Any(controller => !isAlive(controller)))
        {
            _controllers = scan().Where(isAlive).ToArray();
            _generation = generation;
        }
        return _controllers;
    }
}
