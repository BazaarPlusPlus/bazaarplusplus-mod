#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using TheBazaar.AppFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewAssetLoader
{
    public async Task<NativeCardPreviewInstantiateOutcome> InstantiateReadyCardAsync(
        TCardInstance instance,
        Transform parent,
        CancellationToken token = default
    )
    {
        if (instance == null || parent == null)
            return default;

        if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
        {
            return new NativeCardPreviewInstantiateOutcome(
                null,
                new NativeCardPreviewFailure(
                    NativeCardPreviewOperation.Instantiate,
                    NativeCardPreviewFailureReason.AssetLoaderUnavailable,
                    instance.TemplateId
                )
            );
        }

        try
        {
            token.ThrowIfCancellationRequested();

            // Native AssetLoader checks cancellation after instantiation, so let it finish and
            // keep cleanup inside our return/destroy ownership path.
            var gameObject = await assetLoader.InstantiateUICardAsync(
                instance,
                parent,
                CancellationToken.None
            );
            if (gameObject == null)
            {
                return new NativeCardPreviewInstantiateOutcome(
                    null,
                    new NativeCardPreviewFailure(
                        NativeCardPreviewOperation.Instantiate,
                        NativeCardPreviewFailureReason.PreviewComponentUnavailable,
                        instance.TemplateId
                    )
                );
            }

            var cardPreviewBaseType = NativeCardPreviewReflection.CardPreviewBaseType;
            if (cardPreviewBaseType == null)
            {
                Object.Destroy(gameObject);
                return new NativeCardPreviewInstantiateOutcome(
                    null,
                    new NativeCardPreviewFailure(
                        NativeCardPreviewOperation.ResolvePreviewType,
                        NativeCardPreviewFailureReason.PreviewTypeUnavailable,
                        instance.TemplateId
                    )
                );
            }

            var card = gameObject.GetComponent(cardPreviewBaseType);
            if (card == null)
            {
                Object.Destroy(gameObject);
                return new NativeCardPreviewInstantiateOutcome(
                    null,
                    new NativeCardPreviewFailure(
                        NativeCardPreviewOperation.ResolvePreviewComponent,
                        NativeCardPreviewFailureReason.PreviewComponentUnavailable,
                        instance.TemplateId
                    )
                );
            }

            return new NativeCardPreviewInstantiateOutcome(card, null);
        }
        catch (OperationCanceledException)
        {
            return default;
        }
        catch (Exception ex)
        {
            return new NativeCardPreviewInstantiateOutcome(
                null,
                new NativeCardPreviewFailure(
                    NativeCardPreviewOperation.Instantiate,
                    NativeCardPreviewFailureReason.InstantiateException,
                    instance.TemplateId,
                    ex
                )
            );
        }
    }
}
