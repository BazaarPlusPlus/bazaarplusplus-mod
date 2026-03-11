#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewBoard : IDisposable
{
    private readonly IPreviewCardFactory _factory;
    private readonly GameObject _boardRoot;
    private readonly GameObject _visualRoot;
    private readonly GameObject _contentRoot;
    private readonly List<GameObject> _slots = new List<GameObject>();
    private readonly List<GameObject> _cards = new List<GameObject>();
    private readonly List<GameObject> _borderSegments = new List<GameObject>();

    private GameObject _boardPlate;

    private PreviewBoardLayout _layout = new PreviewBoardLayout();

    public MonsterPreviewBoard(string name, IPreviewCardFactory factory)
    {
        _factory = factory;
        _boardRoot = new GameObject(name);
        _visualRoot = new GameObject(name + "_Visual");
        _contentRoot = new GameObject(name + "_Content");
        _visualRoot.transform.SetParent(_boardRoot.transform, false);
        _contentRoot.transform.SetParent(_boardRoot.transform, false);
        BuildVisuals();
        RefreshLayout();
        SetVisible(false);
    }

    public void SetLayout(PreviewBoardLayout layout)
    {
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
        _boardRoot.transform.SetPositionAndRotation(position, rotation);
        RefreshLayout();
    }

    public async Task RebuildAsync(IReadOnlyList<PreviewCardSpec> cards, Func<bool> isCancelled)
    {
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

            var slot = new GameObject($"CardSlot_{index}");
            slot.transform.SetParent(_contentRoot.transform, false);
            _slots.Add(slot);

            RefreshSlot(index);

            var cardObject = await _factory.CreateCardAsync(cards[index], slot.transform);
            if (isCancelled())
            {
                if (cardObject != null)
                    _factory.DestroyCard(cardObject);
                Clear();
                return;
            }

            if (cardObject == null)
                continue;

            _cards.Add(cardObject);
        }

        RefreshLayout();
    }

    public void Clear()
    {
        foreach (var cardObject in _cards)
        {
            if (cardObject != null)
                _factory.DestroyCard(cardObject);
        }
        _cards.Clear();

        foreach (var slot in _slots)
        {
            if (slot != null)
                UnityEngine.Object.Destroy(slot);
        }
        _slots.Clear();
    }

    public void Dispose()
    {
        Clear();
        if (_boardRoot != null)
            UnityEngine.Object.Destroy(_boardRoot);
    }

    private void RefreshLayout()
    {
        RefreshVisuals();
        _contentRoot.transform.localPosition = _layout.LocalOffset;
        _contentRoot.transform.localRotation = Quaternion.identity;
        _contentRoot.transform.localScale = Vector3.one;

        for (var index = 0; index < _slots.Count; index++)
            RefreshSlot(index);
    }

    private void RefreshSlot(int index)
    {
        var slot = _slots[index];
        var spacing = _layout.CardSpacing;
        slot.transform.localPosition = new Vector3(
            spacing.x * index,
            spacing.y * index,
            spacing.z * index
        );
        slot.transform.localRotation = Quaternion.identity;
        slot.transform.localScale = _layout.CardScale;
    }

    private void BuildVisuals()
    {
        _boardPlate = CreatePrimitive("BoardPlate");
        _boardPlate.transform.SetParent(_visualRoot.transform, false);

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"BoardBorder_{index}");
            border.transform.SetParent(_visualRoot.transform, false);
            _borderSegments.Add(border);
        }
    }

    private void RefreshVisuals()
    {
        var size = _layout.BoardSize;
        var halfWidth = size.x * 0.5f;
        var halfDepth = size.y * 0.5f;
        var boardThickness = Mathf.Max(0.01f, _layout.BoardThickness);
        var borderThickness = Mathf.Max(0.01f, _layout.BorderThickness);
        var borderHeight = Mathf.Max(boardThickness, _layout.BorderHeight);

        _visualRoot.transform.localPosition = Vector3.zero;
        _visualRoot.transform.localRotation = Quaternion.identity;
        _visualRoot.transform.localScale = Vector3.one;

        _boardPlate.transform.localPosition = new Vector3(halfWidth, 0f, halfDepth);
        _boardPlate.transform.localRotation = Quaternion.identity;
        _boardPlate.transform.localScale = new Vector3(size.x, boardThickness, size.y);

        UpdateBorder(
            _borderSegments[0],
            new Vector3(halfWidth, borderHeight * 0.5f, 0f),
            new Vector3(size.x + borderThickness, borderHeight, borderThickness)
        );
        UpdateBorder(
            _borderSegments[1],
            new Vector3(halfWidth, borderHeight * 0.5f, size.y),
            new Vector3(size.x + borderThickness, borderHeight, borderThickness)
        );
        UpdateBorder(
            _borderSegments[2],
            new Vector3(0f, borderHeight * 0.5f, halfDepth),
            new Vector3(borderThickness, borderHeight, size.y + borderThickness)
        );
        UpdateBorder(
            _borderSegments[3],
            new Vector3(size.x, borderHeight * 0.5f, halfDepth),
            new Vector3(borderThickness, borderHeight, size.y + borderThickness)
        );
    }

    private static void UpdateBorder(GameObject border, Vector3 position, Vector3 scale)
    {
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
            ? new Color(0.45f, 0.95f, 1f, 0.95f)
            : new Color(0.2f, 0.65f, 0.8f, 0.18f);
        return material;
    }
}
