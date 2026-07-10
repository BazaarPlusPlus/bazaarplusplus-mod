#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Resolves a CollectionCardVm into a live, AssetLoader-created CardPreviewBase instance.
// NativeCardPreviewFactory owns the first SetUp call; the pre-activation prepare callback
// adds collection-specific marker and fade components so canceled cards remain marked if
// they are returned to the native preview pool before handle adoption.
internal sealed class CollectionCardFactory
{
    private readonly NativeCardPreviewFactory _nativeFactory;
    private readonly Transform _parent;
    private readonly CollectionCardCacheSession _cacheSession;
    private readonly Func<object?> _staticDataProvider;
    private readonly Func<object?, Guid, TCardBase?> _templateResolver;
    private readonly Dictionary<Component, NativeCardPreviewHandle> _activeHandles = new();
    private int _instanceCounter;

    public CollectionCardFactory(
        NativeCardPreviewFactory nativeFactory,
        Transform parent,
        CollectionCardCacheSession cacheSession
    )
        : this(
            nativeFactory,
            parent,
            cacheSession,
            BppStaticDataAccess.TryGetReadyManagerObject,
            BppStaticDataAccess.GetCardTemplate
        ) { }

    internal CollectionCardFactory(
        NativeCardPreviewFactory nativeFactory,
        Transform parent,
        CollectionCardCacheSession cacheSession,
        Func<object?> staticDataProvider,
        Func<object?, Guid, TCardBase?> templateResolver
    )
    {
        _nativeFactory = nativeFactory ?? throw new ArgumentNullException(nameof(nativeFactory));
        _parent = parent;
        _cacheSession = cacheSession ?? throw new ArgumentNullException(nameof(cacheSession));
        _staticDataProvider =
            staticDataProvider ?? throw new ArgumentNullException(nameof(staticDataProvider));
        _templateResolver =
            templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
    }

    public bool ReflectionReady => _nativeFactory.ReflectionReady;

    public async Task<CollectionCardBindResult> BindAsync(
        CollectionCardVm vm,
        CancellationToken token = default
    )
    {
        if (vm == null)
            return CollectionCardBindResult.HardMiss();

        token.ThrowIfCancellationRequested();

        var nativeStaticData = _staticDataProvider();
        if (nativeStaticData == null)
            return CollectionCardBindResult.NotReady();

        var template = _templateResolver(nativeStaticData, vm.Id);
        if (template == null)
        {
            BppLog.Warn(
                "CollectionCardFactory",
                $"Template lookup failed for id={vm.Id} ({vm.InternalName})."
            );
            return CollectionCardBindResult.HardMiss();
        }

        return await BindAsync(vm, template, token);
    }

    private async Task<CollectionCardBindResult> BindAsync(
        CollectionCardVm vm,
        TCardBase template,
        CancellationToken token
    )
    {
        var spec = BuildSpec(vm);
        var handle = await _nativeFactory.CreateAsync(
            template,
            spec,
            _parent,
            ++_instanceCounter,
            token,
            PrepareCollectionCardForBind
        );
        if (handle == null)
            return CollectionCardBindResult.NotReady();

        PrepareCollectionCardForBind(handle.Card);
        RegisterCollectionTooltip(handle.Card);
        _activeHandles[handle.Card] = handle;
        return CollectionCardBindResult.Bound(
            new CollectionCardBinding(handle.Card, handle.Kind, Task.CompletedTask)
        );
    }

    public void Return(Component? card, NativeCardPreviewKind kind)
    {
        if (card == null)
            return;

        UnregisterCollectionTooltip(card);

        if (_activeHandles.Remove(card, out var handle))
        {
            _nativeFactory.Return(handle);
            return;
        }

        BppLog.Debug(
            "CollectionCardFactory",
            $"Return skipped for untracked collection card kind={kind}."
        );
    }

    private static NativeCardPreviewSpec BuildSpec(CollectionCardVm vm) =>
        new()
        {
            TemplateId = vm.Id,
            Tier = vm.StartingTier,
            DisplaySpan = vm.Type == ECardType.Skill ? 1 : CardSizeSpan.Resolve(vm.Size),
            InstanceIdPrefix = "bpp-collection",
        };

    private void PrepareCollectionCardForBind(Component card)
    {
        var marker = card.gameObject.GetComponent<CollectionPanelOwnedMarker>();
        if (marker == null)
            marker = card.gameObject.AddComponent<CollectionPanelOwnedMarker>();
        marker.CacheOwner = _cacheSession;

        var canvasGroup = card.gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = card.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
    }

    private static void RegisterCollectionTooltip(Component card)
    {
        if (NativeCardPreviewReflection.TryGetTooltipData(card, out var tooltipData))
            CollectionTierTooltipRegistry.Register(tooltipData.CardInstance);
    }

    private static void UnregisterCollectionTooltip(Component card)
    {
        if (NativeCardPreviewReflection.TryGetTooltipData(card, out var tooltipData))
            CollectionTierTooltipRegistry.Unregister(tooltipData.CardInstance);
    }
}

// One realized card + the bind kind we hand back to the pool on Return + the SetUp task we
// must await before flipping the card visible (to avoid showing a frame mid-LoadArt).
internal readonly struct CollectionCardBinding
{
    public CollectionCardBinding(Component card, NativeCardPreviewKind kind, Task setUpTask)
    {
        Card = card;
        Kind = kind;
        SetUpTask = setUpTask;
    }

    public Component Card { get; }
    public NativeCardPreviewKind Kind { get; }
    public Task SetUpTask { get; }
}

internal enum CollectionCardBindStatus
{
    Bound,
    HardMiss,
    NotReady,
}

internal readonly struct CollectionCardBindResult
{
    private CollectionCardBindResult(
        CollectionCardBindStatus status,
        CollectionCardBinding? binding
    )
    {
        Status = status;
        Binding = binding;
    }

    public CollectionCardBindStatus Status { get; }
    public CollectionCardBinding? Binding { get; }

    public static CollectionCardBindResult Bound(CollectionCardBinding binding) =>
        new(CollectionCardBindStatus.Bound, binding);

    public static CollectionCardBindResult HardMiss() =>
        new(CollectionCardBindStatus.HardMiss, null);

    public static CollectionCardBindResult NotReady() =>
        new(CollectionCardBindStatus.NotReady, null);
}
