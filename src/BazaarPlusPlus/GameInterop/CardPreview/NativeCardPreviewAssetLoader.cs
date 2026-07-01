#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.AppFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewAssetLoader
{
    private readonly string _logComponent;

    public NativeCardPreviewAssetLoader(string logComponent)
    {
        _logComponent = string.IsNullOrWhiteSpace(logComponent)
            ? "NativeCardPreviewAssetLoader"
            : logComponent;
    }

    public async Task<Component?> InstantiateReadyCardAsync(
        TCardInstance instance,
        Transform parent,
        CancellationToken token = default
    )
    {
        if (instance == null || parent == null)
            return null;

        if (!Services.TryGet<AssetLoader>(out var assetLoader) || assetLoader == null)
        {
            BppLog.Warn(
                _logComponent,
                "AssetLoader unavailable; native card preview cannot instantiate UI card."
            );
            return null;
        }

        try
        {
            token.ThrowIfCancellationRequested();

            var gameObject = await assetLoader.InstantiateUICardAsync(
                instance,
                parent,
                CancellationToken.None
            );
            if (gameObject == null)
                return null;

            var cardPreviewBaseType = NativeCardPreviewReflection.CardPreviewBaseType;
            if (cardPreviewBaseType == null)
            {
                Object.Destroy(gameObject);
                BppLog.Warn(
                    _logComponent,
                    $"CardPreviewBase type unavailable for template={instance.TemplateId}; destroyed AssetLoader preview."
                );
                return null;
            }

            var card = gameObject.GetComponent(cardPreviewBaseType);
            if (card == null)
            {
                Object.Destroy(gameObject);
                BppLog.Warn(
                    _logComponent,
                    $"AssetLoader UI card missing CardPreviewBase template={instance.TemplateId}; destroyed preview."
                );
                return null;
            }

            return card;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                _logComponent,
                $"AssetLoader UI card instantiate failed template={instance.TemplateId}: {ex.Message}"
            );
            return null;
        }
    }
}
