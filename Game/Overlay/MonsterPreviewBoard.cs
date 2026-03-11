#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewBoard : IDisposable
{
    private const int BoardSlotCount = 10;
    private const float BoardCenterMarkerSize = 0.16f;
    private const float SlotMarkerSize = 0.08f;
    private const float CardCenterMarkerSize = 0.12f;

    private readonly IPreviewCardFactory _factory;
    private readonly GameObject _boardRoot;
    private readonly GameObject _visualRoot;
    private readonly GameObject _contentRoot;
    private readonly List<GameObject> _boardSlots = new List<GameObject>();
    private readonly List<GameObject> _boardSlotMarkers = new List<GameObject>();
    private readonly List<GameObject> _cardAnchors = new List<GameObject>();
    private readonly List<GameObject> _cards = new List<GameObject>();
    private readonly List<GameObject> _cardCenterMarkers = new List<GameObject>();
    private readonly List<GameObject> _borderSegments = new List<GameObject>();
    private readonly List<int> _cardSizes = new List<int>();

    private GameObject _boardPlate;
    private GameObject _boardCenterMarker;
    private PreviewBoardLayout _layout = new PreviewBoardLayout();

    public bool IsAlive => _boardRoot != null;

    public MonsterPreviewBoard(string name, IPreviewCardFactory factory)
    {
        _factory = factory;
        _boardRoot = new GameObject(name);
        _visualRoot = new GameObject(name + "_Visual");
        _contentRoot = new GameObject(name + "_Content");
        _visualRoot.transform.SetParent(_boardRoot.transform, false);
        _contentRoot.transform.SetParent(_boardRoot.transform, false);
        BuildVisuals();
        BuildBoardSlots();
        RefreshLayout();
        SetVisible(false);
    }

    public void SetLayout(PreviewBoardLayout layout)
    {
        if (!IsAlive)
            return;

        _layout = layout ?? new PreviewBoardLayout();
        RefreshLayout();
    }

    public void SetVisible(bool visible)
    {
        if (_boardRoot != null && _boardRoot.activeSelf != visible)
            _boardRoot.SetActive(visible);
    }

    public void UpdateAnchor(Vector3 position, Quaternion rotation)
    {
        if (!IsAlive)
            return;

        _boardRoot.transform.SetPositionAndRotation(position, rotation);
        RefreshLayout();
    }

    public async Task RebuildAsync(IReadOnlyList<PreviewCardSpec> cards, Func<bool> isCancelled)
    {
        if (!IsAlive)
            return;

        Clear();

        if (cards == null || cards.Count == 0)
            return;

        for (var index = 0; index < cards.Count; index++)
        {
            if (isCancelled())
            {
                Clear();
                return;
            }

            var anchor = new GameObject($"CardAnchor_{index}");
            anchor.transform.SetParent(_contentRoot.transform, false);
            _cardAnchors.Add(anchor);
            _cardSizes.Add(GetCardSize(cards[index]));

            var marker = CreateMarker($"CardCenter_{index}", CardCenterMarkerSize);
            marker.transform.SetParent(anchor.transform, false);
            _cardCenterMarkers.Add(marker);

            RefreshCardAnchor(index);

            var cardObject = await _factory.CreateCardAsync(cards[index], anchor.transform);
            if (isCancelled())
            {
                if (cardObject != null)
                    _factory.DestroyCard(cardObject);
                Clear();
                return;
            }

            if (cardObject == null)
                continue;

            cardObject.transform.SetParent(anchor.transform, false);
            _cards.Add(cardObject);
            RefreshCard(index);
        }

        RefreshLayout();
    }

    public void Clear()
    {
        if (!IsAlive)
            return;

        foreach (var cardObject in _cards)
        {
            if (cardObject != null)
                _factory.DestroyCard(cardObject);
        }
        _cards.Clear();
        _cardSizes.Clear();

        foreach (var marker in _cardCenterMarkers)
        {
            if (marker != null)
                UnityEngine.Object.Destroy(marker);
        }
        _cardCenterMarkers.Clear();

        foreach (var anchor in _cardAnchors)
        {
            if (anchor != null)
                UnityEngine.Object.Destroy(anchor);
        }
        _cardAnchors.Clear();
    }

    public void Dispose()
    {
        Clear();
        if (_boardRoot != null)
            UnityEngine.Object.Destroy(_boardRoot);
    }

    private void RefreshLayout()
    {
        if (!IsAlive)
            return;

        RefreshVisuals();

        _contentRoot.transform.localPosition = _layout.LocalOffset;
        _contentRoot.transform.localRotation = Quaternion.identity;
        _contentRoot.transform.localScale = Vector3.one;

        RefreshBoardSlots();

        for (var index = 0; index < _cardAnchors.Count; index++)
        {
            RefreshCardAnchor(index);
            RefreshCard(index);
        }
    }

    private void BuildVisuals()
    {
        _boardPlate = CreatePrimitive("BoardPlate");
        _boardPlate.transform.SetParent(_visualRoot.transform, false);

        _boardCenterMarker = CreateMarker("BoardCenter", BoardCenterMarkerSize);
        _boardCenterMarker.transform.SetParent(_visualRoot.transform, false);

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"BoardBorder_{index}");
            border.transform.SetParent(_visualRoot.transform, false);
            _borderSegments.Add(border);
        }
    }

    private void BuildBoardSlots()
    {
        for (var index = 0; index < BoardSlotCount; index++)
        {
            var slot = new GameObject($"BoardSlot_{index}");
            slot.transform.SetParent(_contentRoot.transform, false);
            _boardSlots.Add(slot);

            var marker = CreateMarker($"BoardSlotMarker_{index}", SlotMarkerSize);
            marker.transform.SetParent(slot.transform, false);
            _boardSlotMarkers.Add(marker);
        }
    }

    private void RefreshBoardSlots()
    {
        for (var index = 0; index < _boardSlots.Count; index++)
        {
            var slot = _boardSlots[index];
            if (slot == null)
                continue;

            slot.transform.localPosition = new Vector3(
                GetBoardSlotCenterX(index),
                0f,
                0f
            );
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;
        }
    }

    private void RefreshCardAnchor(int index)
    {
        if (index < 0 || index >= _cardAnchors.Count)
            return;

        var anchor = _cardAnchors[index];
        if (anchor == null)
            return;

        var span = Mathf.Clamp(GetCardSize(index), 1, 3);
        var startSlot = GetCardStartSlot(index);
        var endSlot = Mathf.Min(BoardSlotCount - 1, startSlot + span - 1);
        var centerX = (GetBoardSlotCenterX(startSlot) + GetBoardSlotCenterX(endSlot)) * 0.5f;
        var spacing = _layout.CardSpacing;

        anchor.transform.localPosition = new Vector3(
            centerX,
            spacing.y * index,
            spacing.z * index
        );
        anchor.transform.localRotation = Quaternion.identity;
        anchor.transform.localScale = Vector3.one;
    }

    private void RefreshCard(int index)
    {
        if (index < 0 || index >= _cards.Count)
            return;

        var cardObject = _cards[index];
        if (cardObject == null)
            return;

        cardObject.transform.localPosition = Vector3.zero;
        cardObject.transform.localRotation = Quaternion.identity;
        cardObject.transform.localScale = _layout.CardScale;
    }

    private void RefreshVisuals()
    {
        if (_visualRoot == null || _boardPlate == null || _borderSegments.Count < 4)
            return;

        var size = _layout.BoardSize;
        var boardWidth = Mathf.Max(0.01f, size.x);
        var halfWidth = boardWidth * 0.5f;
        var halfDepth = size.y * 0.5f;
        var boardThickness = Mathf.Max(0.01f, _layout.BoardThickness);
        var borderThickness = Mathf.Max(0.01f, _layout.BorderThickness);
        var borderHeight = Mathf.Max(boardThickness, _layout.BorderHeight);

        _visualRoot.transform.localPosition = Vector3.zero;
        _visualRoot.transform.localRotation = Quaternion.identity;
        _visualRoot.transform.localScale = Vector3.one;

        _boardPlate.transform.localPosition = Vector3.zero;
        _boardPlate.transform.localRotation = Quaternion.identity;
        _boardPlate.transform.localScale = new Vector3(boardWidth, boardThickness, size.y);

        if (_boardCenterMarker != null)
        {
            _boardCenterMarker.transform.localPosition = new Vector3(
                0f,
                borderHeight + BoardCenterMarkerSize * 0.5f,
                0f
            );
            _boardCenterMarker.transform.localRotation = Quaternion.identity;
            _boardCenterMarker.transform.localScale = Vector3.one * BoardCenterMarkerSize;
        }

        UpdateBorder(
            _borderSegments[0],
            new Vector3(0f, borderHeight * 0.5f, -halfDepth),
            new Vector3(boardWidth + borderThickness, borderHeight, borderThickness)
        );
        UpdateBorder(
            _borderSegments[1],
            new Vector3(0f, borderHeight * 0.5f, halfDepth),
            new Vector3(boardWidth + borderThickness, borderHeight, borderThickness)
        );
        UpdateBorder(
            _borderSegments[2],
            new Vector3(-halfWidth, borderHeight * 0.5f, 0f),
            new Vector3(borderThickness, borderHeight, size.y + borderThickness)
        );
        UpdateBorder(
            _borderSegments[3],
            new Vector3(halfWidth, borderHeight * 0.5f, 0f),
            new Vector3(borderThickness, borderHeight, size.y + borderThickness)
        );
    }

    private float GetBoardSlotCenterX(int slotIndex)
    {
        var boardWidth = Mathf.Max(0.01f, _layout.BoardSize.x);
        var slotWidth = boardWidth / BoardSlotCount;
        return -boardWidth * 0.5f + slotWidth * (slotIndex + 0.5f);
    }

    private int GetCardStartSlot(int cardIndex)
    {
        var slot = GetLeadingEmptySlots();
        for (var index = 0; index < cardIndex; index++)
            slot += Mathf.Clamp(GetCardSize(index), 1, 3);

        var span = Mathf.Clamp(GetCardSize(cardIndex), 1, 3);
        return Mathf.Clamp(slot, 0, Mathf.Max(0, BoardSlotCount - span));
    }

    private int GetLeadingEmptySlots()
    {
        var occupiedSlots = 0;
        for (var index = 0; index < _cardSizes.Count; index++)
            occupiedSlots += Mathf.Clamp(_cardSizes[index], 1, 3);

        var freeSlots = Mathf.Max(0, BoardSlotCount - occupiedSlots);
        return freeSlots / 2;
    }

    private int GetCardSize(int index)
    {
        if (index < 0 || index >= _cardSizes.Count)
            return 1;

        return Mathf.Clamp(_cardSizes[index], 1, 3);
    }

    private static int GetCardSize(PreviewCardSpec spec)
    {
        return Mathf.Clamp(spec?.Size ?? 1, 1, 3);
    }

    private static void UpdateBorder(GameObject border, Vector3 position, Vector3 scale)
    {
        if (border == null)
            return;

        border.transform.localPosition = position;
        border.transform.localRotation = Quaternion.identity;
        border.transform.localScale = scale;
    }

    private static GameObject CreatePrimitive(string name)
    {
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        primitive.name = name;

        var collider = primitive.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);

        ApplyDebugMaterial(primitive);
        return primitive;
    }

    private static GameObject CreateMarker(string name, float size)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;

        var collider = marker.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);

        ApplyMarkerMaterial(marker);
        marker.transform.localScale = Vector3.one * size;
        return marker;
    }

    private static void ApplyDebugMaterial(GameObject target)
    {
        if (!target.TryGetComponent<Renderer>(out var renderer))
            return;

        var material = CreateDebugMaterial(target.name.Contains("Border"));
        if (material != null)
            renderer.sharedMaterial = material;
    }

    private static Material CreateDebugMaterial(bool isBorder)
    {
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        var material = new Material(shader);
        material.color = isBorder
            ? new Color(1f, 0.2f, 0.2f, 0.95f)
            : new Color(1f, 0.1f, 0.1f, 0.18f);
        return material;
    }

    private static void ApplyMarkerMaterial(GameObject target)
    {
        if (!target.TryGetComponent<Renderer>(out var renderer))
            return;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        var material = new Material(shader);
        material.color = new Color(1f, 0f, 0f, 0.95f);
        renderer.sharedMaterial = material;
    }
}
