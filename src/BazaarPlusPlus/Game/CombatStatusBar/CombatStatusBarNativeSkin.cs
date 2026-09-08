#nullable enable
using BazaarPlusPlus.GameInterop.AssetLoading;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarNativeSkin
{
    private const string ButtonRoot = "Assets/TheBazaar/Art/UI/Buttons/Btn_Rectangle/";
    private static readonly string[] Addresses =
    [
        ButtonRoot + "Btn_Rct_Frame_S_TUI.png",
        ButtonRoot + "Btn_Rct_Brown_S_Center_Active_TUI.png",
    ];

    private readonly Sprite[] _sprites;

    private CombatStatusBarNativeSkin(Sprite[] sprites) => _sprites = sprites;

    private Sprite Frame => _sprites[0];
    private Sprite Backing => _sprites[1];
    internal bool IsValid => _sprites.All(sprite => sprite != null);

    internal static bool TryBeginLoad(out Task<CombatStatusBarNativeSkin?>? pending)
    {
        pending = null;
        if (!Services.TryGet<AssetLoader>(out var loader) || loader == null)
            return false;

        pending = LoadAsync(loader);
        return true;
    }

    private static async Task<CombatStatusBarNativeSkin?> LoadAsync(AssetLoader loader)
    {
        // These are the game's Addressable sprites, including their atlas and slice borders.
        // The shared loader pins them in Global scope across lobby/run scene transitions.
        if (Addresses.Any(address => !loader.DoesAddressExist(address)))
            return null;

        var sprites = await Task.WhenAll(
            Addresses.Select(address =>
                NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address)
            )
        );
        if (sprites.Any(sprite => sprite == null))
            return null;

        return new CombatStatusBarNativeSkin(sprites.Select(sprite => sprite!).ToArray());
    }

    internal void ApplyPlaque(RectTransform rect)
    {
        var scale = rect.rect.height / Frame.rect.height;
        var backingSize = rect.rect.size - (Frame.rect.size - Backing.rect.size) * scale;
        AddLayer("Backing", rect, Backing, backingSize);
        AddLayer("Frame", rect, Frame, rect.rect.size);
    }

    private static void AddLayer(string name, RectTransform parent, Sprite sprite, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.type = Image.Type.Sliced;
        // Preserve the original bevel proportions; stretch only the nine-slice center.
        image.pixelsPerUnitMultiplier = sprite.rect.height / size.y * 100f / sprite.pixelsPerUnit;
        image.raycastTarget = false;
    }
}
