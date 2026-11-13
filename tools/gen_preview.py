"""Generates About/Preview.png (1280x720 Steam Workshop / mod-menu preview for Colony Organizer).

Draws a stylized mock of the mod window (category tree, recipe list with "can make" chips,
detail panel with stock-aware ingredients), a pinned-resources card and the mod's clipboard
icon re-drawn at high resolution (same shapes as tools/gen_icon.py). No game or third-party art.
Deterministic. Run from the mod folder:  python tools/gen_preview.py
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1280, 720
SS = 2  # supersampling factor; everything is drawn at 2x and downscaled
OUT = os.path.join("About", "Preview.png")

ACCENT = (255, 196, 70)
BAND = (21, 23, 26)
WHITE = (242, 242, 242)
WIN_BG = (42, 44, 47)
PANEL = (34, 36, 39)
BORDER = (85, 88, 92)
TEXT = (226, 228, 230)
DIM = (150, 154, 160)
GOOD = (132, 200, 120)
BAD = (220, 96, 86)

BAHN = r"C:\Windows\Fonts\bahnschrift.ttf"


def font(size, weight="Regular"):
    try:
        f = ImageFont.truetype(BAHN, size * SS)
        f.set_variation_by_name(weight)
        return f
    except Exception:
        name = "segoeuib.ttf" if weight in ("Bold", "SemiBold") else "segoeui.ttf"
        return ImageFont.truetype(os.path.join(r"C:\Windows\Fonts", name), size * SS)


def s(v):
    return int(round(v * SS))


def box(b):
    return tuple(s(v) for v in b)


class Pen:
    """Small wrapper so coordinates are written in final (1x) pixels."""

    def __init__(self, img):
        self.img = img
        self.d = ImageDraw.Draw(img, "RGBA")

    @staticmethod
    def _translucent(*cols):
        return any(c is not None and len(c) == 4 and c[3] < 255 for c in cols)

    def _blend(self, fn):
        """ImageDraw overwrites RGBA pixels; draw translucent shapes on a layer and composite instead."""
        lay = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        main = self.d
        self.d = ImageDraw.Draw(lay)
        try:
            fn()
        finally:
            self.d = main
        self.img.alpha_composite(lay)

    def rect(self, b, fill=None, outline=None, width=1, r=0):
        if self._translucent(fill, outline):
            return self._blend(lambda: self._rect(b, fill, outline, width, r))
        self._rect(b, fill, outline, width, r)

    def _rect(self, b, fill, outline, width, r):
        if r:
            self.d.rounded_rectangle(box(b), s(r), fill=fill, outline=outline, width=s(width) if outline else 0)
        else:
            self.d.rectangle(box(b), fill=fill, outline=outline, width=s(width) if outline else 0)

    def line(self, pts, fill, width=1):
        if self._translucent(fill):
            return self._blend(lambda: self.d.line([(s(x), s(y)) for x, y in pts], fill=fill, width=s(width), joint="curve"))
        self.d.line([(s(x), s(y)) for x, y in pts], fill=fill, width=s(width), joint="curve")

    def poly(self, pts, fill):
        self.d.polygon([(s(x), s(y)) for x, y in pts], fill=fill)

    def text(self, xy, t, f, fill, anchor="la"):
        self.d.text((s(xy[0]), s(xy[1])), t, font=f, fill=fill, anchor=anchor)

    def tlen(self, t, f):
        return self.d.textlength(t, font=f) / SS


def shadow(base, b, r=10, blur=14, alpha=115, off=(0, 8)):
    """Soft blurred black drop shadow under a rounded box (1x coords)."""
    pad = blur * 3
    x0, y0, x1, y1 = b
    w, h = int(x1 - x0) + pad * 2, int(y1 - y0) + pad * 2
    m = Image.new("L", (s(w), s(h)), 0)
    ImageDraw.Draw(m).rounded_rectangle((s(pad), s(pad), s(pad + x1 - x0), s(pad + y1 - y0)), s(r), fill=alpha)
    m = m.filter(ImageFilter.GaussianBlur(s(blur)))
    blk = Image.new("RGBA", m.size, (0, 0, 0, 255))
    blk.putalpha(m)
    base.alpha_composite(blk, (s(x0 - pad + off[0]), s(y0 - pad + off[1])))


# ---------------------------------------------------------------- background
img = Image.new("RGBA", (s(W), s(H)), (0, 0, 0, 255))
bg = Image.new("RGB", (1, 2))
bg.putpixel((0, 0), (36, 41, 47))
bg.putpixel((0, 1), (22, 25, 29))
bg = bg.resize((s(W), s(H)), Image.BILINEAR).convert("RGBA")
img.alpha_composite(bg)
# warm glow behind the clipboard
glow = Image.new("L", (s(W), s(H)), 0)
ImageDraw.Draw(glow).ellipse(box((10, 180, 410, 580)), fill=88)
glow = glow.filter(ImageFilter.GaussianBlur(s(90)))
gl = Image.new("RGBA", glow.size, ACCENT + (255,))
gl.putalpha(glow)
img.alpha_composite(gl)
# faint grid
p = Pen(img)
for x in range(0, W, 40):
    p.line([(x, 138), (x, H)], (255, 255, 255, 7), 1)
for y in range(138, H, 40):
    p.line([(0, y), (W, y)], (255, 255, 255, 7), 1)

# ---------------------------------------------------------------- clipboard icon (gen_icon.py shapes, 256-unit grid)
def clipboard(base, ox, oy, size):
    k = size / 256.0
    lay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    q = Pen(lay)
    fg = (232, 236, 240, 255)
    acc = ACCENT + (255,)

    def B(x0, y0, x1, y1):
        return (ox + x0 * k, oy + y0 * k, ox + x1 * k, oy + y1 * k)

    q.rect(B(46, 36, 210, 238), fill=(30, 33, 37, 235), r=18 * k)          # board interior (dark, for silhouette)
    q.rect(B(46, 36, 210, 238), outline=fg, width=14 * k, r=18 * k)        # board
    q.rect(B(88, 14, 168, 58), fill=acc, r=10 * k)                          # clip
    q.rect(B(106, 26, 150, 40), fill=(30, 33, 37, 255), r=5 * k)            # clip hole
    for i in range(3):
        y = 84 + i * 46
        if i == 0:
            q.line([(ox + 72 * k, oy + (y + 14) * k), (ox + 86 * k, oy + (y + 28) * k), (ox + 112 * k, oy + y * k)], acc, 12 * k)
        else:
            q.rect(B(72, y, 100, y + 28), outline=fg, width=7 * k, r=6 * k)
        q.rect(B(120, y + 6, 190 - i * 14, y + 22), fill=fg, r=7 * k)
    shadow(base, B(46, 20, 210, 238), r=18 * k, blur=16, alpha=115, off=(0, 12))
    base.alpha_composite(lay)


clipboard(img, 30, 160, 360)

# ---------------------------------------------------------------- mock window
WX0, WY0, WX1, WY1 = 420, 168, 1240, 640
shadow(img, (WX0, WY0, WX1, WY1), r=8, blur=18, alpha=125, off=(0, 12))
p = Pen(img)
p.rect((WX0, WY0, WX1, WY1), fill=WIN_BG + (255,), outline=BORDER, width=2, r=6)
# title bar
p.rect((WX0 + 2, WY0 + 2, WX1 - 2, WY0 + 44), fill=(50, 53, 57, 255), r=5)
p.text((WX0 + 18, WY0 + 23), "Bills  \u2022  Machining table", font(20, "SemiBold"), TEXT, "lm")
cx = WX1 - 26
p.line([(cx - 7, WY0 + 16), (cx + 7, WY0 + 30)], DIM, 2.5)
p.line([(cx - 7, WY0 + 30), (cx + 7, WY0 + 16)], DIM, 2.5)

TOP = WY0 + 56
BOT = WY1 - 12
# columns
LX0, LX1 = WX0 + 12, WX0 + 206
MX0, MX1 = LX1 + 10, LX1 + 10 + 330
RX0, RX1 = MX1 + 10, WX1 - 12
for b in ((LX0, TOP, LX1, BOT), (MX0, TOP, MX1, BOT), (RX0, TOP, RX1, BOT)):
    p.rect(b, fill=PANEL + (255,), outline=BORDER, width=1.5, r=4)

f_tree = font(18)
f_tree_b = font(18, "SemiBold")
# search box
p.rect((LX0 + 8, TOP + 8, LX1 - 8, TOP + 38), fill=(24, 26, 28, 255), outline=BORDER, width=1, r=4)
p.d.ellipse(box((LX0 + 16, TOP + 15, LX0 + 30, TOP + 29)), outline=DIM, width=s(2))
p.line([(LX0 + 28, TOP + 27), (LX0 + 34, TOP + 33)], DIM, 2)
p.text((LX0 + 42, TOP + 23), "Search", f_tree, (120, 124, 130), "lm")


def tri(x, y, opened, col):
    if opened:
        p.poly([(x, y - 4), (x + 10, y - 4), (x + 5, y + 4)], col)
    else:
        p.poly([(x + 2, y - 5), (x + 2, y + 5), (x + 10, y)], col)


tree = [
    (0, "Weapons", True), (1, "Ranged", None), (1, "Melee", None), (1, "Grenades", None),
    (0, "Ammunition", False),
    (0, "Apparel", True), (1, "Head", None), (1, "Torso", None),
    (0, "Meals", False), (0, "Medicine", False),
]
ty = TOP + 62
for lvl, name, opened in tree:
    x = LX0 + 14 + lvl * 22
    if name == "Ranged":
        p.rect((LX0 + 6, ty - 14, LX1 - 6, ty + 14), fill=ACCENT + (40,), r=3)
        p.rect((LX0 + 6, ty - 14, LX0 + 10, ty + 14), fill=ACCENT + (255,))
    if opened is not None:
        tri(x, ty, opened, DIM)
        p.text((x + 18, ty), name, f_tree_b, TEXT, "lm")
    else:
        p.text((x + 18, ty), name, f_tree, ACCENT if name == "Ranged" else (200, 203, 207), "lm")
    ty += 31

# middle: recipe list
f_name = font(19, "SemiBold")
f_hdr = font(16, "SemiBold")
f_chip = font(18, "Bold")
p.text((MX0 + 14, TOP + 20), "RANGED", f_hdr, DIM, "lm")
p.text((MX1 - 14, TOP + 20), "CAN MAKE", f_hdr, DIM, "rm")
p.line([(MX0 + 8, TOP + 38), (MX1 - 8, TOP + 38)], BORDER, 1)

ICON_COLS = [(110, 140, 170), (190, 150, 100), (120, 170, 130), (170, 120, 160), (150, 150, 155), (200, 120, 90)]


def glyph(x0, y0, sz, col, kind):
    """Abstract square item icon."""
    p.rect((x0, y0, x0 + sz, y0 + sz), fill=(22, 24, 26, 255), outline=(70, 73, 77), width=1, r=4)
    c = col + (255,)
    m = sz / 34.0
    if kind == 0:   # long gun silhouette
        p.rect((x0 + 5 * m, y0 + 14 * m, x0 + 29 * m, y0 + 19 * m), fill=c, r=1)
        p.rect((x0 + 9 * m, y0 + 18 * m, x0 + 13 * m, y0 + 27 * m), fill=c, r=1)
    elif kind == 1:  # block / ingot
        p.poly([(x0 + 6 * m, y0 + 22 * m), (x0 + 12 * m, y0 + 12 * m), (x0 + 28 * m, y0 + 12 * m), (x0 + 24 * m, y0 + 22 * m)], c)
    elif kind == 2:  # gear-ish
        p.d.ellipse(box((x0 + 8 * m, y0 + 8 * m, x0 + 26 * m, y0 + 26 * m)), outline=c, width=max(1, s(4 * m)))
    else:            # diamond
        p.poly([(x0 + 17 * m, y0 + 6 * m), (x0 + 28 * m, y0 + 17 * m), (x0 + 17 * m, y0 + 28 * m), (x0 + 6 * m, y0 + 17 * m)], c)


def bars(x, y, vals, w=70):
    for i, v in enumerate(vals):
        yy = y + i * 7
        p.rect((x, yy, x + w, yy + 4), fill=(60, 63, 67, 255), r=2)
        p.rect((x, yy, x + w * v, yy + 4), fill=(170, 176, 184, 255) if i == 0 else (120, 160, 200, 255), r=2)


def chip(xr, yc, txt, on=True):
    tw = p.tlen(txt, f_chip)
    b = (xr - tw - 20, yc - 15, xr, yc + 15)
    if on:
        p.rect(b, fill=ACCENT + (40,), outline=ACCENT + (255,), width=1.5, r=15)
        p.text(((b[0] + b[2]) / 2, yc), txt, f_chip, ACCENT, "mm")
    else:
        p.rect(b, fill=(255, 255, 255, 10), outline=(90, 70, 70, 255), width=1.5, r=15)
        p.text(((b[0] + b[2]) / 2, yc), txt, f_chip, BAD, "mm")


recipes = [
    ("Assault rifle", "x4", (0.8, 0.6), True),
    ("Bolt-action rifle", "x12", (0.55, 0.9), True),
    ("Machine pistol", "x7", (0.45, 0.4), True),
    ("Sniper rifle", "x0", (0.95, 0.95), False),
    ("Shotgun", "x9", (0.6, 0.3), True),
    ("Light MG", "x2", (0.85, 0.5), True),
]
RH = 58
ry = TOP + 44
SEL = 1
for i, (nm, cnt, st, ok) in enumerate(recipes):
    if i == SEL:
        p.rect((MX0 + 4, ry, MX1 - 4, ry + RH - 4), fill=(74, 70, 58, 255), outline=ACCENT + (200,), width=1.5, r=4)
    elif i % 2 == 0:
        p.rect((MX0 + 4, ry, MX1 - 4, ry + RH - 4), fill=(255, 255, 255, 8), r=4)
    glyph(MX0 + 12, ry + 8, 36, ICON_COLS[i], 0)
    p.text((MX0 + 60, ry + 17), nm, f_name, TEXT if ok else (165, 168, 172), "lm")
    bars(MX0 + 60, ry + 34, st)
    if cnt:
        chip(MX1 - 14, ry + RH / 2 - 2, cnt, ok)
    ry += RH

# right: detail
f_big = font(23, "SemiBold")
f_lab = font(17)
f_lab_b = font(17, "SemiBold")
glyph(RX0 + 14, TOP + 14, 64, ICON_COLS[SEL], 0)
p.text((RX0 + 92, TOP + 34), "Bolt-action", f_big, WHITE, "lm")
p.text((RX0 + 92, TOP + 60), "rifle", f_big, WHITE, "lm")

sy = TOP + 100
for lab, v in (("Range", 0.92), ("Damage", 0.7), ("Accuracy", 0.85)):
    p.text((RX0 + 14, sy), lab, f_lab, DIM, "lm")
    bx0, bx1 = RX0 + 108, RX1 - 14
    p.rect((bx0, sy - 3, bx1, sy + 4), fill=(60, 63, 67, 255), r=3)
    p.rect((bx0, sy - 3, bx0 + (bx1 - bx0) * v, sy + 4), fill=(120, 160, 200, 255), r=3)
    sy += 28

sy += 8
p.text((RX0 + 14, sy), "INGREDIENTS", f_hdr, DIM, "lm")
p.line([(RX0 + 8, sy + 16), (RX1 - 8, sy + 16)], BORDER, 1)
sy += 38
for nm, need, have, col, kind in (("Steel", "75", "1,240", (170, 176, 184), 1), ("Wood", "10", "386", (176, 132, 86), 1),
                                  ("Components", "3", "41", (200, 170, 90), 2)):
    glyph(RX0 + 14, sy - 13, 26, col, kind)
    p.text((RX0 + 50, sy), nm, f_lab_b, TEXT, "lm")
    have_w = p.tlen(have, f_lab_b)
    p.text((RX1 - 14, sy), have, f_lab_b, GOOD, "rm")
    p.text((RX1 - 14 - have_w, sy), need + " / ", f_lab_b, TEXT, "rm")
    sy += 36

# buttons
by = BOT - 54
p.rect((RX0 + 12, by, RX0 + 100, by + 40), fill=(255, 255, 255, 10), outline=ACCENT + (255,), width=2, r=5)
p.text((RX0 + 56, by + 20), "Max", font(20, "SemiBold"), ACCENT, "mm")
p.rect((RX0 + 110, by, RX1 - 12, by + 40), fill=ACCENT + (255,), r=5)
p.text(((RX0 + 110 + RX1 - 12) / 2, by + 20), "Add  x12", font(20, "Bold"), (30, 28, 22), "mm")

# ---------------------------------------------------------------- pinned resources card (floating)
CX0, CY0, CX1, CY1 = 70, 532, 350, 640
shadow(img, (CX0, CY0, CX1, CY1), r=8, blur=12, alpha=125, off=(0, 8))
p = Pen(img)
p.rect((CX0, CY0, CX1, CY1), fill=(38, 40, 43, 250), outline=BORDER, width=1.5, r=6)
p.rect((CX0, CY0, CX0 + 5, CY1), fill=ACCENT + (255,), r=2)
p.text((CX0 + 18, CY0 + 18), "PINNED", f_hdr, DIM, "lm")
py = CY0 + 46
for nm, val, col, kind in (("Steel", "1,240", (170, 176, 184), 1), ("Components", "41", (200, 170, 90), 2)):
    glyph(CX0 + 18, py - 13, 26, col, kind)
    p.text((CX0 + 54, py), nm, f_lab_b, TEXT, "lm")
    p.text((CX1 - 16, py), val, f_lab_b, WHITE, "rm")
    py += 34

# ---------------------------------------------------------------- header + footer
p.rect((0, 0, W, 132), fill=BAND + (255,))
p.rect((0, 132, W, 138), fill=ACCENT + (255,))
p.text((48, 68), "COLONY ORGANIZER", font(86, "Bold"), WHITE, "lm")

p.rect((0, 656, W, H), fill=BAND + (224,))
p.text((48, 688), "Grouped bills, stock-aware \u201ccan make\u201d, pinned resources", font(28), (216, 216, 216), "lm")
fb = font(24, "SemiBold")
badge = "RimWorld 1.6 \u00b7 1.7"
bw = p.tlen(badge, fb) + 36
p.rect((W - 48 - bw, 688 - 19, W - 48, 688 + 19), outline=ACCENT + (255,), width=2, r=19)
p.text((W - 48 - bw / 2, 688), badge, fb, WHITE, "mm")

# ---------------------------------------------------------------- save
out = img.resize((W, H), Image.LANCZOS).convert("RGB")
out.save(OUT, optimize=True)
if os.path.getsize(OUT) >= 1_000_000:
    out.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).save(OUT, optimize=True)
chk = Image.open(OUT)
print(OUT, chk.size, chk.mode, os.path.getsize(OUT), "bytes")
