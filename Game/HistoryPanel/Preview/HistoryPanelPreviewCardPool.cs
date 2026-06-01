#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.CardPreview;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

// Owns a small per-size pool of CardPreviewBase instances cloned from prefabs reflected off
// MonsterBoardTooltip. Both the game type names and the prefab refs are resolved at runtime
// via reflection so the mod compiles against the CI reference DLLs even though the actual
// types live in the live game assemblies. Prefab refs + socket templates are static /
// cross-scene; pooled instances are owned per-renderer.
internal sealed class HistoryPanelPreviewCardPool
{
    private const int DefaultMaxPoolSizePerSize = 30;

    private readonly int _layer;
    private readonly int _maxPoolSizePerSize;
    private readonly Dictionary<ECardSize, Queue<Component>> _pool = new();

    public HistoryPanelPreviewCardPool(
        int layer,
        int maxPoolSizePerSize = DefaultMaxPoolSizePerSize
    )
    {
        _layer = layer;
        _maxPoolSizePerSize = Math.Max(1, maxPoolSizePerSize);
    }

    public static NativeCardPreviewSocketTemplate[]? TryGetSocketTemplates() =>
        NativeCardPreviewPrefabResolver.TryGetSocketTemplates();

    public bool TryEnsurePrefabRefs() =>
        NativeCardPreviewPrefabResolver.TryEnsureResolved(
            requireSkill: false,
            requireSockets: true,
            "HistoryPanelPreviewCardPool"
        );

    public Component? Take(ECardSize size, Transform parent)
    {
        if (!TryEnsurePrefabRefs())
            return null;

        if (!_pool.TryGetValue(size, out var queue))
        {
            queue = new Queue<Component>();
            _pool[size] = queue;
        }

        Component? card = null;
        while (queue.Count > 0)
        {
            var candidate = queue.Dequeue();
            if (candidate != null)
            {
                card = candidate;
                break;
            }
        }

        if (card == null)
        {
            if (
                !NativeCardPreviewPrefabResolver.TryGetPrefab(
                    NativeCardPreviewKind.ForItem(size),
                    requireSkill: false,
                    requireSockets: true,
                    "HistoryPanelPreviewCardPool",
                    out var prefab
                )
                || prefab == null
            )
                return null;

            card = Object.Instantiate(prefab, parent, worldPositionStays: false);
            if (card != null)
                card.name = $"HistoryPanelPreviewCard_{size}";
        }
        else
        {
            card.transform.SetParent(parent, worldPositionStays: false);
        }

        if (card == null)
            return null;

        card.transform.localScale = Vector3.one;
        card.transform.localRotation = Quaternion.identity;
        card.gameObject.SetActive(true);
        NativeCardPreviewReflection.ApplyLayerRecursive(card.gameObject, _layer);
        NativeCardPreviewRuntime.Resize(card, "HistoryPanelPreviewCardPool");

        return card;
    }

    public void Return(Component? card)
    {
        if (card == null)
            return;

        card.gameObject.SetActive(false);

        var size = NativeCardPreviewRuntime.ResolveCardSize(card);
        if (!_pool.TryGetValue(size, out var queue))
        {
            queue = new Queue<Component>();
            _pool[size] = queue;
        }

        if (queue.Count >= _maxPoolSizePerSize)
        {
            var evicted = queue.Dequeue();
            if (evicted != null)
                Object.Destroy(evicted.gameObject);
        }

        queue.Enqueue(card);
    }

    public void DestroyAll()
    {
        foreach (var queue in _pool.Values)
        {
            while (queue.Count > 0)
            {
                var card = queue.Dequeue();
                if (card != null)
                    Object.Destroy(card.gameObject);
            }
        }

        _pool.Clear();
    }
}
