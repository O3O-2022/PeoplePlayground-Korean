"""그림(텍스처) 속 영어 글자를 한글로 바꾼 조각 이미지를 만든다.

게임 원본 그림을 통째로 배포하지 않도록, 글자가 바뀐 부분만 잘라서 저장한다.
플러그인(PPGKoreanFont)이 게임 실행 중에 원본 그림 위에 이 조각을 덮어씌운다.

사용법 (저장소 루트에서):
    python dev/texture/make_textures.py --game "<게임 폴더>"

필요: Pillow, numpy, UnityPy (`pip install pillow numpy UnityPy`)
결과:
    patch/BepInEx/Translation/ko/Image/      조각 이미지 + index.txt (배포되는 결과물)
    docs/compare/11_textures.png             README용 전/후 비교
    installer/preview/*.png                  설치 도우미 미리보기
"""
import argparse, os, sys, urllib.request
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "patch", "BepInEx", "Translation", "ko", "Image")
FONT_CACHE = os.path.join(ROOT, ".build", "fonts")
WINFONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")

# 갈무리(Galmuri) 픽셀 폰트, SIL OFL 1.1. 개발할 때만 받아 쓰고 배포 파일에는 넣지 않는다.
GALMURI = "https://cdn.jsdelivr.net/npm/galmuri@2.40.3/dist/"

SMALL_ITEMS = "sactx-0-1024x512-Uncompressed-Small items-c93c2043"


# ── 폰트 ──
def galmuri(name, size):
    path = os.path.join(FONT_CACHE, name)
    if not os.path.exists(path):
        os.makedirs(FONT_CACHE, exist_ok=True)
        print("download", GALMURI + name)
        urllib.request.urlretrieve(GALMURI + name, path)
    return ImageFont.truetype(path, size)


def malgun(bold=False, size=20):
    return ImageFont.truetype(os.path.join(WINFONTS, "malgunbd.ttf" if bold else "malgun.ttf"), size)


# ── 그리기 도구 ──
def pixel_text(text, font, bold=False):
    """픽셀 폰트를 안티에일리어싱 없이 그려 bool 배열(잉크 부분만)로 돌려준다."""
    im = Image.new("L", (len(text) * font.size * 2 + 8, font.size * 2 + 8), 0)
    d = ImageDraw.Draw(im)
    d.fontmode = "1"
    d.text((2, 2), text, font=font, fill=255)
    a = np.array(im) > 127
    if bold:  # 세로획을 2픽셀로
        a = a | np.roll(a, 1, 1)
    ys, xs = np.where(a)
    return a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def smooth_text(text, font, spacing=0.0):
    """부드러운 글자를 그려 0~1 커버리지 배열(잉크 부분만)로 돌려준다. spacing은 글자 사이 추가 간격(px)."""
    s = 4  # 4배로 그린 뒤 줄여서 가장자리를 매끄럽게
    big = ImageFont.truetype(font.path, font.size * s)
    widths = [big.getlength(c) for c in text]
    w = int(sum(widths) + spacing * s * len(text)) + 16 * s
    im = Image.new("L", (w, int(font.size * s * 1.6)), 0)
    d = ImageDraw.Draw(im)
    x = 8 * s
    for c, cw in zip(text, widths):
        d.text((x, 4 * s), c, font=big, fill=255)
        x += cw + spacing * s
    im = im.resize((im.width // s, im.height // s), Image.LANCZOS)
    a = np.array(im).astype(float) / 255
    ys, xs = np.where(a > 0.02)
    return a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def fit_font(text, font_fn, max_w, max_h, start):
    """max_w x max_h 안에 들어가는 가장 큰 크기의 폰트를 고른다."""
    for size in range(start, 5, -1):
        a = smooth_text(text, font_fn(size))
        if a.shape[1] <= max_w and a.shape[0] <= max_h:
            return font_fn(size), a
    raise ValueError("글자가 들어가지 않습니다: " + text)


def lum(rgb):
    return rgb[..., 0] * 0.3 + rgb[..., 1] * 0.59 + rgb[..., 2] * 0.11


def inpaint(rgb, mask, iters=600):
    """mask 부분을 주변 색으로 매끄럽게 메운다 (글자 지우기)."""
    out = rgb.astype(float).copy()
    out[mask] = rgb[~mask].mean(0)
    for _ in range(iters):
        avg = (np.roll(out, 1, 0) + np.roll(out, -1, 0) + np.roll(out, 1, 1) + np.roll(out, -1, 1)) / 4
        out[mask] = avg[mask]
    return out


def dilate(mask, r):
    out = mask.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out |= np.roll(np.roll(mask, dy, 0), dx, 1)
    return out


def bbox(mask):
    ys, xs = np.where(mask)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def put_mask(arr, mask, x0, y0, color):
    ys, xs = np.where(mask)
    arr[y0 + ys, x0 + xs, :len(color)] = color


def put_cover(arr, cover, x0, y0, color, alpha=1.0):
    """커버리지(0~1)만큼 color를 섞어 칠한다."""
    h, w = cover.shape
    reg = arr[y0:y0 + h, x0:x0 + w]
    c = (cover * alpha)[..., None]
    reg[..., :3] = reg[..., :3] * (1 - c) + np.array(color[:3], float) * c
    if arr.shape[2] == 4:
        reg[..., 3] = np.maximum(reg[..., 3], cover * alpha * 255)


def glow(cover, radius):
    im = Image.fromarray((cover * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(radius))
    return np.array(im).astype(float) / 255


def upscale(mask, k):
    return np.kron(mask, np.ones((k, k), bool))


def to_img(arr):
    return Image.fromarray(np.clip(np.rint(arr), 0, 255).astype(np.uint8), "RGBA")


# ── 게임 그림 읽기 ──
class Game:
    def __init__(self, game_dir):
        import UnityPy
        data = os.path.join(game_dir, "People Playground_Data")
        if not os.path.isdir(data):
            sys.exit("게임 폴더가 아닙니다: " + game_dir)
        env = UnityPy.load(data)
        self.tex = {}
        self.sprites = {}
        for obj in env.objects:
            if obj.type.name == "Texture2D":
                t = obj.read()
                self.tex.setdefault(t.m_Name, []).append(t)
            elif obj.type.name == "Sprite":
                s = obj.read()
                self.sprites.setdefault(s.m_Name, []).append(s)

    def image(self, name, size=None):
        for t in self.tex.get(name, []):
            if size is None or (t.m_Width, t.m_Height) == size:
                return np.array(t.image.convert("RGBA")).astype(float)
        raise KeyError("그림이 없습니다: %s %s" % (name, size))

    def atlas_rect(self, sprite, atlas_tex):
        """아틀라스 안의 스프라이트 위치 (x, y, w, h), y는 위쪽 기준."""
        for s in self.sprites.get(sprite, []):
            if not (s.m_SpriteAtlas and s.m_SpriteAtlas.m_PathID):
                continue
            atlas = s.m_SpriteAtlas.read()
            for key, rd in atlas.m_RenderDataMap:
                if tuple(key) == tuple(s.m_RenderDataKey) and rd.texture.read().m_Name == atlas_tex:
                    r = rd.textureRect
                    th = rd.texture.read().m_Height
                    return int(r.x), int(th - r.y - r.height), int(r.width), int(r.height)
        raise KeyError("아틀라스에서 찾지 못했습니다: " + sprite)


# ── 그림별 한글화 ──
# 각 함수는 (원본 배열, 한글 배열)을 돌려준다. 배열은 RGBA float, 위쪽이 0행.

def make_logo(g):
    """맵을 불러올 때 나오는 로딩 화면의 제목 로고: 흰 글자 + 검은 테두리, 안티에일리어싱 없음"""
    src = g.image("logo", (922, 71))
    H, W = src.shape[:2]
    white = (src[..., 3] > 128) & (lum(src[..., :3]) > 128)
    ink = src[..., 3] > 128
    # 원본 테두리 두께: 흰 글자를 몇 번 넓히면 테두리까지 덮는지
    t = next(r for r in range(1, 6) if (dilate(white, r) | ~ink)[ink].all())
    # 원본 획 굵기(약 10px)에 가까운 굵은 맑은 고딕, 흰 글자 높이를 원본과 같게
    font, cover = fit_font("피플 플레이그라운드", lambda s: malgun(True, s), W - 2 * t - 40, H - 2 * t, 90)
    cover = smooth_text("피플 플레이그라운드", font, spacing=font.size * 0.03)
    body = cover > 0.5
    h, w = body.shape
    x0, y0 = (W - w) // 2, (H - h) // 2
    full = np.zeros((H, W), bool)
    full[y0:y0 + h, x0:x0 + w] = body
    edge = dilate(full, t) & ~full
    out = np.zeros_like(src)
    out[full] = (255, 255, 255, 255)
    out[edge] = (0, 0, 0, 255)
    return src, out


def make_esc_text(g):
    """전체 화면 안내 문구 (회색, 반투명)"""
    src = g.image("EscToExit_Text", (267, 20))
    H, W = src.shape[:2]
    solid = src[..., 3] >= src[..., 3].max() - 2
    color = src[solid][:, :3].mean(0)
    alpha = src[..., 3].max() / 255
    text = "전체 화면을 끝내려면 Esc를 누르세요"
    # 작은 크기에서 보통 굵기는 획이 1픽셀이라 흐려 보인다
    font, cover = fit_font(text, lambda s: malgun(True, s), W - 4, H - 1, 16)
    out = np.zeros_like(src)
    h, w = cover.shape
    put_cover(out, cover, (W - w) // 2, (H - h) // 2, color, alpha)
    return src, out


def make_thumbnail_error(g):
    """창작마당 썸네일을 못 불러왔을 때 나오는 그림"""
    src = g.image("ErrorWhileLoadingThumbnail", (512, 512))
    rgb = src[..., :3]
    text = lum(rgb) > 110
    x0, y0, x1, y1 = bbox(text)
    # "ERROR"(큰 글자)와 "loading thumbnail"(작은 글자) 두 줄을 나눈다
    rows = np.where(text.any(1))[0]
    gap = np.argmax(np.diff(rows) > 1)
    big = (rows[0], rows[gap] + 1)
    small = (rows[gap + 1], rows[-1] + 1)
    color = rgb[text].max(0)
    out = src.copy()
    out[..., :3] = inpaint(rgb, dilate(text, 3))
    cx = (x0 + x1) / 2
    for line, (a, b), bold in [("오류", big, True), ("썸네일을 불러오지 못했습니다", small, False)]:
        want_h = b - a
        font, cover = fit_font(line, lambda s, bold=bold: malgun(bold, s), 440, int(want_h * 1.25), int(want_h * 1.6))
        h, w = cover.shape
        put_cover(out, cover, int(cx - w / 2), int((a + b) / 2 - h / 2), color)
    return src, out


def make_open_closed(g):
    """밸브 표시판: 왼쪽 CLOSED(빨강) → 차단, 오른쪽 OPEN(초록) → 개방.
    원본은 작은 픽셀 글꼴을 키운 모양이라 11픽셀 글꼴을 2배로 키운다 (7픽셀 한글은 뭉개진다)"""
    src = g.image("OpenClosed", (256, 51))
    rgb = src[..., :3]
    ink = lum(rgb) < 200
    xs = np.where(ink.any(0))[0]
    split = xs[np.argmax(np.diff(xs))] + 1  # 두 단어 사이 빈칸
    out = src.copy()
    out[..., :3] = 255
    font = galmuri("Galmuri11.ttf", 12)
    for word, part in [("차단", ink[:, :split]), ("개방", ink[:, split:])]:
        off = 0 if word == "차단" else split
        x0, y0, x1, y1 = bbox(part)
        color = rgb[:, off:off + part.shape[1]][part].mean(0)
        k = 2
        m = upscale(pixel_text(word, font, bold=True), k)
        h, w = m.shape
        put_mask(out, m, off + (x0 + x1) // 2 - w // 2, (y0 + y1) // 2 - h // 2, color)
    return src, out


def make_danger_sign(g):
    """네덜란드어 경고판 GEVAAR! ZIEKELIJK WARM (위험! 미친 듯이 뜨거움) → 위험! 앗뜨거!
    원문의 장난스러운 말투를 살린다"""
    src = g.image("gevaar ziekelijk warm", (35, 21))
    rgb = src[..., :3]
    H, W = src.shape[:2]
    plate = (src[..., 3] > 200)
    # 판 테두리(어두운 1픽셀)만 빼고 글자를 찾는다. 빨간 글자는 테두리 바로 옆까지 닿아 있다
    inner = plate & ~dilate(~plate, 1)
    base = np.median(lum(rgb)[inner])
    red = inner & (rgb[..., 0] > rgb[..., 1] + 40)
    ink = red | (inner & (np.abs(lum(rgb) - base) > 18) & ~dilate(~plate, 2))
    grey = ink & ~red
    red_c = rgb[red].mean(0)
    grey_c = rgb[grey & (lum(rgb) > base)].mean(0)
    out = src.copy()
    out[..., :3] = inpaint(rgb, ink)
    x0, y0, x1, y1 = bbox(ink)
    font = galmuri("Galmuri7.ttf", 8)
    l1, l2 = pixel_text("위험!", font), pixel_text("앗뜨거!", font)
    total = l1.shape[0] + 2 + l2.shape[0]
    top = (y0 + y1) // 2 - total // 2
    cx = (x0 + x1) // 2
    put_mask(out, l1, cx - l1.shape[1] // 2, top, red_c)
    put_mask(out, l2, cx - l2.shape[1] // 2, top + l1.shape[0] + 2, grey_c)
    return src, out


def grid(s):
    return np.array([[c == "#" for c in row] for row in s.strip("\n").split("\n")])


# 1톤 추 글자는 폰트로는 뭉개져서 픽셀을 직접 찍었다. 원본처럼 위에 1, 아래에 톤.
# ㅗ와 ㄴ 받침 사이를 한 줄 띄우고 ㄴ 세로획을 두 줄로 해야 받침이 보인다.
WEIGHT_ONE = grid("""
##.
.#.
###
""")
WEIGHT_TON = grid("""
#########.
##........
########..
##........
#########.
....##....
##########
..........
##........
##........
#########.
""")


def make_weight(g, sprite):
    """1톤 추 (아틀라스 안 26x21 스프라이트). 1 / TON → 1 / 톤"""
    atlas = g.image(SMALL_ITEMS, (1024, 512))
    x, y, w, h = g.atlas_rect(sprite, SMALL_ITEMS)
    src = atlas[y:y + h, x:x + w].copy()
    rgb = src[..., :3]
    region = np.zeros((h, w), bool)
    region[5:18, 4:22] = True
    L = lum(rgb)
    base = np.median(L[region])
    dev = np.abs(L - base)
    core = region & (dev > 40)
    text = region & (core | (dilate(core, 1) & (dev > 15)))  # 글자와 글자 둘레의 음영만
    ink = rgb[core].mean(0)
    out = src.copy()
    out[..., :3] = inpaint(rgb, text)
    for glyph, top in [(WEIGHT_ONE, 4), (WEIGHT_TON, 8)]:
        put_mask(out, glyph, 13 - (glyph.shape[1] + 1) // 2, top, ink)
    return src, out, (x, y)


def fit_sprite(thumb, sprite):
    """썸네일 안에 스프라이트가 몇 배로, 어디에 그려졌는지 찾는다 (최근접 확대)."""
    bg = thumb[0, 0, :3]
    ys, xs = np.where(np.abs(thumb[..., :3] - bg).sum(-1) > 12)
    bw, bh = xs.max() - xs.min() + 1, ys.max() - ys.min() + 1
    sh, sw = sprite.shape[:2]
    best = None
    for W in range(bw - 2, bw + 3):
        for Hh in range(bh - 2, bh + 3):
            r = np.array(to_img(sprite).resize((W, Hh), Image.NEAREST)).astype(float)
            a = r[..., 3] > 128
            for ox in range(xs.min() - 3, xs.min() + 4):
                for oy in range(ys.min() - 3, ys.min() + 4):
                    if ox < 0 or oy < 0 or ox + W > thumb.shape[1] or oy + Hh > thumb.shape[0]:
                        continue
                    err = np.abs(thumb[oy:oy + Hh, ox:ox + W, :3][a] - r[..., :3][a]).mean()
                    if best is None or err < best[0]:
                        best = (err, W, Hh, ox, oy)
    if best[0] > 12:
        raise ValueError("썸네일과 스프라이트가 맞지 않습니다")
    return best[1:]


def make_weight_thumb(g, sprite_src, sprite_ko):
    """아이템 목록의 1000kg 추 아이콘 = 스프라이트를 확대한 그림. 바뀐 픽셀만 같은 배율로 옮긴다."""
    src = g.image("1000kg Weight", (256, 256))
    W, Hh, ox, oy = fit_sprite(src, sprite_src)
    big_src = np.array(to_img(sprite_src).resize((W, Hh), Image.NEAREST)).astype(float)
    big_ko = np.array(to_img(sprite_ko).resize((W, Hh), Image.NEAREST)).astype(float)
    changed = np.abs(big_ko - big_src).sum(-1) > 0
    out = src.copy()
    reg = out[oy:oy + Hh, ox:ox + W]
    reg[changed] = big_ko[changed]
    return src, out


def make_screen_thumb(g, name, word, text_scale):
    """검은 화면에 글자가 빛나는 아이템 아이콘 (텍스트 디스플레이, 키 트리거)"""
    src = g.image(name, (256, 256))
    rgb = src[..., :3]
    L = lum(rgb)
    # 가운데 화면 안의 밝은 글자
    screen = np.zeros(L.shape, bool)
    screen[70:190, 60:200] = True
    bg = np.median(L[screen])
    text = screen & (L > bg + 60)
    x0, y0, x1, y1 = bbox(text)
    color = rgb[text & (L >= np.percentile(L[text], 70))].mean(0)
    out = src.copy()
    halo = screen & dilate(text, 6) & (L > bg + 4)
    out[..., :3] = inpaint(rgb, halo | dilate(text, 2))
    m = upscale(pixel_text(word, galmuri("Galmuri11.ttf", 12)), text_scale)
    h, w = m.shape
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    px, py = cx - w // 2, cy - h // 2
    full = np.zeros(L.shape)
    full[py:py + h, px:px + w] = m
    put_cover(out, glow(full, 5), 0, 0, color, 0.45)
    put_mask(out, m, px, py, color)
    return src, out


# ── 저장 ──
def changed_box(src, out, pad=1):
    diff = np.abs(src - out).sum(-1) > 0
    if not diff.any():
        return None
    x0, y0, x1, y1 = bbox(diff)
    H, W = diff.shape
    return max(0, x0 - pad), max(0, y0 - pad), min(W, x1 + pad), min(H, y1 + pad)


class Writer:
    def __init__(self):
        os.makedirs(OUT, exist_ok=True)
        for f in os.listdir(OUT):
            if f.endswith(".png") or f == "index.txt":
                os.remove(os.path.join(OUT, f))
        self.lines = [
            "# 그림 속 글자 한글화 조각 (dev/texture/make_textures.py가 만든다. 직접 고치지 말 것)",
            "# 텍스처이름<TAB>너비<TAB>높이<TAB>x<TAB>y<TAB>조각 파일<TAB>원본 확인 파일",
            "# x, y는 그림 왼쪽 위 기준. 원본 확인 파일과 게임 그림이 다르면(게임 업데이트) 그 조각은 건너뛴다.",
        ]

    def add(self, file, tex_name, tex_size, src, out, offset=(0, 0)):
        box = changed_box(src, out)
        x0, y0, x1, y1 = box
        Image.fromarray(np.clip(np.rint(out[y0:y1, x0:x1]), 0, 255).astype(np.uint8), "RGBA").save(
            os.path.join(OUT, file + ".png"), optimize=True)
        Image.fromarray(np.clip(np.rint(src[y0:y1, x0:x1]), 0, 255).astype(np.uint8), "RGBA").save(
            os.path.join(OUT, file + ".orig.png"), optimize=True)
        self.lines.append("\t".join([tex_name, str(tex_size[0]), str(tex_size[1]),
                                     str(offset[0] + x0), str(offset[1] + y0), file + ".png", file + ".orig.png"]))

    def close(self):
        with open(os.path.join(OUT, "index.txt"), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(self.lines) + "\n")


# ── 비교 이미지 ──
BG = (24, 24, 28)


def on_bg(arr, bg=(45, 45, 52)):
    im = to_img(arr)
    base = Image.new("RGBA", im.size, bg + (255,))
    base.alpha_composite(im)
    return base.convert("RGB")


def scaled(arr, max_w, max_h, pixel=False):
    im = on_bg(arr)
    k = min(max_w / im.width, max_h / im.height)
    if pixel and k >= 2:
        k = int(k)
    return im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.NEAREST if pixel else Image.LANCZOS)


def compare_rows(items, col_w, row_h, label_font, head=True):
    """items: (이름, 원본, 한글, 픽셀아트?) → 원본 | 한글 두 열 비교 그림"""
    pad, gap, head_h = 10, 14, 34 if head else 0
    width = pad * 3 + col_w * 2
    heights = []
    cells = []
    for name, src, out, pixel in items:
        a, b = scaled(src, col_w, row_h, pixel), scaled(out, col_w, row_h, pixel)
        cells.append((name, a, b))
        heights.append(max(a.height, b.height) + 22)
    img = Image.new("RGB", (width, head_h + sum(heights) + gap * len(items) + pad), BG)
    d = ImageDraw.Draw(img)
    if head:
        d.rectangle([pad, 4, pad + col_w, head_h - 6], fill=(90, 90, 100))
        d.rectangle([pad * 2 + col_w, 4, pad * 2 + col_w * 2, head_h - 6], fill=(40, 110, 200))
        d.text((pad + 8, 7), "영어 원본", font=label_font, fill=(255, 255, 255))
        d.text((pad * 2 + col_w + 8, 7), "한국어 패치", font=label_font, fill=(255, 255, 255))
    y = head_h + 6
    small = malgun(False, 13)
    for (name, a, b), h in zip(cells, heights):
        d.text((pad, y), name, font=small, fill=(170, 170, 180))
        y += 20
        img.paste(a, (pad + (col_w - a.width) // 2, y))
        img.paste(b, (pad * 2 + col_w + (col_w - b.width) // 2, y))
        y += h - 22 + gap
    return img


def image_preview(shown, width):
    """설치 도우미용: 그림 번역 미리보기. 제목 로고는 위아래로 크게, 나머지는 원본 → 한글 두 칸씩"""
    items = {name: (src, out, pixel) for name, src, out, pixel in shown}
    label = malgun(True, 13)
    small = malgun(False, 12)
    pad = 8
    col = (width - pad * 3) // 2
    logo_src, logo_out, _ = items["로딩 화면 제목"]
    rows = [scaled(logo_src, width - pad * 2, 40), scaled(logo_out, width - pad * 2, 40)]
    pairs = [(n, scaled(items[n][0], col, 64, items[n][2]), scaled(items[n][1], col, 64, items[n][2]))
             for n in ["1톤 추", "밸브 표시판", "경고판 (원래 네덜란드어)"]]
    height = 30 + sum(r.height + 6 for r in rows) + sum(max(a.height, b.height) + 24 for _, a, b in pairs) + pad
    img = Image.new("RGB", (width, height), BG)
    d = ImageDraw.Draw(img)
    d.rectangle([pad, 4, pad + col, 24], fill=(90, 90, 100))
    d.rectangle([pad * 2 + col, 4, pad * 2 + col * 2, 24], fill=(40, 110, 200))
    d.text((pad + 6, 5), "영어 원본", font=label, fill=(255, 255, 255))
    d.text((pad * 2 + col + 6, 5), "한국어 패치", font=label, fill=(255, 255, 255))
    y = 30
    for r in rows:
        img.paste(r, ((width - r.width) // 2, y))
        y += r.height + 6
    for name, a, b in pairs:
        d.text((pad, y), name, font=small, fill=(170, 170, 180))
        y += 18
        img.paste(a, (pad + (col - a.width) // 2, y))
        img.paste(b, (pad * 2 + col + (col - b.width) // 2, y))
        y += max(a.height, b.height) + 6
    return img


def text_preview(width):
    """설치 도우미용: 글자 번역 미리보기 (README 비교 이미지에서 잘라 쓴다)"""
    src = Image.open(os.path.join(ROOT, "docs", "compare", "05_item.png")).convert("RGB")
    half = (src.width - 12) // 2
    en, ko = src.crop((0, 0, half, src.height)), src.crop((half + 12, 0, src.width, src.height))
    k = width / half
    en = en.resize((width, int(en.height * k)), Image.LANCZOS)
    ko = ko.resize((width, int(ko.height * k)), Image.LANCZOS)
    img = Image.new("RGB", (width, en.height * 2 + 8), BG)
    img.paste(en, (0, 0))
    img.paste(ko, (0, en.height + 8))
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", default=os.environ.get("PPG_GAME_DIR", r"D:\Steam\steamapps\common\People Playground"))
    a = ap.parse_args()
    g = Game(a.game)
    w = Writer()
    shown = []

    src, out = make_logo(g)
    w.add("logo", "logo", (922, 71), src, out)
    shown.append(("로딩 화면 제목", src, out, False))

    src, out = make_esc_text(g)
    w.add("esc_to_exit", "EscToExit_Text", (267, 20), src, out)
    shown.append(("전체 화면 안내", src, out, False))

    src, out = make_open_closed(g)
    w.add("open_closed", "OpenClosed", (256, 51), src, out)
    shown.append(("밸브 표시판", src, out, True))

    weights = {}
    for sprite, file in [("OneTonWeight", "weight"), ("OneTonWeightRusty", "weight_rusty"), ("OneTonNoText", "weight_cream")]:
        src, out, off = make_weight(g, sprite)
        w.add(file, SMALL_ITEMS, (1024, 512), src, out, off)
        weights[sprite] = (src, out)
    shown.append(("1톤 추", *weights["OneTonWeight"], True))

    src, out = make_weight_thumb(g, *weights["OneTonWeight"])
    w.add("thumb_weight", "1000kg Weight", (256, 256), src, out)
    thumb_weight = (src, out)

    src, out = make_danger_sign(g)
    w.add("danger_sign", "gevaar ziekelijk warm", (35, 21), src, out)
    shown.append(("경고판 (원래 네덜란드어)", src, out, True))

    src, out = make_screen_thumb(g, "Display", "디스플레이", 2)
    w.add("thumb_display", "Display", (256, 256), src, out)
    thumb_display = (src, out)

    src, out = make_screen_thumb(g, "Key Trigger", "없음", 2)
    w.add("thumb_key_trigger", "Key Trigger", (256, 256), src, out)
    thumb_key = (src, out)

    src, out = make_thumbnail_error(g)
    w.add("thumbnail_error", "ErrorWhileLoadingThumbnail", (512, 512), src, out)
    thumb_error = (src, out)
    w.close()
    print("조각", len(w.lines) - 3, "개 →", OUT)

    # 아이콘 네 개는 한 줄로 모아서 보여 준다
    def icons(pairs, k):
        return np.concatenate([np.pad(p[k], ((0, 0), (0, 8), (0, 0)), constant_values=0) for p in pairs], axis=1)
    icon_pairs = [thumb_weight, thumb_display, thumb_key, thumb_error]
    icon_pairs = [(p[0] if p[0].shape[0] == 256 else np.array(to_img(p[0]).resize((256, 256), Image.LANCZOS)).astype(float),
                   p[1] if p[1].shape[0] == 256 else np.array(to_img(p[1]).resize((256, 256), Image.LANCZOS)).astype(float))
                  for p in icon_pairs]
    shown.append(("아이템 아이콘, 썸네일", icons(icon_pairs, 0), icons(icon_pairs, 1), False))

    label = malgun(True, 17)
    readme = compare_rows(shown, 560, 150, label)
    readme.save(os.path.join(ROOT, "docs", "compare", "11_textures.png"), optimize=True)

    prev = os.path.join(ROOT, "installer", "preview")
    os.makedirs(prev, exist_ok=True)
    image_preview(shown, 340).save(os.path.join(prev, "image.png"), optimize=True)
    text_preview(340).save(os.path.join(prev, "text.png"), optimize=True)
    print("비교 이미지 저장 완료")


if __name__ == "__main__":
    main()
