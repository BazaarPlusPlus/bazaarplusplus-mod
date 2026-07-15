#nullable enable
using System;
using BazaarPlusPlus.GameInterop.SteamTimeline;

namespace BazaarPlusPlus.Game.SteamTimeline;

internal enum SteamTimelineRunExit
{
    Completed,
    Interrupted,
    Disabled,
    PluginStopped,
}

internal enum SteamTimelineCommandKind
{
    StartPhase,
    SetPhaseId,
    EndPhase,
    StartBattleRange,
    CompleteBattleRange,
    InterruptBattleRange,
    AddLevelMarker,
}

internal enum SteamTimelineLocale
{
    English,
    SimplifiedChinese,
    TraditionalChinese,
}

internal sealed class SteamTimelineCommand
{
    private SteamTimelineCommand(
        SteamTimelineCommandKind kind,
        string? phaseId = null,
        SteamTimelineBattleContext? battle = null,
        SteamTimelineBattleResult battleResult = SteamTimelineBattleResult.Unknown,
        SteamTimelineRunExit runExit = SteamTimelineRunExit.Completed,
        int? level = null,
        int? day = null,
        string? hero = null
    )
    {
        Kind = kind;
        PhaseId = phaseId;
        Battle = battle;
        BattleResult = battleResult;
        RunExit = runExit;
        Level = level;
        Day = day;
        Hero = hero;
    }

    internal SteamTimelineCommandKind Kind { get; }

    internal string? PhaseId { get; }

    internal SteamTimelineBattleContext? Battle { get; }

    internal SteamTimelineBattleResult BattleResult { get; }

    internal SteamTimelineRunExit RunExit { get; }

    internal int? Level { get; }

    internal int? Day { get; }

    internal string? Hero { get; }

    internal static SteamTimelineCommand StartPhase() => new(SteamTimelineCommandKind.StartPhase);

    internal static SteamTimelineCommand SetPhaseId(string phaseId) =>
        new(SteamTimelineCommandKind.SetPhaseId, phaseId: phaseId);

    internal static SteamTimelineCommand EndPhase(SteamTimelineRunExit exit) =>
        new(SteamTimelineCommandKind.EndPhase, runExit: exit);

    internal static SteamTimelineCommand StartBattleRange(SteamTimelineBattleContext battle) =>
        new(SteamTimelineCommandKind.StartBattleRange, battle: battle);

    internal static SteamTimelineCommand CompleteBattleRange(
        SteamTimelineBattleContext battle,
        SteamTimelineBattleResult result
    ) => new(SteamTimelineCommandKind.CompleteBattleRange, battle: battle, battleResult: result);

    internal static SteamTimelineCommand InterruptBattleRange(SteamTimelineBattleContext battle) =>
        new(SteamTimelineCommandKind.InterruptBattleRange, battle: battle);

    internal static SteamTimelineCommand AddLevelMarker(int level, int? day, string? hero) =>
        new(SteamTimelineCommandKind.AddLevelMarker, level: level, day: day, hero: hero);
}

internal readonly struct SteamTimelineEventText
{
    internal SteamTimelineEventText(
        string title,
        string description,
        string icon,
        uint priority,
        SteamTimelineClipPriority clipPriority
    )
    {
        Title = title;
        Description = description;
        Icon = icon;
        Priority = priority;
        ClipPriority = clipPriority;
    }

    internal string Title { get; }

    internal string Description { get; }

    internal string Icon { get; }

    internal uint Priority { get; }

    internal SteamTimelineClipPriority ClipPriority { get; }
}
