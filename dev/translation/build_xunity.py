"""번역 원본(ko/*.json)과 추출한 영어 원문(*.json)으로 XUnity 번역 파일을 만든다.

사용법 (저장소 루트에서): python dev/translation/build_xunity.py
출력: patch/BepInEx/Translation/ko/Text/10_items.txt ~ 80_formats.txt, 95_manual_punct.txt
      (90_manual.txt는 사람이 직접 고치는 파일이라 덮어쓰지 않는다)
"""
import json, re, os, sys, collections

S = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(S))
J = lambda p: json.load(open(os.path.join(S, p), encoding="utf-8"))
OUT = os.path.join(ROOT, "patch", "BepInEx", "Translation", "ko", "Text")
sys.path.insert(0, os.path.join(ROOT, "tools"))
os.makedirs(OUT, exist_ok=True)
BS = "\\"


def enc(s):
    """Encode a plain key/value for an XUnity translation file."""
    s = s.replace(BS, BS + BS).replace("\r", BS + "r").replace("\n", BS + "n").replace("=", BS + "=")
    assert "//" not in s, s
    return s


def enc_regex_key(pat):
    """Regex keys: '=' must be escaped; real CR/LF are written as \\r/\\n escapes."""
    assert BS + BS not in pat, pat
    return pat.replace("=", BS + "=").replace("\r", BS + "r").replace("\n", BS + "n")


files = collections.OrderedDict()
stats = collections.Counter()


def put(fname, en, ko):
    if not ko or en == ko:
        return
    if "//" in en or "//" in ko:
        stats["skip_slash"] += 1
        return
    files.setdefault(fname, collections.OrderedDict())[en] = ko


# items
items = J("items.json"); N = J("ko/items_names.json"); D = J("ko/items_desc.json")
for n, d in items:
    put("10_items.txt", n, N[n])
    if d.strip():
        put("10_items.txt", d, D[n])

# ui assets
UIK = J("ui_keys.json"); UIB = J("ko/ui_by_index.json")
for i, en in enumerate(UIK):
    ko = UIB.get(str(i))
    if ko:
        put("20_ui.txt", en, ko)

# settings
A = J("attr_strings.json"); ST = J("ko/settings.json")
for r in A:
    if r["attr"] == "SettingAttribute":
        k = r["where"].split(".")[-1]
        if k in ST:
            put("30_settings.txt", r["args"][0], ST[k][0])
            put("30_settings.txt", r["args"][1], ST[k][1])
for en, ko in ST["__enums"].items():
    put("30_settings.txt", en, ko)

# code strings (plain) and format strings (-> regex)
fmt = []
CK = J("code_keys.json"); CB = J("ko/code_by_index.json")
for i, en in enumerate(CK):
    ko = CB.get(str(i))
    if not ko:
        continue
    if re.search(r"\{\d+(:[^}]*)?\}", en):
        fmt.append((en, ko))
    else:
        put("40_code.txt", en, ko)
for en, ko in J("ko/extra.json").items():
    put("40_code.txt", en, ko)
for en, ko in J("ko/controls.json").items():
    put("50_controls.txt", en, ko)


def fmt_to_regex(en, ko):
    parts = re.split(r"(\{\d+(?::[^}]*)?\})", en)
    pat = "^"
    order = []
    for p in parts:
        m = re.fullmatch(r"\{(\d+)(?::[^}]*)?\}", p)
        if m:
            idx = int(m.group(1))
            if idx in order:
                pat += r"\k<g" + str(idx) + ">"
            else:
                pat += r"(?<g" + str(idx) + r">[^\r\n]+?)"
            order.append(idx)
        else:
            pat += re.escape(p).replace(BS + " ", " ")
    pat += "$"
    if BS + BS in pat:
        return None
    rep = ko
    for n in set(order):
        rep = re.sub(r"\{" + str(n) + r"(?::[^}]*)?\}", "${g" + str(n) + "}", rep)
    return pat, rep


rx = []
for en, ko in fmt:
    r = fmt_to_regex(en, ko)
    if r:
        rx.append(r)
    else:
        stats["fmt_skip"] += 1
for en, ko in [("{0} iterations", "{0}회"), ("{0} seconds", "{0}초"), ("{0} degrees", "{0}도")]:
    rx.append(fmt_to_regex(en, ko))
# XUnity checks splitters from the last one defined to the first: put the most specific (most literal text) last.
rx.sort(key=lambda pr: len(re.sub(r"\(\?<g\d+>[^)]*\)|\\k<g\d+>", "", pr[0])))


def wr(fname, lines):
    with open(os.path.join(OUT, fname), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


# The game's HasTooltipBehaviour trims tooltip text and appends "." unless it already ends with punctuation
# (ItemButton tooltips, UI tooltips, context menu descriptions). Add the punctuated variant of every entry.
PUNCT = (".", "!", "¡", "?", "~", ";", ":", "\"", "'", ")", "]")
all_keys = set(k for d in files.values() for k in d)
for fname, d in files.items():
    extra = collections.OrderedDict()
    for k, v in d.items():
        ks = k.strip()
        if not ks or ks.endswith(PUNCT):
            continue
        kv = ks + "."
        if kv in all_keys or kv in extra:
            continue
        vs = v.strip()
        extra[kv] = vs if vs.endswith(PUNCT) else vs + "."
    stats[fname + " (+마침표)"] = len(extra)
    d.update(extra)

for fname, d in files.items():
    wr(fname, [f"{enc(k)}={enc(v)}" for k, v in d.items()])
    stats[fname] = len(d)
wr("80_formats.txt", [f'sr:"{enc_regex_key(p)}"="{enc(r)}"' for p, r in rx])

# 90_manual.txt (hand-written) plain entries also need the tooltip "." variant -> 95_manual_punct.txt
pass  # validate는 tools/ 에서 가져온다
from validate import decode  # noqa: E402
manual_extra = []
for line in open(os.path.join(OUT, "90_manual.txt"), encoding="utf-8").read().split("\n"):
    if not line.strip() or line.lstrip().startswith("//"):
        continue
    kv = decode(line)
    if not kv or kv[0].startswith(("r:", "sr:")):
        continue
    k, v = kv[0].strip(), kv[1].strip()
    if k.endswith(PUNCT) or (k + ".") in all_keys:
        continue
    manual_extra.append(f"{enc(k + '.')}={enc(v if v.endswith(PUNCT) else v + '.')}")
wr("95_manual_punct.txt", manual_extra)
stats["95_manual_punct.txt"] = len(manual_extra)
stats["80_formats.txt"] = len(rx)
print(dict(stats))
