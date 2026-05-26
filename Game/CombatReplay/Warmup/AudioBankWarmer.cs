#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Assets.Scripts.Audio;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Infrastructure;
using FMOD.Studio;
using FMODUnity;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Warmup;

internal static class AudioBankWarmer
{
    private static readonly string[] DiagnosticBusPathFields =
    {
        "MasterBusPath",
        "BoardDiegeticBusPath",
        "BoardPresentationBusPath",
        "CombatBusPath",
        "MonsterNonVerbalBusPath",
        "VOBusPath",
        "EnvironmentSpecificBusPath",
        "EnvironmentFocusBusPath",
    };

    internal static async Task WarmAudioBanksAsync()
    {
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Saved replay audio warmup skipped because SoundManager is unavailable."
                );
                return;
            }

            var stats = new ReplayAudioWarmupStats();
            var collectionManager = Services.Get<CollectionManager>();
            if (collectionManager == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Saved replay audio warmup cannot resolve equipped board audio because CollectionManager is unavailable."
                );
            }

            var boardAssets = UnityEngine
                .Object.FindObjectsOfType<HeroBoardController>(true)
                .Where(controller =>
                    controller != null && controller.gameObject.scene.rootCount > 0
                )
                .Select(controller => controller.AssociatedDataSO)
                .Where(asset => asset != null)
                .Distinct()
                .ToList();

            var playerBoard = await SoundtrackWarmer.TryGetPlayerBoardAsync(collectionManager);
            SoundtrackWarmer.AddBoardAsset(boardAssets, playerBoard);

            var opponentBoard = await SoundtrackWarmer.TryGetOpponentBoardAsync(collectionManager);
            SoundtrackWarmer.AddBoardAsset(boardAssets, opponentBoard);

            if (boardAssets.Count == 0)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Saved replay audio warmup found no player or opponent board assets."
                );
            }

            foreach (var boardAsset in boardAssets)
            {
                await SoundtrackWarmer.WarmBoardAudioAsync(soundManager, boardAsset!, stats);
            }

            await SoundtrackWarmer.WarmSoundtracksAsync(soundManager, collectionManager, boardAssets, stats);

            BppLog.Info(
                "AudioBankWarmer",
                "Saved replay audio warmup finished: "
                    + $"boardBanks(loaded={stats.BoardBanksLoaded}, alreadyLoaded={stats.BoardBanksAlreadyLoaded}, failed={stats.BoardBanksFailed}, skipped={stats.BoardBanksSkipped}) "
                    + $"soundtrackBanks(loaded={stats.SoundtrackBanksLoaded}, alreadyLoaded={stats.SoundtrackBanksAlreadyLoaded}, failed={stats.SoundtrackBanksFailed}, skipped={stats.SoundtrackBanksSkipped})"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "AudioBankWarmer",
                $"Saved replay audio warmup failed: {ex.Message}"
            );
        }
    }

    internal static void EnsureAudioReadyForPlayback()
    {
        try
        {
            LogAudioState("pre-fix");

            var gameServiceManager = Singleton<GameServiceManager>.Instance;
            if (gameServiceManager?.GamePaused == true)
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-1: GamePaused=true, calling PauseOrUnpauseGame(false)."
                );
                gameServiceManager.PauseOrUnpauseGame(toPauseOrUnpause: false);
            }

            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Replay audio readiness aborted: SoundManager unavailable."
                );
                return;
            }

            BppLog.Info(
                "AudioBankWarmer",
                "Replay audio readiness layer-2: SoundManager.PauseBusses(false)."
            );
            soundManager.PauseBusses(isPausing: false);

            StopAllTrackedSfxEventInstances(soundManager);
            ReassertSfxVolumeFromPreferences();

            LogAudioState("post-fix");
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "AudioBankWarmer",
                $"Replay audio readiness step failed: {ex.Message}"
            );
        }
    }

    internal static void LogAudioState(string label)
    {
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] SoundManager unavailable."
                );
                return;
            }

            var soundManagerType = typeof(SoundManager);
            foreach (var fieldName in DiagnosticBusPathFields)
            {
                LogBusState(label, soundManager, soundManagerType, fieldName);
            }

            try
            {
                var settingsField = soundManagerType.GetField(
                    "SoundSettings",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
                );
                if (settingsField?.GetValue(null) is SoundSettings soundSettings)
                {
                    LogVcaState(label, "SfxVCA", soundSettings.SfxVCA);
                    LogVcaState(label, "MusicVCA", soundSettings.MusicVCA);
                    LogVcaState(label, "VoVCA", soundSettings.VoVCA);
                }
                else
                {
                    BppLog.Info(
                        "AudioBankWarmer",
                        $"[ReplayAudioDiag/{label}] SoundSettings static field is null."
                    );
                }
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] VCA read failed: {ex.Message}"
                );
            }

            try
            {
                var dict = GetSfxEventInstancesDict(soundManager);
                if (dict != null)
                {
                    var keys = string.Join(",", dict.Keys);
                    BppLog.Info(
                        "AudioBankWarmer",
                        $"[ReplayAudioDiag/{label}] tracked-sfx count={dict.Count} keys=[{keys}]"
                    );
                }
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] tracked-sfx read failed: {ex.Message}"
                );
            }

            try
            {
                var pauseSnapshotField = soundManagerType.GetField(
                    "PauseSnapshot",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );
                if (pauseSnapshotField?.GetValue(soundManager) is EventReference pauseSnapshot)
                {
                    BppLog.Info(
                        "AudioBankWarmer",
                        $"[ReplayAudioDiag/{label}] PauseSnapshot.Guid={pauseSnapshot.Guid} isNull={pauseSnapshot.IsNull}"
                    );
                }
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] PauseSnapshot read failed: {ex.Message}"
                );
            }

            try
            {
                var gsm = Singleton<GameServiceManager>.Instance;
                BppLog.Info(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] GamePaused={gsm?.GamePaused} StateName={Data.CurrentState?.StateName}"
                );
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] GamePaused read failed: {ex.Message}"
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "AudioBankWarmer",
                $"[ReplayAudioDiag/{label}] diagnostics failed: {ex.Message}"
            );
        }
    }

    private static void LogBusState(
        string label,
        SoundManager soundManager,
        Type soundManagerType,
        string fieldName
    )
    {
        try
        {
            var field = soundManagerType.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
            var path = field?.GetValue(soundManager) as string;
            if (string.IsNullOrEmpty(path))
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] bus.{fieldName}=<no-path>"
                );
                return;
            }

            var bus = RuntimeManager.GetBus(path);
            if (!bus.isValid())
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] bus.{fieldName} path={path} invalid"
                );
                return;
            }

            bus.getPaused(out var paused);
            bus.getVolume(out var vol, out var finalVol);
            BppLog.Info(
                "AudioBankWarmer",
                $"[ReplayAudioDiag/{label}] bus.{fieldName} path={path} paused={paused} vol={vol:F3} final={finalVol:F3}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "AudioBankWarmer",
                $"[ReplayAudioDiag/{label}] bus.{fieldName} read failed: {ex.Message}"
            );
        }
    }

    private static void LogVcaState(string label, string vcaName, VCA vca)
    {
        try
        {
            if (!vca.isValid())
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    $"[ReplayAudioDiag/{label}] vca.{vcaName} invalid"
                );
                return;
            }

            vca.getVolume(out var vol, out var finalVol);
            BppLog.Info(
                "AudioBankWarmer",
                $"[ReplayAudioDiag/{label}] vca.{vcaName} vol={vol:F3} final={finalVol:F3}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "AudioBankWarmer",
                $"[ReplayAudioDiag/{label}] vca.{vcaName} read failed: {ex.Message}"
            );
        }
    }

    private static Dictionary<string, EventInstance>? GetSfxEventInstancesDict(
        SoundManager soundManager
    )
    {
        var sfxPlayer = soundManager.SFXPlayer;
        if (sfxPlayer == null)
            return null;

        var dictField = sfxPlayer
            .GetType()
            .GetField(
                "sfxEventInstances",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
            );
        return dictField?.GetValue(sfxPlayer) as Dictionary<string, EventInstance>;
    }

    private static void StopAllTrackedSfxEventInstances(SoundManager soundManager)
    {
        try
        {
            var dict = GetSfxEventInstancesDict(soundManager);
            if (dict == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-3: sfxEventInstances dictionary not accessible; skipping."
                );
                return;
            }

            if (dict.Count == 0)
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-3: no tracked SFX EventInstances to stop."
                );
                return;
            }

            var keys = dict.Keys.ToList();
            BppLog.Info(
                "AudioBankWarmer",
                $"Replay audio readiness layer-3: stopping {keys.Count} tracked SFX EventInstance(s): [{string.Join(",", keys)}]"
            );

            foreach (var key in keys)
            {
                try
                {
                    var instance = dict[key];
                    if (instance.isValid())
                    {
                        instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                        instance.release();
                    }
                }
                catch (Exception ex)
                {
                    BppLog.Warn(
                        "AudioBankWarmer",
                        $"Replay audio readiness layer-3: stop/release for key={key} failed: {ex.Message}"
                    );
                }
            }

            dict.Clear();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "AudioBankWarmer",
                $"Replay audio readiness layer-3 failed: {ex.Message}"
            );
        }
    }

    private static void ReassertSfxVolumeFromPreferences()
    {
        try
        {
            var prefs = PlayerPreferences.Data;
            if (prefs == null)
            {
                BppLog.Info(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-4: PlayerPreferences.Data is null; skipping."
                );
                return;
            }

            var setVolume = typeof(SoundManager).GetMethod(
                "SetVolume",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            );
            var volumeTypeEnum = typeof(SoundManager).GetNestedType(
                "VolumeType",
                BindingFlags.NonPublic | BindingFlags.Public
            );
            if (setVolume == null || volumeTypeEnum == null)
            {
                BppLog.Warn(
                    "AudioBankWarmer",
                    "Replay audio readiness layer-4: SoundManager.SetVolume/VolumeType not found; skipping."
                );
                return;
            }

            var sfxValue = Enum.Parse(volumeTypeEnum, "SFX");
            setVolume.Invoke(null, new[] { sfxValue, (object)prefs.VolumeSfx });
            BppLog.Info(
                "AudioBankWarmer",
                $"Replay audio readiness layer-4: re-asserted SFX volume to {prefs.VolumeSfx:F3} from PlayerPreferences."
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "AudioBankWarmer",
                $"Replay audio readiness layer-4 failed: {ex.Message}"
            );
        }
    }
}
