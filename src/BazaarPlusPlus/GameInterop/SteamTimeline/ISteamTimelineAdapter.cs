#nullable enable

namespace BazaarPlusPlus.GameInterop.SteamTimeline;

internal interface ISteamTimelineAdapter
{
    bool IsOperational { get; }

    string SteamUiLanguage { get; }

    bool TryInitialize();

    bool TryStartGamePhase();

    bool TrySetGamePhaseId(string phaseId);

    bool TryAddGamePhaseTag(string name, string icon, string group, uint priority);

    bool TrySetGamePhaseAttribute(string group, string value, uint priority);

    bool TryStartRangeEvent(
        string title,
        string description,
        string icon,
        uint priority,
        SteamTimelineClipPriority clipPriority,
        out SteamTimelineRangeHandle handle
    );

    bool TryUpdateRangeEvent(
        SteamTimelineRangeHandle handle,
        string title,
        string description,
        string icon,
        uint priority,
        SteamTimelineClipPriority clipPriority
    );

    bool TryEndRangeEvent(SteamTimelineRangeHandle handle);

    bool TryAddInstantaneousEvent(
        string title,
        string description,
        string icon,
        uint priority,
        SteamTimelineClipPriority clipPriority
    );

    bool TryEndGamePhase();
}
