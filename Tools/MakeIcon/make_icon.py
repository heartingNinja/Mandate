# Draws the Mandate icon: the board in miniature. A gold hub with an "M" in the middle, a ring of nodes in the seat
# colours (neutral grey between them), edges like the in-game board, on the game's dark background.
#
#   python Tools/MakeIcon/make_icon.py
#
# Writes (paths from the project root):
#   Assets/Mandate/Art/MandateIcon.png                     1024 px, the app icon (Player Settings default icon)
#   Assets/WebGLTemplates/Mandate/TemplateData/favicon.ico 16-256 px, the browser tab icon
#   Assets/WebGLTemplates/Mandate/TemplateData/icon.png    256 px, shown while the web build loads
#   Assets/WebGLTemplates/Mandate/thumbnail.png            128 px, the template's picture in Player Settings
# Placeholder art: replace any of these files with your own and keep the names.

import math, os, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
S = 1024                     # drawn at 4x and scaled down, for smooth edges
BIG = S * 4

BACKGROUND = (26, 28, 33)    # the game's camera background, a touch lighter so the icon reads on dark tabs
EDGE = (120, 124, 134)
NEUTRAL = (107, 107, 117)    # neutralColor in MandateGameView
HUB = (242, 200, 64)
SEATS = [(230, 77, 64), (64, 140, 242), (77, 191, 89), (242, 166, 51)]   # playerColors 1-4


def draw():
    im = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    k = BIG / S
    d.rounded_rectangle([0, 0, BIG - 1, BIG - 1], radius=int(210 * k), fill=BACKGROUND)

    cx = cy = BIG / 2
    ring_r, node_r, hub_r = 340 * k, 82 * k, 150 * k
    nodes = []
    for i in range(8):
        a = -math.pi / 2 + i * math.pi / 4
        nodes.append((cx + ring_r * math.cos(a), cy + ring_r * math.sin(a)))

    # Edges: round the ring, and spokes from the hub to the seat nodes.
    w = int(22 * k)
    for i in range(8):
        d.line([nodes[i], nodes[(i + 1) % 8]], fill=EDGE, width=w)
    for i in range(0, 8, 2):
        d.line([(cx, cy), nodes[i]], fill=EDGE, width=w)

    # Seat nodes on the even spots, neutral ones between.
    for i, (x, y) in enumerate(nodes):
        colour = SEATS[i // 2] if i % 2 == 0 else NEUTRAL
        r = node_r if i % 2 == 0 else node_r * 0.72
        d.ellipse([x - r - 10 * k, y - r - 10 * k, x + r + 10 * k, y + r + 10 * k], fill=BACKGROUND)   # dark outline
        d.ellipse([x - r, y - r, x + r, y + r], fill=colour)

    # The hub, with the M.
    d.ellipse([cx - hub_r - 14 * k, cy - hub_r - 14 * k, cx + hub_r + 14 * k, cy + hub_r + 14 * k], fill=BACKGROUND)
    d.ellipse([cx - hub_r, cy - hub_r, cx + hub_r, cy + hub_r], fill=HUB)
    font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", int(190 * k))
    d.text((cx, cy + 6 * k), "M", font=font, fill=BACKGROUND, anchor="mm")

    return im.resize((S, S), Image.LANCZOS)


def save(img, rel, size=None, **kw):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    (img if size is None else img.resize((size, size), Image.LANCZOS)).save(path, **kw)
    print("wrote", rel)


icon = draw()
save(icon, "Assets/Mandate/Art/MandateIcon.png")
save(icon, "Assets/WebGLTemplates/Mandate/TemplateData/favicon.ico", sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
save(icon, "Assets/WebGLTemplates/Mandate/TemplateData/icon.png", 256)
save(icon, "Assets/WebGLTemplates/Mandate/thumbnail.png", 128)
if len(sys.argv) > 1:   # a preview sheet at real sizes, for checking how small it still reads
    sheet = Image.new("RGBA", (16 + 32 + 64 + 128 + 256 + 60, 256), (240, 240, 240, 255))
    x = 10
    for s in (16, 32, 64, 128, 256):
        sheet.paste(icon.resize((s, s), Image.LANCZOS), (x, 256 - s), icon.resize((s, s), Image.LANCZOS))
        x += s + 10
    sheet.save(sys.argv[1])
    print("preview", sys.argv[1])
