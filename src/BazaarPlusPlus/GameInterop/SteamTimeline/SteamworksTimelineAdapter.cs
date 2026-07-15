#nullable enable
using System;
using Steamworks;

namespace BazaarPlusPlus.GameInterop.SteamTimeline;

internal sealed class SteamworksTimelineAdapter : ISteamTimelineAdapter
{
    private readonly Action<SteamTimelineAdapterFailure> _onFailure;
    private bool _disabled;

    internal SteamworksTimelineAdapter(Action<SteamTimelineAdapterFailure> onFailure)
    {
        _onFailure = onFailure ?? throw new ArgumentNullException(nameof(onFailure));
    }

    public bool IsOperational => !_disabled;

    public string SteamUiLanguage { get; private set; } = "english";

    public bool TryInitialize()
    {
        string? language = null;
        var succeeded = TryInvoke(
            SteamTimelineAdapterOperation.Probe,
            () =>
            {
                if (!SteamAPI.IsSteamRunning())
                    throw new InvalidOperationException("Steam client is not running.");
                if (global::Steamworks.CSteamAPIContext.GetSteamTimeline() == IntPtr.Zero)
                {
                    throw new NotSupportedException(
                        "The active Steam client does not expose ISteamTimeline v004."
                    );
                }
                language = SteamUtils.GetSteamUILanguage();
            }
        );
        if (succeeded && !string.IsNullOrWhiteSpace(language))
            SteamUiLanguage = language!;
        return succeeded;
    }

    public bool TryStartGamePhase() =>
        TryInvoke(
            SteamTimelineAdapterOperation.StartGamePhase,
            () => global::Steamworks.SteamTimeline.StartGamePhase()
        );

    public bool TrySetGamePhaseId(string phaseId) =>
        TryInvoke(
            SteamTimelineAdapterOperation.SetGamePhaseId,
            () => global::Steamworks.SteamTimeline.SetGamePhaseID(phaseId)
        );

    public bool TryAddGamePhaseTag(string name, string icon, string group, uint priority) =>
        TryInvoke(
            SteamTimelineAdapterOperation.AddGamePhaseTag,
            () => global::Steamworks.SteamTimeline.AddGamePhaseTag(name, icon, group, priority)
        );

    public bool TrySetGamePhaseAttribute(string group, string value, uint priority) =>
        TryInvoke(
            SteamTimelineAdapterOperation.SetGamePhaseAttribute,
            () => global::Steamworks.SteamTimeline.SetGamePhaseAttribute(group, value, priority)
        );

    public bool TryStartRangeEvent(
        string title,
        string description,
        string icon,
        uint priority,
        SteamTimelineClipPriority clipPriority,
        out SteamTimelineRangeHandle handle
    )
    {
        var nativeHandle = default(TimelineEventHandle_t);
        var succeeded = TryInvoke(
            SteamTimelineAdapterOperation.StartRangeEvent,
            () =>
                nativeHandle = global::Steamworks.SteamTimeline.StartRangeTimelineEvent(
                    title,
                    description,
                    icon,
                    priority,
                    0f,
                    ToNative(clipPriority)
                )
        );
        handle = succeeded ? new SteamTimelineRangeHandle((ulong)nativeHandle) : default;
        if (!succeeded || handle.IsValid)
            return succeeded;

        Disable(
            SteamTimelineAdapterOperation.StartRangeEvent,
            new InvalidOperationException("Steam returned an invalid Timeline range handle.")
        );
        return false;
    }

    public bool TryUpdateRangeEvent(
        SteamTimelineRangeHandle handle,
        string title,
        string description,
        string icon,
        uint priority,
        SteamTimelineClipPriority clipPriority
    ) =>
        TryInvoke(
            SteamTimelineAdapterOperation.UpdateRangeEvent,
            () =>
                global::Steamworks.SteamTimeline.UpdateRangeTimelineEvent(
                    (TimelineEventHandle_t)handle.Value,
                    title,
                    description,
                    icon,
                    priority,
                    ToNative(clipPriority)
                )
        );

    public bool TryEndRangeEvent(SteamTimelineRangeHandle handle) =>
        TryInvoke(
            SteamTimelineAdapterOperation.EndRangeEvent,
            () =>
                global::Steamworks.SteamTimeline.EndRangeTimelineEvent(
                    (TimelineEventHandle_t)handle.Value,
                    0f
                )
        );

    public bool TryAddInstantaneousEvent(
        string title,
        string description,
        string icon,
        uint priority,
        SteamTimelineClipPriority clipPriority
    ) =>
        TryInvoke(
            SteamTimelineAdapterOperation.AddInstantaneousEvent,
            () =>
                global::Steamworks.SteamTimeline.AddInstantaneousTimelineEvent(
                    title,
                    description,
                    icon,
                    priority,
                    0f,
                    ToNative(clipPriority)
                )
        );

    public bool TryEndGamePhase() =>
        TryInvoke(
            SteamTimelineAdapterOperation.EndGamePhase,
            () => global::Steamworks.SteamTimeline.EndGamePhase()
        );

    private bool TryInvoke(SteamTimelineAdapterOperation operation, Action action)
    {
        if (_disabled)
            return false;

        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            Disable(operation, ex);
            return false;
        }
    }

    private void Disable(SteamTimelineAdapterOperation operation, Exception exception)
    {
        if (_disabled)
            return;

        _disabled = true;
        _onFailure(new SteamTimelineAdapterFailure(operation, exception));
    }

    private static ETimelineEventClipPriority ToNative(SteamTimelineClipPriority priority) =>
        priority switch
        {
            SteamTimelineClipPriority.Standard =>
                ETimelineEventClipPriority.k_ETimelineEventClipPriority_Standard,
            SteamTimelineClipPriority.Featured =>
                ETimelineEventClipPriority.k_ETimelineEventClipPriority_Featured,
            _ => ETimelineEventClipPriority.k_ETimelineEventClipPriority_None,
        };
}
