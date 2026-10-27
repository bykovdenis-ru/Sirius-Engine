"""Generates the Sirius application icon.

Run:  python tools/make_icon.py
Outputs: assets/sirius.ico, assets/icon.png
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
ASSETS.mkdir(parents=True, exist_ok=True)

SIZE = 1024


def lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def vertical_gradient(size: int, top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    image = Image.new("RGB", (size, size))
    draw = ImageDraw.Draw(image)
    for y in range(size):
        t = y / (size - 1)
        draw.line([(0, y), (size, y)], fill=lerp(top, bottom, t))
    return image


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def ring_layer(size: int, center: tuple[int, int], radius: int, width: int) -> Image.Image:
    """Draws an anti-aliased gradient ring."""
    scale = 4
    big = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    cx, cy = center[0] * scale, center[1] * scale
    r = radius * scale
    w = width * scale

    gradient = [
        (0.00, (94, 234, 212)),
        (0.35, (56, 189, 248)),
        (0.65, (129, 140, 248)),
        (0.85, (192, 132, 252)),
        (1.00, (94, 234, 212)),
    ]

    def color_at(t: float) -> tuple[int, int, int]:
        t %= 1.0
        for i in range(len(gradient) - 1):
            t0, c0 = gradient[i]
            t1, c1 = gradient[i + 1]
            if t0 <= t <= t1:
                local = (t - t0) / (t1 - t0) if t1 > t0 else 0
                return lerp(c0, c1, local)
        return gradient[-1][1]

    steps = 720
    for i in range(steps):
        start = -90 + i * (360 / steps)
        end = -90 + (i + 1) * (360 / steps) + 0.4
        color = color_at(i / steps)
        draw.arc(
            [cx - r, cy - r, cx + r, cy + r],
            start=start,
            end=end,
            fill=color + (255,),
            width=w,
        )

    return big.resize((size, size), Image.LANCZOS)


def load_font(size: int) -> ImageFont.FreeTypeFont:
    candidates = [
        Path("C:/Windows/Fonts/segoeuib.ttf"),
        Path("C:/Windows/Fonts/segoeui.ttf"),
        Path("C:/Windows/Fonts/arialbd.ttf"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default()


def main() -> None:
    canvas = vertical_gradient(SIZE, (36, 40, 84), (11, 14, 32)).convert("RGBA")
    mask = rounded_mask(SIZE, 232)

    # Subtle inner glow in the top-left corner.
    glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((-260, -320, 640, 460), fill=(109, 94, 252, 90))
    glow = glow.filter(ImageFilter.GaussianBlur(130))
    canvas = Image.alpha_composite(canvas, glow)

    # Orbit ring.
    ring = ring_layer(SIZE, (SIZE // 2, SIZE // 2), 352, 46)
    ring_glow = ring.filter(ImageFilter.GaussianBlur(28))
    canvas = Image.alpha_composite(canvas, ring_glow)
    canvas = Image.alpha_composite(canvas, ring)

    # Letter "S" with a soft shadow.
    font = load_font(500)
    text_layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    text_draw = ImageDraw.Draw(text_layer)
    bbox = text_draw.textbbox((0, 0), "S", font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    pos = ((SIZE - tw) / 2 - bbox[0], (SIZE - th) / 2 - bbox[1] - 14)
    text_draw.text(pos, "S", font=font, fill=(255, 255, 255, 255))

    shadow = text_layer.filter(ImageFilter.GaussianBlur(30))
    shadow = Image.new("RGBA", (SIZE, SIZE), (90, 120, 255, 110))
    shadow.putalpha(text_layer.getchannel("A").filter(ImageFilter.GaussianBlur(26)))
    canvas = Image.alpha_composite(canvas, shadow)
    canvas = Image.alpha_composite(canvas, text_layer)

    # Orbiting spark.
    spark = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    sx, sy = int(SIZE / 2 + 352 * math.cos(math.radians(-38))), int(SIZE / 2 + 352 * math.sin(math.radians(-38)))
    ImageDraw.Draw(spark).ellipse((sx - 30, sy - 30, sx + 30, sy + 30), fill=(103, 232, 249, 255))
    spark_glow = spark.filter(ImageFilter.GaussianBlur(22))
    canvas = Image.alpha_composite(canvas, spark_glow)
    canvas = Image.alpha_composite(canvas, spark)

    # Apply rounded corners and a thin light border.
    rounded = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    rounded.paste(canvas, (0, 0), mask)
    border = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(border).rounded_rectangle(
        (3, 3, SIZE - 4, SIZE - 4), radius=230, outline=(255, 255, 255, 46), width=5
    )
    rounded = Image.alpha_composite(rounded, border)

    rounded.save(ASSETS / "icon.png", "PNG")

    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [rounded.resize((s, s), Image.LANCZOS) for s in sizes]
    frames[-1].save(ASSETS / "sirius.ico", format="ICO", sizes=[(s, s) for s in sizes])

    print(f"icon.png  -> {ASSETS / 'icon.png'}")
    print(f"sirius.ico -> {ASSETS / 'sirius.ico'} ({', '.join(map(str, sizes))})")


if __name__ == "__main__":
    main()
