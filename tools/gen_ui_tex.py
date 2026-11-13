"""Generates the white UI glyphs (tinted in game with GUI.color) into 1.6/Textures/BillOrganizer/UI. python tools/gen_ui_tex.py
The game font has no reliable glyphs for stars/arrows, so the picker draws these textures instead of Unicode characters."""
import math
from PIL import Image, ImageDraw
S, OUT, W = 256, 32, (255, 255, 255, 255)

def save(name, draw):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw(ImageDraw.Draw(img))
    img.resize((OUT, OUT), Image.LANCZOS).save(f"1.6/Textures/BillOrganizer/UI/{name}.png")

def star_pts(cx, cy, r, ri):
    return [(cx + (r if i % 2 == 0 else ri) * math.sin(i * math.pi / 5), cy - (r if i % 2 == 0 else ri) * math.cos(i * math.pi / 5)) for i in range(10)]

save("star", lambda d: d.polygon(star_pts(128, 134, 112, 46), fill=W))
def star_o(d):
    d.polygon(star_pts(128, 134, 112, 46), fill=W)
    d.polygon(star_pts(128, 134, 70, 29), fill=(0, 0, 0, 0))
save("star_o", star_o)
save("arrow_r", lambda d: d.polygon([(80, 40), (190, 128), (80, 216)], fill=W))
save("arrow_d", lambda d: d.polygon([(40, 80), (216, 80), (128, 190)], fill=W))
save("tri_up", lambda d: d.polygon([(40, 190), (216, 190), (128, 70)], fill=W))
save("tri_down", lambda d: d.polygon([(40, 70), (216, 70), (128, 190)], fill=W))
def plus(d):
    d.rounded_rectangle((100, 36, 156, 220), 12, fill=W); d.rounded_rectangle((36, 100, 220, 156), 12, fill=W)
save("plus", plus)
def check(d): d.line([(48, 138), (104, 194), (210, 66)], fill=W, width=40, joint="curve")
save("check", check)
def cross(d):
    d.line([(60, 60), (196, 196)], fill=W, width=36); d.line([(196, 60), (60, 196)], fill=W, width=36)
save("cross", cross)
def pin(d):
    d.rounded_rectangle((70, 30, 186, 110), 18, fill=W); d.polygon([(70, 110), (186, 110), (150, 160), (106, 160)], fill=W)
    d.rectangle((120, 160, 136, 226), fill=W)
save("pin", pin)

# --- added 5/nov: own glyphs for "duplicate" (card button) and link / unlink (bench gizmos), so Paste is not reused for other meanings
def duplicate(d):
    d.rounded_rectangle((28, 28, 156, 156), 18, outline=W, width=22)
    d.rounded_rectangle((96, 96, 224, 224), 18, fill=W)
    d.rounded_rectangle((148, 112, 172, 208), 6, fill=(0, 0, 0, 0)); d.rounded_rectangle((112, 148, 208, 172), 6, fill=(0, 0, 0, 0))
save("duplicate", duplicate)

def save_big(name, draw, out=64):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw(ImageDraw.Draw(img))
    img.resize((out, out), Image.LANCZOS).save(f"1.6/Textures/BillOrganizer/UI/{name}.png")

def link_rings(d):
    d.rounded_rectangle((20, 80, 150, 176), 48, outline=W, width=24)
    d.rounded_rectangle((106, 80, 236, 176), 48, outline=W, width=24)
def link(d): link_rings(d)
def unlink(d):
    # two rings pulled apart with an open gap (a broken chain) and small "snap" ticks
    d.rounded_rectangle((6, 84, 132, 172), 40, outline=W, width=22)
    d.rounded_rectangle((124, 84, 250, 172), 40, outline=W, width=22)
    d.rectangle((92, 96, 164, 160), fill=(0, 0, 0, 0))
    for (a, b) in (((112, 66), (98, 40)), ((144, 66), (158, 40)), ((112, 190), (98, 216)), ((144, 190), (158, 216))):
        d.line([a, b], fill=W, width=14)
save_big("link", link); save_big("unlink", unlink)

# --- added 11/nov: "bonus" marker of the recipe list (the item gives something beyond its stats: bandwidth, ability, drug effect).
# A four-point sparkle, so it does not read as the favorite star.
def bonus(d):
    d.polygon([(128, 12), (154, 102), (244, 128), (154, 154), (128, 244), (102, 154), (12, 128), (102, 102)], fill=W)
save("bonus", bonus)
