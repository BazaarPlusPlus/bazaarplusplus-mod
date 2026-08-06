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
/// it would become — under the socket. Placed notes render as a chip tinted with the letter's
/// category accent color; implied letters render as dim neutral chips. Each letter's
/// item-category identity (Burn, Heal, Weapon, ...) comes from the note templates' condition
/// graphs and reuses the game's own keyword icon and accent color; unresolvable tags degrade
/// to the letter alone. Purely visual: the canvas has no raycaster and every graphic is
/// raycast-transparent.
/// </summary>
internal sealed class MusicNoteSocketOverlay : MonoBehaviour
{
    private const int CanvasSortingOrder = 10;

    // Canvas-space (1080p reference) distance from the socket center down to the badge top.
    private const float BadgeVerticalOffset = 52f;

    // One size for both states: active/implied differ only in weight and color, so the
    // badge row reads as a single aligned strip.
    private const float LetterFontSize = 20f;

    // TMP sprite tint for implied badges: multiplied toward grey so inactive icons read as
    // ghosted, while active sprites render untinted full-color.
    private const string ImpliedIconTint = "#7A7C82A8";

    private static readonly Color ImpliedBackColor = new(0.05f, 0.06f, 0.08f, 0.52f);
    private static readonly Color ActiveLetterColor = Color.white;
    private static readonly Color ImpliedLetterColor = new(0.64f, 0.67f, 0.72f, 0.88f);
    private static readonly Color ActiveAccentFallbackColor = new(0.82f, 0.66f, 0.30f, 1f);

    // Bronze-trim hairline along the chip edge, echoing the board's metal plate borders.
    // Implied chips keep it near-invisible so only placed notes read as framed plates.
    private static readonly Color ActiveEdgeColor = new(0.88f, 0.71f, 0.38f, 0.62f);
    private static readonly Color ImpliedEdgeColor = new(0.55f, 0.52f, 0.46f, 0.16f);

    private static readonly string[] LetterNames = BuildLetterNames();

    private static Sprite? _roundedSprite;
    private static Sprite? _roundedEdgeSprite;

    private sealed class BadgeSlot
    {
        internal BadgeSlot(RectTransform root, Image back, Image edge, TextMeshProUGUI letter)
        {
            Root = root;
            Back = back;
            Edge = edge;
            Letter = letter;
        }

        internal RectTransform Root { get; }
        internal Image Back { get; }
        internal Image Edge { get; }
        internal TextMeshProUGUI Letter { get; }
        internal string? RenderedText { get; set; }
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
        string? iconName = null;
        TMPro.TMP_SpriteAsset? iconAsset = null;
        if (tags != null && tags.Count > 0)
        {
            var display = tags[0].HiddenTag is EHiddenTag hiddenTag
                ? NativeTagTypography.Resolve(hiddenTag)
                : NativeTagTypography.Resolve(tags[0].CardTag!.Value);
            accentColor = display.AccentColor;

            iconName = display.IconName;
            // The Weapon keyword config has a color but no icon (the game's own note
            // tooltips render it icon-less); borrow the damage attribute icon instead.
            if (string.IsNullOrEmpty(iconName) && tags[0].CardTag == ECardTag.Weapon)
                iconName = NativeTagTypography.Resolve(ECardAttributeType.DamageAmount).IconName;

            if (!string.IsNullOrEmpty(iconName))
                iconAsset = KeywordIconSpriteProvider.ResolveAsset(iconName);
        }

        // The icon renders inline through TMP sprite markup so it keeps the sprite asset's
        // glyph metrics — a standalone Image loses the baseline and each icon sits at its own
        // vertical offset. The asset is assigned explicitly so resolution never depends on the
        // global TMP fallback chain.
        var letterName = LetterNames[(int)badge.Letter % LetterNames.Length];
        var text =
            iconAsset == null ? letterName
            : isActive ? $"<sprite name=\"{iconName}\"> {letterName}"
            : $"<sprite name=\"{iconName}\" color={ImpliedIconTint}> {letterName}";
        if (slot.Letter.spriteAsset != iconAsset)
            slot.Letter.spriteAsset = iconAsset;
        if (!string.Equals(slot.RenderedText, text, StringComparison.Ordinal))
        {
            slot.Letter.text = text;
            slot.RenderedText = text;
        }

        slot.Letter.color = isActive ? ActiveLetterColor : ImpliedLetterColor;
        slot.Edge.color = isActive ? ActiveEdgeColor : ImpliedEdgeColor;
        if (isActive)
        {
            // Chip tinted toward the category color, matching how the game colors keywords.
            var accent = accentColor ?? ActiveAccentFallbackColor;
            var back = Color.Lerp(accent, Color.black, 0.58f);
            back.a = 0.94f;
            slot.Back.color = back;
        }
        else
        {
            slot.Back.color = ImpliedBackColor;
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
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter)
        );
        rootObject.transform.SetParent(_canvasObject.transform, false);

        var root = (RectTransform)rootObject.transform;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 1f);

        // The background lives on an ignoreLayout child stretched to the root: an Image on
        // the layout root itself would feed its sprite's native size into the size fitter
        // (max over co-located layout elements) and a large atlas sprite would blow the
        // badge up to that size.
        var backObject = new GameObject(
            "Back",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement)
        );
        backObject.transform.SetParent(rootObject.transform, false);
        var backRect = (RectTransform)backObject.transform;
        backRect.anchorMin = Vector2.zero;
        backRect.anchorMax = Vector2.one;
        backRect.offsetMin = Vector2.zero;
        backRect.offsetMax = Vector2.zero;
        backObject.GetComponent<LayoutElement>().ignoreLayout = true;
        var back = backObject.GetComponent<Image>();
        back.sprite = GetRoundedSprite();
        back.type = Image.Type.Sliced;
        back.raycastTarget = false;

        var edgeObject = new GameObject(
            "Edge",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement)
        );
        edgeObject.transform.SetParent(rootObject.transform, false);
        var edgeRect = (RectTransform)edgeObject.transform;
        edgeRect.anchorMin = Vector2.zero;
        edgeRect.anchorMax = Vector2.one;
        edgeRect.offsetMin = Vector2.zero;
        edgeRect.offsetMax = Vector2.zero;
        edgeObject.GetComponent<LayoutElement>().ignoreLayout = true;
        var edge = edgeObject.GetComponent<Image>();
        edge.sprite = GetRoundedEdgeSprite();
        edge.type = Image.Type.Sliced;
        edge.raycastTarget = false;

        var layout = rootObject.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 9, 3, 3);
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = rootObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var letterObject = new GameObject("Letter", typeof(RectTransform));
        letterObject.transform.SetParent(rootObject.transform, false);
        var letter = letterObject.AddComponent<TextMeshProUGUI>();
        _typography.Apply(letter);
        letter.fontSize = LetterFontSize;
        letter.alignment = TextAlignmentOptions.Center;
        letter.raycastTarget = false;

        return new BadgeSlot(root, back, edge, letter);
    }

    private void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;
        _visible = visible;
        if (_canvasObject != null && _canvasObject.activeSelf != visible)
            _canvasObject.SetActive(visible);
    }

    // Same soft rounded-rect shape the combat status bar draws for its chips; duplicated
    // per ADR-0009 rather than extracting the status bar's private helper for looks alone.
    private static Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null)
            return _roundedSprite;

        const int size = 32;
        const float radius = 11f;
        const float edgeSoftness = 1.5f;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        for (var y = 0; y < size; y++)
        {
            // Subtle top-lit vertical gradient baked into the white fill (texture row 0 is
            // the bottom); Image.color multiplies on top, so the shade survives tinting.
            var shade = Mathf.Lerp(0.78f, 1f, (y + 0.5f) / size);
            for (var x = 0; x < size; x++)
            {
                var alpha = Mathf.Clamp01(
                    0.5f - RoundedSignedDistance(x, y, size, radius) / edgeSoftness
                );
                texture.SetPixel(x, y, new Color(shade, shade, shade, alpha));
            }
        }

        texture.Apply();
        _roundedSprite = CreateSlicedSprite(texture, size, radius);
        return _roundedSprite;
    }

    // Hairline ring along the chip boundary, tinted per state as a metal-trim edge.
    private static Sprite GetRoundedEdgeSprite()
    {
        if (_roundedEdgeSprite != null)
            return _roundedEdgeSprite;

        const int size = 32;
        const float radius = 11f;
        const float ringInset = 1.1f;
        const float ringWidth = 1.2f;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = RoundedSignedDistance(x, y, size, radius);
                var alpha = Mathf.Clamp01(1f - Mathf.Abs(distance + ringInset) / ringWidth);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        _roundedEdgeSprite = CreateSlicedSprite(texture, size, radius);
        return _roundedEdgeSprite;
    }

    private static float RoundedSignedDistance(int x, int y, int size, float radius)
    {
        var halfSize = size * 0.5f;
        var innerHalfExtent = halfSize - radius;
        var distanceX = Mathf.Abs(x + 0.5f - halfSize) - innerHalfExtent;
        var distanceY = Mathf.Abs(y + 0.5f - halfSize) - innerHalfExtent;
        var outsideX = Mathf.Max(distanceX, 0f);
        var outsideY = Mathf.Max(distanceY, 0f);
        return Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
            + Mathf.Min(Mathf.Max(distanceX, distanceY), 0f)
            - radius;
    }

    private static Sprite CreateSlicedSprite(Texture2D texture, int size, float radius)
    {
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius)
        );
    }

    private static string[] BuildLetterNames()
    {
        var names = new string[MusicNoteLetterMath.LetterCount];
        for (var i = 0; i < names.Length; i++)
            names[i] = ((EMusicNote)i).ToString();
        return names;
    }
}
