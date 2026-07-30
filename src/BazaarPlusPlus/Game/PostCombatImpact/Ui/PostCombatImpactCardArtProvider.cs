#nullable enable
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BazaarPlusPlus.Game.PostCombatImpact.Ui;

internal sealed class PostCombatImpactCardArtProvider : IDisposable
{
    private readonly Dictionary<string, AsyncOperationHandle<CardAssetDataSO>> _cardHandles = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, AsyncOperationHandle<Texture>> _skillHandles = new(
        StringComparer.Ordinal
    );

    internal Task<PostCombatImpactCardArt?> Get(CombatImpactEntity entity) =>
        string.Equals(entity.TypeLabel, "Skill", StringComparison.Ordinal)
            ? GetSkill(entity.ArtKey)
            : GetCard(entity.ArtKey);

    private async Task<PostCombatImpactCardArt?> GetCard(string? artKey)
    {
        if (string.IsNullOrWhiteSpace(artKey))
            return null;
        if (_cardHandles.TryGetValue(artKey!, out var existing))
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
        _cardHandles[artKey!] = handle;
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

    private async Task<PostCombatImpactCardArt?> GetSkill(string? artKey)
    {
        if (string.IsNullOrWhiteSpace(artKey))
            return null;
        if (_skillHandles.TryGetValue(artKey!, out var existing))
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

        var handle = Addressables.LoadAssetAsync<Texture>(artKey);
        _skillHandles[artKey!] = handle;
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

    private static PostCombatImpactCardArt? From(Texture? texture) =>
        texture == null ? null : new PostCombatImpactCardArt(texture, new Rect(0f, 0f, 1f, 1f));

    public void Dispose()
    {
        foreach (var handle in _cardHandles.Values)
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
        _cardHandles.Clear();

        foreach (var handle in _skillHandles.Values)
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
        _skillHandles.Clear();
    }
}

internal readonly record struct PostCombatImpactCardArt(Texture Texture, Rect Uv);
