#nullable enable

namespace BazaarPlusPlus.Game.CombatLog;

internal enum CombatLogDisplayMode
{
    Release,
    Debug,
}

internal enum CombatLogVerbosity
{
    Compact,
    Standard,
    Verbose,
}

internal readonly struct CombatLogDisplayOptions
{
    public CombatLogDisplayOptions(
        CombatLogDisplayMode mode,
        CombatLogVerbosity verbosity,
        bool showEvents = true,
        bool showCombatants = true,
        bool showCards = true,
        bool showRewards = true,
        bool showSystem = true,
        bool showUnknown = false,
        bool showEmptyFrames = false
    )
    {
        Mode = mode;
        Verbosity = verbosity;
        ShowEvents = showEvents;
        ShowCombatants = showCombatants;
        ShowCards = showCards;
        ShowRewards = showRewards;
        ShowSystem = showSystem;
        ShowUnknown = showUnknown;
        ShowEmptyFrames = showEmptyFrames;
    }

    public CombatLogDisplayMode Mode { get; }

    public CombatLogVerbosity Verbosity { get; }

    public bool ShowEvents { get; }

    public bool ShowCombatants { get; }

    public bool ShowCards { get; }

    public bool ShowRewards { get; }

    public bool ShowSystem { get; }

    public bool ShowUnknown { get; }

    public bool ShowEmptyFrames { get; }

    public static CombatLogDisplayOptions ReleaseStandard =>
        new(CombatLogDisplayMode.Release, CombatLogVerbosity.Standard);

    public static CombatLogDisplayOptions DebugVerbose =>
        new(CombatLogDisplayMode.Debug, CombatLogVerbosity.Verbose, showUnknown: true);
}
