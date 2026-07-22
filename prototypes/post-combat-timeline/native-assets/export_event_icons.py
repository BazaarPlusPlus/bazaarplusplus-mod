"""PROTOTYPE: export native combat glyphs from the local game bundle."""

from pathlib import Path

import UnityPy
from PIL import Image


GAME_ROOT = Path.home() / "Library/Application Support/Steam/steamapps/common/The Bazaar"
BUNDLE = GAME_ROOT / "TheBazaar.app/Contents/Resources/Data/StreamingAssets/aa/StandaloneOSX/fonts_assets_all.bundle"
OUTPUT = Path(__file__).resolve().parents[1] / "assets/native/events"
ICON_NAMES = ("Damage", "Health", "Regen", "Shield", "Burn", "Poison", "Charge", "Destroy")


def fit_native_icon(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    image.thumbnail((64, 72), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (64, 72), (0, 0, 0, 0))
    canvas.alpha_composite(image, ((64 - image.width) // 2, (72 - image.height) // 2))
    return canvas


def main() -> None:
    UnityPy.config.FALLBACK_UNITY_VERSION = "6000.3.11f1"
    environment = UnityPy.load(str(BUNDLE))
    sprites = {obj.peek_name(): obj for obj in environment.objects if obj.type.name == "Sprite"}
    missing = [name for name in ICON_NAMES if name not in sprites]
    if missing:
        raise RuntimeError(f"Missing native combat sprites: {', '.join(missing)}")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name in ICON_NAMES:
        image = sprites[name].read().image
        fit_native_icon(image).save(OUTPUT / f"{name.lower()}.png", format="PNG", optimize=True)
    print(f"event_icons={len(ICON_NAMES)}/{len(ICON_NAMES)}")
    print(OUTPUT)


if __name__ == "__main__":
    main()
