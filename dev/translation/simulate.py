"""Rough simulation of XUnity lookup: direct -> r: regex -> sr: splitter (last defined first, recursive)."""
import glob, os, re, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "tools"))
from validate import decode, strip_quotes, to_py  # noqa: E402

S = os.path.dirname(os.path.abspath(__file__))
direct, regexes, splitters = {}, [], []
for f in sorted(glob.glob(os.path.join(os.path.dirname(os.path.dirname(S)), "patch", "BepInEx", "Translation", "ko", "Text", "*.txt"))):
    for line in open(f, encoding="utf-8").read().split("\n"):
        if not line.strip() or line.lstrip().startswith("//"):
            continue
        k, v = decode(line)
        if k.startswith("sr:") or k.startswith("r:"):
            sr = k.startswith("sr:")
            pat = to_py(strip_quotes(k, "sr:" if sr else "r:"))
            (splitters if sr else regexes).append((re.compile(pat), strip_quotes(v, "r:")))
        else:
            direct.setdefault(k, v)
            direct.setdefault(k.strip(), v.strip())


def translatable(t):
    return bool(re.search(r"[A-Za-z]", t))


def lookup(t, depth=0):
    if not t.strip() or not translatable(t):
        return t
    for key in (t, t.strip()):
        if key in direct:
            lead = t[: len(t) - len(t.lstrip())]; trail = t[len(t.rstrip()):]
            return (lead + direct[key] + trail) if key != t else direct[key]
    for rx, rep in regexes:
        m = rx.fullmatch(t) if rx.pattern.startswith("^") else rx.search(t)
        if m:
            out = rep
            for i in range(len(m.groups()), 0, -1):
                out = out.replace("$" + str(i), m.group(i) or "")
            return out
    if depth < 15:
        for rx, rep in reversed(splitters):
            m = rx.match(t)
            if m:
                out = rep
                for name in reversed(list(rx.groupindex)):
                    val = m.group(name)
                    if val is None:
                        out = out.replace("${" + name + "}", ""); continue
                    tr = val if name.endswith("_i") else lookup(val, depth + 1)
                    out = out.replace("${" + name + "}", tr)
                return out
    return t  # untranslated (partial allowed)


samples = [
    "<b>Human</b>\r\nA human being, very fragile.",
    "<b>Blood Tank</b>\r\nA tank that can store 5L of liquid. Activation will cycle to the next pressure mode. A <color=#FF4444>red signal<color=#FFFFFF> will cycle to the previous mode. \n\nPUSH: Force liquid out of the tank\nPULL: Force liquid to flow in the tank\nIDLE: Let other pressure sources decide: liquid will seek equilibrium\nDRAIN: Pull and drain liquid",
    "<b>Firearms</b>\r\nThings that shoot a projectile somehow.",
    "<b>2 limbs selected</b>\r\n<color=#FF5555>dead\r\nbleeding\r\nbrain damage\r\n</color>3 bruises\n1 stab wound\n2 gunshot wounds\n45% burnt\n",
    "integrity\r\n25%\n\nblood amount\r\n50%\n\nblood pressure\r\n65%\n\ninternal temp.\r\n37 °C",
    "Grid size set to 0.5 m",
    "Target temperature in °C (0 to 1000)",
    "Copied 12 objects",
    "<i>Life serum</i>\n2L / 5L",
    "<align=\"center\"><#fff>LIQUID CONTENTS\n</align></color>\n\n50% Human blood\r\n50% Life serum\r\n",
    "<align=center>CURRENT REACTOR DATA</align>\r\n\r\nTEMPERATURE: 350.25°C\nCOOLING PUMPS: 40%\nCONTROL ROD DEPTH: 20%\n\r\nREACTOR IS SUBCRITICAL\nPOWER OUTPUT: 120 E\n- - - -\r\n",
    "Hovering highlights have been enabled",
    "Changed to Powers tab",
    "Press Space to move forward",
    "Now playing <color=#ff54f9>song01</color>",
    "32 iterations",
    "Settings",
    "Set tank to push mode",
]
for s in samples:
    print(repr(s)[:70], "\n   ->", repr(lookup(s)), "\n")
