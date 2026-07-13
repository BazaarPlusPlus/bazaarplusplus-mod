#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewFactory
{
    private readonly NativeCardPreviewPool _pool;
    private readonly NativeCardPreviewAssetLoader _assetLoader;

    public NativeCardPreviewFactory(NativeCardPreviewPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _assetLoader = new NativeCardPreviewAssetLoader();
    }

    public bool ReflectionReady => NativeCardPreviewReflection.SetUpMethod != null;

    public bool TryResolveSpan(
        NativeCardPreviewSpec? spec,
        out int span,
        out NativeCardPreviewFailure? failure
    )
    {
        span = 1;
        if (!TryResolveTemplate(spec, out var template, out failure))
            return false;

        span = CardSizeSpan.Resolve(template.Size);
        return true;
    }

    public Task<NativeCardPreviewCreateOutcome> CreateAsync(
        NativeCardPreviewSpec? spec,
        Transform parent,
        int instanceIndex,
        CancellationToken token = default,
        Action<Component>? prepareBeforeActivate = null
    )
    {
        if (parent == null || spec == null)
            return Task.FromResult(NativeCardPreviewCreateOutcome.Unavailable());
        if (!TryResolveTemplate(spec, out var template, out var failure))
        {
            return Task.FromResult(
                failure == null
                    ? NativeCardPreviewCreateOutcome.Unavailable()
                    : NativeCardPreviewCreateOutcome.Degraded(failure)
            );
        }

        return CreateAsync(template, spec, parent, instanceIndex, token, prepareBeforeActivate);
    }

    public async Task<NativeCardPreviewCreateOutcome> CreateAsync(
        TCardBase template,
        NativeCardPreviewSpec spec,
        Transform parent,
        int instanceIndex,
        CancellationToken token = default,
        Action<Component>? prepareBeforeActivate = null
    )
    {
        if (template == null || spec == null || parent == null)
            return NativeCardPreviewCreateOutcome.Unavailable();

        if (!TryResolveKind(template, out var kind))
        {
            return NativeCardPreviewCreateOutcome.Degraded(
                new NativeCardPreviewFailure(
                    NativeCardPreviewOperation.ResolveKind,
                    NativeCardPreviewFailureReason.UnsupportedCardType,
                    template.Id
                )
            );
        }

        var instance = BuildSyntheticInstance(spec, kind, instanceIndex);
        NativeCardPreviewFailure? instantiateFailure = null;
        var lease = await _pool.TakeAsync(
            kind,
            parent,
            async () =>
            {
                var outcome = await _assetLoader.InstantiateReadyCardAsync(instance, parent, token);
                instantiateFailure = outcome.Failure;
                return outcome.Card;
            },
            token,
            prepareBeforeActivate
        );
        if (!lease.HasValue)
        {
            return instantiateFailure == null
                ? NativeCardPreviewCreateOutcome.Unavailable()
                : NativeCardPreviewCreateOutcome.Degraded(instantiateFailure);
        }

        var leased = lease.Value;
        var card = leased.Card;
        var ownsCard = true;
        try
        {
            if (!leased.AlreadySetUp)
            {
                var setUpFailure = await NativeCardPreviewRuntime.InvokeSetUpSafe(
                    card,
                    template,
                    instance,
                    token
                );
                if (setUpFailure != null)
                    return NativeCardPreviewCreateOutcome.Degraded(setUpFailure);
            }

            token.ThrowIfCancellationRequested();

            var resizeFailure = NativeCardPreviewRuntime.Resize(card, template.Id);
            if (resizeFailure != null)
                return NativeCardPreviewCreateOutcome.Degraded(resizeFailure);

            var rect = card.transform as RectTransform ?? card.GetComponent<RectTransform>();
            if (rect == null)
            {
                return NativeCardPreviewCreateOutcome.Degraded(
                    new NativeCardPreviewFailure(
                        NativeCardPreviewOperation.ResolveRect,
                        NativeCardPreviewFailureReason.RectUnavailable,
                        template.Id
                    )
                );
            }

            ownsCard = false;
            return NativeCardPreviewCreateOutcome.Ready(
                new NativeCardPreviewHandle(card, rect, kind, Task.CompletedTask, spec)
            );
        }
        catch (OperationCanceledException)
        {
            return NativeCardPreviewCreateOutcome.Unavailable();
        }
        finally
        {
            if (ownsCard)
                _pool.Return(card, kind);
        }
    }

    public NativeCardPreviewFailure? Show(NativeCardPreviewHandle? handle, bool show = true)
    {
        if (handle == null)
            return null;
        return NativeCardPreviewRuntime.Show(handle.Card, show, handle.Spec.TemplateId);
    }

    public void Return(NativeCardPreviewHandle? handle) => _pool.Return(handle);

    public void DestroyAll() => _pool.DestroyAll();

    private bool TryResolveTemplate(
        NativeCardPreviewSpec? spec,
        out TCardBase template,
        out NativeCardPreviewFailure? failure
    )
    {
        template = null!;
        failure = null;
        if (spec == null || spec.TemplateId == Guid.Empty)
            return false;

        var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
        if (staticData == null)
        {
            failure = new NativeCardPreviewFailure(
                NativeCardPreviewOperation.ResolveTemplate,
                NativeCardPreviewFailureReason.StaticDataUnavailable,
                spec.TemplateId
            );
            return false;
        }

        var resolved = BppStaticDataAccess.GetCardTemplate(staticData, spec.TemplateId);
        if (resolved == null)
        {
            failure = new NativeCardPreviewFailure(
                NativeCardPreviewOperation.ResolveTemplate,
                NativeCardPreviewFailureReason.TemplateUnavailable,
                spec.TemplateId
            );
            return false;
        }

        template = resolved;
        return true;
    }

    private static bool TryResolveKind(TCardBase template, out NativeCardPreviewKind kind)
    {
        kind = default;
        if (template.Type == ECardType.Skill)
        {
            kind = NativeCardPreviewKind.ForSkill();
            return true;
        }

        if (template.Type == ECardType.Item)
        {
            kind = NativeCardPreviewKind.ForItem(ResolveCardSize(template.Size));
            return true;
        }

        return false;
    }

    private static TCardInstance BuildSyntheticInstance(
        NativeCardPreviewSpec spec,
        NativeCardPreviewKind kind,
        int index
    )
    {
        var attributes =
            spec.Attributes != null
                ? new Dictionary<ECardAttributeType, int>(spec.Attributes)
                : new Dictionary<ECardAttributeType, int>();
        var instanceId = $"{spec.InstanceIdPrefix}-{Mathf.Max(0, index)}";

        if (kind.Type == ECardType.Skill)
        {
            return new TCardInstanceSkill
            {
                TemplateId = spec.TemplateId,
                TemplateVersion = string.Empty,
                InstanceId = instanceId,
                Tier = spec.Tier,
                Attributes = attributes,
            };
        }

        return new TCardInstanceItem
        {
            TemplateId = spec.TemplateId,
            TemplateVersion = string.Empty,
            InstanceId = instanceId,
            Tier = spec.Tier,
            SocketId = spec.SocketId ?? (EContainerSocketId)Mathf.Clamp(index, 0, 9),
            EnchantmentType = spec.EnchantmentType,
            Attributes = attributes,
        };
    }

    private static ECardSize ResolveCardSize(ECardSize size) =>
        size switch
        {
            ECardSize.Small => ECardSize.Small,
            ECardSize.Medium => ECardSize.Medium,
            ECardSize.Large => ECardSize.Large,
            _ => ECardSize.Small,
        };
}
