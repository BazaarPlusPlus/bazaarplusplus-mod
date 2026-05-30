#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.HistoryPanel.Preview;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal readonly struct CollectionCardKind : IEquatable<CollectionCardKind>
{
    public CollectionCardKind(ECardType type, ECardSize size)
    {
        Type = type;
        Size = size;
    }

    public ECardType Type { get; }
    public ECardSize Size { get; }

    public static CollectionCardKind ForSkill() => new(ECardType.Skill, ECardSize.Medium);

    public static CollectionCardKind ForItem(ECardSize size) => new(ECardType.Item, size);

    public bool Equals(CollectionCardKind other) => Type == other.Type && Size == other.Size;

    public override bool Equals(object? obj) => obj is CollectionCardKind other && Equals(other);

    public override int GetHashCode() => ((int)Type * 397) ^ (int)Size;

    public override string ToString() => Type == ECardType.Skill ? "Skill" : $"Item-{Size}";
}

// Per-kind pool of CardPreviewBase instances cloned from MonsterBoardTooltip's four prefab
// fields (_smallItemReference / _mediumItemReference / _largeItemReference / _skillReference).
// Mirrors HistoryPanelPreviewCardPool's Take/Return/eviction shape, but is keyed by
// (ECardType, ECardSize) instead of just ECardSize so the Skill prefab can be served without
// pretending it is an Item.
//
// Prefab refs are resolved via reflection through Harmony.AccessTools to keep the mod
// resilient to future game-side renames; if the lookup fails the panel logs and stays
// closed rather than crashing.
internal sealed class CollectionCardPool
{
    private const int DefaultMaxPoolSizePerKind = 30;
    private const string MonsterBoardTooltipTypeName = "TheBazaar.UI.Tooltips.MonsterBoardTooltip";

    private static readonly object PrefabRefsLock = new();
    private static readonly Dictionary<CollectionCardKind, Component> PrefabRefs = new();
    private static bool _prefabRefsResolved;

    private static readonly Type? MonsterBoardTooltipType = AccessTools.TypeByName(
        MonsterBoardTooltipTypeName
    );

    private static readonly FieldInfo? SmallItemReferenceField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_smallItemReference")
            : null;

    private static readonly FieldInfo? MediumItemReferenceField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_mediumItemReference")
            : null;

    private static readonly FieldInfo? LargeItemReferenceField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_largeItemReference")
            : null;

    private static readonly FieldInfo? SkillReferenceField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_skillReference")
            : null;

    private static readonly MethodInfo? ResizeMethod =
        HistoryPanelCardPreviewReflection.ResizeMethod;

    private readonly int _layer;
    private readonly int _maxPoolSizePerKind;
    private readonly Dictionary<CollectionCardKind, Queue<Component>> _pool = new();

    public CollectionCardPool(int layer, int maxPoolSizePerKind = DefaultMaxPoolSizePerKind)
    {
        _layer = layer;
        _maxPoolSizePerKind = Math.Max(1, maxPoolSizePerKind);
    }

    public bool TryEnsurePrefabRefs()
    {
        lock (PrefabRefsLock)
        {
            if (_prefabRefsResolved)
                return true;
        }

        if (
            MonsterBoardTooltipType == null
            || SmallItemReferenceField == null
            || MediumItemReferenceField == null
            || LargeItemReferenceField == null
            || SkillReferenceField == null
        )
        {
            BppLog.Warn(
                "CollectionCardPool",
                $"{MonsterBoardTooltipTypeName} reflection metadata missing; collection panel unavailable on this game version."
            );
            return false;
        }

        var tooltips = Resources.FindObjectsOfTypeAll(MonsterBoardTooltipType);
        if (tooltips == null || tooltips.Length == 0)
            return false;

        foreach (var tooltipObj in tooltips)
        {
            if (tooltipObj is not Component tooltip || tooltip == null)
                continue;

            var small = SmallItemReferenceField.GetValue(tooltip) as Component;
            var medium = MediumItemReferenceField.GetValue(tooltip) as Component;
            var large = LargeItemReferenceField.GetValue(tooltip) as Component;
            var skill = SkillReferenceField.GetValue(tooltip) as Component;
            if (small == null || medium == null || large == null || skill == null)
                continue;

            lock (PrefabRefsLock)
            {
                PrefabRefs[CollectionCardKind.ForItem(ECardSize.Small)] = small;
                PrefabRefs[CollectionCardKind.ForItem(ECardSize.Medium)] = medium;
                PrefabRefs[CollectionCardKind.ForItem(ECardSize.Large)] = large;
                PrefabRefs[CollectionCardKind.ForSkill()] = skill;
                _prefabRefsResolved = true;
            }

            BppLog.Info(
                "CollectionCardPool",
                $"Acquired CardPreviewBase prefab refs (small='{small.name}', medium='{medium.name}', large='{large.name}', skill='{skill.name}')."
            );
            return true;
        }

        return false;
    }

    public Component? Take(CollectionCardKind kind, Transform parent)
    {
        if (!TryEnsurePrefabRefs())
            return null;

        if (!_pool.TryGetValue(kind, out var queue))
        {
            queue = new Queue<Component>();
            _pool[kind] = queue;
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
            Component? prefab;
            lock (PrefabRefsLock)
            {
                prefab = PrefabRefs.TryGetValue(kind, out var resolved) ? resolved : null;
            }

            if (prefab == null)
                return null;

            card = Object.Instantiate(prefab, parent, worldPositionStays: false);
            if (card != null)
            {
                card.name = $"CollectionPanelCard_{kind}";
                // Stamp the marker once at instantiation; the OnDestroy + LoadArt patches use
                // it to gate cache participation, and the marker survives every Take/Return
                // cycle so the gating stays consistent across pool reuse.
                if (card.gameObject.GetComponent<CollectionPanelOwnedMarker>() == null)
                    card.gameObject.AddComponent<CollectionPanelOwnedMarker>();
                // CanvasGroup drives the per-card fade-in. Added once at instantiation;
                // alpha is reset to 0 below on every Take so each rebind starts invisible
                // and the virtualizer's TickFades ramps it back up to 1 after Show.
                if (card.gameObject.GetComponent<CanvasGroup>() == null)
                    card.gameObject.AddComponent<CanvasGroup>();
            }
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
        HistoryPanelPreviewCardPool.ApplyLayerRecursive(card.gameObject, _layer);

        // Start each (re)bind invisible so the fade ramp owns the perceived appearance.
        var canvasGroup = card.gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        try
        {
            ResizeMethod?.Invoke(card, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            BppLog.Warn("CollectionCardPool", $"CardPreviewBase.Resize threw: {ex.Message}");
        }

        return card;
    }

    public void Return(Component? card, CollectionCardKind kind)
    {
        if (card == null)
            return;

        card.gameObject.SetActive(false);

        if (!_pool.TryGetValue(kind, out var queue))
        {
            queue = new Queue<Component>();
            _pool[kind] = queue;
        }

        if (queue.Count >= _maxPoolSizePerKind)
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
