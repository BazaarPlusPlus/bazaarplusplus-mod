#nullable enable
namespace BazaarPlusPlus.Core.RunContext;

// Mod-owned mirror of the game's EVictoryCondition. Keeping a Core-owned enum means IRunContext
// (re-exported to every feature via IBppServices) carries no game-DLL type. GameInterop maps the
// game value to this at the boundary; mirrors the existing RunExitKind precedent.
internal enum RunVictoryOutcome
{
    Win,
    Lose,
    Draw,
}
