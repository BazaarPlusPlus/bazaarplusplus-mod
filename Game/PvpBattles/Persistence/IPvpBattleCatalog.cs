#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.PvpBattles.Persistence;

internal interface IPvpBattleCatalog
{
    void Save(PvpBattleManifest manifest);

    PvpBattleManifest? TryLoad(string battleId);

    IReadOnlyList<PvpBattleManifest> ListRecentBattles(int limit);
}
