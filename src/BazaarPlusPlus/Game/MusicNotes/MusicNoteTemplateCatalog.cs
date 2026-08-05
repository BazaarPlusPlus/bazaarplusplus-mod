#nullable enable
using BazaarGameShared.Domain.Cards.Socket;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.StaticCards;

namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>
/// Letter → granted-attribute lookup for sockets whose note is not placed yet (the badge needs
/// an icon for the letter a socket would become). Built once per GameData manager generation by
/// scanning the card map for music-note templates on a worker thread — the map load is the
/// documented first-open SQLite cost, so it never runs on the Unity main thread. While the map
/// is loading (or when the scan fails) lookups return null and badges render letter-only, which
/// self-heals on later frames. Main-thread callers only.
/// </summary>
internal static class MusicNoteTemplateCatalog
{
    private static object? _managerRef;
    private static volatile Dictionary<int, ECardAttributeType>? _attributesByLetter;
    private static bool _loadInFlight;
    private static bool _loadFailed;

    internal static ECardAttributeType? TryGetAttributeForLetter(EMusicNote letter)
    {
        var manager = BppStaticDataAccess.TryGetReadyManagerObject();
        if (manager == null)
            return null;

        if (!ReferenceEquals(manager, _managerRef))
        {
            // The game swaps the manager reference after a GameData download; drop the old map.
            _managerRef = manager;
            _attributesByLetter = null;
            _loadInFlight = false;
            _loadFailed = false;
        }

        var map = _attributesByLetter;
        if (map != null)
            return map.TryGetValue((int)letter, out var attribute) ? attribute : null;

        if (!_loadInFlight && !_loadFailed)
        {
            _loadInFlight = true;
            var capturedManager = manager;
            Task.Run(() => LoadOnWorker(capturedManager));
        }

        return null;
    }

    private static void LoadOnWorker(object capturedManager)
    {
        Dictionary<int, ECardAttributeType>? built = null;
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
                built = new Dictionary<int, ECardAttributeType>(MusicNoteLetterMath.LetterCount);
                foreach (var template in cardMap.Values)
                {
                    if (
                        template is TCardMusicNoteSocketEffect note
                        && !built.ContainsKey((int)note.MusicNote)
                        && MusicNoteEffectClassifier.TryGetAttribute(note)
                            is ECardAttributeType attribute
                    )
                    {
                        built[(int)note.MusicNote] = attribute;
                    }
                }
            }
        }
        catch
        {
            failed = true;
        }

        // Publish only if the generation did not change while loading.
        if (!ReferenceEquals(_managerRef, capturedManager))
            return;
        if (failed)
            _loadFailed = true;
        else
            _attributesByLetter = built;
        _loadInFlight = false;
    }
}
