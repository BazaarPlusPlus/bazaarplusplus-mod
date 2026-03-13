#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus;

internal sealed class MonsterPreviewBoard : IDisposable
{
    private const int BoardSlotCount = 10;
    private const int SkillSlotCount = 3;
    private const float ContentYOffset = 0.2f;
    private const float BrandingBoardWidth = 0.5f;
    private const float BoardCenterMarkerSize = 0.16f;
    private const float SlotMarkerSize = 0.08f;
    private const float CardCenterMarkerSize = 0.12f;
    private const float SkillBoardGap = 0.04f;
    private const float SkillSlotInset = 0.12f;
    private const float SkillCardScaleFactor = 0.8f;
    private static readonly Color ItemBoardFillColor = new Color(0.34f, 0.29f, 0.24f, 0.88f);
    private static readonly Color ItemBoardBorderColor = new Color(1f, 0.2f, 0.2f, 0.95f);
    private static readonly Color ItemBoardAccentColor = new Color(1f, 0.1f, 0.1f, 0.18f);
    private static readonly Color SkillBoardFillColor = new Color(0.24f, 0.29f, 0.33f, 0.16f);
    private static readonly Color SkillBoardBorderColor = new Color(0.25f, 0.7f, 1f, 0.95f);
    private static readonly Color BrandingBoardFillColor = new Color(0.17f, 0.20f, 0.23f, 0.62f);
    private static readonly Color BrandingBoardBorderColor = new Color(0.18f, 0.24f, 0.34f, 0.95f);
    private static readonly Color DebugItemMarkerColor = new Color(1f, 0f, 0f, 0.95f);
    private static readonly Color DebugSkillMarkerColor = new Color(0.25f, 0.75f, 1f, 0.95f);

    private readonly IPreviewCardFactory _factory;
    private readonly IPreviewCardFactory _skillFactory;
    private readonly GameObject _boardRoot;
    private readonly GameObject _visualRoot;
    private readonly GameObject _itemContentRoot;
    private readonly GameObject _skillContentRoot;
    private readonly List<GameObject> _boardSlots = new List<GameObject>();
    private readonly List<GameObject> _boardSlotMarkers = new List<GameObject>();
    private readonly List<GameObject> _cardAnchors = new List<GameObject>();
    private readonly List<GameObject> _cards = new List<GameObject>();
    private readonly List<GameObject> _cardCenterMarkers = new List<GameObject>();
    private readonly List<GameObject> _borderSegments = new List<GameObject>();
    private readonly List<int> _cardSizes = new List<int>();
    private readonly List<GameObject> _skillSlots = new List<GameObject>();
    private readonly List<GameObject> _skillSlotMarkers = new List<GameObject>();
    private readonly List<GameObject> _skillCards = new List<GameObject>();

    private GameObject _boardPlate;
    private GameObject _boardFill;
    private GameObject _skillBoardFill;
    private GameObject _brandingBoardFill;
    private readonly List<GameObject> _skillBoardBorders = new List<GameObject>();
    private readonly List<GameObject> _brandingBoardBorders = new List<GameObject>();
    private TextMesh _brandingText;
    private GameObject _boardCenterMarker;
    private PreviewBoardPresentation _presentation = new PreviewBoardPresentation();
    private PreviewBoardDebugOptions _debugOptions = new PreviewBoardDebugOptions();

    public bool IsAlive => _boardRoot != null;

    public MonsterPreviewBoard(string name, IPreviewCardFactory factory, IPreviewCardFactory skillFactory)
    {
        _factory = factory;
        _skillFactory = skillFactory;
        _boardRoot = new GameObject(name);
        _visualRoot = new GameObject(name + "_Visual");
        _itemContentRoot = new GameObject(name + "_ItemContent");
        _skillContentRoot = new GameObject(name + "_SkillContent");
        _visualRoot.transform.SetParent(_boardRoot.transform, false);
        _itemContentRoot.transform.SetParent(_boardRoot.transform, false);
        _skillContentRoot.transform.SetParent(_boardRoot.transform, false);
        BuildVisuals();
        BuildBoardSlots();
        BuildSkillSlots();
        RefreshLayout();
        SetVisible(false);
        BppLog.Info("MonsterPreviewBoard", $"Created board root='{_boardRoot.name}'");
    }

    public void SetPresentation(PreviewBoardPresentation presentation)
    {
        if (!IsAlive)
            return;

        _presentation = presentation ?? new PreviewBoardPresentation();
        RefreshLayout();
    }

    public void SetDebugOptions(PreviewBoardDebugOptions debugOptions)
    {
        if (!IsAlive)
            return;

        _debugOptions = debugOptions ?? new PreviewBoardDebugOptions();
        RefreshDebugVisuals();
    }

    public void SetVisible(bool visible)
    {
        if (_boardRoot != null && _boardRoot.activeSelf != visible)
        {
            _boardRoot.SetActive(visible);
            BppLog.Info("MonsterPreviewBoard", $"SetVisible root='{_boardRoot.name}' visible={visible}");
        }
    }

    public void UpdateAnchor(Vector3 position, Quaternion rotation)
    {
        if (!IsAlive)
            return;

        _boardRoot.transform.SetPositionAndRotation(position, rotation);
        RefreshLayout();
    }

    public async Task RebuildAsync(
        IReadOnlyList<PreviewCardSpec> cards,
        IReadOnlyList<PreviewCardSpec> skillCards,
        Func<bool> isCancelled
    )
    {
        if (!IsAlive)
            return;

        Clear();

        await RebuildItemsAsync(cards, isCancelled);
        if (isCancelled())
        {
            Clear();
            return;
        }

        await RebuildSkillsAsync(skillCards, isCancelled);
        if (isCancelled())
        {
            Clear();
            return;
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

        foreach (var skillObject in _skillCards)
        {
            if (skillObject != null)
                _skillFactory.DestroyCard(skillObject);
        }
        _skillCards.Clear();
    }

    public void Dispose()
    {
        Clear();
        if (_boardRoot != null)
            UnityEngine.Object.Destroy(_boardRoot);
    }

    private async Task RebuildItemsAsync(IReadOnlyList<PreviewCardSpec> cards, Func<bool> isCancelled)
    {
        if (cards == null || cards.Count == 0)
            return;

        for (var index = 0; index < cards.Count; index++)
        {
            if (isCancelled())
                return;

            var anchor = new GameObject($"CardAnchor_{index}");
            anchor.transform.SetParent(_itemContentRoot.transform, false);
            _cardAnchors.Add(anchor);
            _cardSizes.Add(GetCardSize(cards[index]));

            var marker = CreateMarker($"CardCenter_{index}", CardCenterMarkerSize, false);
            marker.transform.SetParent(anchor.transform, false);
            _cardCenterMarkers.Add(marker);

            RefreshCardAnchor(index);

            var cardObject = await _factory.CreateCardAsync(cards[index], anchor.transform);
            if (isCancelled())
            {
                if (cardObject != null)
                    _factory.DestroyCard(cardObject);
                return;
            }

            if (cardObject == null)
                continue;

            cardObject.transform.SetParent(anchor.transform, false);
            _cards.Add(cardObject);
            RefreshCard(index);
        }
    }

    private async Task RebuildSkillsAsync(IReadOnlyList<PreviewCardSpec> skillCards, Func<bool> isCancelled)
    {
        if (skillCards == null || skillCards.Count == 0)
            return;

        var count = Mathf.Min(skillCards.Count, _skillSlots.Count);
        var leadingEmpty = GetLeadingEmptySkillSlots(count);

        for (var index = 0; index < count; index++)
        {
            if (isCancelled())
                return;

            var slotIndex = leadingEmpty + index;
            if (slotIndex >= _skillSlots.Count)
                break;

            var slot = _skillSlots[slotIndex];
            var cardObject = await _skillFactory.CreateCardAsync(skillCards[index], slot.transform);
            if (isCancelled())
            {
                if (cardObject != null)
                    _skillFactory.DestroyCard(cardObject);
                return;
            }

            if (cardObject == null)
                continue;

            cardObject.transform.SetParent(slot.transform, false);
            cardObject.transform.localPosition = Vector3.zero;
            cardObject.transform.localRotation = Quaternion.identity;
            cardObject.transform.localScale = _presentation.CardScale * SkillCardScaleFactor;
            _skillCards.Add(cardObject);
        }
    }

    private void RefreshLayout()
    {
        if (!IsAlive)
            return;

        RefreshVisuals();
        RefreshDebugVisuals();

        _itemContentRoot.transform.localPosition =
            _presentation.LocalOffset + new Vector3(0f, ContentYOffset, 0f);
        _itemContentRoot.transform.localRotation = Quaternion.identity;
        _itemContentRoot.transform.localScale = Vector3.one;

        _skillContentRoot.transform.localPosition =
            _presentation.LocalOffset
            + new Vector3(
                _presentation.BoardSize.x * 0.5f
                    + _presentation.SkillBoardWidth * 0.5f
                    + SkillBoardGap,
                ContentYOffset,
                0f
            );
        _skillContentRoot.transform.localRotation = Quaternion.identity;
        _skillContentRoot.transform.localScale = Vector3.one;

        RefreshBoardSlots();
        RefreshSkillSlots();

        for (var index = 0; index < _cardAnchors.Count; index++)
        {
            RefreshCardAnchor(index);
            RefreshCard(index);
        }
    }

    private void BuildVisuals()
    {
        _boardPlate = CreatePrimitive("BoardPlate", false, keepCollider: true);
        _boardPlate.layer = LayerMask.NameToLayer("Input");
        _boardPlate.AddComponent<PreviewBoardSurfaceMarker>();
        if (_boardPlate.TryGetComponent<Renderer>(out var boardPlateRenderer))
            boardPlateRenderer.enabled = false;
        _boardPlate.transform.SetParent(_visualRoot.transform, false);

        _boardFill = CreatePrimitive("BoardFill", false);
        _boardFill.transform.SetParent(_visualRoot.transform, false);

        _skillBoardFill = CreatePrimitive("SkillBoardFill", true);
        _skillBoardFill.transform.SetParent(_visualRoot.transform, false);

        _brandingBoardFill = CreatePrimitive("BrandingBoardFill", true);
        _brandingBoardFill.transform.SetParent(_visualRoot.transform, false);

        _boardCenterMarker = CreateMarker("BoardCenter", BoardCenterMarkerSize, false);
        _boardCenterMarker.transform.SetParent(_visualRoot.transform, false);

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"BoardBorder_{index}", false);
            border.transform.SetParent(_visualRoot.transform, false);
            _borderSegments.Add(border);
        }

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"SkillBoardBorder_{index}", true);
            border.transform.SetParent(_visualRoot.transform, false);
            _skillBoardBorders.Add(border);
        }

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"BrandingBoardBorder_{index}", true);
            border.transform.SetParent(_visualRoot.transform, false);
            _brandingBoardBorders.Add(border);
        }

        var brandingTextObject = new GameObject("BrandingText");
        brandingTextObject.transform.SetParent(_visualRoot.transform, false);
        _brandingText = brandingTextObject.AddComponent<TextMesh>();
        _brandingText.text = "BazaarPlusPlus";
        _brandingText.anchor = TextAnchor.MiddleCenter;
        _brandingText.alignment = TextAlignment.Center;
        _brandingText.characterSize = 0.24f;
        _brandingText.fontSize = 64;
        _brandingText.color = new Color(0.82f, 0.93f, 1f, 1f);
    }

    private void BuildBoardSlots()
    {
        for (var index = 0; index < BoardSlotCount; index++)
        {
            var slot = new GameObject($"BoardSlot_{index}");
            slot.transform.SetParent(_itemContentRoot.transform, false);
            _boardSlots.Add(slot);

            var marker = CreateMarker($"BoardSlotMarker_{index}", SlotMarkerSize, false);
            marker.transform.SetParent(slot.transform, false);
            _boardSlotMarkers.Add(marker);
        }
    }

    private void BuildSkillSlots()
    {
        for (var index = 0; index < SkillSlotCount; index++)
        {
            var slot = new GameObject($"SkillSlot_{index}");
            slot.transform.SetParent(_skillContentRoot.transform, false);
            _skillSlots.Add(slot);

            var marker = CreatePrimitive($"SkillSlotMarker_{index}", true);
            marker.transform.SetParent(slot.transform, false);
            _skillSlotMarkers.Add(marker);
        }
    }

    private void RefreshBoardSlots()
    {
        for (var index = 0; index < _boardSlots.Count; index++)
        {
            var slot = _boardSlots[index];
            if (slot == null)
                continue;

            slot.transform.localPosition = new Vector3(GetBoardSlotCenterX(index), 0f, 0f);
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;
        }
    }

    private void RefreshSkillSlots()
    {
        for (var index = 0; index < _skillSlots.Count; index++)
        {
            var slot = _skillSlots[index];
            if (slot == null)
                continue;

            slot.transform.localPosition = new Vector3(0f, 0f, GetSkillSlotCenterZ(index));
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;

            if (index < _skillSlotMarkers.Count && _skillSlotMarkers[index] != null)
            {
                _skillSlotMarkers[index].transform.localPosition = Vector3.zero;
                _skillSlotMarkers[index].transform.localRotation = Quaternion.identity;
                _skillSlotMarkers[index].transform.localScale = new Vector3(
                    Mathf.Max(0.01f, _presentation.SkillBoardWidth - SkillSlotInset * 2f),
                    0.03f,
                    Mathf.Max(0.01f, GetSkillSlotDepth() - SkillSlotInset)
                );
            }
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
        var spacing = _presentation.CardSpacing;

        anchor.transform.localPosition = new Vector3(centerX, spacing.y * index, spacing.z * index);
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
        cardObject.transform.localScale = _presentation.CardScale;
    }

    private void RefreshVisuals()
    {
        if (_visualRoot == null || _boardPlate == null || _borderSegments.Count < 4)
            return;

        var size = _presentation.BoardSize;
        var boardWidth = Mathf.Max(0.01f, size.x);
        var skillBoardWidth = Mathf.Max(0.01f, _presentation.SkillBoardWidth);
        var halfWidth = boardWidth * 0.5f;
        var halfDepth = size.y * 0.5f;
        var boardThickness = Mathf.Max(0.01f, _presentation.BoardThickness);
        var borderThickness = Mathf.Max(0.01f, _presentation.BorderThickness);
        var borderHeight = Mathf.Max(boardThickness, _presentation.BorderHeight);
        var skillBoardCenterX = halfWidth + SkillBoardGap + skillBoardWidth * 0.5f;
        var brandingBoardCenterX =
            halfWidth + SkillBoardGap * 2f + skillBoardWidth + BrandingBoardWidth * 0.5f;

        _visualRoot.transform.localPosition = Vector3.zero;
        _visualRoot.transform.localRotation = Quaternion.identity;
        _visualRoot.transform.localScale = Vector3.one;

        var combinedDepth = size.y;
        var combinedCenterZ = 0f;

        _boardPlate.transform.localPosition = new Vector3(0f, 0f, combinedCenterZ);
        _boardPlate.transform.localRotation = Quaternion.identity;
        _boardPlate.transform.localScale = new Vector3(boardWidth, boardThickness, combinedDepth);

        if (_boardFill != null)
        {
            _boardFill.transform.localPosition = new Vector3(0f, boardThickness * 0.2f, combinedCenterZ);
            _boardFill.transform.localRotation = Quaternion.identity;
            _boardFill.transform.localScale = new Vector3(
                Mathf.Max(0.01f, boardWidth - borderThickness * 0.5f),
                Mathf.Max(0.01f, boardThickness * 0.35f),
                Mathf.Max(0.01f, combinedDepth - borderThickness * 0.5f)
            );
        }

        if (_skillBoardFill != null)
        {
            _skillBoardFill.transform.localPosition = new Vector3(
                skillBoardCenterX,
                boardThickness * 0.2f,
                combinedCenterZ
            );
            _skillBoardFill.transform.localRotation = Quaternion.identity;
            _skillBoardFill.transform.localScale = new Vector3(
                skillBoardWidth,
                Mathf.Max(0.01f, boardThickness * 0.35f),
                combinedDepth
            );
        }

        if (_brandingBoardFill != null)
        {
            _brandingBoardFill.transform.localPosition = new Vector3(
                brandingBoardCenterX,
                boardThickness * 0.2f,
                combinedCenterZ
            );
            _brandingBoardFill.transform.localRotation = Quaternion.identity;
            _brandingBoardFill.transform.localScale = new Vector3(
                BrandingBoardWidth,
                Mathf.Max(0.01f, boardThickness * 0.35f),
                combinedDepth
            );
        }

        if (_boardCenterMarker != null)
        {
            _boardCenterMarker.transform.localPosition = new Vector3(0f, borderHeight + BoardCenterMarkerSize * 0.5f, 0f);
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
            new Vector3(-halfWidth, borderHeight * 0.5f, combinedCenterZ),
            new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
        );
        UpdateBorder(
            _borderSegments[3],
            new Vector3(halfWidth, borderHeight * 0.5f, combinedCenterZ),
            new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
        );

        if (_skillBoardBorders.Count >= 4)
        {
            var skillHalfWidth = skillBoardWidth * 0.5f;
            UpdateBorder(
                _skillBoardBorders[0],
                new Vector3(skillBoardCenterX, borderHeight * 0.5f, -halfDepth),
                new Vector3(skillBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _skillBoardBorders[1],
                new Vector3(skillBoardCenterX, borderHeight * 0.5f, halfDepth),
                new Vector3(skillBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _skillBoardBorders[2],
                new Vector3(skillBoardCenterX - skillHalfWidth, borderHeight * 0.5f, combinedCenterZ),
                new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
            );
            UpdateBorder(
                _skillBoardBorders[3],
                new Vector3(skillBoardCenterX + skillHalfWidth, borderHeight * 0.5f, combinedCenterZ),
                new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
            );
        }

        if (_brandingBoardBorders.Count >= 4)
        {
            var brandingHalfWidth = BrandingBoardWidth * 0.5f;
            UpdateBorder(
                _brandingBoardBorders[0],
                new Vector3(brandingBoardCenterX, borderHeight * 0.5f, -halfDepth),
                new Vector3(BrandingBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _brandingBoardBorders[1],
                new Vector3(brandingBoardCenterX, borderHeight * 0.5f, halfDepth),
                new Vector3(BrandingBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _brandingBoardBorders[2],
                new Vector3(brandingBoardCenterX - brandingHalfWidth, borderHeight * 0.5f, combinedCenterZ),
                new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
            );
            UpdateBorder(
                _brandingBoardBorders[3],
                new Vector3(brandingBoardCenterX + brandingHalfWidth, borderHeight * 0.5f, combinedCenterZ),
                new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
            );
        }

        if (_brandingText != null)
        {
            _brandingText.transform.localPosition = new Vector3(
                brandingBoardCenterX,
                borderHeight + 0.01f,
                0f
            );
            _brandingText.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);
            _brandingText.transform.localScale = Vector3.one * 0.2f;
        }
    }

    private void RefreshDebugVisuals()
    {
        var debugEnabled = IsDebugVisualEnabled();

        SetActive(_boardCenterMarker, debugEnabled && _debugOptions.ShowAnchorPoint);
        SetActive(_brandingText?.gameObject, debugEnabled && _debugOptions.ShowLabels);

        for (var index = 0; index < _boardSlotMarkers.Count; index++)
            SetActive(_boardSlotMarkers[index], debugEnabled && _debugOptions.ShowItemSlots);

        for (var index = 0; index < _skillSlotMarkers.Count; index++)
            SetActive(_skillSlotMarkers[index], debugEnabled && _debugOptions.ShowSkillSlots);

        for (var index = 0; index < _cardCenterMarkers.Count; index++)
            SetActive(_cardCenterMarkers[index], debugEnabled && _debugOptions.ShowCardBounds);
    }

    private float GetBoardSlotCenterX(int slotIndex)
    {
        var boardWidth = Mathf.Max(0.01f, _presentation.BoardSize.x);
        var slotWidth = boardWidth / BoardSlotCount;
        return -boardWidth * 0.5f + slotWidth * (slotIndex + 0.5f);
    }

    private float GetSkillSlotCenterZ(int slotIndex)
    {
        var slotDepth = GetSkillSlotDepth();
        return _presentation.BoardSize.y * 0.5f - slotDepth * (slotIndex + 0.5f);
    }

    private float GetSkillSlotDepth()
    {
        return Mathf.Max(0.01f, _presentation.BoardSize.y / SkillSlotCount);
    }

    private int GetLeadingEmptySkillSlots(int filledCount)
    {
        var freeSlots = Mathf.Max(0, SkillSlotCount - filledCount);
        return freeSlots / 2;
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

    private bool IsDebugVisualEnabled()
    {
        return _presentation.DebugEnabled && _debugOptions.Enabled;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private static GameObject CreatePrimitive(string name, bool isSkill, bool keepCollider = false)
    {
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        primitive.name = name;

        var collider = primitive.GetComponent<Collider>();
        if (collider != null && !keepCollider)
            UnityEngine.Object.Destroy(collider);

        ApplyBoardMaterial(primitive, name.Contains("Border"), isSkill);
        return primitive;
    }


    private static GameObject CreateMarker(string name, float size, bool isSkill)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;

        var collider = marker.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);

        ApplyDebugMarkerMaterial(marker, isSkill);
        marker.transform.localScale = Vector3.one * size;
        return marker;
    }

    private static void ApplyBoardMaterial(GameObject target, bool isBorder, bool isSkill)
    {
        if (!target.TryGetComponent<Renderer>(out var renderer))
            return;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        var material = new Material(shader);
        var isFill = target.name.Contains("Fill");
        var isBranding = target.name.Contains("Branding");
        material.color = isBranding
            ? (isBorder ? BrandingBoardBorderColor : BrandingBoardFillColor)
            : isSkill
                ? (isBorder ? SkillBoardBorderColor : SkillBoardFillColor)
            : isFill
                ? ItemBoardFillColor
                : (isBorder ? ItemBoardBorderColor : ItemBoardAccentColor);
        renderer.sharedMaterial = material;
    }

    private static void ApplyDebugMarkerMaterial(GameObject target, bool isSkill)
    {
        if (!target.TryGetComponent<Renderer>(out var renderer))
            return;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        var material = new Material(shader);
        material.color = isSkill ? DebugSkillMarkerColor : DebugItemMarkerColor;
        renderer.sharedMaterial = material;
    }
}
