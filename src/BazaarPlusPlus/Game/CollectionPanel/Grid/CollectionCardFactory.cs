#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.CustomCards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Resolves a CollectionCardVm into a live, SetUp-ready CardPreviewBase instance vended from
// the pool. Both the Item and Skill branches eventually call CardPreviewBase.SetUp via
// reflection so the mod compiles against any DLL surface where the method exists; the
// instance argument is constructed locally (filling InstanceId / TemplateVersion / Attributes)
// to avoid the NPE paths inside the game's CardPreviewBase.SetUp.
internal sealed class CollectionCardFactory
{
    private readonly CollectionCardPool _pool;
    private readonly Transform _parent;
    private readonly Func<object?> _staticDataProvider;
    private readonly Func<object?, Guid, TCardBase?> _templateResolver;
    private readonly Func<BppCustomCardDescriptor, string?, TCardBase> _customTemplateBuilder;
    private readonly BppCustomCardMaterialDonorResolver _customMaterialDonorResolver;
    private int _instanceCounter;

    public CollectionCardFactory(CollectionCardPool pool, Transform parent)
        : this(
            pool,
            parent,
            BppStaticDataAccess.TryGetReadyManagerObject,
            BppStaticDataAccess.GetCardTemplate,
            BppCustomCardTemplateFactory.Build,
            new BppCustomCardMaterialDonorResolver()
        ) { }

    internal CollectionCardFactory(
        CollectionCardPool pool,
        Transform parent,
        Func<object?> staticDataProvider,
        Func<object?, Guid, TCardBase?> templateResolver,
        Func<BppCustomCardDescriptor, string?, TCardBase>? customTemplateBuilder = null,
        BppCustomCardMaterialDonorResolver? customMaterialDonorResolver = null
    )
    {
        _pool = pool;
        _parent = parent;
        _staticDataProvider =
            staticDataProvider ?? throw new ArgumentNullException(nameof(staticDataProvider));
        _templateResolver =
            templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
        _customTemplateBuilder = customTemplateBuilder ?? BppCustomCardTemplateFactory.Build;
        _customMaterialDonorResolver =
            customMaterialDonorResolver ?? new BppCustomCardMaterialDonorResolver();
    }

    public bool ReflectionReady => NativeCardPreviewReflection.SetUpMethod != null;

    public CollectionCardBindResult TryBind(CollectionCardVm vm)
    {
        if (vm == null)
            return CollectionCardBindResult.HardMiss();

        if (BppCustomCardRegistry.Current?.TryGet(vm.Id, out var descriptor) == true)
        {
            // Achievements are display-only; without bundled art there is nothing to draw and a
            // donor key would leak the donor card's illustration. Require bundled art.
            if (BppCustomCardRegistry.Current?.HasBundledArt(vm.Id) != true)
            {
                BppLog.Warn(
                    "CollectionCardFactory",
                    $"Custom card {vm.Id} ({vm.InternalName}) has no bundled art; skipping."
                );
                return CollectionCardBindResult.HardMiss();
            }

            var staticData = _staticDataProvider();
            if (staticData == null)
                return CollectionCardBindResult.NotReady();

            var donor = _customMaterialDonorResolver.Resolve(staticData, descriptor!.Size);
            if (donor.Status == BppCustomCardMaterialDonorStatus.Pending)
                return CollectionCardBindResult.NotReady(); // donor scan in flight; retried next frame
            if (
                donor.Status != BppCustomCardMaterialDonorStatus.Ready
                || string.IsNullOrEmpty(donor.ArtKey)
            )
            {
                BppLog.Warn(
                    "CollectionCardFactory",
                    $"Custom card {vm.Id} ({vm.InternalName}) has no donor art; skipping."
                );
                return CollectionCardBindResult.HardMiss();
            }

            TCardBase customTemplate;
            try
            {
                customTemplate = _customTemplateBuilder(descriptor, donor.ArtKey);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CollectionCardFactory",
                    $"Custom template build failed for id={vm.Id} ({vm.InternalName}): {ex.Message}"
                );
                return CollectionCardBindResult.HardMiss();
            }

            return Bind(vm, customTemplate);
        }

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

        return Bind(vm, template);
    }

    private CollectionCardBindResult Bind(CollectionCardVm vm, TCardBase template)
    {
        var kind =
            vm.Type == ECardType.Skill
                ? NativeCardPreviewKind.ForSkill()
                : NativeCardPreviewKind.ForItem(vm.Size);
        var card = _pool.Take(kind, _parent);
        if (card == null)
            return CollectionCardBindResult.NotReady();

        var instance = BuildSyntheticInstance(vm);
        var setUpTask = NativeCardPreviewRuntime.InvokeSetUpSafe(
            card,
            template,
            instance,
            "CollectionCardFactory"
        );
        return CollectionCardBindResult.Bound(new CollectionCardBinding(card, kind, setUpTask));
    }

    public void Return(Component? card, NativeCardPreviewKind kind) => _pool.Return(card, kind);

    private TCardInstance BuildSyntheticInstance(CollectionCardVm vm)
    {
        var attributes = new Dictionary<ECardAttributeType, int>();
        var id = $"bpp-collection-{++_instanceCounter}";

        if (vm.Type == ECardType.Skill)
        {
            return new TCardInstanceSkill
            {
                TemplateId = vm.Id,
                TemplateVersion = string.Empty,
                InstanceId = id,
                Tier = vm.StartingTier,
                Attributes = attributes,
            };
        }

        return new TCardInstanceItem
        {
            TemplateId = vm.Id,
            TemplateVersion = string.Empty,
            InstanceId = id,
            Tier = vm.StartingTier,
            Attributes = attributes,
        };
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
