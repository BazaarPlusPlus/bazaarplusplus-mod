#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using HarmonyLib;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.UI.Tooltips;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.ItemBoard;

internal sealed class ItemBoardOverlay : IDisposable
{
    private static readonly Type? MonsterBoardTooltipType = AccessTools.TypeByName(
        "TheBazaar.UI.Tooltips.MonsterBoardTooltip"
    );

    private static readonly FieldInfo? MonsterBoardTooltipField = AccessTools.Field(
        typeof(CardTooltipController),
        "_monsterBoardTooltip"
    );

    private static readonly MethodInfo? HandlePoolingMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "HandlePooling")
            : null;

    private static readonly MethodInfo? AddCardMethod =
        MonsterBoardTooltipType != null ? AccessTools.Method(MonsterBoardTooltipType, "AddCard") : null;

    private static readonly MethodInfo? SetCarpetMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "SetCarpet")
            : null;

    private static readonly MethodInfo? ShowMethod =
        MonsterBoardTooltipType != null ? AccessTools.Method(MonsterBoardTooltipType, "Show") : null;

    private static readonly MethodInfo? HideMethod =
        MonsterBoardTooltipType != null ? AccessTools.Method(MonsterBoardTooltipType, "Hide") : null;

    private static readonly FieldInfo? SkillParentField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_skillParent")
            : null;

    private static readonly FieldInfo? HealthTextField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_healthText")
            : null;

    private static readonly FieldInfo? CarpetImageField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_carpetImage")
            : null;

    private GameObject? _overlayRoot;
    private RectTransform? _overlayRootRect;
    private Component? _view;
    private RectTransform? _viewRect;
    private Transform? _hostRoot;
    private bool _hasPinnedAnchoredPosition;
    private Vector2 _pinnedAnchoredPosition;

    public bool IsAlive => _overlayRoot != null && _view != null;

    public bool Ensure(CardTooltipController controller)
    {
        if (controller == null)
            return false;

        var sourceView = MonsterBoardTooltipField?.GetValue(controller) as Component;
        if (sourceView == null)
        {
            BppLog.Warn("ItemBoardOverlay", "Ensure failed because source MonsterBoardTooltip was null");
            return false;
        }

        var rootCanvas = controller.RootCanvas;
        if (rootCanvas == null)
        {
            BppLog.Warn("ItemBoardOverlay", "Ensure failed because tooltip root canvas was null");
            return false;
        }

        if (!ReferenceEquals(_hostRoot, rootCanvas) || !IsAlive)
            CreateOverlay(sourceView, rootCanvas);

        UpdatePlacement(sourceView);
        return IsAlive;
    }

    public void Render(ItemBoardRenderInput input)
    {
        if (!IsAlive || _view == null || input?.Monster == null)
            return;

        var monster = input.Monster;
        HandlePoolingMethod?.Invoke(_view, null);
        RenderItemsOnly(monster.Player.Hand.Items);
        if (input.Carpet != null)
            SetCarpetMethod?.Invoke(_view, new object[] { input.Carpet });

        if (input.AnchoredPosition.HasValue)
            SetAnchoredPosition(input.AnchoredPosition.Value);

        _overlayRoot!.SetActive(true);
        _overlayRoot.transform.SetAsLastSibling();
        _view.gameObject.SetActive(true);
        ShowMethod?.Invoke(_view, new object[] { input.ShowTime });
        BppLog.Info(
            "ItemBoardOverlay",
            $"Render monster={monster.InternalName ?? monster.Id.ToString()} carpet={(input.Carpet != null ? input.Carpet.name : "null")} items={monster.Player?.Hand?.Items?.Count ?? 0} anchored={_viewRect?.anchoredPosition}"
        );
    }

    public void SetAnchoredPosition(Vector2 anchoredPosition)
    {
        _hasPinnedAnchoredPosition = true;
        _pinnedAnchoredPosition = anchoredPosition;
        if (_viewRect != null)
            _viewRect.anchoredPosition = anchoredPosition;
    }

    public void ClearAnchoredPositionOverride()
    {
        _hasPinnedAnchoredPosition = false;
    }

    public void Hide(float hideTime = 0f)
    {
        if (!IsAlive || _view == null || _overlayRoot == null)
            return;

        HideMethod?.Invoke(_view, new object[] { hideTime });
        _overlayRoot.SetActive(false);
        BppLog.Info("ItemBoardOverlay", "Hide");
    }

    public void Dispose()
    {
        if (_overlayRoot != null)
            UnityEngine.Object.Destroy(_overlayRoot);

        _overlayRoot = null;
        _overlayRootRect = null;
        _view = null;
        _viewRect = null;
        _hostRoot = null;
        _hasPinnedAnchoredPosition = false;
        _pinnedAnchoredPosition = default;
    }

    private void CreateOverlay(Component sourceView, Transform hostRoot)
    {
        Dispose();

        _hostRoot = hostRoot;
        _overlayRoot = new GameObject("BppItemBoardOverlay", typeof(RectTransform));
        _overlayRootRect = _overlayRoot.GetComponent<RectTransform>();
        _overlayRootRect.SetParent(hostRoot, false);
        _overlayRootRect.anchorMin = Vector2.zero;
        _overlayRootRect.anchorMax = Vector2.one;
        _overlayRootRect.offsetMin = Vector2.zero;
        _overlayRootRect.offsetMax = Vector2.zero;
        _overlayRootRect.pivot = new Vector2(0.5f, 0.5f);

        var clone = UnityEngine.Object.Instantiate(sourceView.gameObject, _overlayRootRect, false);
        clone.name = "BppItemBoardTooltip";
        _view = clone.GetComponent(
            MonsterBoardTooltipType?.FullName ?? "TheBazaar.UI.Tooltips.MonsterBoardTooltip"
        );
        _viewRect = clone.GetComponent<RectTransform>();
        if (_viewRect != null)
        {
            _viewRect.anchorMin = new Vector2(0.5f, 0.5f);
            _viewRect.anchorMax = new Vector2(0.5f, 0.5f);
            _viewRect.pivot = new Vector2(0.5f, 0.5f);
        }

        ConfigureItemsOnlyVisuals();

        _overlayRoot.SetActive(false);
        BppLog.Info(
            "ItemBoardOverlay",
            $"Created overlay hostRoot={hostRoot.name} cloneAlive={_view != null}"
        );
    }

    private void UpdatePlacement(Component sourceView)
    {
        if (_overlayRootRect == null || _viewRect == null || _hostRoot == null)
            return;

        if (_hasPinnedAnchoredPosition)
        {
            _viewRect.anchoredPosition = _pinnedAnchoredPosition;
            return;
        }

        var sourceRect = sourceView.GetComponent<RectTransform>();
        if (sourceRect == null)
        {
            _viewRect.anchoredPosition = new Vector2(260f, -20f);
            return;
        }

        _viewRect.sizeDelta = sourceRect.rect.size;
        var canvas = _hostRoot.GetComponent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        var screenPoint = RectTransformUtility.WorldToScreenPoint(camera, sourceRect.position);
        if (
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayRootRect,
                screenPoint,
                camera,
                out var localPoint
            )
        )
        {
            _viewRect.anchoredPosition = localPoint;
            return;
        }

        _viewRect.anchoredPosition = new Vector2(260f, -20f);
    }

    private void ConfigureItemsOnlyVisuals()
    {
        if (_view == null)
            return;

        if (SkillParentField?.GetValue(_view) is RectTransform skillParent)
            skillParent.gameObject.SetActive(false);

        var carpetTransform = (CarpetImageField?.GetValue(_view) as Component)?.transform;
        if (HealthTextField?.GetValue(_view) is TMP_Text healthText)
            HideHealthVisuals(healthText.transform, carpetTransform);
    }

    private void RenderItemsOnly(IEnumerable<TCardInstanceItem>? items)
    {
        if (_view == null || AddCardMethod == null || items == null)
            return;

        foreach (var item in items)
        {
            if (item != null)
                AddCardMethod.Invoke(_view, new object[] { item });
        }
    }

    private static void HideHealthVisuals(Transform? healthTextTransform, Transform? carpetTransform)
    {
        if (healthTextTransform == null)
            return;

        healthTextTransform.gameObject.SetActive(false);

        var healthRoot = FindHealthContainer(healthTextTransform, carpetTransform);
        if (healthRoot != null)
            healthRoot.gameObject.SetActive(false);
    }

    private static Transform? FindHealthContainer(
        Transform healthTextTransform,
        Transform? carpetTransform
    )
    {
        Transform? fallback = healthTextTransform.parent;
        for (var current = healthTextTransform.parent; current != null; current = current.parent)
        {
            if (current == carpetTransform)
                break;

            if (current.name.IndexOf("health", StringComparison.OrdinalIgnoreCase) >= 0)
                return current;

            if (
                current != healthTextTransform.parent
                && current.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length > 1
            )
            {
                fallback = current;
            }
        }

        return fallback;
    }
}
