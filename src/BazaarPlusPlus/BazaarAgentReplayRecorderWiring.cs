#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus;

/// <summary>
/// Composition-root delegate bodies behind <see cref="IBazaarAgentReplayRecorder"/>. This is the
/// only place the BazaarAgent replay facade touches game types: decode the GhostBattlePayload
/// blob, run the full recording guard set (the union of HistoryPanelReplayService.CanRecordReplay
/// and the CombatReplayVideoRecorder OnPlaybackStarting guards — any miss there records nothing
/// silently), and call into the internal CombatReplayRuntime. The runtime accessor is lazy: the
/// facade is published before CombatReplayRuntime is attached. Main thread only.
/// </summary>
internal static class BazaarAgentReplayRecorderWiring
{
    private static int _ffmpegPrewarmKicked;

    public static IBazaarAgentReplayRecorder Create(
        Func<CombatReplayRuntime?> runtimeAccessor,
        IBppServices services
    )
    {
        return new BazaarAgentReplayRecorder(
            tryStartRecord: (payloadBytes, expectedBattleId) =>
                TryStartRecord(runtimeAccessor(), services, payloadBytes, expectedBattleId),
            tryContinueReplay: () => TryContinueReplay(runtimeAccessor()),
            getReplayPhase: () => GetReplayPhase(runtimeAccessor(), services)
        );
    }

    private static BppReplayControlResult TryStartRecord(
        CombatReplayRuntime? runtime,
        IBppServices services,
        byte[] payloadBytes,
        string? expectedBattleId
    )
    {
        PrewarmFfmpegOnce(services);

        if (runtime == null)
            return BppReplayControlResult.Unavailable("Combat replay runtime is unavailable.");

        if (!GhostBattlePayloadCodec.TryDeserialize(payloadBytes, out var ghost, out var error))
            return BppReplayControlResult.Invalid($"Payload decode failed: {error}");

        var manifest = ghost!.BattleManifest;
        var payload = ghost.ReplayPayload;
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.BattleId))
            return BppReplayControlResult.Invalid("Payload is missing the battle manifest.");
        if (payload == null)
            return BppReplayControlResult.Invalid("Payload is missing the replay payload.");

        var battleId = manifest.BattleId;
        if (
            !string.IsNullOrWhiteSpace(ghost.BattleId)
            && !string.Equals(ghost.BattleId, battleId, StringComparison.Ordinal)
        )
        {
            return BppReplayControlResult.Invalid(
                $"Payload battleId '{ghost.BattleId}' does not match manifest battleId '{battleId}'."
            );
        }

        if (
            !string.IsNullOrWhiteSpace(expectedBattleId)
            && !string.Equals(expectedBattleId, battleId, StringComparison.Ordinal)
        )
        {
            return BppReplayControlResult.Invalid(
                $"Request battleId '{expectedBattleId}' does not match payload battleId '{battleId}'."
            );
        }

        if (!CanRecordNow(runtime, services, out var reason))
            return BppReplayControlResult.Rejected(reason);

        if (!runtime.ReplayImportedBattle(manifest, payload, recordVideo: true))
            return BppReplayControlResult.Rejected("Replay runtime rejected the imported battle.");

        BppLog.Info(
            "BazaarAgentReplayRecorder",
            $"Accepted external record request for battle {battleId}."
        );
        return BppReplayControlResult.Accepted(battleId);
    }

    // The recorder's OnPlaybackStarting bails out silently when any of these fail (the replay
    // still plays, nothing is recorded) — so a record request must be rejected up front instead
    // of returning "accepted" for a session that will never produce an mp4.
    private static bool CanRecordNow(
        CombatReplayRuntime runtime,
        IBppServices services,
        out string reason
    )
    {
        if (!runtime.CanReplaySavedCombats(out reason))
            return false;

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            reason = "Video recording is unavailable on this device (no async GPU readback).";
            return false;
        }

        if (string.IsNullOrEmpty(FfmpegLocator.Resolve(services.Paths.PluginsDirectoryPath)))
        {
            reason = "Video recording is unavailable (FFmpeg could not be resolved).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(services.Paths.CombatReplayVideoDirectoryPath))
        {
            reason = "Video recording is unavailable (video directory is not configured).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static BppReplayControlResult TryContinueReplay(CombatReplayRuntime? runtime)
    {
        if (runtime == null)
            return BppReplayControlResult.Unavailable("Combat replay runtime is unavailable.");

        if (!runtime.TryContinueReplay(out var reason))
            return BppReplayControlResult.Rejected(reason);

        return BppReplayControlResult.Accepted(runtime.ActiveBattleId);
    }

    private static BppReplayPhaseSnapshot GetReplayPhase(
        CombatReplayRuntime? runtime,
        IBppServices services
    )
    {
        PrewarmFfmpegOnce(services);

        if (runtime == null)
            return new BppReplayPhaseSnapshot(BppReplayPhase.None, null);

        if (runtime.IsReplayStartInProgress)
            return new BppReplayPhaseSnapshot(BppReplayPhase.Starting, runtime.ActiveBattleId);

        if (AppState.CurrentState is ReplayState replay)
        {
            var phase = replay.IsReplaying
                ? BppReplayPhase.Playing
                : BppReplayPhase.FinishedAwaitingContinue;
            return new BppReplayPhaseSnapshot(phase, runtime.ActiveBattleId);
        }

        return new BppReplayPhaseSnapshot(BppReplayPhase.None, null);
    }

    // FfmpegLocator.Resolve runs a ~2 s liveness probe on first call and caches process-wide.
    // Kick it off-thread the first time the host actually touches the facade (phase reads happen
    // every snapshot tick) so the CanRecordNow gate only ever reads the warm cache. Doing it here
    // rather than at publish time keeps the cost away from installs without the host plugin.
    private static void PrewarmFfmpegOnce(IBppServices services)
    {
        if (Interlocked.Exchange(ref _ffmpegPrewarmKicked, 1) != 0)
            return;

        var pluginsDirectoryPath = services.Paths.PluginsDirectoryPath;
        _ = Task.Run(() => FfmpegLocator.Resolve(pluginsDirectoryPath));
    }
}
