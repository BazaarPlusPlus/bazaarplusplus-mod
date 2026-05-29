#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Players;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.UI.Tooltips;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CardSetPreview;

// Clones the native MonsterBoardTooltip via reflection and drives it items-only
// (skill + health hidden) so CardSetPreview reuses the game's item rendering
// pipeline verbatim instead of reimplementing it. Cohesive concerns are
// delegated to MonsterBoardTooltipBindings (cached reflection), SponsorPanelRenderer
// (sponsor chrome), SyntheticMonsterFactory (synthetic TMonster), and
// ItemBoardTextHelpers (pure string/CJK helpers).
internal sealed class ItemBoardOverlay : IDisposable
{
    private sealed class OverlayRuntimeBehaviour : MonoBehaviour { }

    private readonly SponsorPanelRenderer _sponsorPanel = new();

    private GameObject? _overlayRoot;
    private RectTransform? _overlayRootRect;
    private OverlayRuntimeBehaviour? _runtimeBehaviour;
    private Component? _view;
    private RectTransform? _viewRect;
    private Transform? _hostRoot;
    private bool _hasPinnedAnchoredPosition;
    private Vector2 _pinnedAnchoredPosition;
    private ItemBoardTemplateSetRequest? _currentRequest;
    private Coroutine? _revealCoroutine;
    private int _revealRevision;

    public bool IsAlive => _overlayRoot != null && _view != null;

    public bool Ensure(CardTooltipController controller)
    {
        if (controller == null)
            return false;

        var sourceView =
            MonsterBoardTooltipBindings.MonsterBoardTooltipField?.GetValue(controller) as Component;
        if (sourceView == null)
        {
            BppLog.Warn(
                "ItemBoardOverlay",
                "Ensure failed because source MonsterBoardTooltip was null"
            );
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
        ConfigureItemsOnlyVisuals();
        FlushRenderedCards();
        MonsterBoardTooltipBindings.HandlePoolingMethod?.Invoke(_view, null);
        RenderItems(monster.Player.Hand.Items);
        if (input.Carpet != null)
            MonsterBoardTooltipBindings.SetCarpetMethod?.Invoke(_view, new object[] { input.Carpet });

        if (input.AnchoredPosition.HasValue)
            SetAnchoredPosition(input.AnchoredPosition.Value);

        SetScale(input.Scale);
        _sponsorPanel.UpdateSponsorVisual(
            _currentRequest?.SponsorText,
            _currentRequest?.SponsorName,
            _currentRequest?.SponsorTier ?? 0,
            _currentRequest?.CandidateIndex ?? 0,
            _currentRequest?.CandidateCount ?? 0,
            _currentRequest?.IsAlertState == true,
            input.Scale,
            _viewRect?.anchoredPosition
        );
        _overlayRoot!.SetActive(true);
        _overlayRoot.transform.SetAsLastSibling();
        _view.gameObject.SetActive(true);
        MonsterBoardTooltipBindings.ShowMethod?.Invoke(_view, new object[] { input.ShowTime });
        ScheduleRevealPasses();
        BppLog.Info(
            "ItemBoardOverlay",
            $"Render monster={monster.InternalName ?? monster.Id.ToString()} carpet={(input.Carpet != null ? input.Carpet.name : "null")} items={monster.Player?.Hand?.Items?.Count ?? 0} anchored={_viewRect?.anchoredPosition}"
        );
    }

    public void RenderTemplateSet(ItemBoardTemplateSetRequest request)
    {
        if (!IsAlive || request == null)
            return;

        _currentRequest = request.Clone();
        var items =
            _currentRequest.Items?.Where(item => item?.TemplateId != Guid.Empty).ToList()
            ?? new List<ItemBoardItemSpec>();
        if (items.Count == 0)
        {
            Hide();
            return;
        }

        Render(
            new ItemBoardRenderInput
            {
                Monster = SyntheticMonsterFactory.BuildSyntheticMonster(items),
                AnchoredPosition = _currentRequest.AnchoredPosition,
                Scale = _currentRequest.Scale,
                ShowTime = _currentRequest.ShowTime,
            }
        );
    }

    public void SetAnchoredPosition(Vector2 anchoredPosition)
    {
        _hasPinnedAnchoredPosition = true;
        _pinnedAnchoredPosition = anchoredPosition;
        if (_viewRect != null)
            _viewRect.anchoredPosition = anchoredPosition;

        _sponsorPanel.UpdateSponsorPlacement(
            _viewRect?.localScale.x ?? 1f,
            _viewRect?.anchoredPosition
        );
    }

    public void ClearAnchoredPositionOverride()
    {
        _hasPinnedAnchoredPosition = false;
    }

    public void SetScale(float scale)
    {
        if (_viewRect == null)
            return;

        var clamped = Mathf.Clamp(scale, 0.2f, 2f);
        _viewRect.localScale = Vector3.one * clamped;
        _sponsorPanel.UpdateSponsorPlacement(clamped, _viewRect.anchoredPosition);
    }

    public void Hide(float hideTime = 0f)
    {
        if (!IsAlive || _view == null || _overlayRoot == null)
            return;

        StopRevealCoroutine();
        MonsterBoardTooltipBindings.HideMethod?.Invoke(_view, new object[] { hideTime });
        _overlayRoot.SetActive(false);
        BppLog.Info("ItemBoardOverlay", "Hide");
    }

    public void ForceRevealCards()
    {
        if (
            _overlayRoot == null
            || MonsterBoardTooltipBindings.CardPreviewBaseType == null
            || MonsterBoardTooltipBindings.CardPreviewShowMethod == null
        )
            return;

        var previewCards = _overlayRoot.GetComponentsInChildren(
            MonsterBoardTooltipBindings.CardPreviewBaseType,
            false
        );
        foreach (var previewCard in previewCards)
        {
            if (previewCard == null || !previewCard.gameObject.activeInHierarchy)
                continue;

            MonsterBoardTooltipBindings.CardPreviewShowMethod.Invoke(
                previewCard,
                new object[] { true }
            );
        }
    }

    private void ScheduleRevealPasses()
    {
        if (_runtimeBehaviour == null)
            return;

        StopRevealCoroutine();
        _revealRevision++;
        _revealCoroutine = _runtimeBehaviour.StartCoroutine(
            RevealCardsAfterFrames(_revealRevision)
        );
    }

    private void StopRevealCoroutine()
    {
        if (_runtimeBehaviour == null || _revealCoroutine == null)
            return;

        _runtimeBehaviour.StopCoroutine(_revealCoroutine);
        _revealCoroutine = null;
    }

    private System.Collections.IEnumerator RevealCardsAfterFrames(int revision)
    {
        yield return null;
        if (revision != _revealRevision)
            yield break;

        ForceRevealCards();
        yield return null;
        if (revision != _revealRevision)
            yield break;

        ForceRevealCards();
        yield return null;
        if (revision != _revealRevision)
            yield break;

        ForceRevealCards();
        _revealCoroutine = null;
    }

    public void Dispose()
    {
        StopRevealCoroutine();
        if (_overlayRoot != null)
            UnityEngine.Object.Destroy(_overlayRoot);

        _overlayRoot = null;
        _overlayRootRect = null;
        _runtimeBehaviour = null;
        _view = null;
        _viewRect = null;
        _sponsorPanel.Reset();
        _hostRoot = null;
        _hasPinnedAnchoredPosition = false;
        _pinnedAnchoredPosition = default;
        _currentRequest = null;
    }

    private void CreateOverlay(Component sourceView, Transform hostRoot)
    {
        Dispose();

        _hostRoot = hostRoot;
        _overlayRoot = new GameObject("BppItemBoardOverlay", typeof(RectTransform));
        _overlayRootRect = _overlayRoot.GetComponent<RectTransform>();
        _runtimeBehaviour = _overlayRoot.AddComponent<OverlayRuntimeBehaviour>();
        _overlayRootRect.SetParent(hostRoot, false);
        _overlayRootRect.anchorMin = Vector2.zero;
        _overlayRootRect.anchorMax = Vector2.one;
        _overlayRootRect.offsetMin = Vector2.zero;
        _overlayRootRect.offsetMax = Vector2.zero;
        _overlayRootRect.pivot = new Vector2(0.5f, 0.5f);

        var clone = UnityEngine.Object.Instantiate(sourceView.gameObject, _overlayRootRect, false);
        clone.name = "BppItemBoardTooltip";
        _view = clone.GetComponent(
            MonsterBoardTooltipBindings.MonsterBoardTooltipType?.FullName
                ?? "TheBazaar.UI.Tooltips.MonsterBoardTooltip"
        );
        _viewRect = clone.GetComponent<RectTransform>();
        if (_viewRect != null)
        {
            _viewRect.anchorMin = new Vector2(0.5f, 0.5f);
            _viewRect.anchorMax = new Vector2(0.5f, 0.5f);
            _viewRect.pivot = new Vector2(0.5f, 0.5f);
        }

        ConfigureItemsOnlyVisuals();
        _sponsorPanel.CreateSponsorPanel(_overlayRootRect);

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
            _sponsorPanel.UpdateSponsorPlacement(_viewRect.localScale.x, _viewRect.anchoredPosition);
            return;
        }

        var sourceRect = sourceView.GetComponent<RectTransform>();
        if (sourceRect == null)
        {
            _viewRect.anchoredPosition = new Vector2(260f, -20f);
            _sponsorPanel.UpdateSponsorPlacement(_viewRect.localScale.x, _viewRect.anchoredPosition);
            return;
        }

        _viewRect.sizeDelta = sourceRect.rect.size;
        var canvas = _hostRoot.GetComponent<Canvas>();
        var camera =
            canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
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
            _sponsorPanel.UpdateSponsorPlacement(_viewRect.localScale.x, _viewRect.anchoredPosition);
            return;
        }

        _viewRect.anchoredPosition = new Vector2(260f, -20f);
        _sponsorPanel.UpdateSponsorPlacement(_viewRect.localScale.x, _viewRect.anchoredPosition);
    }

    private void ConfigureItemsOnlyVisuals()
    {
        if (_view == null)
            return;

        if (
            MonsterBoardTooltipBindings.SkillParentField?.GetValue(_view) is RectTransform skillParent
        )
            skillParent.gameObject.SetActive(false);

        var carpetTransform = (
            MonsterBoardTooltipBindings.CarpetImageField?.GetValue(_view) as Component
        )?.transform;
        if (MonsterBoardTooltipBindings.HealthTextField?.GetValue(_view) is TMP_Text healthText)
            HideHealthVisuals(healthText.transform, carpetTransform);
    }

    private void FlushRenderedCards()
    {
        if (_view == null)
            return;

        FlushPool(MonsterBoardTooltipBindings.SmallItemPoolField);
        FlushPool(MonsterBoardTooltipBindings.MediumItemPoolField);
        FlushPool(MonsterBoardTooltipBindings.LargeItemPoolField);
        FlushPool(MonsterBoardTooltipBindings.SkillPoolField);
        ClearListField(MonsterBoardTooltipBindings.ActiveCardsField);
        ClearListField(MonsterBoardTooltipBindings.ActiveSkillsField);

        if (MonsterBoardTooltipBindings.SocketsField?.GetValue(_view) is RectTransform[] sockets)
        {
            foreach (var socket in sockets)
                FlushChildren(socket);
        }

        if (
            MonsterBoardTooltipBindings.SkillParentField?.GetValue(_view) is RectTransform skillParent
        )
            FlushChildren(skillParent);
    }

    private void FlushPool(FieldInfo? poolField)
    {
        if (poolField?.GetValue(_view) is not System.Collections.IEnumerable pool)
            return;

        foreach (var entry in pool)
        {
            if (entry is not Component component)
                continue;

            component.transform.localScale = Vector3.one;
            component.gameObject.SetActive(false);
        }
    }

    private void ClearListField(FieldInfo? listField)
    {
        if (listField?.GetValue(_view) is System.Collections.IList list)
            list.Clear();
    }

    private static void FlushChildren(Transform? parent)
    {
        if (parent == null)
            return;

        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (child == null)
                continue;

            child.localScale = Vector3.one;
            child.gameObject.SetActive(false);
        }
    }

    private void RenderItems(IEnumerable<TCardInstanceItem>? items)
    {
        if (_view == null || MonsterBoardTooltipBindings.AddCardMethod == null || items == null)
            return;

        foreach (var item in items)
        {
            if (item != null)
                MonsterBoardTooltipBindings.AddCardMethod.Invoke(_view, new object[] { item });
        }
    }

    private static void HideHealthVisuals(
        Transform? healthTextTransform,
        Transform? carpetTransform
    )
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
