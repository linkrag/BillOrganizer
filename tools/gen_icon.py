"""Generates 1.6/Textures/BillOrganizer/Catalog.png (bottom-bar and bench button icon: a task clipboard with a checklist). python tools/gen_icon.py"""
from PIL import Image, ImageDraw
S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
fg, accent = (232, 236, 240, 255), (255, 196, 70, 255)
# board
d.rounded_rectangle((46, 36, 210, 238), 18, outline=fg, width=14)
# clip
d.rounded_rectangle((88, 14, 168, 58), 10, fill=accent)
d.rounded_rectangle((106, 26, 150, 40), 5, fill=(0, 0, 0, 0))
# rows: check + line (first one done, in accent)
for i in range(3):
    y = 84 + i * 46
    if i == 0:
        d.line([(72, y + 14), (86, y + 28), (112, y)], fill=accent, width=12, joint="curve")
    else:
        d.rounded_rectangle((72, y, 100, y + 28), 6, outline=fg, width=7)
    d.rounded_rectangle((120, y + 6, 190 - i * 14, y + 22), 7, fill=fg)
img = img.resize((64, 64), Image.LANCZOS)
img.save("1.6/Textures/BillOrganizer/Catalog.png")
