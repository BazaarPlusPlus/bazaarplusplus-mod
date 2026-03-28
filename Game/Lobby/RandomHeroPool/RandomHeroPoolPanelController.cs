#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using TheBazaar.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

internal sealed class RandomHeroPoolPanelController : MonoBehaviour
{
    private const string LogCategory = "RandomHeroPool";
    private const string ConfigButtonObjectName = "BPP_RandomHeroPoolConfigButton";
    private const string PopupObjectName = "BPP_RandomHeroPoolPopup";
    private const string PopupActionsRootObjectName = "BPP_RandomHeroPoolPopupActions";
    private const string PopupEntriesRootObjectName = "BPP_RandomHeroPoolPopupEntries";
    private const string PopupSelectAllButtonObjectName = "BPP_RandomHeroPoolSelectAll";
    private const string PopupClearButtonObjectName = "BPP_RandomHeroPoolClear";
    private const string PopupEntryPrefix = "BPP_RandomHeroPoolHero_";
    private const string PopupEmptyEntryObjectName = "BPP_RandomHeroPoolHero_Empty";
    private const string SelectedPoolPrefsKeyPrefix = "BPP.RandomHeroPool.Selected";
    private const string KnownUnlockedPrefsKeyPrefix = "BPP.RandomHeroPool.KnownUnlocked";
    private const string AnonymousAccountScope = "anonymous";

    private static readonly System.Reflection.FieldInfo? HeroItemViewsField = AccessTools.Field(
        typeof(HeroSelectButtonsView),
        "HeroItemViews"
    );

    private static readonly System.Reflection.FieldInfo? RandomHeroToggleField = AccessTools.Field(
        typeof(HeroSelectButtonsView),
        "RandomHeroToggle"
    );

    private static readonly System.Reflection.FieldInfo? HeroItemIsUnlockedField = AccessTools.Field(
        typeof(HeroItemView),
        "_isUnlocked"
    );

    private readonly Dictionary<string, Toggle> _heroEntryToggles = new(StringComparer.Ordinal);

    private HeroSelectButtonsView? _view;
    private Toggle? _randomHeroToggle;
    private Button? _configButton;
    private RectTransform? _popupRoot;
    private Transform? _popupEntriesRoot;
    private RandomHeroPoolState? _state;
    private string[] _unlockedHeroIds = Array.Empty<string>();
    private bool _popupVisible;
    private bool _isRebindingToggles;
    private bool _warnedMissingHeroUnlockStateField;
    private bool _warnedMissingRandomHeroToggleField;
    private bool _warnedMissingCanvasCamera;
    private bool _attachCompleted;
    private float _nextAttachRetryAt;

    internal static void Attach(HeroSelectButtonsView view)
    {
        if (view == null)
            return;

        var controller = view.GetComponent<RandomHeroPoolPanelController>();
        if (controller == null)
            controller = view.gameObject.AddComponent<RandomHeroPoolPanelController>();

        controller.TryAttach(view);
    }

    private void OnDisable()
    {
        SetPopupVisible(false);
    }

    private void OnDestroy()
    {
        if (_configButton != null)
            _configButton.onClick.RemoveListener(OnConfigButtonClicked);
    }

    private void LateUpdate()
    {
        if (!_attachCompleted && _view != null && Time.unscaledTime >= _nextAttachRetryAt)
        {
            _nextAttachRetryAt = Time.unscaledTime + 0.5f;
            TryAttach(_view);
        }

        if (_configButton == null || _randomHeroToggle == null)
            return;

        var shouldBeVisible = _randomHeroToggle.gameObject.activeSelf;
        if (_configButton.gameObject.activeSelf != shouldBeVisible)
            _configButton.gameObject.SetActive(shouldBeVisible);

        if (!shouldBeVisible && _popupVisible)
            SetPopupVisible(false);
    }

    private void TryAttach(HeroSelectButtonsView view)
    {
        _attachCompleted = false;
        _view = view;
        if (!TryReadRandomHeroToggle(view, out var randomHeroToggle))
            return;

        _randomHeroToggle = randomHeroToggle;
        if (!TryEnsureConfigButton(randomHeroToggle))
            return;

        if (!TryEnsurePopup())
            return;

        RefreshPopupEntries();
        SetPopupVisible(false);
        _attachCompleted = true;
    }

    private bool TryReadRandomHeroToggle(HeroSelectButtonsView view, out Toggle randomHeroToggle)
    {
        var reflectedToggle = RandomHeroToggleField?.GetValue(view) as Toggle;
        if (reflectedToggle != null)
        {
            randomHeroToggle = reflectedToggle;
            return true;
        }

        randomHeroToggle = null!;
        if (!_warnedMissingRandomHeroToggleField)
        {
            _warnedMissingRandomHeroToggleField = true;
            BppLog.Warn(
                LogCategory,
                "RandomHeroToggle field was unavailable; skipping random hero pool UI injection."
            );
        }

        return false;
    }

    private bool TryEnsureConfigButton(Toggle randomHeroToggle)
    {
        var randomToggleTransform = randomHeroToggle.transform;
        var randomToggleParent = randomToggleTransform.parent;
        if (randomToggleParent == null)
        {
            BppLog.Warn(
                LogCategory,
                "Random hero toggle has no parent transform; skipping pool UI injection."
            );
            return false;
        }

        var configButtonTransform = randomToggleParent.Find(ConfigButtonObjectName);
        GameObject configButtonObject;
        if (configButtonTransform != null)
        {
            configButtonObject = configButtonTransform.gameObject;
        }
        else
        {
            configButtonObject = UnityEngine.Object.Instantiate(
                randomHeroToggle.gameObject,
                randomToggleParent
            );
            configButtonObject.name = ConfigButtonObjectName;
            configButtonObject.transform.SetSiblingIndex(randomToggleTransform.GetSiblingIndex() + 1);
        }

        var button = configButtonObject.GetComponent<Button>();
        if (button == null)
            button = configButtonObject.AddComponent<Button>();

        var randomToggleSelectable = randomHeroToggle.GetComponent<Selectable>();
        if (randomToggleSelectable != null)
        {
            button.transition = randomToggleSelectable.transition;
            button.colors = randomToggleSelectable.colors;
            button.spriteState = randomToggleSelectable.spriteState;
            button.animationTriggers = randomToggleSelectable.animationTriggers;
            button.targetGraphic = configButtonObject.GetComponentInChildren<Graphic>(
                includeInactive: true
            );
        }
        else if (button.targetGraphic == null)
        {
            button.targetGraphic = configButtonObject.GetComponentInChildren<Graphic>(
                includeInactive: true
            );
        }

        var clonedToggle = configButtonObject.GetComponent<Toggle>();
        if (clonedToggle != null)
        {
            clonedToggle.onValueChanged.RemoveAllListeners();
            clonedToggle.enabled = false;
        }

        SetToggleCheckmarksVisible(configButtonObject.transform, visible: false);

        if (!TrySetLabel(configButtonObject, "Pool"))
        {
            BppLog.Warn(LogCategory, "Config button label was missing on cloned random toggle.");
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnConfigButtonClicked);
        button.interactable = true;
        button.gameObject.SetActive(randomHeroToggle.gameObject.activeSelf);

        _configButton = button;
        return true;
    }

    private bool TryEnsurePopup()
    {
        if (_view == null)
        {
            BppLog.Warn(LogCategory, "Hero select view was unavailable while building popup.");
            return false;
        }

        if (_configButton == null)
        {
            BppLog.Warn(LogCategory, "Config button was unavailable while building popup.");
            return false;
        }

        var hostRect = _view.transform as RectTransform;
        if (hostRect == null)
        {
            BppLog.Warn(
                LogCategory,
                "Hero select root is not a RectTransform; skipping popup initialization."
            );
            return false;
        }

        var existingPopup = hostRect.Find(PopupObjectName) as RectTransform;
        if (existingPopup != null)
        {
            _popupRoot = existingPopup;
            _popupEntriesRoot = existingPopup.Find(PopupEntriesRootObjectName);
            if (_popupEntriesRoot != null)
            {
                EnsureActionButtons(existingPopup);
                return true;
            }

            existingPopup.name = $"{PopupObjectName}_Stale";
            UnityEngine.Object.Destroy(existingPopup.gameObject);
            _popupRoot = null;
            _popupEntriesRoot = null;
        }

        var popupObject = new GameObject(
            PopupObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );
        _popupRoot = popupObject.GetComponent<RectTransform>();
        _popupRoot.SetParent(hostRect, worldPositionStays: false);
        _popupRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _popupRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _popupRoot.pivot = new Vector2(0f, 1f);
        _popupRoot.anchoredPosition = Vector2.zero;

        var backgroundImage = popupObject.GetComponent<Image>();
        backgroundImage.color = ResolvePopupBackgroundColor();
        backgroundImage.raycastTarget = true;

        var rootLayout = popupObject.GetComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(8, 8, 8, 8);
        rootLayout.spacing = 4f;
        rootLayout.childAlignment = TextAnchor.UpperLeft;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;

        var fitter = popupObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = CreatePassiveEntry("BPP_RandomHeroPoolHeader", "Random Hero Pool", _popupRoot);
        if (header == null)
        {
            BppLog.Warn(LogCategory, "Could not create random hero pool popup header.");
            return false;
        }

        EnsureActionButtons(_popupRoot);

        var entriesRoot = new GameObject(
            PopupEntriesRootObjectName,
            typeof(RectTransform),
            typeof(VerticalLayoutGroup)
        );
        var entriesRect = entriesRoot.GetComponent<RectTransform>();
        entriesRect.SetParent(_popupRoot, worldPositionStays: false);

        var entriesLayout = entriesRoot.GetComponent<VerticalLayoutGroup>();
        entriesLayout.spacing = 2f;
        entriesLayout.childAlignment = TextAnchor.UpperLeft;
        entriesLayout.childControlWidth = true;
        entriesLayout.childControlHeight = true;
        entriesLayout.childForceExpandWidth = true;
        entriesLayout.childForceExpandHeight = false;

        _popupEntriesRoot = entriesRoot.transform;
        return true;
    }

    private void EnsureActionButtons(Transform popupRootTransform)
    {
        var actionsRoot = popupRootTransform.Find(PopupActionsRootObjectName);
        if (actionsRoot == null)
        {
            var actionsObject = new GameObject(
                PopupActionsRootObjectName,
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup)
            );
            var actionRect = actionsObject.GetComponent<RectTransform>();
            actionRect.SetParent(popupRootTransform, worldPositionStays: false);

            var actionLayout = actionsObject.GetComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 4f;
            actionLayout.childAlignment = TextAnchor.UpperLeft;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = true;
            actionLayout.childForceExpandHeight = false;
            actionsRoot = actionsObject.transform;
        }

        EnsureActionButton(
            actionsRoot,
            PopupSelectAllButtonObjectName,
            "Select All",
            OnSelectAllClicked
        );
        EnsureActionButton(actionsRoot, PopupClearButtonObjectName, "Clear", OnClearClicked);
    }

    private void EnsureActionButton(
        Transform actionsRoot,
        string objectName,
        string label,
        Action onClicked
    )
    {
        var existing = actionsRoot.Find(objectName);
        var actionObject = existing != null
            ? existing.gameObject
            : CreateActionButton(objectName, label, actionsRoot, onClicked);
        if (actionObject == null)
            return;

        var actionButton = actionObject.GetComponent<Button>();
        if (actionButton == null)
            return;

        actionButton.onClick.RemoveAllListeners();
        actionButton.onClick.AddListener(() => onClicked());
        TrySetLabel(actionObject, label);
    }

    private GameObject? CreateActionButton(
        string objectName,
        string label,
        Transform parent,
        Action onClicked
    )
    {
        if (_configButton == null)
            return null;

        var actionObject = UnityEngine.Object.Instantiate(_configButton.gameObject, parent);
        actionObject.name = objectName;

        var toggle = actionObject.GetComponent<Toggle>();
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.enabled = false;
        }

        SetToggleCheckmarksVisible(actionObject.transform, visible: false);
        var button = actionObject.GetComponent<Button>();
        if (button == null)
            button = actionObject.AddComponent<Button>();

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClicked());
        button.interactable = true;
        TrySetLabel(actionObject, label);
        return actionObject;
    }

    private void OnConfigButtonClicked()
    {
        if (_popupRoot == null || _popupEntriesRoot == null)
        {
            BppLog.Warn(LogCategory, "Popup was unavailable when opening random hero pool panel.");
            return;
        }

        if (!_popupVisible)
            RefreshPopupEntries();

        SetPopupVisible(!_popupVisible);
    }

    private void SetPopupVisible(bool visible)
    {
        _popupVisible = visible && _popupRoot != null;
        if (_popupRoot == null)
            return;

        _popupRoot.gameObject.SetActive(_popupVisible);
        if (!_popupVisible)
            return;

        PositionPopup();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_popupRoot);
    }

    private void PositionPopup()
    {
        if (_popupRoot == null || _configButton == null)
            return;

        var popupParent = _popupRoot.parent as RectTransform;
        var buttonRect = _configButton.transform as RectTransform;
        if (popupParent == null || buttonRect == null)
        {
            BppLog.Warn(LogCategory, "Popup positioning failed because a RectTransform was missing.");
            return;
        }

        var canvasCamera = ResolveCanvasCamera(popupParent);
        var corners = new Vector3[4];
        buttonRect.GetWorldCorners(corners);
        var screenTopRight = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[2]);
        if (
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                popupParent,
                screenTopRight,
                canvasCamera,
                out var anchoredPosition
            )
        )
        {
            BppLog.Warn(LogCategory, "Popup positioning failed while converting button coordinates.");
            return;
        }

        _popupRoot.anchoredPosition = anchoredPosition + new Vector2(12f, -6f);
    }

    private Camera? ResolveCanvasCamera(RectTransform popupParent)
    {
        var canvas = popupParent.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        if (canvas.worldCamera != null)
            return canvas.worldCamera;

        if (Camera.main != null)
            return Camera.main;

        if (!_warnedMissingCanvasCamera)
        {
            _warnedMissingCanvasCamera = true;
            BppLog.Warn(
                LogCategory,
                "Popup canvas camera was unavailable; using null camera fallback."
            );
        }

        return null;
    }

    private void RefreshPopupEntries()
    {
        if (_popupEntriesRoot == null || _view == null)
            return;

        _heroEntryToggles.Clear();
        for (var index = _popupEntriesRoot.childCount - 1; index >= 0; index--)
        {
            var child = _popupEntriesRoot.GetChild(index);
            if (child == null)
                continue;

            UnityEngine.Object.Destroy(child.gameObject);
        }

        if (!TryReadHeroItemViews(_view, out var heroItemViews))
            return;

        _unlockedHeroIds = heroItemViews
            .Where(candidate => candidate != null && IsUnlocked(candidate))
            .Select(candidate => candidate.Hero.ToString())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (_unlockedHeroIds.Length == 0)
        {
            _state = null;
            CreatePassiveEntry(PopupEmptyEntryObjectName, "No unlocked heroes", _popupEntriesRoot);
            return;
        }

        var mergedPool = RandomHeroPoolPreferences.MergeWithKnownUnlockedHeroIds(
            _unlockedHeroIds,
            LoadSelectedHeroIds(),
            LoadKnownUnlockedHeroIds()
        );
        ApplyState(RandomHeroPoolStateFactory.Create(_unlockedHeroIds, mergedPool), persist: true);
        SaveKnownUnlockedHeroIds(_unlockedHeroIds);

        foreach (var heroId in _unlockedHeroIds)
            CreateHeroToggleEntry(heroId, _popupEntriesRoot);

        RebindHeroToggles();
    }

    private void OnSelectAllClicked()
    {
        if (_unlockedHeroIds.Length == 0)
            return;

        ApplyState(RandomHeroPoolStateFactory.Create(_unlockedHeroIds, _unlockedHeroIds), persist: true);
    }

    private void OnClearClicked()
    {
        if (_state == null || _unlockedHeroIds.Length == 0)
            return;

        var keepHeroId = _state.SelectedHeroIds.FirstOrDefault() ?? _unlockedHeroIds[0];
        var next = _state;
        foreach (var heroId in _unlockedHeroIds)
        {
            var shouldSelect = StringComparer.Ordinal.Equals(heroId, keepHeroId);
            next = next.SetSelected(heroId, shouldSelect);
        }

        ApplyState(next, persist: true);
    }

    private void OnHeroToggled(string heroId, bool isSelected)
    {
        if (_isRebindingToggles || _state == null)
            return;

        var next = _state.SetSelected(heroId, isSelected);
        ApplyState(next, persist: true);
    }

    private void ApplyState(RandomHeroPoolState nextState, bool persist)
    {
        _state = nextState;
        if (persist)
            SaveSelectedHeroIds(nextState.SelectedHeroIds);
        RebindHeroToggles();
    }

    private void RebindHeroToggles()
    {
        if (_state == null)
            return;

        _isRebindingToggles = true;
        try
        {
            foreach (var pair in _heroEntryToggles)
            {
                pair.Value.SetIsOnWithoutNotify(_state.IsSelected(pair.Key));
            }
        }
        finally
        {
            _isRebindingToggles = false;
        }
    }

    private void CreateHeroToggleEntry(string heroId, Transform parent)
    {
        if (_configButton == null)
        {
            BppLog.Warn(LogCategory, "Config button template was unavailable for popup entries.");
            return;
        }

        var entryObject = UnityEngine.Object.Instantiate(_configButton.gameObject, parent);
        entryObject.name = $"{PopupEntryPrefix}{heroId}";
        TrySetLabel(entryObject, heroId);

        var button = entryObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
            button.enabled = false;
        }

        var toggle = entryObject.GetComponent<Toggle>();
        if (toggle == null)
            toggle = entryObject.AddComponent<Toggle>();

        toggle.onValueChanged.RemoveAllListeners();
        toggle.enabled = true;
        toggle.interactable = true;
        toggle.onValueChanged.AddListener(isSelected => OnHeroToggled(heroId, isSelected));
        SetToggleCheckmarksVisible(entryObject.transform, visible: true);
        _heroEntryToggles[heroId] = toggle;
    }

    private static bool TryReadHeroItemViews(
        HeroSelectButtonsView view,
        out IReadOnlyList<HeroItemView> heroItemViews
    )
    {
        heroItemViews = Array.Empty<HeroItemView>();
        if (HeroItemViewsField?.GetValue(view) is not IEnumerable<HeroItemView> reflectedViews)
        {
            BppLog.Warn(
                LogCategory,
                "HeroItemViews field was unavailable; skipping random hero pool popup entries."
            );
            return false;
        }

        heroItemViews = reflectedViews.Where(candidate => candidate != null).ToArray();
        return true;
    }

    private bool IsUnlocked(HeroItemView heroItemView)
    {
        if (HeroItemIsUnlockedField?.GetValue(heroItemView) is bool unlocked)
            return unlocked;

        if (!_warnedMissingHeroUnlockStateField)
        {
            _warnedMissingHeroUnlockStateField = true;
            BppLog.Warn(
                LogCategory,
                "HeroItemView unlock-state field was unavailable; falling back to button state."
            );
        }

        return heroItemView.HeroButton != null && heroItemView.HeroButton.interactable;
    }

    private GameObject? CreatePassiveEntry(string objectName, string label, Transform parent)
    {
        if (_configButton == null)
        {
            BppLog.Warn(LogCategory, "Config button template was unavailable for popup entries.");
            return null;
        }

        var entryObject = UnityEngine.Object.Instantiate(_configButton.gameObject, parent);
        entryObject.name = objectName;

        var button = entryObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
            button.enabled = false;
        }

        var toggle = entryObject.GetComponent<Toggle>();
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.enabled = false;
        }

        SetToggleCheckmarksVisible(entryObject.transform, visible: false);
        TrySetLabel(entryObject, label);
        return entryObject;
    }

    private IReadOnlyCollection<string>? LoadSelectedHeroIds()
    {
        return LoadHeroIdCollection(BuildScopedPrefsKey(SelectedPoolPrefsKeyPrefix));
    }

    private void SaveSelectedHeroIds(IEnumerable<string> heroIds)
    {
        SaveHeroIdCollection(BuildScopedPrefsKey(SelectedPoolPrefsKeyPrefix), heroIds);
    }

    private IReadOnlyCollection<string>? LoadKnownUnlockedHeroIds()
    {
        return LoadHeroIdCollection(BuildScopedPrefsKey(KnownUnlockedPrefsKeyPrefix));
    }

    private void SaveKnownUnlockedHeroIds(IEnumerable<string> heroIds)
    {
        SaveHeroIdCollection(BuildScopedPrefsKey(KnownUnlockedPrefsKeyPrefix), heroIds);
    }

    private static IReadOnlyCollection<string>? LoadHeroIdCollection(string key)
    {
        if (!PlayerPrefs.HasKey(key))
            return null;

        var raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<string[]>(raw);
        }
        catch (Exception ex)
        {
            BppLog.Warn(LogCategory, $"Failed to parse saved random hero pool '{key}': {ex.Message}");
            return null;
        }
    }

    private static void SaveHeroIdCollection(string key, IEnumerable<string> heroIds)
    {
        var normalized = heroIds
            .Where(heroId => !string.IsNullOrWhiteSpace(heroId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            PlayerPrefs.DeleteKey(key);
        }
        else
        {
            PlayerPrefs.SetString(key, JsonConvert.SerializeObject(normalized));
        }

        PlayerPrefs.Save();
    }

    private static string BuildScopedPrefsKey(string keyPrefix)
    {
        return $"{keyPrefix}.{ResolveAccountScopeForPrefs()}";
    }

    private static string ResolveAccountScopeForPrefs()
    {
        try
        {
            var dataType = AccessTools.TypeByName("Data");
            if (dataType == null)
                return AnonymousAccountScope;

            var profileProperty = AccessTools.Property(dataType, "Profile");
            var profile = profileProperty?.GetValue(null);
            if (profile == null)
                return AnonymousAccountScope;

            var accountIdProperty = AccessTools.Property(profile.GetType(), "AccountId");
            var accountId = accountIdProperty?.GetValue(profile)?.ToString();
            if (!string.IsNullOrWhiteSpace(accountId))
                return Uri.EscapeDataString(accountId);

            var usernameProperty = AccessTools.Property(profile.GetType(), "Username");
            var username = usernameProperty?.GetValue(profile)?.ToString();
            if (!string.IsNullOrWhiteSpace(username))
                return Uri.EscapeDataString(username);
        }
        catch
        {
        }

        return AnonymousAccountScope;
    }

    private static void SetToggleCheckmarksVisible(Transform root, bool visible)
    {
        foreach (var candidate in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (candidate == null || candidate == root)
                continue;

            if (candidate.name.IndexOf("checkmark", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            candidate.gameObject.SetActive(visible);
        }
    }

    private static bool TrySetLabel(GameObject root, string labelText)
    {
        var label = root
            .GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(candidate =>
                candidate != null && !string.IsNullOrWhiteSpace(candidate.text)
            );
        if (label == null)
            return false;

        label.text = labelText;
        return true;
    }

    private Color ResolvePopupBackgroundColor()
    {
        if (_randomHeroToggle == null)
            return new Color(0.12f, 0.12f, 0.12f, 0.92f);

        var selectable = _randomHeroToggle.GetComponent<Selectable>();
        if (selectable?.targetGraphic == null)
            return new Color(0.12f, 0.12f, 0.12f, 0.92f);

        var color = selectable.targetGraphic.color;
        color.a = Mathf.Clamp(color.a, 0.85f, 0.98f);
        return color;
    }
}
