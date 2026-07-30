#nullable enable
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal sealed class PostCombatImpactCardArtProvider : IDisposable
{
    private readonly Dictionary<string, AsyncOperationHandle<CardAssetDataSO>> _handles = new(
        StringComparer.Ordinal
    );

    internal async Task<PostCombatImpactCardArt?> Get(string? artKey)
    {
        if (string.IsNullOrWhiteSpace(artKey))
            return null;
        if (_handles.TryGetValue(artKey!, out var existing))
        {
            try
            {
                await existing.Task;
                return existing.Status == AsyncOperationStatus.Succeeded
                    ? From(existing.Result)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        var handle = Addressables.LoadAssetAsync<CardAssetDataSO>(artKey);
        _handles[artKey!] = handle;
        try
        {
            await handle.Task;
            return handle.Status == AsyncOperationStatus.Succeeded ? From(handle.Result) : null;
        }
        catch
        {
            return null;
        }
    }

    private static PostCombatImpactCardArt? From(CardAssetDataSO? asset)
    {
        var material = asset?.cardMaterial;
        if (material?.mainTexture == null)
            return null;

        var scale = material.mainTextureScale;
        var offset = material.mainTextureOffset;
        return new PostCombatImpactCardArt(
            material.mainTexture,
            new Rect(offset.x, offset.y, scale.x, scale.y)
        );
    }

    public void Dispose()
    {
        foreach (var handle in _handles.Values)
        {
            try
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            catch
            {
                // Best-effort cleanup during plugin teardown.
            }
        }
        _handles.Clear();
    }
}

internal readonly record struct PostCombatImpactCardArt(Texture Texture, Rect Uv);
