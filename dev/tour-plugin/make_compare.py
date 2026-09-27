"""Build before/after comparison images (English original vs Korean patch) for the README."""
import os
from PIL import Image, ImageDraw, ImageFont

# 게임 폴더는 환경 변수 PPG_GAME_DIR 로 바꿀 수 있다
SRC = os.path.join(os.environ.get("PPG_GAME_DIR", r"D:\Steam\steamapps\common\People Playground"), "BepInEx", "tour")
DST = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "docs", "compare")
os.makedirs(DST, exist_ok=True)
FONT = ImageFont.truetype(r"C:\Windows\Fonts\malgunbd.ttf", 26)

# name, crop box (x0, y0, x1, y1) in 1920x1080, scale, layout ("h" = side by side, "v" = stacked)
SCENES = [
    ("01_menu", (0, 400, 1920, 690), 0.62, "v"),
    ("02_maps", (0, 0, 1920, 1080), 0.42, "h"),
    ("03_settings", (0, 0, 1920, 1080), 0.42, "h"),
    ("04_controls", (0, 0, 1920, 1080), 0.42, "h"),
    ("05_item", (0, 845, 350, 985), 2.3, "h"),
    ("06_tool", (1330, 190, 1920, 470), 1.35, "h"),
    ("07_context", (440, 160, 1400, 880), 0.84, "h"),
    ("08_detail", (1380, 30, 1920, 280), 1.5, "h"),
    ("09_machine", (1150, 150, 1920, 880), 1.0, "h"),
    ("10_pause", (0, 390, 700, 700), 1.15, "h"),
]
LABEL_H = 44
GAP = 12
BG = (24, 24, 28)


def panel(path, box, scale, label, color):
    im = Image.open(path).convert("RGB").crop(box)
    im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
    out = Image.new("RGB", (im.width, im.height + LABEL_H), BG)
    d = ImageDraw.Draw(out)
    d.rectangle([0, 0, im.width, LABEL_H - 6], fill=color)
    d.text((14, 6), label, font=FONT, fill=(255, 255, 255))
    out.paste(im, (0, LABEL_H))
    return out


for name, box, scale, layout in SCENES:
    en = panel(os.path.join(SRC, name + "_en.png"), box, scale, "영어 원본", (90, 90, 100))
    ko = panel(os.path.join(SRC, name + "_ko.png"), box, scale, "한국어 패치", (40, 110, 200))
    if layout == "h":
        out = Image.new("RGB", (en.width * 2 + GAP, en.height), BG)
        out.paste(en, (0, 0)); out.paste(ko, (en.width + GAP, 0))
    else:
        out = Image.new("RGB", (en.width, en.height * 2 + GAP), BG)
        out.paste(en, (0, 0)); out.paste(ko, (0, en.height + GAP))
    path = os.path.join(DST, name + ".png")
    out.save(path, optimize=True)
    print(name, out.size, os.path.getsize(path) // 1024, "KB")
