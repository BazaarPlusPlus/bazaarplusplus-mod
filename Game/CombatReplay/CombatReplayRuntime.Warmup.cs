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
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed partial class CombatReplayRuntime
{
    private static readonly ECardSize[] ReplayWarmupCardSizes =
    {
        ECardSize.Small,
        ECardSize.Medium,
        ECardSize.Large,
    };

    private static async Task WarmReplayPresentationAssetsAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new ReplayWarmupStats();
        await WarmReplayAssetLoaderAsync(manifest, sequence, stats);
        await WarmReplayCombatVfxAsync(sequence, stats);
        stopwatch.Stop();
        BppLog.Info(
            "CombatReplayRuntime",
            $"Saved replay warmup finished in {stopwatch.ElapsedMilliseconds}ms "
                + $"sharedAssets(preloaded={stats.SharedAssetsPreloaded}, skipped={stats.SharedAssetsSkipped}) "
                + $"cards(preloaded={stats.CardsPreloaded}, skipped={stats.CardsSkipped}, failed={stats.CardsFailed}) "
                + $"overrideAssets(preloaded={stats.OverrideAssetsPreloaded}, skipped={stats.OverrideAssetsSkipped}, failed={stats.OverrideAssetsFailed}) "
                + $"combatVfx(prewarmed={stats.VfxPrewarmed}, skipped={stats.VfxSkipped}, failed={stats.VfxFailed})"
        );
    }

    private static async Task WarmReplayAssetLoaderAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        if (assetLoader == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                "Saved replay visual warmup skipped because AssetLoader is unavailable."
            );
            return;
        }

        if (TryReserveReplaySharedAssetsPreload())
        {
            try
            {
                await assetLoader.PreloadAssets();
                stats.SharedAssetsPreloaded++;
            }
            catch (Exception ex)
            {
                ReleaseReplaySharedAssetsPreload();
                BppLog.Warn(
                    "CombatReplayRuntime",
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

        foreach (var snapshot in EnumerateReplayItemSnapshots(manifest))
        {
            if (!Guid.TryParse(snapshot.TemplateId, out var templateId))
                continue;

            var key = $"{templateId:N}:{snapshot.Size}";
            preloadRequests.TryAdd(key, (templateId, snapshot.Size));
        }

        var cardSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var cardWarmupTasks = preloadRequests.Select(request =>
            WarmReplayCardAsync(assetLoader, request.Key, request.Value, cardSemaphore, stats)
        );
        await Task.WhenAll(cardWarmupTasks);

        var overrideSemaphore = new SemaphoreSlim(ReplayWarmupConcurrency);
        var overrideWarmupTasks = sequence
            .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Select(overrideKey =>
                WarmReplayOverrideAssetAsync(assetLoader, overrideKey, overrideSemaphore, stats)
            );
        await Task.WhenAll(overrideWarmupTasks);
    }

    private static async Task WarmReplayCardAsync(
        AssetLoader assetLoader,
        string cacheKey,
        (Guid TemplateId, ECardSize Size) request,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (!TryReserveReplayCacheKey(ReplayPreloadedCardKeys, cacheKey))
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
            ReleaseReplayCacheKey(ReplayPreloadedCardKeys, cacheKey);
            stats.CardsFailed++;
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Saved replay card preload failed for template={request.TemplateId} size={request.Size}: {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task WarmReplayOverrideAssetAsync(
        AssetLoader assetLoader,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        if (!TryReserveReplayCacheKey(ReplayPreloadedOverrideKeys, overrideKey))
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
            ReleaseReplayCacheKey(ReplayPreloadedOverrideKeys, overrideKey);
            stats.OverrideAssetsFailed++;
            BppLog.Debug(
                "CombatReplayRuntime",
                $"Saved replay override VFX preload skipped for '{overrideKey}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static IEnumerable<CombatReplayCardSnapshot> EnumerateReplayItemSnapshots(
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

    private static async Task WarmReplayCombatVfxAsync(
        CombatSequenceMessages sequence,
        ReplayWarmupStats stats
    )
    {
        Services.TryGet<AssetLoader>(out var assetLoader);
        Services.TryGet<VFXManager>(out var vfxManager);
        if (assetLoader == null || vfxManager == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
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
                WarmReplayActionVfxAsync(assetLoader, vfxManager, action, vfxSemaphore, stats)
            );
        }

        foreach (
            var overrideKey in sequence
                .CombatMessage.Data.VfxKeys.Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
        )
        {
            vfxTasks.Add(
                WarmReplayOverrideVfxAsync(
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

    private static async Task WarmReplayActionVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        ActionType action,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetReplayVfxConfig(vfxManager);
        if (vfxConfig == null)
        {
            await WarmReplayVfxReferenceAsync(
                assetLoader,
                vfxManager.GetVFX(action),
                semaphore,
                stats
            );
            return;
        }

        if (TryIsActionAttributeMapped(vfxConfig, action))
        {
            foreach (var size in ReplayWarmupCardSizes)
            {
                await WarmReplayVfxReferenceAsync(
                    assetLoader,
                    TryGetMappedActionVfx(vfxConfig, size, action),
                    semaphore,
                    stats
                );
            }
        }

        await WarmReplayVfxReferenceAsync(assetLoader, vfxManager.GetVFX(action), semaphore, stats);
    }

    private static async Task WarmReplayOverrideVfxAsync(
        AssetLoader assetLoader,
        VFXManager vfxManager,
        IReadOnlyCollection<ActionType> actionTypes,
        string overrideKey,
        SemaphoreSlim semaphore,
        ReplayWarmupStats stats
    )
    {
        var vfxConfig = GetReplayVfxConfig(vfxManager);
        if (vfxConfig == null)
            return;

        foreach (var action in actionTypes)
        {
            foreach (var size in ReplayWarmupCardSizes)
            {
                await WarmReplayVfxReferenceAsync(
                    assetLoader,
                    await TryGetOverrideActionVfxAsync(vfxConfig, action, size, overrideKey),
                    semaphore,
                    stats
                );
            }
        }
    }

    private static object? GetReplayVfxConfig(VFXManager vfxManager)
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

    private static async Task WarmReplayVfxReferenceAsync(
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
        if (!TryReserveReplayCacheKey(ReplayPrewarmedVfxKeys, key))
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
            ReleaseReplayCacheKey(ReplayPrewarmedVfxKeys, key);
            stats.VfxFailed++;
            BppLog.Debug(
                "CombatReplayRuntime",
                $"Saved replay VFX warmup skipped for '{key}': {ex.Message}"
            );
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static bool TryReserveReplaySharedAssetsPreload()
    {
        lock (ReplayWarmupCacheLock)
        {
            if (ReplaySharedAssetsPreloaded)
                return false;

            ReplaySharedAssetsPreloaded = true;
            return true;
        }
    }

    private static void ReleaseReplaySharedAssetsPreload()
    {
        lock (ReplayWarmupCacheLock)
        {
            ReplaySharedAssetsPreloaded = false;
        }
    }

    private static bool TryReserveReplayCacheKey(HashSet<string> cache, string key)
    {
        lock (ReplayWarmupCacheLock)
        {
            return cache.Add(key);
        }
    }

    private static void ReleaseReplayCacheKey(HashSet<string> cache, string key)
    {
        lock (ReplayWarmupCacheLock)
        {
            cache.Remove(key);
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

    private static void WarmReplayAudioBanks()
    {
        try
        {
            var soundManager = Services.Get<SoundManager>();
            if (soundManager == null)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    "Saved replay audio warmup skipped because SoundManager is unavailable."
                );
                return;
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

            if (boardAssets.Count == 0)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    "Saved replay audio warmup found no HeroBoardController instances in the scene."
                );
                return;
            }

            foreach (var boardAsset in boardAssets)
            {
                WarmReplayAudioBank(soundManager, boardAsset!);
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn("CombatReplayRuntime", $"Saved replay audio warmup failed: {ex.Message}");
        }
    }

    private static void WarmReplayAudioBank(SoundManager soundManager, BoardAssetDataSO boardAsset)
    {
        if (string.IsNullOrWhiteSpace(boardAsset.boardBank))
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Board '{boardAsset.name}' has no boardBank; replay SFX may be incomplete."
            );
            return;
        }

        if (string.IsNullOrWhiteSpace(boardAsset.boardAssetBank))
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Board '{boardAsset.name}' has no boardAssetBank; replay SFX may be incomplete."
            );
            return;
        }

        BppLog.Info(
            "CombatReplayRuntime",
            $"Warm replay audio bank: board='{boardAsset.name}', metadata='{boardAsset.boardBank}', asset='{boardAsset.boardAssetBank}'"
        );
        soundManager.LoadBank(
            FModBank.EBankType.SFX,
            boardAsset.boardBank,
            boardAsset.boardAssetBank
        );
    }
}
