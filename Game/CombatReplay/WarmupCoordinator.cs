#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Assets.Scripts.Audio;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using FMOD.Studio;
using FMODUnity;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay;

// Process-wide caches: addressables/audio banks live for the lifetime of the running game, so the
// reservation set follows that lifetime, not any one CombatReplayRuntime instance.
internal static class WarmupCoordinator
{
    private const int ReplayWarmupConcurrency = 4;

    private static readonly object CacheLock = new();
    private static readonly HashSet<string> PreloadedCardKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> PreloadedOverrideKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> PrewarmedVfxKeys = new(StringComparer.Ordinal);
    private static bool SharedAssetsPreloaded;

    private static readonly ECardSize[] WarmupCardSizes =
    {
        ECardSize.Small,
        ECardSize.Medium,
        ECardSize.Large,
    };

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

    public static async Task WarmPresentationAssetsAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new ReplayWarmupStats();
        await WarmAssetLoaderAsync(manifest, sequence, stats);
        await WarmCombatVfxAsync(sequence, stats);
        stopwatch.Stop();
        BppLog.Info(
            "WarmupCoordinator",
            $"Saved replay warmup finished in {stopwatch.ElapsedMilliseconds}ms "
                + $"sharedAssets(preloaded={stats.SharedAssetsPreloaded}, skipped={stats.SharedAssetsSkipped}) "
                + $"cards(preloaded={stats.CardsPreloaded}, skipped={stats.CardsSkipped}, failed={stats.CardsFailed}) "
                + $"overrideAssets(preloaded={stats.OverrideAssetsPreloaded}, skipped={stats.OverrideAssetsSkipped}, failed={stats.OverrideAssetsFailed}) "
                + $"combatVfx(prewarmed={stats.VfxPrewarmed}, skipped={stats.VfxSkipped}, failed={stats.VfxFailed})"
        );
    }

    public static async Task WarmAudioBanksAsync()
    {
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
                    "Saved replay audio warmup skipped because SoundManager is unavailable."
                );
                return;
            }

            var stats = new ReplayAudioWarmupStats();
            var collectionManager = Services.Get<CollectionManager>();
            if (collectionManager == null)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
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

            var playerBoard = await TryGetPlayerBoardAsync(collectionManager);
            AddBoardAsset(boardAssets, playerBoard);

            var opponentBoard = await TryGetOpponentBoardAsync(collectionManager);
            AddBoardAsset(boardAssets, opponentBoard);

            if (boardAssets.Count == 0)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
                    "Saved replay audio warmup found no player or opponent board assets."
                );
            }

            foreach (var boardAsset in boardAssets)
            {
                await WarmBoardAudioAsync(soundManager, boardAsset!, stats);
            }

            await WarmSoundtracksAsync(soundManager, collectionManager, boardAssets, stats);

            BppLog.Info(
                "WarmupCoordinator",
                "Saved replay audio warmup finished: "
                    + $"boardBanks(loaded={stats.BoardBanksLoaded}, alreadyLoaded={stats.BoardBanksAlreadyLoaded}, failed={stats.BoardBanksFailed}, skipped={stats.BoardBanksSkipped}) "
                    + $"soundtrackBanks(loaded={stats.SoundtrackBanksLoaded}, alreadyLoaded={stats.SoundtrackBanksAlreadyLoaded}, failed={stats.SoundtrackBanksFailed}, skipped={stats.SoundtrackBanksSkipped})"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay audio warmup failed: {ex.Message}"
            );
        }
    }

    public static void EnsureAudioReadyForPlayback()
    {
        try
        {
            LogAudioState("pre-fix");

            var gameServiceManager = Singleton<GameServiceManager>.Instance;
            if (gameServiceManager?.GamePaused == true)
            {
                BppLog.Info(
                    "WarmupCoordinator",
                    "Replay audio readiness layer-1: GamePaused=true, calling PauseOrUnpauseGame(false)."
                );
                gameServiceManager.PauseOrUnpauseGame(toPauseOrUnpause: false);
            }

            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
                    "Replay audio readiness aborted: SoundManager unavailable."
                );
                return;
            }

            BppLog.Info(
                "WarmupCoordinator",
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
                "WarmupCoordinator",
                $"Replay audio readiness step failed: {ex.Message}"
            );
        }
    }

    public static void LogAudioState(string label)
    {
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
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
                        "WarmupCoordinator",
                        $"[ReplayAudioDiag/{label}] SoundSettings static field is null."
                    );
                }
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
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
                        "WarmupCoordinator",
                        $"[ReplayAudioDiag/{label}] tracked-sfx count={dict.Count} keys=[{keys}]"
                    );
                }
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
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
                        "WarmupCoordinator",
                        $"[ReplayAudioDiag/{label}] PauseSnapshot.Guid={pauseSnapshot.Guid} isNull={pauseSnapshot.IsNull}"
                    );
                }
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
                    $"[ReplayAudioDiag/{label}] PauseSnapshot read failed: {ex.Message}"
                );
            }

            try
            {
                var gsm = Singleton<GameServiceManager>.Instance;
                BppLog.Info(
                    "WarmupCoordinator",
                    $"[ReplayAudioDiag/{label}] GamePaused={gsm?.GamePaused} StateName={Data.CurrentState?.StateName}"
                );
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "WarmupCoordinator",
                    $"[ReplayAudioDiag/{label}] GamePaused read failed: {ex.Message}"
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"[ReplayAudioDiag/{label}] diagnostics failed: {ex.Message}"
            );
        }
    }

    private static async Task WarmAssetLoaderAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        if (assetLoader == null)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                "Saved replay visual warmup skipped because AssetLoader is unavailable."
            );
            return;
        }

        if (TryReserveSharedAssetsPreload())
        {
            try
            {
                await assetLoader.PreloadAssets();
                stats.SharedAssetsPreloaded++;
            }
            catch (Exception ex)
            {
                ReleaseSharedAssetsPreload();
                BppLog.Warn(
                    "WarmupCoordinator",
                    $"Saved replay asset preload failed: {ex.Message}"
                );
            }
        }
        else
        {
            stats.SharedAssetsSkipped++;
        }

        var preloadRequests = new Dictionary<string, (Guid TemplateId, ECardSize Size)>(
            StringComparer.Ordinal
        );

        foreach (var snapshot in EnumerateItemSnapshots(manifest))
        {
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;

            var key = $"{templateId:N}:{snapshot.Size}";
            preloadRequests.TryAdd(key, (templateId, snapshot.Size));
        }

        var cardSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var cardWarmupTasks = preloadRequests.Select(request =>
            WarmCardAsync(assetLoader, request.Key, request.Value, cardSemaphore, stats)
        );
        await Task.WhenAll(cardWarmupTasks);

        var overrideSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var overrideWarmupTasks = sequence
            .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Select(overrideKey =>
                WarmOverrideAssetAsync(assetLoader, overrideKey, overrideSemaphore, stats)
            );
        await Task.WhenAll(overrideWarmupTasks);
    }

    private static async Task WarmCardAsync(
        AssetLoader assetLoader,
        string cacheKey,
        (Guid TemplateId, ECardSize Size) request,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (!TryReserveCacheKey(PreloadedCardKeys, cacheKey))
        {
            stats.CardsSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            await assetLoader.PreloadCard(request.TemplateId, request.Size);
            stats.CardsPreloaded++;
        }
        catch (Exception ex)
        {
            ReleaseCacheKey(PreloadedCardKeys, cacheKey);
            stats.CardsFailed++;
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay card preload failed for template={request.TemplateId} size={request.Size}: {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task WarmOverrideAssetAsync(
        AssetLoader assetLoader,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (!TryReserveCacheKey(PreloadedOverrideKeys, overrideKey))
        {
            stats.OverrideAssetsSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            _ = await assetLoader.LoadAssetAsyncByAddress<GameObject>(overrideKey);
            stats.OverrideAssetsPreloaded++;
        }
        catch (Exception ex)
        {
            ReleaseCacheKey(PreloadedOverrideKeys, overrideKey);
            stats.OverrideAssetsFailed++;
            BppLog.Debug(
                "WarmupCoordinator",
                $"Saved replay override VFX preload skipped for '{overrideKey}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static IEnumerable<CombatReplayCardSnapshot> EnumerateItemSnapshots(
        PvpBattleManifest manifest
    )
    {
        foreach (
            var capture in new[] { manifest.Snapshots.PlayerHand, manifest.Snapshots.OpponentHand }
        )
        {
            if (capture.Status == PvpBattleCaptureStatus.Missing || capture.Items == null)
                continue;

            foreach (var snapshot in capture.Items)
            {
                if (snapshot?.Type == ECardType.Item)
                    yield return snapshot;
            }
        }
    }

    private static async Task WarmCombatVfxAsync(
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        Services.TryGet<VFXManager>(out var vfxManager);
        if (assetLoader == null || vfxManager == null)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                "Saved replay combat VFX warmup skipped because replay asset services are unavailable."
            );
            return;
        }

        var actionTypes = sequence
            .CombatMessage.Data.Frames.SelectMany(frame =>
                frame?.Events ?? Enumerable.Empty<ICombatSimEvent>()
            )
            .OfType<CombatSimEventEffectExecuted>()
            .Select(evt => DTOUtils.GetActionType(evt.ActionType))
            .Where(action => action != ActionType.Unknown)
            .Distinct()
            .ToList();
        var vfxSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var vfxTasks = new List<Task>();

        foreach (var action in actionTypes)
        {
            vfxTasks.Add(
                WarmActionVfxAsync(assetLoader, vfxManager, action, vfxSemaphore, stats)
            );
        }

        foreach (
            var overrideKey in sequence
                .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
        )
        {
            vfxTasks.Add(
                WarmOverrideVfxAsync(
                    assetLoader,
                    vfxManager,
                    actionTypes,
                    overrideKey,
                    vfxSemaphore,
                    stats
                )
            );
        }
        await Task.WhenAll(vfxTasks);
    }

    private static async Task WarmActionVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        ActionType action,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetVfxConfig(vfxManager);
        if (vfxConfig == null)
        {
            await WarmVfxReferenceAsync(
                assetLoader,
                vfxManager.GetVFX(action),
                semaphore,
                stats
            );
            return;
        }

        if (TryIsActionAttributeMapped(vfxConfig, action))
        {
            foreach (var size in WarmupCardSizes)
            {
                await WarmVfxReferenceAsync(
                    assetLoader,
                    TryGetMappedActionVfx(vfxConfig, size, action),
                    semaphore,
                    stats
                );
            }
        }

        await WarmVfxReferenceAsync(assetLoader, vfxManager.GetVFX(action), semaphore, stats);
    }

    private static async Task WarmOverrideVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        IReadOnlyCollection<ActionType> actionTypes,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetVfxConfig(vfxManager);
        if (vfxConfig == null)
            return;

        foreach (var action in actionTypes)
        {
            foreach (var size in WarmupCardSizes)
            {
                await WarmVfxReferenceAsync(
                    assetLoader,
                    await TryGetOverrideActionVfxAsync(vfxConfig, action, size, overrideKey),
                    semaphore,
                    stats
                );
            }
        }
    }

    private static object? GetVfxConfig(VFXManager vfxManager)
    {
        return vfxManager
            .GetType()
            .GetField(
                "vfxManagerSO",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?.GetValue(vfxManager);
    }

    private static bool TryIsActionAttributeMapped(object vfxConfig, ActionType action)
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "IsActionAttributeMapped",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        return method?.Invoke(vfxConfig, new object[] { action }) as bool? == true;
    }

    private static AssetReference? TryGetMappedActionVfx(
        object vfxConfig,
        ECardSize size,
        ActionType action
    )
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "GetVFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ECardSize), typeof(ActionType) },
                null
            );
        return method?.Invoke(vfxConfig, new object[] { size, action }) as AssetReference;
    }

    private static async Task<AssetReference?> TryGetOverrideActionVfxAsync(
        object vfxConfig,
        ActionType action,
        ECardSize size,
        string overrideKey
    )
    {
        var method = vfxConfig
            .GetType()
            .GetMethod(
                "GetActionOverrideVFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ActionType), typeof(ECardSize), typeof(string) },
                null
            );
        if (method == null)
            return null;

        var taskObject = method.Invoke(vfxConfig, new object[] { action, size, overrideKey });
        if (taskObject is Task<AssetReference> typedTask)
            return await typedTask;

        if (taskObject is not Task task)
            return taskObject as AssetReference;

        await task;
        return task.GetType()
                .GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(task) as AssetReference;
    }

    private static async Task WarmVfxReferenceAsync(
        AssetLoader assetLoader,
        AssetReference? assetReference,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (assetReference == null || !assetReference.RuntimeKeyIsValid())
            return;

        var key = !string.IsNullOrWhiteSpace(assetReference.AssetGUID)
            ? assetReference.AssetGUID
            : assetReference.ToString();
        if (!TryReserveCacheKey(PrewarmedVfxKeys, key))
        {
            stats.VfxSkipped++;
            return;
        }

        await semaphore.WaitAsync();
        try
        {
            _ = await assetLoader.LoadAssetAsyncByReference<GameObject>(assetReference);
            stats.VfxPrewarmed++;
        }
        catch (Exception ex)
        {
            ReleaseCacheKey(PrewarmedVfxKeys, key);
            stats.VfxFailed++;
            BppLog.Debug(
                "WarmupCoordinator",
                $"Saved replay VFX warmup skipped for '{key}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static bool TryReserveSharedAssetsPreload()
    {
        lock (CacheLock)
        {
            if (SharedAssetsPreloaded)
                return false;

            SharedAssetsPreloaded = true;
            return true;
        }
    }

    private static void ReleaseSharedAssetsPreload()
    {
        lock (CacheLock)
        {
            SharedAssetsPreloaded = false;
        }
    }

    private static bool TryReserveCacheKey(HashSet<string> cache, string key)
    {
        lock (CacheLock)
        {
            return cache.Add(key);
        }
    }

    private static void ReleaseCacheKey(HashSet<string> cache, string key)
    {
        lock (CacheLock)
        {
            cache.Remove(key);
        }
    }

    private static async Task<BoardAssetDataSO?> TryGetPlayerBoardAsync(
        CollectionManager? collectionManager
    )
    {
        if (collectionManager == null)
            return null;

        try
        {
            return await collectionManager.GetEquippedBoard();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay audio warmup could not resolve player board audio: {ex.Message}"
            );
            return null;
        }
    }

    private static async Task<BoardAssetDataSO?> TryGetOpponentBoardAsync(
        CollectionManager? collectionManager
    )
    {
        var loadout = Data.SimPvpOpponent?.PlayerLoadout;
        if (collectionManager == null || loadout == null)
            return null;

        try
        {
#pragma warning disable CS0618
            return await collectionManager.GetEquippedBoard(loadout);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay audio warmup could not resolve opponent board audio: {ex.Message}"
            );
            return null;
        }
    }

    private static void AddBoardAsset(
        ICollection<BoardAssetDataSO> boardAssets,
        BoardAssetDataSO? boardAsset
    )
    {
        if (
            boardAsset == null
            || boardAssets.Any(existing => ReferenceEquals(existing, boardAsset))
        )
            return;

        boardAssets.Add(boardAsset);
    }

    private static async Task WarmBoardAudioAsync(
        SoundManager soundManager,
        BoardAssetDataSO boardAsset,
        ReplayAudioWarmupStats stats
    )
    {
        if (string.IsNullOrWhiteSpace(boardAsset.boardBank))
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Board '{boardAsset.name}' has no boardBank; replay SFX may be incomplete."
            );
            stats.BoardBanksSkipped++;
            return;
        }

        if (string.IsNullOrWhiteSpace(boardAsset.boardAssetBank))
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Board '{boardAsset.name}' has no boardAssetBank; replay SFX may be incomplete."
            );
            stats.BoardBanksSkipped++;
            return;
        }

        var wasMetadataLoaded = soundManager.IsBankLoaded(boardAsset.boardBank, isMetadata: false);
        var wasAssetLoaded = soundManager.IsBankLoaded(boardAsset.boardAssetBank, isMetadata: false);
        BppLog.Info(
            "WarmupCoordinator",
            $"Warm replay audio bank: board='{boardAsset.name}', metadata='{boardAsset.boardBank}', asset='{boardAsset.boardAssetBank}'"
        );
        var loaded = await soundManager.LoadBankAsync(
            FModBank.EBankType.SFX,
            boardAsset.boardBank,
            boardAsset.boardAssetBank
        );
        if (!loaded)
        {
            stats.BoardBanksFailed++;
            return;
        }

        if (wasMetadataLoaded && wasAssetLoaded)
            stats.BoardBanksAlreadyLoaded++;
        else
            stats.BoardBanksLoaded++;
    }

    private static async Task WarmSoundtracksAsync(
        SoundManager soundManager,
        CollectionManager? collectionManager,
        IReadOnlyCollection<BoardAssetDataSO> boardAssets,
        ReplayAudioWarmupStats stats
    )
    {
        var warmedAny = false;
        var warmedSoundtracks = new HashSet<string>(StringComparer.Ordinal);
        warmedAny |= await WarmSoundtrackAsync(
            soundManager,
            await TryGetSoundtrackAsync(collectionManager),
            stats,
            warmedSoundtracks,
            setPlayingSoundtrack: true
        );

        foreach (var boardAsset in boardAssets)
        {
            warmedAny |= await WarmSoundtrackAsync(
                soundManager,
                boardAsset.soundtrack,
                stats,
                warmedSoundtracks,
                setPlayingSoundtrack: soundManager.PlayingSoundTrackSO == null
            );
        }

        if (!warmedAny)
        {
            stats.SoundtrackBanksSkipped++;
            BppLog.Warn(
                "WarmupCoordinator",
                "Saved replay audio warmup could not resolve any soundtrack; replay combat music fallback may be incomplete."
            );
        }
    }

    private static async Task<bool> WarmSoundtrackAsync(
        SoundManager soundManager,
        SoundtrackSO? soundtrack,
        ReplayAudioWarmupStats stats,
        ISet<string> warmedSoundtracks,
        bool setPlayingSoundtrack
    )
    {
        if (soundtrack == null)
            return false;

        var key = !string.IsNullOrWhiteSpace(soundtrack.SoundtrackPath)
            ? soundtrack.SoundtrackPath
            : soundtrack.name;
        if (!string.IsNullOrWhiteSpace(key) && warmedSoundtracks.Contains(key))
            return true;

        var loadedSoundtrack = await TryLoadSoundtrackAssetAsync(soundtrack);
        if (loadedSoundtrack == null)
        {
            stats.SoundtrackBanksFailed++;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(key))
            warmedSoundtracks.Add(key);

        if (loadedSoundtrack.MusicTracks == null || loadedSoundtrack.MusicTracks.Length == 0)
        {
            stats.SoundtrackBanksSkipped++;
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay soundtrack '{loadedSoundtrack.name}' has no music tracks to warm."
            );
            return false;
        }

        if (setPlayingSoundtrack)
            soundManager.PlayingSoundTrackSO = loadedSoundtrack;

        for (uint trackIndex = 0; trackIndex < loadedSoundtrack.MusicTracks.Length; trackIndex++)
        {
            await WarmSoundtrackTrackAsync(
                soundManager,
                loadedSoundtrack,
                trackIndex,
                stats
            );
        }

        return true;
    }

    private static async Task<SoundtrackSO?> TryGetSoundtrackAsync(
        CollectionManager? collectionManager
    )
    {
        if (collectionManager == null)
            return null;

        try
        {
            var soundtrack = await collectionManager.GetEquippedSoundtrack();
            return soundtrack != null ? soundtrack.SoundtrackObject : null;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay audio warmup could not resolve equipped soundtrack: {ex.Message}"
            );
            return null;
        }
    }

    private static async Task<SoundtrackSO?> TryLoadSoundtrackAssetAsync(
        SoundtrackSO soundtrack
    )
    {
        if (string.IsNullOrWhiteSpace(soundtrack.SoundtrackPath))
            return soundtrack;

        try
        {
            var handle = Addressables.LoadAssetAsync<SoundtrackSO>(soundtrack.SoundtrackPath);
            await handle.Task;
            if (
                handle.Status
                == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
            )
                return handle.Result;

            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay soundtrack load failed for path '{soundtrack.SoundtrackPath}'."
            );
            return soundtrack;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay soundtrack load failed for path '{soundtrack.SoundtrackPath}': {ex.Message}"
            );
            return soundtrack;
        }
    }

    private static bool TryGetSoundtrackTrackBanks(
        SoundtrackSO soundtrack,
        uint trackIndex,
        out string? metadataBank,
        out string? assetBank
    )
    {
        metadataBank = null;
        assetBank = null;

        try
        {
            var soundtrackType = soundtrack.GetType();
            var trackBankNameMethod = soundtrackType.GetMethod(
                "TrackBankName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(uint), typeof(bool) },
                null
            );
            if (trackBankNameMethod != null)
            {
                metadataBank = trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex, false })
                    as string;
                assetBank = trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex, true })
                    as string;
                return !string.IsNullOrWhiteSpace(metadataBank)
                    && !string.IsNullOrWhiteSpace(assetBank);
            }

            trackBankNameMethod = soundtrackType.GetMethod(
                "TrackBankName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(uint) },
                null
            );
            if (trackBankNameMethod == null)
                return false;

            metadataBank = trackBankNameMethod.Invoke(soundtrack, new object[] { trackIndex }) as string;
            assetBank = string.IsNullOrWhiteSpace(metadataBank) ? null : metadataBank + ".assets";
            return !string.IsNullOrWhiteSpace(metadataBank) && !string.IsNullOrWhiteSpace(assetBank);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay soundtrack '{soundtrack.name}' track {trackIndex} bank metadata lookup failed: {ex.Message}"
            );
            return false;
        }
    }

    private static async Task WarmSoundtrackTrackAsync(
        SoundManager soundManager,
        SoundtrackSO soundtrack,
        uint trackIndex,
        ReplayAudioWarmupStats stats
    )
    {
        if (
            !TryGetSoundtrackTrackBanks(
                soundtrack,
                trackIndex,
                out var metadataBank,
                out var assetBank
            )
        )
        {
            stats.SoundtrackBanksSkipped++;
            BppLog.Warn(
                "WarmupCoordinator",
                $"Saved replay soundtrack '{soundtrack.name}' track {trackIndex} has incomplete bank metadata."
            );
            return;
        }

        var wasMetadataLoaded = soundManager.IsBankLoaded(metadataBank, isMetadata: false);
        var wasAssetLoaded = soundManager.IsBankLoaded(assetBank, isMetadata: false);
        var loaded = await soundManager.LoadBankAsync(
            FModBank.EBankType.Music,
            metadataBank,
            assetBank
        );
        if (!loaded)
        {
            stats.SoundtrackBanksFailed++;
            return;
        }

        if (wasMetadataLoaded && wasAssetLoaded)
            stats.SoundtrackBanksAlreadyLoaded++;
        else
            stats.SoundtrackBanksLoaded++;
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
                    "WarmupCoordinator",
                    $"[ReplayAudioDiag/{label}] bus.{fieldName}=<no-path>"
                );
                return;
            }

            var bus = RuntimeManager.GetBus(path);
            if (!bus.isValid())
            {
                BppLog.Info(
                    "WarmupCoordinator",
                    $"[ReplayAudioDiag/{label}] bus.{fieldName} path={path} invalid"
                );
                return;
            }

            bus.getPaused(out var paused);
            bus.getVolume(out var vol, out var finalVol);
            BppLog.Info(
                "WarmupCoordinator",
                $"[ReplayAudioDiag/{label}] bus.{fieldName} path={path} paused={paused} vol={vol:F3} final={finalVol:F3}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
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
                    "WarmupCoordinator",
                    $"[ReplayAudioDiag/{label}] vca.{vcaName} invalid"
                );
                return;
            }

            vca.getVolume(out var vol, out var finalVol);
            BppLog.Info(
                "WarmupCoordinator",
                $"[ReplayAudioDiag/{label}] vca.{vcaName} vol={vol:F3} final={finalVol:F3}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
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
                    "WarmupCoordinator",
                    "Replay audio readiness layer-3: sfxEventInstances dictionary not accessible; skipping."
                );
                return;
            }

            if (dict.Count == 0)
            {
                BppLog.Info(
                    "WarmupCoordinator",
                    "Replay audio readiness layer-3: no tracked SFX EventInstances to stop."
                );
                return;
            }

            var keys = dict.Keys.ToList();
            BppLog.Info(
                "WarmupCoordinator",
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
                        "WarmupCoordinator",
                        $"Replay audio readiness layer-3: stop/release for key={key} failed: {ex.Message}"
                    );
                }
            }

            dict.Clear();
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
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
                    "WarmupCoordinator",
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
                    "WarmupCoordinator",
                    "Replay audio readiness layer-4: SoundManager.SetVolume/VolumeType not found; skipping."
                );
                return;
            }

            var sfxValue = Enum.Parse(volumeTypeEnum, "SFX");
            setVolume.Invoke(null, new[] { sfxValue, (object)prefs.VolumeSfx });
            BppLog.Info(
                "WarmupCoordinator",
                $"Replay audio readiness layer-4: re-asserted SFX volume to {prefs.VolumeSfx:F3} from PlayerPreferences."
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "WarmupCoordinator",
                $"Replay audio readiness layer-4 failed: {ex.Message}"
            );
        }
    }

    private sealed class ReplayWarmupStats
    {
        public int SharedAssetsPreloaded;
        public int SharedAssetsSkipped;
        public int CardsPreloaded;
        public int CardsSkipped;
        public int CardsFailed;
        public int OverrideAssetsPreloaded;
        public int OverrideAssetsSkipped;
        public int OverrideAssetsFailed;
        public int VfxPrewarmed;
        public int VfxSkipped;
        public int VfxFailed;
    }

    private sealed class ReplayAudioWarmupStats
    {
        public int BoardBanksLoaded;
        public int BoardBanksAlreadyLoaded;
        public int BoardBanksFailed;
        public int BoardBanksSkipped;
        public int SoundtrackBanksLoaded;
        public int SoundtrackBanksAlreadyLoaded;
        public int SoundtrackBanksFailed;
        public int SoundtrackBanksSkipped;
    }
}
