#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.TagTypography;
using TheBazaar;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.MusicNotes;

/// <summary>
/// Hold-to-peek badge overlay for the music-note board mechanic: while the shared preview
/// modifier (HoldUpgradePreview, default Shift) is held outside combat/recap/replay, every
/// unlocked player socket shows the note letter it holds — or, via the anchor rule, the letter
/// it would become — under the socket. Placed notes render emphasized (bold, accent outline);
/// implied letters render dimmed. Each letter's item-category tags (Burn, Heal, Weapon, ...)
/// come from the note templates' condition graphs and reuse the game's own keyword icons and
/// accent colors; unresolvable tags degrade to the letter alone. Purely visual: the canvas has
/// no raycaster and every graphic is raycast-transparent.
/// </summary>
internal sealed class MusicNoteSocketOverlay : MonoBehaviour
{
    private const int CanvasSortingOrder = 10;

    // Canvas-space (1080p reference) distance from the socket center down to the badge top.
    private const float BadgeVerticalOffset = 52f;
    private const float IconSize = 24f;
    private const int MaxIconsPerBadge = 2;
    private const float ActiveLetterFontSize = 24f;
    private const float ImpliedLetterFontSize = 19f;

    private static readonly Color ActiveBackColor = new(0.05f, 0.06f, 0.08f, 0.88f);
    private static readonly Color ImpliedBackColor = new(0.05f, 0.06f, 0.08f, 0.30f);
    private static readonly Color ActiveLetterFallbackColor = new(1f, 0.85f, 0.45f, 1f);
    private static readonly Color ImpliedLetterColor = new(0.78f, 0.81f, 0.85f, 0.66f);
    private static readonly Color ActiveIconColor = Color.white;
    private static readonly Color ImpliedIconColor = new(1f, 1f, 1f, 0.42f);
    private static readonly Color ActiveOutlineFallbackColor = new(1f, 0.85f, 0.45f, 0.85f);

    private static readonly string[] LetterNames = BuildLetterNames();

    private sealed class BadgeSlot
    {
        internal BadgeSlot(
            RectTransform root,
            Image back,
            Outline outline,
            Image[] icons,
            TextMeshProUGUI letter
        )
        {
            Root = root;
            Back = back;
            BackOutline = outline;
            Icons = icons;
            Letter = letter;
        }

        internal RectTransform Root { get; }
        internal Image Back { get; }
        internal Outline BackOutline { get; }
        internal Image[] Icons { get; }
        internal TextMeshProUGUI Letter { get; }
        internal string? RenderedLetter { get; set; }
        internal bool? RenderedActive { get; set; }
    }

    private GameObject? _canvasObject;
    private RectTransform? _canvasRect;
    private NativeGameTypography.OwnedTextPreparation? _typography;
    private readonly List<BadgeSlot> _slots = new();
    private bool _visible;

    private void Update()
    {
        if (!ShouldShow())
        {
            SetVisible(false);
            return;
        }

        var badges = MusicNoteBoardReader.Read();
        var board = Singleton<BoardManager>.Instance;
        var mainCamera = Camera.main;
        if (badges == null || badges.Count == 0 || board == null || mainCamera == null)
        {
            SetVisible(false);
            return;
        }

        if (!EnsureUi())
        {
            SetVisible(false);
            return;
        }

        if (!_visible)
            KeywordIconSpriteProvider.BeginResolvePass();
        SetVisible(true);
        Render(badges, board, mainCamera);
    }

    private void OnDisable()
    {
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (_canvasObject != null)
        {
            Destroy(_canvasObject);
            _canvasObject = null;
        }
        _canvasRect = null;
        _slots.Clear();
    }

    private static bool ShouldShow()
    {
        if (!BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
            return false;

        try
        {
            // Same gates the enchant-preview tooltip uses, plus saved-replay playback: the
            // overlay is a board-management aid only.
            if (Data.IsInCombat)
                return false;
            if (AppState.CurrentState is ReplayState)
                return false;
            if (Singleton<BoardManager>.Instance?.IsRecapViewOpen == true)
                return false;
            return Data.Run?.Player != null;
        }
        catch
        {
            // Touches game statics per frame; never log here. Hide if unsure.
            return false;
        }
    }

    private bool EnsureUi()
    {
        if (_canvasObject != null)
            return true;

        if (
            NativeGameTypography.PrepareOwnedText(out _typography)
                != NativeGameTypography.Outcome.Ready
            || _typography == null
        )
            return false;

        _canvasObject = new GameObject(
            "BppMusicNoteOverlayCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler)
        );
        _canvasObject.transform.SetParent(transform, false);

        var canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

        var scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.55f;

        _canvasRect = (RectTransform)_canvasObject.transform;
        return true;
    }

    private void Render(List<MusicNoteSocketBadge> badges, BoardManager board, Camera mainCamera)
    {
        var sockets = board.playerItemSockets;
        var used = 0;
        foreach (var badge in badges)
        {
            var socket = FindSocket(sockets, badge.SocketIndex);
            if (socket == null)
                continue;

            var screen = mainCamera.WorldToScreenPoint(socket.transform.position);
            if (screen.z <= 0f)
                continue;

            var slot = GetSlot(used);
            if (slot == null)
                break;
            used++;

            PositionSlot(slot, screen);
            StyleSlot(slot, badge);
        }

        for (var i = used; i < _slots.Count; i++)
        {
            if (_slots[i].Root.gameObject.activeSelf)
                _slots[i].Root.gameObject.SetActive(false);
        }
    }

    private static ItemSocketController? FindSocket(ItemSocketController[]? sockets, int index)
    {
        if (sockets == null)
            return null;
        foreach (var socket in sockets)
        {
            if (socket != null && (int)socket.SocketNumber == index)
                return socket;
        }
        return null;
    }

    private void PositionSlot(BadgeSlot slot, Vector3 screen)
    {
        if (_canvasRect == null)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            new Vector2(screen.x, screen.y),
            null,
            out var local
        );
        slot.Root.anchoredPosition = new Vector2(local.x, local.y - BadgeVerticalOffset);
        if (!slot.Root.gameObject.activeSelf)
            slot.Root.gameObject.SetActive(true);
    }

    private static void StyleSlot(BadgeSlot slot, in MusicNoteSocketBadge badge)
    {
        var isActive = badge.Kind == MusicNoteSocketBadgeKind.Active;

        // The letter identity (which tags a letter keys on) is per-letter game data, so the
        // catalog answers for placed and implied badges alike; the placed template is only a
        // fallback while the catalog is still loading.
        var tags = MusicNoteTemplateCatalog.TryGetTagsForLetter(badge.Letter);
        if (tags == null && isActive)
            tags = MusicNoteEffectClassifier.GetTags(badge.PlacedTemplate);

        Color? accentColor = null;
        var iconCount = 0;
        if (tags != null)
        {
            for (var i = 0; i < tags.Count && iconCount < MaxIconsPerBadge; i++)
            {
                var display = tags[i].HiddenTag is EHiddenTag hiddenTag
                    ? NativeTagTypography.Resolve(hiddenTag)
                    : NativeTagTypography.Resolve(tags[i].CardTag!.Value);
                accentColor ??= display.AccentColor;

                if (string.IsNullOrEmpty(display.IconName))
                    continue;
                var sprite = KeywordIconSpriteProvider.Resolve(display.IconName).Sprite;
                if (sprite == null)
                    continue;

                var icon = slot.Icons[iconCount];
                if (icon.sprite != sprite)
                    icon.sprite = sprite;
                icon.color = isActive ? ActiveIconColor : ImpliedIconColor;
                if (!icon.gameObject.activeSelf)
                    icon.gameObject.SetActive(true);
                iconCount++;
            }
        }

        for (var i = iconCount; i < slot.Icons.Length; i++)
        {
            if (slot.Icons[i].gameObject.activeSelf)
                slot.Icons[i].gameObject.SetActive(false);
        }

        var letterText = LetterNames[(int)badge.Letter % LetterNames.Length];
        if (!string.Equals(slot.RenderedLetter, letterText, StringComparison.Ordinal))
        {
            slot.Letter.text = letterText;
            slot.RenderedLetter = letterText;
        }

        if (slot.RenderedActive != isActive)
        {
            slot.Letter.fontSize = isActive ? ActiveLetterFontSize : ImpliedLetterFontSize;
            slot.Letter.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
            slot.RenderedActive = isActive;
        }

        slot.Letter.color = isActive
            ? (accentColor ?? ActiveLetterFallbackColor)
            : ImpliedLetterColor;
        slot.Back.color = isActive ? ActiveBackColor : ImpliedBackColor;
        slot.BackOutline.enabled = isActive;
        if (isActive)
        {
            var outlineColor = accentColor ?? ActiveOutlineFallbackColor;
            outlineColor.a = 0.85f;
            slot.BackOutline.effectColor = outlineColor;
        }
    }

    private BadgeSlot? GetSlot(int index)
    {
        while (_slots.Count <= index)
        {
            var slot = CreateSlot(_slots.Count);
            if (slot == null)
                return null;
            _slots.Add(slot);
        }
        return _slots[index];
    }

    private BadgeSlot? CreateSlot(int index)
    {
        if (_canvasObject == null || _typography == null)
            return null;

        var rootObject = new GameObject(
            $"NoteBadge{index}",
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter)
        );
        rootObject.transform.SetParent(_canvasObject.transform, false);

        var root = (RectTransform)rootObject.transform;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 1f);

        var back = rootObject.GetComponent<Image>();
        back.raycastTarget = false;

        var outline = rootObject.GetComponent<Outline>();
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = false;
        outline.enabled = false;

        var layout = rootObject.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 9, 3, 3);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = rootObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var icons = new Image[MaxIconsPerBadge];
        for (var i = 0; i < icons.Length; i++)
        {
            var iconObject = new GameObject(
                $"Icon{i}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement)
            );
            iconObject.transform.SetParent(rootObject.transform, false);
            var icon = iconObject.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            var iconLayout = iconObject.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = IconSize;
            iconLayout.preferredHeight = IconSize;
            iconObject.SetActive(false);
            icons[i] = icon;
        }

        var letterObject = new GameObject("Letter", typeof(RectTransform));
        letterObject.transform.SetParent(rootObject.transform, false);
        var letter = letterObject.AddComponent<TextMeshProUGUI>();
        _typography.Apply(letter);
        letter.fontSize = ImpliedLetterFontSize;
        letter.alignment = TextAlignmentOptions.Center;
        letter.raycastTarget = false;

        return new BadgeSlot(root, back, outline, icons, letter);
    }

    private void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;
        _visible = visible;
        if (_canvasObject != null && _canvasObject.activeSelf != visible)
            _canvasObject.SetActive(visible);
    }

    private static string[] BuildLetterNames()
    {
        var names = new string[MusicNoteLetterMath.LetterCount];
        for (var i = 0; i < names.Length; i++)
            names[i] = ((EMusicNote)i).ToString();
        return names;
    }
}
