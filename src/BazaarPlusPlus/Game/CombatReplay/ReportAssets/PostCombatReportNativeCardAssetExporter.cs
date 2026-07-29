#nullable enable
using System.Collections;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.StaticCards;
using TheBazaar.AppFramework;
using TheBazaar.Game.CardFrames;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CombatReplay.ReportAssets;

/// <summary>
/// Cache-first exporter for report card art. Skills copy their native ArtKey texture directly;
/// items render only the template illustration layer from the game's Collection
/// ItemVisualsController through a private URP camera. No object in this pipeline is attached to a
/// display canvas or captured from the main backbuffer.
/// </summary>
internal sealed class PostCombatReportNativeCardAssetExporter : IDisposable
{
    private const string ExportLayerName = "Inspection_Overlay";
    private const int RenderKeySchemaVersion = 1;
    private const int SkillOutputPixels = 512;
    private const int ItemOutputHeight = 512;
    private const int ItemTransparentCropPaddingPixels = 0;
    private const string SkillRendererVersion = "2";
    private const string ItemRendererVersion = "12";
    private const string CaptureProfileVersion = "8";
    private const string EncoderVersion = "2.1.11";
    private static readonly WaitForEndOfFrame OffscreenFrameBoundary = new();

    private readonly string _gameBuild;
    private readonly int _exportLayer;
    private readonly ReportAssetCache _cache;
    private readonly ReportAssetMaterializationCoordinator _materialization;
    private CancellationTokenSource? _cancellation;
    private OffscreenItemPreviewRenderer? _itemRenderer;
    private bool _disposed;

    internal PostCombatReportNativeCardAssetExporter(string outputRoot, string? gameBuild)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException(
                "Report asset output root is required.",
                nameof(outputRoot)
            );

        _gameBuild = Normalize(gameBuild, "unknown-build");
        _exportLayer = LayerMask.NameToLayer(ExportLayerName);
        if (_exportLayer < 0)
            throw new InvalidOperationException(
                $"Required native report layer '{ExportLayerName}' is unavailable."
            );
        _cache = new ReportAssetCache(outputRoot);
        _materialization = new ReportAssetMaterializationCoordinator(_cache);
    }

    internal IEnumerator MaterializeBattle(PvpBattleManifest manifest, Action onCompleted)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
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
        var cacheHitCount = 0;
        var cacheFollowerCount = 0;
        var cacheMissCount = 0;
        var unityMaterializerInvocationCount = 0;

        foreach (var entry in SelectUniqueMaterializations(manifest))
        {
            if (token.IsCancellationRequested)
                break;

            Task<ReportAssetMaterializationCoordinator.ReportAssetMaterializationReservation> reservationTask;
            try
            {
                // This cache reservation is deliberately the first operation. A valid hit must not
                // construct a factory, touch Addressables, create a Camera/RT, or invoke readback.
                // Cache locking, hashing, and PNG validation are filesystem/CPU work and must not
                // block the Unity main thread.
                reservationTask = _materialization.ReserveAsync(entry.RenderKey, token);
            }
            catch (Exception ex)
            {
                ReportFailure(manifest.BattleId, entry.TemplateId, null, ex);
                continue;
            }

            while (!reservationTask.IsCompleted)
                yield return null;
            if (reservationTask.IsCanceled || reservationTask.IsFaulted)
            {
                ReportFailure(
                    manifest.BattleId,
                    entry.TemplateId,
                    null,
                    reservationTask.Exception?.GetBaseException()
                );
                continue;
            }

            var reservation = reservationTask.Result;
            using (reservation)
            {
                if (token.IsCancellationRequested)
                    break;
                if (reservation.Kind == ReportAssetMaterializationReservationKind.Hit)
                {
                    cacheHitCount++;
                    continue;
                }
                if (reservation.Kind == ReportAssetMaterializationReservationKind.Follower)
                {
                    cacheFollowerCount++;
                    while (!reservation.Completion.IsCompleted && !token.IsCancellationRequested)
                        yield return null;
                    continue;
                }

                cacheMissCount++;
                unityMaterializerInvocationCount++;
                var operation = entry.IsSkill
                    ? MaterializeSkill(manifest.BattleId, entry, reservation, token)
                    : MaterializeItem(manifest.BattleId, entry, reservation, token);
                try
                {
                    while (operation.MoveNext())
                        yield return operation.Current;
                }
                finally
                {
                    (operation as IDisposable)?.Dispose();
                }
            }
        }

        PostCombatReportAssetDiagnostics.ReportBatchCompleted(
            manifest.BattleId,
            cacheHitCount,
            cacheFollowerCount,
            cacheMissCount,
            unityMaterializerInvocationCount
        );
        onCompleted();
    }

    internal IReadOnlyList<PostCombatReportAssetFile> ListAvailableCardAssets(
        PvpBattleManifest manifest
    )
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        return ResolveAvailableCardAssets(BuildAssetLookupCandidates(manifest));
    }

    internal Task<IReadOnlyList<PostCombatReportAssetFile>> ListAvailableCardAssetsAsync(
        PvpBattleManifest manifest,
        CancellationToken cancellationToken = default
    )
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        // Build render identities on the Unity thread because the render key includes the active
        // Unity color space. Only immutable strings/records cross into the worker.
        var candidates = BuildAssetLookupCandidates(manifest);
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ResolveAvailableCardAssets(candidates);
            },
            cancellationToken
        );
    }

    private IReadOnlyList<AssetLookupCandidate> BuildAssetLookupCandidates(
        PvpBattleManifest manifest
    )
    {
        var candidates = new List<AssetLookupCandidate>();
        foreach (var snapshot in EnumerateReportCards(manifest))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;
            if (!TryBuildEntry(snapshot, out var entry))
                continue;
            candidates.Add(
                new AssetLookupCandidate(
                    snapshot.InstanceId,
                    entry.IsSkill ? "skill-art" : "item-card-preview",
                    entry.RenderKey,
                    BppCardDisplayName.Resolve(entry.Identity.Template, snapshot.Name)
                )
            );
        }
        return candidates;
    }

    private IReadOnlyList<PostCombatReportAssetFile> ResolveAvailableCardAssets(
        IReadOnlyList<AssetLookupCandidate> candidates
    )
    {
        var result = new List<PostCombatReportAssetFile>();
        foreach (var candidate in candidates)
        {
            if (!_cache.TryResolve(candidate.RenderKey, out var resolved))
                continue;

            result.Add(
                new PostCombatReportAssetFile(
                    PostCombatReportAssetBindingKind.Entity,
                    candidate.BindingKey,
                    candidate.SemanticRole,
                    resolved.FilePath,
                    resolved.RenderKeyHash,
                    resolved.ContentHash,
                    resolved.PixelWidth,
                    resolved.PixelHeight,
                    candidate.DisplayName
                )
            );
        }
        return result;
    }

    internal void Cancel()
    {
        if (_cancellation is { IsCancellationRequested: false })
            _cancellation.Cancel();
        _itemRenderer?.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _itemRenderer?.Dispose();
        _itemRenderer = null;
    }

    private IEnumerator MaterializeSkill(
        string? battleId,
        MaterializationEntry entry,
        ReportAssetMaterializationCoordinator.ReportAssetMaterializationReservation reservation,
        CancellationToken token
    )
    {
        Task<Texture?> loadTask;
        try
        {
            loadTask = LoadSkillTextureAsync(entry.Identity, token);
        }
        catch (Exception ex)
        {
            ReportFailure(battleId, entry.TemplateId, reservation.StagingFilePath, ex);
            yield break;
        }

        while (!loadTask.IsCompleted && !token.IsCancellationRequested)
            yield return null;
        if (
            token.IsCancellationRequested
            || loadTask.IsCanceled
            || loadTask.IsFaulted
            || loadTask.Result == null
        )
        {
            ReportFailure(
                battleId,
                entry.TemplateId,
                reservation.StagingFilePath,
                loadTask.Exception?.GetBaseException()
            );
            yield break;
        }

        var texture = loadTask.Result;
        Task<ReportAssetProducedFile> exportTask;
        try
        {
            exportTask = NativeReportTexturePngExporter.ExportTextureAsync(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                SkillOutputPixels,
                SkillOutputPixels,
                reservation.StagingFilePath,
                token,
                ReportAssetReadbackSource.MaterialTexture
            );
        }
        catch (Exception ex)
        {
            ReportFailure(battleId, entry.TemplateId, reservation.StagingFilePath, ex);
            yield break;
        }

        while (!exportTask.IsCompleted && !token.IsCancellationRequested)
            yield return null;
        if (token.IsCancellationRequested || exportTask.IsCanceled || exportTask.IsFaulted)
        {
            ReportFailure(
                battleId,
                entry.TemplateId,
                reservation.StagingFilePath,
                exportTask.Exception?.GetBaseException()
            );
            yield break;
        }

        var publishTask = PublishAsync(reservation, exportTask.Result, battleId, entry.TemplateId);
        while (!publishTask.IsCompleted)
            yield return null;
    }

    private IEnumerator MaterializeItem(
        string? battleId,
        MaterializationEntry entry,
        ReportAssetMaterializationCoordinator.ReportAssetMaterializationReservation reservation,
        CancellationToken token
    )
    {
        var renderer = _itemRenderer ??= new OffscreenItemPreviewRenderer(_exportLayer);
        Task<OffscreenItemVisual?> createTask;
        try
        {
            createTask = renderer.CreateAsync(entry.Identity.Template, token);
        }
        catch (Exception ex)
        {
            ReportFailure(battleId, entry.TemplateId, reservation.StagingFilePath, ex);
            yield break;
        }

        while (!createTask.IsCompleted && !token.IsCancellationRequested)
            yield return null;
        if (token.IsCancellationRequested || createTask.IsCanceled || createTask.IsFaulted)
        {
            ReportFailure(
                battleId,
                entry.TemplateId,
                reservation.StagingFilePath,
                createTask.Exception?.GetBaseException()
            );
            yield break;
        }

        var handle = createTask.Result;
        if (handle == null)
        {
            ReportFailure(
                battleId,
                entry.TemplateId,
                reservation.StagingFilePath,
                new InvalidOperationException("Native collection item visual could not be created.")
            );
            yield break;
        }

        RenderTexture? target = null;
        try
        {
            target = renderer.BeginRender(handle, entry.OutputWidth, entry.OutputHeight);
        }
        catch (Exception ex)
        {
            ReportFailure(battleId, entry.TemplateId, reservation.StagingFilePath, ex);
            renderer.Return(handle);
            yield break;
        }

        // Collection visuals use ordinary Renderers, so URP can submit this permanently-disabled
        // private camera directly into the destination RT without participating in the live camera
        // loop or touching the main backbuffer.
        renderer.Render(target);
        yield return OffscreenFrameBoundary;

        Task<ReportAssetProducedFile>? exportTask = null;
        try
        {
            if (token.IsCancellationRequested)
            {
                renderer.CancelRender(target);
                yield break;
            }
            exportTask = renderer.CompleteRenderAndReadback(
                target,
                reservation.StagingFilePath,
                token
            );
            target = null;
        }
        catch (Exception ex)
        {
            renderer.CancelRender(target);
            target = null;
            ReportFailure(battleId, entry.TemplateId, reservation.StagingFilePath, ex);
        }
        finally
        {
            renderer.Return(handle);
        }

        if (exportTask == null)
            yield break;
        while (!exportTask.IsCompleted && !token.IsCancellationRequested)
            yield return null;
        if (token.IsCancellationRequested || exportTask.IsCanceled || exportTask.IsFaulted)
        {
            ReportFailure(
                battleId,
                entry.TemplateId,
                reservation.StagingFilePath,
                exportTask.Exception?.GetBaseException()
            );
            yield break;
        }

        var publishTask = PublishAsync(reservation, exportTask.Result, battleId, entry.TemplateId);
        while (!publishTask.IsCompleted)
            yield return null;
    }

    private static async Task<Texture?> LoadSkillTextureAsync(
        PostCombatReportRenderIdentity identity,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        var artKey = identity.Template.ArtKey;
        if (
            string.IsNullOrWhiteSpace(artKey)
            || string.Equals(artKey, "Invalid", StringComparison.Ordinal)
        )
            return null;
        if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
            return null;

        var texture = await assetLoader.LoadAssetAsyncByAddress<Texture>(artKey);
        token.ThrowIfCancellationRequested();
        return texture;
    }

    private static async Task PublishAsync(
        ReportAssetMaterializationCoordinator.ReportAssetMaterializationReservation reservation,
        ReportAssetProducedFile produced,
        string? battleId,
        Guid templateId
    )
    {
        try
        {
            await reservation
                .PublishAsync(produced.PixelWidth, produced.PixelHeight)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportFailure(battleId, templateId, reservation.StagingFilePath, ex);
        }
    }

    private IReadOnlyList<MaterializationEntry> SelectUniqueMaterializations(
        PvpBattleManifest manifest
    )
    {
        var result = new List<MaterializationEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in EnumerateReportCards(manifest))
        {
            if (TryBuildEntry(snapshot, out var entry) && seen.Add(entry.RenderKey.RenderKeyHash))
                result.Add(entry);
        }
        return result;
    }

    private bool TryBuildEntry(PvpBattleCardSnapshot snapshot, out MaterializationEntry entry)
    {
        entry = null!;
        if (
            snapshot == null
            || snapshot.Type is not (ECardType.Item or ECardType.Skill)
            || !Guid.TryParse(snapshot.TemplateId, out var templateId)
            || templateId == Guid.Empty
            || !PostCombatReportRenderIdentityResolver.TryResolve(
                templateId,
                _gameBuild,
                out var identity
            )
            || identity.Template.Type != snapshot.Type
        )
        {
            return false;
        }

        var isSkill = snapshot.Type == ECardType.Skill;
        var outputWidth = isSkill
            ? SkillOutputPixels
            : ResolveItemOutputWidth(identity.Template.Size);
        var outputHeight = isSkill ? SkillOutputPixels : ItemOutputHeight;
        var profileName = isSkill ? "native-texture-copy" : "urp-offscreen-template-art";
        var captureProfile = new ReportAssetCaptureProfile(
            profileName,
            CaptureProfileVersion,
            outputWidth,
            outputHeight,
            outputWidth,
            outputHeight,
            10000,
            10000,
            isSkill ? 0 : ItemTransparentCropPaddingPixels,
            "#00000000",
            isSkill ? -1 : _exportLayer,
            QualitySettings.activeColorSpace.ToString(),
            "imagesharp-png",
            EncoderVersion
        );
        var renderKey = ReportAssetRenderKey.CreateTemplateAsset(
            RenderKeySchemaVersion,
            _gameBuild,
            identity.GameDataIdentity,
            identity.ResolvedTemplateVersion,
            identity.ResolvedSkinIdentity,
            profileName,
            isSkill ? SkillRendererVersion : ItemRendererVersion,
            isSkill ? "skill-art" : "item-template-art",
            templateId.ToString("N"),
            identity.Template.Size.ToString(),
            $"front|art:{Normalize(identity.Template.ArtKey, "invalid-art-key")}|resolved-size:{identity.Template.Size}",
            captureProfile
        );
        entry = new MaterializationEntry(
            templateId,
            identity,
            isSkill,
            outputWidth,
            outputHeight,
            renderKey
        );
        return true;
    }

    private static int ResolveItemOutputWidth(ECardSize size) =>
        size switch
        {
            ECardSize.Medium => 544,
            ECardSize.Large => 1072,
            _ => 280,
        };

    private static IEnumerable<PvpBattleCardSnapshot> EnumerateReportCards(
        PvpBattleManifest manifest
    ) =>
        EnumerateCapture(manifest.Snapshots?.PlayerHand)
            .Concat(EnumerateCapture(manifest.Snapshots?.PlayerSkills))
            .Concat(EnumerateCapture(manifest.Snapshots?.OpponentHand))
            .Concat(EnumerateCapture(manifest.Snapshots?.OpponentSkills));

    private static IEnumerable<PvpBattleCardSnapshot> EnumerateCapture(
        PvpBattleCardSetCapture? capture
    ) => capture?.Items?.Where(snapshot => snapshot != null) ?? [];

    private static void ReportFailure(
        string? battleId,
        Guid? templateId,
        string? outputPath,
        Exception? exception
    ) =>
        PostCombatReportAssetDiagnostics.ReportFailure(
            battleId,
            templateId,
            exception is OperationCanceledException
                ? PostCombatReportCardPreviewReasonCode.Canceled
                : PostCombatReportCardPreviewReasonCode.CaptureFailed,
            outputPath,
            exception: exception
        );

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed record MaterializationEntry(
        Guid TemplateId,
        PostCombatReportRenderIdentity Identity,
        bool IsSkill,
        int OutputWidth,
        int OutputHeight,
        ReportAssetRenderKey RenderKey
    );

    private sealed record AssetLookupCandidate(
        string BindingKey,
        string SemanticRole,
        ReportAssetRenderKey RenderKey,
        string DisplayName
    );

    private sealed record OffscreenItemVisual(
        GameObject GameObject,
        ItemVisualsController Controller,
        Renderer IllustrationRenderer
    );

    private sealed class OffscreenItemPreviewRenderer : IDisposable
    {
        private static readonly Vector3 IsolatedWorldOrigin = new(0f, -10000f, 0f);
        private const float FramingMargin = 1.06f;
        private const float CameraPadding = 10f;

        private readonly int _layer;
        private readonly GameObject _cameraObject;
        private readonly Camera _camera;
        private readonly UniversalAdditionalCameraData _cameraData;
        private readonly Light _light;
        private readonly GameObject _root;
        private bool _disposed;

        internal OffscreenItemPreviewRenderer(int layer)
        {
            _layer = layer;

            _cameraObject = new GameObject(
                "BPP_PostCombatReportOffscreenCamera",
                typeof(Camera),
                typeof(Light)
            );
            _cameraObject.layer = layer;
            _camera = _cameraObject.GetComponent<Camera>();
            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.cullingMask = 1 << layer;
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 1000f;
            _camera.depth = -10000f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.useOcclusionCulling = false;
            _cameraData = _camera.GetUniversalAdditionalCameraData();
            _cameraData.renderType = CameraRenderType.Base;
            _cameraData.renderShadows = false;
            _cameraData.requiresDepthOption = CameraOverrideOption.Off;
            _cameraData.requiresColorOption = CameraOverrideOption.Off;
            _cameraData.renderPostProcessing = false;
            _cameraData.antialiasing = AntialiasingMode.None;
            _cameraData.stopNaN = false;
            _cameraData.dithering = false;
            _cameraData.allowXRRendering = false;
            _cameraData.cameraStack?.Clear();

            _light = _cameraObject.GetComponent<Light>();
            _light.enabled = false;
            _light.type = LightType.Directional;
            _light.cullingMask = 1 << layer;
            _light.intensity = 1.15f;
            _light.shadows = LightShadows.None;

            _root = new GameObject("BPP_PostCombatReportOffscreenRoot");
            _root.layer = layer;
            _root.transform.position = IsolatedWorldOrigin;
            _root.SetActive(false);
        }

        internal async Task<OffscreenItemVisual?> CreateAsync(
            TCardBase template,
            CancellationToken token
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OffscreenItemPreviewRenderer));
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            token.ThrowIfCancellationRequested();
            if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
                return null;

            GameObject? itemObject = null;
            try
            {
                itemObject = await assetLoader.ConstructAndInstantiateCardVisuals(
                    template,
                    _root,
                    cardDataObject: null,
                    isPremium: false
                );
                token.ThrowIfCancellationRequested();
                if (
                    itemObject == null
                    || !itemObject.TryGetComponent<ItemVisualsController>(out var controller)
                )
                    return null;

                var cardAsset = await controller.GetCardAssetData(template);
                token.ThrowIfCancellationRequested();
                if (cardAsset == null || cardAsset.cardMaterial == null)
                    throw new InvalidDataException("Native collection item has no card material.");

                await controller.Setup(
                    cardAsset,
                    template.StartingTier,
                    cardBackAsset: null,
                    isPremium: false,
                    eEnchantmentType: null
                );
                token.ThrowIfCancellationRequested();
                if (
                    !NativeItemVisualArtwork.TryGetIllustrationRenderer(
                        controller,
                        out var illustrationRenderer
                    )
                )
                {
                    throw new InvalidDataException(
                        "Native collection item has no illustration renderer."
                    );
                }

                itemObject.transform.SetParent(_root.transform, worldPositionStays: false);
                itemObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                itemObject.transform.localScale = Vector3.one;
                controller.ShowCardArt(show: true);
                controller.ToggleFakeDropShadow(value: false);
                Helpers.SetLayerRecursive(itemObject, _layer);
                DisableEmbeddedCanvases(itemObject);
                Helpers.SetAllRenderersEnabledState(
                    itemObject,
                    active: false,
                    includeInactive: true
                );

                var visual = new OffscreenItemVisual(itemObject, controller, illustrationRenderer);
                itemObject = null;
                return visual;
            }
            finally
            {
                if (itemObject != null)
                {
                    itemObject.SetActive(false);
                    Object.Destroy(itemObject);
                }
            }
        }

        internal RenderTexture BeginRender(
            OffscreenItemVisual handle,
            int outputWidth,
            int outputHeight
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OffscreenItemPreviewRenderer));
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));
            if (HasLayerCollision())
                throw new InvalidOperationException(
                    $"Export layer {_layer} is already used by a non-report renderer."
                );

            var target = NativeReportTexturePngExporter.CreateTarget(
                outputWidth,
                outputHeight,
                "BPP_ReportOffscreenItem"
            );
            var ownsTarget = true;
            try
            {
                _camera.targetTexture = target;
                _camera.forceIntoRenderTexture = true;
                _camera.aspect = outputWidth / (float)outputHeight;
                _root.SetActive(true);
                try
                {
                    handle.GameObject.SetActive(true);
                    Helpers.SetLayerRecursive(handle.GameObject, _layer);
                    DisableEmbeddedCanvases(handle.GameObject);
                    Helpers.SetAllRenderersEnabledState(
                        handle.GameObject,
                        active: false,
                        includeInactive: true
                    );
                    if (handle.IllustrationRenderer == null)
                        throw new InvalidDataException(
                            "Native collection item illustration renderer was destroyed."
                        );
                    handle.IllustrationRenderer.enabled = true;
                    FrameVisual(handle);
                    ValidateIsolation(handle);
                    _light.enabled = true;
                }
                catch
                {
                    _light.enabled = false;
                    Helpers.SetAllRenderersEnabledState(
                        handle.GameObject,
                        active: false,
                        includeInactive: true
                    );
                    _root.SetActive(false);
                    _camera.targetTexture = null;
                    throw;
                }
                ownsTarget = false;
                return target;
            }
            finally
            {
                if (ownsTarget)
                {
                    if (target.IsCreated())
                        target.Release();
                    Object.Destroy(target);
                }
            }
        }

        internal void Render(RenderTexture target)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OffscreenItemPreviewRenderer));
            if (target == null || _camera.targetTexture != target)
                throw new InvalidOperationException(
                    "Offscreen item render target ownership was lost."
                );
            if (_camera.enabled)
                throw new InvalidOperationException(
                    "Offscreen report camera must remain disabled."
                );

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(_camera, request))
                throw new NotSupportedException(
                    "URP does not support single-camera report rendering."
                );
            RenderPipeline.SubmitRenderRequest(_camera, request);
        }

        internal Task<ReportAssetProducedFile> CompleteRenderAndReadback(
            RenderTexture target,
            string outputPath,
            CancellationToken token
        )
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OffscreenItemPreviewRenderer));
            if (target == null || _camera.targetTexture != target)
                throw new InvalidOperationException(
                    "Offscreen item render target ownership was lost."
                );

            _light.enabled = false;
            Helpers.SetAllRenderersEnabledState(_root, active: false, includeInactive: true);
            _root.SetActive(false);
            _camera.targetTexture = null;
            return NativeReportTexturePngExporter.ReadbackAndWriteAsync(
                target,
                outputPath,
                token,
                ReportAssetReadbackSource.OffscreenCamera,
                trimTransparentBounds: true,
                transparentPaddingPixels: ItemTransparentCropPaddingPixels
            );
        }

        internal void CancelRender(RenderTexture? target)
        {
            _light.enabled = false;
            Helpers.SetAllRenderersEnabledState(_root, active: false, includeInactive: true);
            _root.SetActive(false);
            _camera.targetTexture = null;
            if (target == null)
                return;
            if (target.IsCreated())
                target.Release();
            Object.Destroy(target);
        }

        internal void Return(OffscreenItemVisual handle)
        {
            if (handle == null || handle.GameObject == null)
                return;
            handle.GameObject.SetActive(false);
            Object.Destroy(handle.GameObject);
        }

        internal void Hide()
        {
            _light.enabled = false;
            _camera.targetTexture = null;
            Helpers.SetAllRenderersEnabledState(_root, active: false, includeInactive: true);
            _root.SetActive(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Hide();
            Object.Destroy(_root);
            Object.Destroy(_cameraObject);
        }

        private void FrameVisual(OffscreenItemVisual handle)
        {
            var renderers = handle
                .GameObject.GetComponentsInChildren<Renderer>(includeInactive: true)
                .Where(renderer =>
                    renderer != null && renderer.enabled && renderer is not ParticleSystemRenderer
                )
                .ToArray();
            if (renderers.Length == 0)
                throw new InvalidDataException("Native collection item has no enabled renderers.");

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            if (!IsFinitePositive(bounds.size.magnitude))
                throw new InvalidDataException(
                    "Native collection item has invalid renderer bounds."
                );

            var faceNormal = handle.Controller.transform.up.normalized;
            var screenUp = handle.Controller.transform.forward.normalized;
            if (faceNormal.sqrMagnitude < 0.5f || screenUp.sqrMagnitude < 0.5f)
                throw new InvalidDataException("Native collection item orientation is invalid.");

            var rotation = Quaternion.LookRotation(-faceNormal, screenUp);
            _camera.transform.rotation = rotation;
            var cameraRight = _camera.transform.right;
            var cameraUp = _camera.transform.up;
            var halfWidth = 0f;
            var halfHeight = 0f;
            var halfDepth = 0f;
            foreach (var corner in EnumerateCorners(bounds))
            {
                var relative = corner - bounds.center;
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(relative, cameraRight)));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(relative, cameraUp)));
                halfDepth = Mathf.Max(halfDepth, Mathf.Abs(Vector3.Dot(relative, faceNormal)));
            }
            if (!IsFinitePositive(halfWidth) || !IsFinitePositive(halfHeight))
                throw new InvalidDataException("Native collection item cannot be framed.");

            _camera.orthographicSize =
                Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.01f, _camera.aspect)) * FramingMargin;
            var distance = halfDepth + CameraPadding;
            var position = bounds.center + faceNormal * distance;
            _camera.transform.position = position;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = Mathf.Max(100f, distance + halfDepth + CameraPadding);
        }

        private void ValidateIsolation(OffscreenItemVisual handle)
        {
            if (
                _camera.enabled
                || _camera.targetTexture == null
                || _camera.cullingMask != 1 << _layer
                || _cameraData.renderType != CameraRenderType.Base
                || (_cameraData.cameraStack?.Count ?? 0) != 0
            )
                throw new InvalidOperationException(
                    "Offscreen report camera isolation is invalid."
                );
            var enabledRenderers = handle
                .GameObject.GetComponentsInChildren<Renderer>(includeInactive: true)
                .Where(renderer => renderer != null && renderer.enabled)
                .ToArray();
            if (enabledRenderers.Length != 1 || enabledRenderers[0] != handle.IllustrationRenderer)
            {
                throw new InvalidOperationException(
                    "Offscreen item export must enable only the template illustration renderer."
                );
            }
            if (
                handle
                    .GameObject.GetComponentsInChildren<Canvas>(includeInactive: true)
                    .Any(canvas => canvas != null && canvas.enabled)
            )
                throw new InvalidOperationException("Native collection item has an active Canvas.");
            if (
                handle
                    .GameObject.GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Any(renderer => renderer != null && renderer.gameObject.layer != _layer)
            )
                throw new InvalidOperationException(
                    "Native collection item escaped the report layer."
                );
        }

        private bool HasLayerCollision()
        {
            foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (
                    gameObject == null
                    || !gameObject.activeInHierarchy
                    || gameObject.layer != _layer
                    || gameObject == _cameraObject
                    || gameObject.transform.IsChildOf(_root.transform)
                )
                {
                    continue;
                }
                if (
                    gameObject.GetComponent<Renderer>() != null
                    || gameObject.GetComponent<Canvas>() != null
                    || gameObject.GetComponent<CanvasRenderer>() != null
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static void DisableEmbeddedCanvases(GameObject itemObject)
        {
            foreach (
                var canvas in itemObject.GetComponentsInChildren<Canvas>(includeInactive: true)
            )
            {
                if (canvas != null)
                    canvas.enabled = false;
            }
        }

        private static IEnumerable<Vector3> EnumerateCorners(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            yield return new Vector3(min.x, min.y, min.z);
            yield return new Vector3(min.x, min.y, max.z);
            yield return new Vector3(min.x, max.y, min.z);
            yield return new Vector3(min.x, max.y, max.z);
            yield return new Vector3(max.x, min.y, min.z);
            yield return new Vector3(max.x, min.y, max.z);
            yield return new Vector3(max.x, max.y, min.z);
            yield return new Vector3(max.x, max.y, max.z);
        }

        private static bool IsFinitePositive(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }
}
