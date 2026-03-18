#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayController
{
    private readonly CombatReplayStore _store;
    private readonly CombatReplayLoader _loader;

    public CombatReplayController(CombatReplayStore store, CombatReplayLoader loader)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public string? ActiveReplayId { get; private set; }

    public IReadOnlyList<CombatReplayRecord> ListSavedReplays()
    {
        return _store.List();
    }

    public CombatReplayRecord? GetLatestReplay()
    {
        return _store.List().FirstOrDefault();
    }

    public CombatSequenceMessages? LoadReplay(string replayId)
    {
        var record = _store.Load(replayId);
        if (record == null)
            return null;

        ActiveReplayId = record.ReplayId;
        return _loader.Load(record);
    }
}
