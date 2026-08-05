#nullable enable
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.StaticCards;

namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>
/// Letter → item-category-tags lookup for note badges (the letter identity — e.g. B keys on
/// Burn items — lives in the note templates' condition graphs, not in code). Built once per
/// GameData manager generation by scanning the card map for music-note templates on a worker
/// thread — the map load is the documented first-open SQLite cost, so it never runs on the
/// Unity main thread. While the map is loading (or when the scan fails) lookups return null
/// and badges render letter-only, which self-heals on later frames. Main-thread callers only.
/// </summary>
internal static class MusicNoteTemplateCatalog
{
    // Guards every field below: the main thread swaps generations while a worker may be
    // publishing, and the worker's generation guard must be atomic with its publish —
    // otherwise a load for an old manager can slip its stale map in after a swap.
    private static readonly object Gate = new();
    private static object? _managerRef;
    private static Dictionary<int, IReadOnlyList<MusicNoteTag>>? _tagsByLetter;
    private static bool _loadInFlight;
    private static bool _loadFailed;

    internal static IReadOnlyList<MusicNoteTag>? TryGetTagsForLetter(EMusicNote letter)
    {
        var manager = BppStaticDataAccess.TryGetReadyManagerObject();
        if (manager == null)
            return null;

        Dictionary<int, IReadOnlyList<MusicNoteTag>>? map;
        var startLoad = false;
        lock (Gate)
        {
            if (!ReferenceEquals(manager, _managerRef))
            {
                // The game swaps the manager reference after a GameData download; drop the
                // old map. An in-flight load for the old manager fails its generation guard.
                _managerRef = manager;
                _tagsByLetter = null;
                _loadInFlight = false;
                _loadFailed = false;
            }

            map = _tagsByLetter;
            if (map == null && !_loadInFlight && !_loadFailed)
            {
                _loadInFlight = true;
                startLoad = true;
            }
        }

        if (map != null)
            return map.TryGetValue((int)letter, out var tags) ? tags : null;

        if (startLoad)
        {
            var capturedManager = manager;
            Task.Run(() => LoadOnWorker(capturedManager));
        }

        return null;
    }

    private static void LoadOnWorker(object capturedManager)
    {
        Dictionary<int, IReadOnlyList<MusicNoteTag>>? built = null;
        var failed = false;
        try
        {
            var cardMap = BppStaticDataAccess.LoadCardMap(capturedManager);
            if (cardMap == null)
            {
                failed = true;
            }
            else
            {
                built = new Dictionary<int, IReadOnlyList<MusicNoteTag>>(
                    MusicNoteLetterMath.LetterCount
                );
                foreach (var template in cardMap.Values)
                {
                    if (
                        template is TCardMusicNoteSocketEffect note
                        && !built.ContainsKey((int)note.MusicNote)
                    )
                    {
                        var tags = MusicNoteEffectClassifier.GetTags(note);
                        if (tags.Count > 0)
                            built[(int)note.MusicNote] = tags;
                    }
                }
            }
        }
        catch
        {
            failed = true;
        }

        lock (Gate)
        {
            // Publish only if the generation did not change while loading.
            if (!ReferenceEquals(_managerRef, capturedManager))
                return;
            if (failed)
                _loadFailed = true;
            else
                _tagsByLetter = built;
            _loadInFlight = false;
        }
    }
}
