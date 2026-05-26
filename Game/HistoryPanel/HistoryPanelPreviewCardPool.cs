#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal readonly struct HistoryPanelPreviewSocketTemplate
{
    public HistoryPanelPreviewSocketTemplate(
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot
    )
    {
        AnchoredPosition = anchoredPosition;
        SizeDelta = sizeDelta;
        AnchorMin = anchorMin;
        AnchorMax = anchorMax;
        Pivot = pivot;
    }

    public Vector2 AnchoredPosition { get; }
    public Vector2 SizeDelta { get; }
    public Vector2 AnchorMin { get; }
    public Vector2 AnchorMax { get; }
    public Vector2 Pivot { get; }
}

// Owns a small per-size pool of CardPreviewBase instances cloned from prefabs reflected off
// MonsterBoardTooltip. Both the game type names and the prefab refs are resolved at runtime
// via reflection so the mod compiles against the CI reference DLLs even though the actual
// types live in the live game assemblies. Prefab refs + socket templates are static /
// cross-scene; pooled instances are owned per-renderer.
internal sealed class HistoryPanelPreviewCardPool
{
    private const int DefaultMaxPoolSizePerSize = 30;
    private const string MonsterBoardTooltipTypeName = "TheBazaar.UI.Tooltips.MonsterBoardTooltip";

    private static readonly object PrefabRefsLock = new();
    private static readonly Dictionary<ECardSize, Component> PrefabRefs = new();
    private static HistoryPanelPreviewSocketTemplate[]? _socketTemplates;
    private static bool _prefabRefsResolved;

    private static readonly Type? MonsterBoardTooltipType = AccessTools.TypeByName(
        MonsterBoardTooltipTypeName
    );

    private static readonly FieldInfo? SmallItemReferenceField = MonsterBoardTooltipType != null
        ? AccessTools.Field(MonsterBoardTooltipType, "_smallItemReference")
        : null;

    private static readonly FieldInfo? MediumItemReferenceField = MonsterBoardTooltipType != null
        ? AccessTools.Field(MonsterBoardTooltipType, "_mediumItemReference")
        : null;

    private static readonly FieldInfo? LargeItemReferenceField = MonsterBoardTooltipType != null
        ? AccessTools.Field(MonsterBoardTooltipType, "_largeItemReference")
        : null;

    private static readonly FieldInfo? SocketsField = MonsterBoardTooltipType != null
        ? AccessTools.Field(MonsterBoardTooltipType, "_sockets")
        : null;

    private static readonly MethodInfo? ResizeMethod = HistoryPanelCardPreviewReflection.ResizeMethod;
    private static readonly PropertyInfo? SizeProperty = HistoryPanelCardPreviewReflection.SizeProperty;

    private readonly int _layer;
    private readonly int _maxPoolSizePerSize;
    private readonly Dictionary<ECardSize, Queue<Component>> _pool = new();

    public HistoryPanelPreviewCardPool(int layer, int maxPoolSizePerSize = DefaultMaxPoolSizePerSize)
    {
        _layer = layer;
        _maxPoolSizePerSize = Math.Max(1, maxPoolSizePerSize);
    }

    public static bool ArePrefabRefsResolved
    {
        get
        {
            lock (PrefabRefsLock)
                return _prefabRefsResolved;
        }
    }

    public static HistoryPanelPreviewSocketTemplate[]? TryGetSocketTemplates()
    {
        lock (PrefabRefsLock)
            return _prefabRefsResolved ? _socketTemplates : null;
    }

    public bool TryEnsurePrefabRefs()
    {
        lock (PrefabRefsLock)
        {
            if (_prefabRefsResolved)
                return true;
        }

        if (MonsterBoardTooltipType == null
            || SmallItemReferenceField == null
            || MediumItemReferenceField == null
            || LargeItemReferenceField == null
            || SocketsField == null)
        {
            BppLog.Warn(
                "HistoryPanelPreviewCardPool",
                $"{MonsterBoardTooltipTypeName} reflection metadata missing; preview unavailable."
            );
            return false;
        }

        // FindObjectsOfTypeAll includes inactive objects, so we don't rely on a live
        // CardTooltipController in the current scene.
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
            var sockets = SocketsField.GetValue(tooltip) as RectTransform[];

            if (small == null || medium == null || large == null || sockets == null || sockets.Length == 0)
                continue;

            var templates = CaptureSocketTemplates(sockets);
            lock (PrefabRefsLock)
            {
                PrefabRefs[ECardSize.Small] = small;
                PrefabRefs[ECardSize.Medium] = medium;
                PrefabRefs[ECardSize.Large] = large;
                _socketTemplates = templates;
                _prefabRefsResolved = true;
            }

            BppLog.Info(
                "HistoryPanelPreviewCardPool",
                $"Acquired CardPreviewBase prefab refs (small='{small.name}', medium='{medium.name}', large='{large.name}', sockets={templates.Length})."
            );
            return true;
        }

        return false;
    }

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
            Component? prefab;
            lock (PrefabRefsLock)
            {
                prefab = PrefabRefs.TryGetValue(size, out var resolved) ? resolved : null;
            }

            if (prefab == null)
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

        card.gameObject.SetActive(true);
        ApplyLayerRecursive(card.gameObject, _layer);

        try
        {
            ResizeMethod?.Invoke(card, Array.Empty<object>());
        }
        catch (Exception ex)
        {
            BppLog.Warn("HistoryPanelPreviewCardPool", $"CardPreviewBase.Resize threw: {ex.Message}");
        }

        return card;
    }

    public void Return(Component? card)
    {
        if (card == null)
            return;

        card.gameObject.SetActive(false);

        var size = ResolveCardSize(card);
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

    public static void ApplyLayerRecursive(GameObject root, int layer)
    {
        if (root == null)
            return;

        root.layer = layer;
        var transform = root.transform;
        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child != null)
                ApplyLayerRecursive(child.gameObject, layer);
        }
    }

    private static ECardSize ResolveCardSize(Component card)
    {
        if (SizeProperty == null)
            return ECardSize.Small;

        try
        {
            var raw = SizeProperty.GetValue(card);
            if (raw is int intValue)
            {
                return intValue switch
                {
                    1 => ECardSize.Small,
                    2 => ECardSize.Medium,
                    3 => ECardSize.Large,
                    _ => ECardSize.Small,
                };
            }
        }
        catch
        {
            // fall through
        }

        return ECardSize.Small;
    }

    private static HistoryPanelPreviewSocketTemplate[] CaptureSocketTemplates(RectTransform[] sockets)
    {
        var templates = new HistoryPanelPreviewSocketTemplate[sockets.Length];
        for (var i = 0; i < sockets.Length; i++)
        {
            var socket = sockets[i];
            if (socket == null)
            {
                templates[i] = new HistoryPanelPreviewSocketTemplate(
                    Vector2.zero,
                    new Vector2(160f, 220f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f)
                );
                continue;
            }

            templates[i] = new HistoryPanelPreviewSocketTemplate(
                socket.anchoredPosition,
                socket.sizeDelta,
                socket.anchorMin,
                socket.anchorMax,
                socket.pivot
            );
        }
        return templates;
    }
}

internal static class HistoryPanelCardPreviewReflection
{
    private const string CardPreviewBaseTypeName = "TheBazaar.UI.CardPreviewBase";

    public static readonly Type? CardPreviewBaseType = AccessTools.TypeByName(
        CardPreviewBaseTypeName
    );

    public static readonly MethodInfo? SetUpMethod = CardPreviewBaseType != null
        ? AccessTools.Method(CardPreviewBaseType, "SetUp")
        : null;

    public static readonly MethodInfo? ShowMethod = CardPreviewBaseType != null
        ? AccessTools.Method(CardPreviewBaseType, "Show")
        : null;

    public static readonly MethodInfo? ResizeMethod = CardPreviewBaseType != null
        ? AccessTools.Method(CardPreviewBaseType, "Resize")
        : null;

    public static readonly PropertyInfo? SizeProperty = CardPreviewBaseType != null
        ? AccessTools.Property(CardPreviewBaseType, "Size")
        : null;
}
