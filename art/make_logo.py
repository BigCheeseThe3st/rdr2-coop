#!/usr/bin/env python3
"""Draws the RDR2 Co-op logo: art/logo-1920x1080.png (Nexus main image) and
art/logo-1024.png (square). Fonts: Rye and Oswald (SIL Open Font Licence, in
art/fonts). Run: python3 art/make_logo.py"""
import math
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
RYE = str(HERE / "fonts/Rye-Regular.ttf")
OSWALD = str(HERE / "fonts/Oswald.ttf")

BONE = (238, 228, 208)
RED = (178, 28, 22)
DEEP = (16, 8, 6)


def oswald(size, weight=600):
    f = ImageFont.truetype(OSWALD, size)
    try:
        f.set_variation_by_axes([weight])
    except Exception:
        pass
    return f


def sky(w, h):
    """Dusk: deep red at the horizon fading to near black, a low sun, two ridges."""
    img = Image.new("RGB", (w, h))
    px = img.load()
    horizon = int(h * 0.70)
    for y in range(h):
        t = min(1.0, y / horizon)
        c = [int(a + (b - a) * t ** 1.6) for a, b in zip((14, 6, 6), (150, 46, 22))]
        for x in range(w):
            px[x, y] = tuple(c)
    d = ImageDraw.Draw(img, "RGBA")
    sx, sy, r = int(w * 0.5), int(horizon - h * 0.02), int(h * 0.24)
    for i in range(40, 0, -1):   # glow
        rr = r + i * h * 0.012
        d.ellipse([sx - rr, sy - rr, sx + rr, sy + rr], fill=(230, 120, 50, 5))
    d.ellipse([sx - r, sy - r, sx + r, sy + r], fill=(236, 170, 92))
    for i, (col, base, amp, seed) in enumerate([((70, 18, 12), 0.66, 0.07, 3), (DEEP, 0.76, 0.05, 11)]):
        rnd = random.Random(seed)
        pts, y0 = [], h * base
        phases = [rnd.uniform(0, 6.28) for _ in range(4)]
        for x in range(0, w + 8, 8):
            v = sum(math.sin(x / w * (k + 1.3) * 3.1 + phases[k]) / (k + 1) for k in range(4))
            if i == 0:   # a mesa on the left: flat top, sloped sides
                dx = abs(x - w * 0.22) / (w * 0.10)
                v = max(v, 1.7 * min(1.0, (1.25 - dx) / 0.35))   # negative far away: no effect
            pts.append((x, y0 - v * h * amp))
        d.polygon(pts + [(w, h), (0, h)], fill=col)
    return img


def hat(d, cx, cy, s, fill, tilt=0.0):
    """A cowboy hat: brim curling up at the ends, pinched crown, red band."""
    def rot(px, py):
        x, y = px - cx, py - cy
        c, n = math.cos(tilt), math.sin(tilt)
        return (cx + x * c - y * n, cy + x * n + y * c)
    top, bottom = [], []
    for i in range(41):
        u = -1 + i / 20                       # -1..1 across the brim
        curl = 0.16 * s * abs(u) ** 3.2       # the ends turn up
        top.append(rot(cx + u * s, cy - 0.05 * s - curl))
        bottom.append(rot(cx + u * 0.96 * s, cy + 0.11 * s * (1 - u * u) - curl * 0.8))
    d.polygon(top + bottom[::-1], fill=fill)
    crown = []
    for i in range(41):
        u = -1 + i / 20                       # across the crown
        x = cx + u * 0.40 * s * (1 - 0.12 * (1 + math.cos(math.pi * (u + 1) / 2)) * 0)
        side = 0.40 * s * (0.86 + 0.14 * (1 - abs(u)))
        h = 0.66 * s * (1 - 0.18 * u * u) - 0.10 * s * math.exp(-(u / 0.22) ** 2)   # crease in the middle
        crown.append(rot(cx + u * side, cy - 0.04 * s - h))
    crown = [rot(cx - 0.43 * s, cy - 0.04 * s)] + crown + [rot(cx + 0.43 * s, cy - 0.04 * s)]
    d.polygon(crown, fill=fill)
    d.polygon([rot(cx - 0.425 * s, cy - 0.17 * s), rot(cx + 0.425 * s, cy - 0.17 * s),
               rot(cx + 0.43 * s, cy - 0.06 * s), rot(cx - 0.43 * s, cy - 0.06 * s)], fill=RED)


def centred(d, y, text, font, fill, spacing=0, shadow=None):
    w = d.textlength(text, font=font) + spacing * (len(text) - 1)
    x = (d.im.size[0] - w) / 2
    for ch in text:
        if shadow:
            d.text((x + shadow[0], y + shadow[1]), ch, font=font, fill=shadow[2])
        d.text((x, y), ch, font=font, fill=fill)
        x += d.textlength(ch, font=font) + spacing


def star(d, cx, cy, r, fill):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rr = r if i % 2 == 0 else r * 0.42
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    d.polygon(pts, fill=fill)


def rule(d, cx, y, half, s):
    d.rectangle([cx - half, y - 2 * s, cx - 30 * s, y + 2 * s], fill=RED)
    d.rectangle([cx + 30 * s, y - 2 * s, cx + half, y + 2 * s], fill=RED)
    star(d, cx, y, 14 * s, RED)


def grain(img, amount=10, seed=1):
    noise = Image.effect_noise(img.size, amount).convert("RGB")
    return Image.blend(img, Image.composite(noise, img, Image.new("L", img.size, 40)), 0.25)


def vignette(img, strength=0.55):
    w, h = img.size
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).ellipse([-w * 0.25, -h * 0.35, w * 1.25, h * 1.35], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(min(w, h) * 0.18))
    dark = Image.new("RGB", (w, h), (0, 0, 0))
    return Image.composite(img, Image.blend(img, dark, strength), mask)


def render(w, h, scale_text):
    S = 2   # supersample
    W, H = w * S, h * S
    img = sky(W, H)
    d = ImageDraw.Draw(img, "RGBA")
    s = S * scale_text
    cx = W / 2
    # Two hats, overlapping: the party.
    hat(d, cx - 128 * s, H * 0.255, 112 * s, DEEP, tilt=-0.07)
    hat(d, cx + 128 * s, H * 0.255, 112 * s, DEEP, tilt=0.07)
    centred(d, H * 0.29, "RDR2", oswald(int(62 * s), 700), BONE, spacing=26 * s)
    centred(d, H * 0.37, "CO-OP", ImageFont.truetype(RYE, int(250 * s)), BONE, spacing=6 * s,
            shadow=(8 * s, 10 * s, (120, 16, 12, 230)))
    rule(d, cx, H * 0.70, 430 * s, s)
    centred(d, H * 0.735, "PLAY THE STORY TOGETHER", oswald(int(54 * s), 500), BONE, spacing=10 * s)
    parts, f, gap = ["1 - 4 PLAYERS", "STORY MODE", "UNOFFICIAL MOD"], oswald(int(30 * s), 400), 64 * s
    widths = [d.textlength(t, font=f) + 5 * s * (len(t) - 1) for t in parts]
    x = (W - sum(widths) - gap * (len(parts) - 1)) / 2
    y = H * 0.83
    for n, (t, tw) in enumerate(zip(parts, widths)):
        for ch in t:
            d.text((x, y), ch, font=f, fill=(214, 190, 160))
            x += d.textlength(ch, font=f) + 5 * s
        x += gap - 5 * s
        if n < len(parts) - 1:
            star(d, x - gap / 2, y + 26 * s, 10 * s, RED)
    img = img.resize((w, h), Image.LANCZOS)
    return vignette(grain(img))


def render_banner(w=1300, h=372):
    """Nexus page header: title in the middle (it stays visible when the page
    crops the sides), one hat either side."""
    S = 2
    W, H = w * S, h * S
    img = sky(W, int(H * 1.9)).crop((0, int(H * 0.55), W, int(H * 0.55) + H))   # the sunset band
    d = ImageDraw.Draw(img, "RGBA")
    cx = W / 2
    hat(d, cx - 470 * S, H * 0.62, 95 * S, DEEP, tilt=-0.08)
    hat(d, cx + 470 * S, H * 0.62, 95 * S, DEEP, tilt=0.08)
    centred(d, H * 0.06, "RDR2", oswald(int(34 * S), 700), BONE, spacing=16 * S)
    centred(d, H * 0.15, "CO-OP", ImageFont.truetype(RYE, int(150 * S)), BONE, spacing=4 * S,
            shadow=(5 * S, 6 * S, (120, 16, 12, 230)))
    centred(d, H * 0.80, "PLAY THE STORY TOGETHER", oswald(int(30 * S), 500), BONE, spacing=7 * S)
    img = img.resize((w, h), Image.LANCZOS)
    return vignette(grain(img), 0.45)


def render_icon(size=1024):
    """Server icon (Discord shows it round and small): the two hats and CO-OP,
    big, inside the circle; none of the small print."""
    S = 2
    W = size * S
    img = sky(W, W)
    d = ImageDraw.Draw(img, "RGBA")
    cx = W / 2
    hat(d, cx - 0.155 * W, W * 0.33, 0.135 * W, DEEP, tilt=-0.07)
    hat(d, cx + 0.155 * W, W * 0.33, 0.135 * W, DEEP, tilt=0.07)
    centred(d, W * 0.38, "CO-OP", ImageFont.truetype(RYE, int(0.235 * W)), BONE, spacing=0.004 * W,
            shadow=(0.008 * W, 0.010 * W, (120, 16, 12, 230)))
    img = img.resize((size, size), Image.LANCZOS)
    return vignette(grain(img), 0.35)


if __name__ == "__main__":
    render_banner().save(HERE / "header-1300x372.png", optimize=True)
    render(1920, 1080, 1.0).save(HERE / "logo-1920x1080.png", optimize=True)
    render(1024, 1024, 0.62).save(HERE / "logo-1024.png", optimize=True)
    render_icon().save(HERE / "icon-1024.png", optimize=True)
    print("wrote", HERE / "logo-1920x1080.png", HERE / "logo-1024.png")
