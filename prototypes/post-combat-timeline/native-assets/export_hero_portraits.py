"""PROTOTYPE: export the combatants' native collection portraits."""

from hashlib import sha256
from pathlib import Path

import UnityPy
from PIL import Image


GAME_ROOT = Path.home() / "Library/Application Support/Steam/steamapps/common/The Bazaar"
BUNDLE_ROOT = GAME_ROOT / "TheBazaar.app/Contents/Resources/Data/StreamingAssets/aa/StandaloneOSX"
OUTPUT = Path(__file__).resolve().parents[1] / "assets/native/heroes"
PORTRAITS = {
    "jul": ("skin_jul_01_assets_all.bundle", "Skin_JUL_01a_PreviewCollection_TUI"),
    "mak": ("skin_mak_01_assets_all.bundle", "Skin_MAK_01a_PreviewCollection_TUI"),
}


def fit_portrait(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    image.thumbnail((128, 128), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    canvas.alpha_composite(image, ((128 - image.width) // 2, (128 - image.height) // 2))
    return canvas


def load_named_sprite(bundle: Path, sprite_name: str) -> Image.Image:
    environment = UnityPy.load(str(bundle))
    matches = [
        obj
        for obj in environment.objects
        if obj.type.name == "Sprite" and obj.peek_name() == sprite_name
    ]
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected exactly one Sprite named {sprite_name!r} in {bundle.name}; found {len(matches)}"
        )
    return matches[0].read().image


def main() -> None:
    UnityPy.config.FALLBACK_UNITY_VERSION = "6000.3.11f1"
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for hero, (bundle_name, sprite_name) in PORTRAITS.items():
        portrait = fit_portrait(load_named_sprite(BUNDLE_ROOT / bundle_name, sprite_name))
        output = OUTPUT / f"{hero}.thumb.png"
        portrait.save(output, format="PNG", optimize=True)
        digest = sha256(output.read_bytes()).hexdigest()
        print(f"{hero}={sprite_name} sha256={digest}")
    print(OUTPUT)


if __name__ == "__main__":
    main()
