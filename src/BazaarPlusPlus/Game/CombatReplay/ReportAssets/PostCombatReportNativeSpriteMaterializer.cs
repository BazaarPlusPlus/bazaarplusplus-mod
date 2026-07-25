#nullable enable
using System.Collections;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.TagTypography;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

/// <summary>
/// Materializes native hero portraits and status glyphs into the same global content-addressed
/// cache as card previews. Render-key reservations happen before any native Sprite lookup, so a
/// valid cache hit never invokes the Unity/game materializer path.
/// </summary>
internal sealed class PostCombatReportNativeSpriteMaterializer : IDisposable
{
    private const int RenderKeySchemaVersion = 1;
    private const string RendererVersion = "1";
    private const string CaptureProfileVersion = "1";
    private const string EncoderVersion = "1";

    private readonly string _gameBuild;
    private readonly ReportAssetCache _cache;
    private readonly ReportAssetMaterializationCoordinator _materialization;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    internal PostCombatReportNativeSpriteMaterializer(string outputRoot, string? gameBuild)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException(
                "Report asset output root is required.",
                nameof(outputRoot)
            );

        _gameBuild = Normalize(gameBuild, "unknown-build");
        _cache = new ReportAssetCache(ResolveGlobalCacheRoot(outputRoot));
        _materialization = new ReportAssetMaterializationCoordinator(_cache);
    }

    internal IEnumerator MaterializeBattle(
        PvpBattleManifest manifest,
        CombatReportDocumentV1 document,
        Action onCompleted
    )
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (document == null)
            throw new ArgumentNullException(nameof(document));
        if (onCompleted == null)
            throw new ArgumentNullException(nameof(onCompleted));

        if (_disposed)
        {
            onCompleted();
            yield break;
        }

        Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var uniqueEntries = BuildBindings(manifest, document)
            .GroupBy(entry => entry.RenderKey.RenderKeyHash, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        foreach (var entry in uniqueEntries)
        {
            if (token.IsCancellationRequested)
                break;

            Task<ReportAssetMaterializationCoordinator.ReportAssetMaterializationReservation> reservationTask;
            try
            {
                reservationTask = _materialization.ReserveAsync(entry.RenderKey, token);
            }
            catch
            {
                continue;
            }

            while (!reservationTask.IsCompleted)
                yield return null;
            if (reservationTask.IsCanceled || reservationTask.IsFaulted)
                continue;

            var reservation = reservationTask.Result;
            using (reservation)
            {
                if (token.IsCancellationRequested)
                    break;
                if (reservation.Kind == ReportAssetMaterializationReservationKind.Hit)
                    continue;
                if (reservation.Kind == ReportAssetMaterializationReservationKind.Follower)
                {
                    while (!reservation.Completion.IsCompleted && !token.IsCancellationRequested)
                        yield return null;
                    continue;
                }

                Task<NativeReportSpriteLoadOutcome> loadTask;
                try
                {
                    loadTask = entry.LoadAsync(token);
                }
                catch
                {
                    continue;
                }

                while (!loadTask.IsCompleted && !token.IsCancellationRequested)
                    yield return null;
                if (
                    token.IsCancellationRequested
                    || loadTask.IsCanceled
                    || loadTask.IsFaulted
                    || !loadTask.Result.IsReady
                )
                {
                    continue;
                }

                Task<ReportAssetProducedFile>? exportTask = null;
                try
                {
                    exportTask = NativeReportSpritePngWriter.WriteAsync(
                        loadTask.Result.Sprite!,
                        reservation.StagingFilePath,
                        token
                    );
                }
                catch
                {
                    // Reservation disposal fails the singleflight; this individual optional icon
                    // degrades without erasing any other report asset.
                }
                if (exportTask == null)
                    continue;

                while (!exportTask.IsCompleted && !token.IsCancellationRequested)
                    yield return null;
                if (token.IsCancellationRequested || exportTask.IsCanceled || exportTask.IsFaulted)
                {
                    continue;
                }

                Task<ReportAssetResolvedAsset>? publishTask = null;
                try
                {
                    var produced = exportTask.Result;
                    publishTask = reservation.PublishAsync(
                        produced.PixelWidth,
                        produced.PixelHeight
                    );
                }
                catch
                {
                    // Optional native bindings degrade to the deterministic Viewer placeholder.
                }
                if (publishTask == null)
                    continue;
                while (!publishTask.IsCompleted)
                    yield return null;
                if (publishTask.IsFaulted)
                    _ = publishTask.Exception;
            }
        }

        onCompleted();
    }

    internal IReadOnlyList<PostCombatReportAssetFile> ListAvailableAssets(
        PvpBattleManifest manifest,
        CombatReportDocumentV1 document
    )
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        return ResolveAvailableAssets(BuildAssetLookupCandidates(manifest, document));
    }

    internal Task<IReadOnlyList<PostCombatReportAssetFile>> ListAvailableAssetsAsync(
        PvpBattleManifest manifest,
        CombatReportDocumentV1 document,
        CancellationToken cancellationToken = default
    )
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        // Build bindings on the Unity thread because the render key includes activeColorSpace.
        // The worker receives only immutable cache lookup data and never touches Unity objects.
        var candidates = BuildAssetLookupCandidates(manifest, document);
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ResolveAvailableAssets(candidates);
            },
            cancellationToken
        );
    }

    private IReadOnlyList<AssetLookupCandidate> BuildAssetLookupCandidates(
        PvpBattleManifest manifest,
        CombatReportDocumentV1 document
    ) =>
        BuildBindings(manifest, document)
            .Select(entry => new AssetLookupCandidate(
                entry.BindingKind,
                entry.BindingKey,
                entry.SemanticRole,
                entry.RenderKey
            ))
            .ToList();

    private IReadOnlyList<PostCombatReportAssetFile> ResolveAvailableAssets(
        IReadOnlyList<AssetLookupCandidate> candidates
    )
    {
        var result = new List<PostCombatReportAssetFile>();
        foreach (var entry in candidates)
        {
            if (!_cache.TryResolve(entry.RenderKey, out var resolved))
                continue;
            result.Add(
                new PostCombatReportAssetFile(
                    entry.BindingKind,
                    entry.BindingKey,
                    entry.SemanticRole,
                    resolved.FilePath,
                    resolved.RenderKeyHash,
                    resolved.ContentHash,
                    resolved.PixelWidth,
                    resolved.PixelHeight
                )
            );
        }
        return result;
    }

    internal void Cancel()
    {
        if (_cancellation is { IsCancellationRequested: false })
            _cancellation.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private IEnumerable<NativeSpriteBinding> BuildBindings(
        PvpBattleManifest manifest,
        CombatReportDocumentV1 document
    )
    {
        if (TryBuildHeroBinding(manifest.Participants?.PlayerHero, "player:Player", out var player))
        {
            yield return player;
        }
        if (
            TryBuildHeroBinding(
                manifest.Participants?.OpponentHero,
                "player:Opponent",
                out var opponent
            )
        )
        {
            yield return opponent;
        }

        var seenSemantics = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reportEvent in document.Events ?? [])
        {
            ReportStatusIconSemantic semantic;
            if (
                !ReportStatusIconSemanticResolver.TryGetByStableKey(
                    reportEvent.IconSemanticKey,
                    out semantic
                ) && !ReportStatusIconSemanticResolver.TryResolve(reportEvent, out semantic)
            )
            {
                continue;
            }
            if (!seenSemantics.Add(semantic.StableKey))
                continue;
            yield return BuildStatusBinding(semantic);
        }
    }

    private bool TryBuildHeroBinding(
        string? capturedHero,
        string entityId,
        out NativeSpriteBinding binding
    )
    {
        binding = null!;
        if (
            string.IsNullOrWhiteSpace(capturedHero)
            || !Enum.TryParse<EHero>(capturedHero, ignoreCase: false, out var hero)
            || !HeroPortraitSpriteProvider.IsRenderableHero(hero)
        )
        {
            return false;
        }

        var stableHero = $"{(int)hero}:{hero}";
        binding = new NativeSpriteBinding(
            PostCombatReportAssetBindingKind.Entity,
            entityId,
            "hero-portrait",
            BuildRenderKey(
                "hero-portrait",
                "hero:" + stableHero,
                "default-skin-for:" + stableHero,
                "default-game-portrait"
            ),
            token => LoadHeroAsync(hero, token)
        );
        return true;
    }

    private NativeSpriteBinding BuildStatusBinding(ReportStatusIconSemantic semantic) =>
        new(
            PostCombatReportAssetBindingKind.EventSemantic,
            semantic.StableKey,
            "status-effect-icon",
            BuildRenderKey(
                "status-effect-icon",
                semantic.StableKey,
                "native-tooltip-keyword-configuration",
                "attribute:" + semantic.NativeAttributeKey
            ),
            token => LoadStatusAsync(semantic, token)
        );

    private ReportAssetRenderKey BuildRenderKey(
        string assetType,
        string templateId,
        string resolvedSkinIdentity,
        string variant
    ) =>
        new(
            RenderKeySchemaVersion,
            _gameBuild,
            "native-runtime-assets:" + _gameBuild,
            "native-sprite-v1",
            resolvedSkinIdentity,
            "native-sprite-region",
            RendererVersion,
            assetType,
            templateId,
            "und",
            "native",
            "none",
            "none",
            "none",
            variant,
            new ReportAssetCaptureProfile(
                "native-sprite-region",
                CaptureProfileVersion,
                0,
                0,
                0,
                0,
                10_000,
                10_000,
                0,
                "#00000000",
                -1,
                QualitySettings.activeColorSpace.ToString(),
                "unity-texture2d-png",
                EncoderVersion
            ),
            Array.Empty<ReportAssetRenderAttribute>()
        );

    private static async Task<NativeReportSpriteLoadOutcome> LoadHeroAsync(
        EHero hero,
        CancellationToken token
    )
    {
        if (token.IsCancellationRequested)
            return NativeReportSpriteLoadOutcome.Unavailable;
        var outcome = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero);
        if (token.IsCancellationRequested || outcome?.Sprite == null || outcome.Identity == null)
            return NativeReportSpriteLoadOutcome.Unavailable;
        return new NativeReportSpriteLoadOutcome(outcome.Sprite, outcome.Identity.StableKey);
    }

    private static Task<NativeReportSpriteLoadOutcome> LoadStatusAsync(
        ReportStatusIconSemantic semantic,
        CancellationToken token
    )
    {
        if (token.IsCancellationRequested)
            return Task.FromResult(NativeReportSpriteLoadOutcome.Unavailable);
        var outcome = NativeStatusIconSpriteProvider.Resolve(semantic.NativeAttributeKey);
        return Task.FromResult(
            outcome.IsReady
                ? new NativeReportSpriteLoadOutcome(outcome.Sprite, outcome.StableNativeIdentity)
                : NativeReportSpriteLoadOutcome.Unavailable
        );
    }

    private static string ResolveGlobalCacheRoot(string outputRoot)
    {
        var fullPath = Path.GetFullPath(outputRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (
            string.Equals(
                Path.GetFileName(fullPath),
                "card-previews",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Report asset cache root has no parent.");
        }
        return fullPath;
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed record NativeSpriteBinding(
        PostCombatReportAssetBindingKind BindingKind,
        string BindingKey,
        string SemanticRole,
        ReportAssetRenderKey RenderKey,
        Func<CancellationToken, Task<NativeReportSpriteLoadOutcome>> LoadAsync
    );

    private sealed record AssetLookupCandidate(
        PostCombatReportAssetBindingKind BindingKind,
        string BindingKey,
        string SemanticRole,
        ReportAssetRenderKey RenderKey
    );
}

internal sealed record NativeReportSpriteLoadOutcome(Sprite? Sprite, string NativeIdentity)
{
    internal static NativeReportSpriteLoadOutcome Unavailable { get; } = new(null, string.Empty);

    internal bool IsReady => Sprite != null && !string.IsNullOrWhiteSpace(NativeIdentity);
}

internal static class NativeReportSpritePngWriter
{
    internal static Task<ReportAssetProducedFile> WriteAsync(
        Sprite sprite,
        string outputPath,
        CancellationToken cancellationToken
    )
    {
        if (sprite == null || sprite.texture == null)
            throw new InvalidDataException("Native report sprite has no texture.");
        if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
            throw new InvalidDataException("Rotated packed report sprites are not supported.");

        Rect rect;
        try
        {
            rect = sprite.textureRect;
        }
        catch (UnityException ex)
        {
            throw new InvalidDataException(
                "Tightly packed report sprites cannot be extracted without changing their art.",
                ex
            );
        }

        var width = Mathf.RoundToInt(rect.width);
        var height = Mathf.RoundToInt(rect.height);
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("Native report sprite has invalid geometry.");

        return NativeReportTexturePngExporter.ExportTextureAsync(
            sprite.texture,
            rect,
            width,
            height,
            outputPath,
            cancellationToken,
            ReportAssetReadbackSource.UnitySprite
        );
    }
}
