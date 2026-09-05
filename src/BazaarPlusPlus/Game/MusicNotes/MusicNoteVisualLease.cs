#nullable enable
namespace BazaarPlusPlus.Game.MusicNotes;

// Captures native rendering flags once per ownership interval. Refresh must not overwrite
// the saved value with our own suppression, and removed/pooled visuals must be released.
internal sealed class MusicNoteVisualLease<T>(Func<T, bool> read, Action<T, bool> write)
    where T : notnull
{
    private readonly Dictionary<T, bool> _saved = [];
    private readonly HashSet<T> _seen = [];
    private readonly List<T> _removed = [];

    internal void BeginRefresh() => _seen.Clear();

    internal void Suppress(T target)
    {
        _seen.Add(target);
        if (!_saved.ContainsKey(target))
            _saved.Add(target, read(target));
        write(target, true);
    }

    internal void EndRefresh()
    {
        _removed.Clear();
        foreach (var pair in _saved)
        {
            if (_seen.Contains(pair.Key))
                continue;
            write(pair.Key, pair.Value);
            _removed.Add(pair.Key);
        }
        foreach (var target in _removed)
            _saved.Remove(target);
    }

    internal void Restore()
    {
        foreach (var pair in _saved)
            write(pair.Key, pair.Value);
        _saved.Clear();
        _seen.Clear();
        _removed.Clear();
    }
}
