#nullable enable
using BazaarBattleService;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.EncounterOffers;

internal static class EncounterOfferHeroMapper
{
    public static bool TryToRuntime(EHero hero, out BazaarTypes.EBazaarHero runtimeHero)
    {
        switch (hero)
        {
            case EHero.Common:
                runtimeHero = BazaarTypes.EBazaarHero.Common;
                return true;
            case EHero.Pygmalien:
                runtimeHero = BazaarTypes.EBazaarHero.Pygmalien;
                return true;
            case EHero.Vanessa:
                runtimeHero = BazaarTypes.EBazaarHero.Vanessa;
                return true;
            case EHero.Stelle:
                runtimeHero = BazaarTypes.EBazaarHero.Stelle;
                return true;
            case EHero.Jules:
                runtimeHero = BazaarTypes.EBazaarHero.Jules;
                return true;
            case EHero.Dooley:
                runtimeHero = BazaarTypes.EBazaarHero.Dooley;
                return true;
            case EHero.Mak:
                runtimeHero = BazaarTypes.EBazaarHero.Mak;
                return true;
            case EHero.Karnok:
                runtimeHero = BazaarTypes.EBazaarHero.Hero7;
                return true;
            default:
                runtimeHero = BazaarTypes.EBazaarHero.Invalid;
                return false;
        }
    }

    public static bool TryFromRuntime(BazaarTypes.EBazaarHero runtimeHero, out EHero hero)
    {
        switch (runtimeHero)
        {
            case BazaarTypes.EBazaarHero.Common:
                hero = EHero.Common;
                return true;
            case BazaarTypes.EBazaarHero.Pygmalien:
                hero = EHero.Pygmalien;
                return true;
            case BazaarTypes.EBazaarHero.Vanessa:
                hero = EHero.Vanessa;
                return true;
            case BazaarTypes.EBazaarHero.Stelle:
                hero = EHero.Stelle;
                return true;
            case BazaarTypes.EBazaarHero.Jules:
                hero = EHero.Jules;
                return true;
            case BazaarTypes.EBazaarHero.Dooley:
                hero = EHero.Dooley;
                return true;
            case BazaarTypes.EBazaarHero.Mak:
                hero = EHero.Mak;
                return true;
            case BazaarTypes.EBazaarHero.Hero7:
                hero = EHero.Karnok;
                return true;
            default:
                hero = EHero.Common;
                return false;
        }
    }
}
