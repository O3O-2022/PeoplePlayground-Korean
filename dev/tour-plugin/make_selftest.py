"""Build BepInEx\\ppgk_selftest_in.txt: every dictionary source string + realistic composite strings."""
import glob, os, re, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "tools"))
from validate import decode  # noqa: E402

S = os.path.dirname(os.path.abspath(__file__))
BS = "\\"
out = []


def flat(s):
    return s.replace(BS, BS + BS).replace("\r", BS + "r").replace("\n", BS + "n").replace("\t", BS + "t")


# 1) every plain dictionary key
for f in sorted(glob.glob(os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "patch", "BepInEx", "Translation", "ko", "Text", "*.txt"))):
    for line in open(f, encoding="utf-8").read().split("\n"):
        if not line.strip() or line.lstrip().startswith("//"):
            continue
        k, v = decode(line)
        if not (k.startswith("r:") or k.startswith("sr:")):
            out.append(k)

# 2) item tooltips exactly as the game builds them
import json
items = json.load(open(os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "dev", "translation", "items.json"), encoding="utf-8"))
for n, d in items:
    if d.strip():
        out.append("<b>" + n + "</b>\r\n" + d)

# 3) composite / formatted samples
out += [
    "<b>2 limbs selected</b>\r\n<color=#FF5555>dead\r\nbleeding\r\nbrain damage\r\n</color>3 bruises\n1 stab wound\n2 gunshot wounds\nbroken bone\n45% burnt\n",
    "<b>14 limbs selected</b>\r\n<color=#FF5555></color>",
    "integrity\r\n25%\n\nblood amount\r\n50%\n\nblood pressure\r\n65%\n\ninternal temp.\r\n37 °C",
    "<i>Life serum</i>\n2L / 5L",
    "<i>Mixture</i>\n3L / 5L",
    "<align=\"center\"><#fff>LIQUID CONTENTS\n</align></color>\n\n50% Human blood\r\n50% Life serum\r\n",
    "<align=center>CURRENT REACTOR DATA</align>\r\n\r\nTEMPERATURE: 350.25°C\nCOOLING PUMPS: 40%\nCONTROL ROD DEPTH: 20%\n\r\nREACTOR IS SUBCRITICAL\nPOWER OUTPUT: 120 E\n- - - -\r\n",
    "Grid size set to 0.5 m", "Angle snap set to 15 degrees", "Copied 12 objects", "Pasted 3 objects",
    "Target temperature in °C (0 to 1000)", "Target height in meters from 0 m to 5 km",
    "Target distance in meters from 1 m to 100 m", "Target delay in seconds from 0.1s to 100s",
    "Value from 1 to 999", "72 BPM", "Press Space to move forward", "Press F",
    "32 iterations", "30 seconds", "15 degrees", "Changed to Powers tab", "Changed to Tools tab",
    "Hovering highlights have been enabled", "Now playing <color=#ff54f9>song01</color>",
    "<b>Human</b> removed", "Drag selected", "Wire selected", "3.5 LITER",
    "Mods caused a 1200ms freeze on load...", "Retrieved 12 subscriptions", "45% downloaded",
    "<b>Firearms</b>\r\nThings that shoot a projectile somehow.",
    "<b>Modded Categories</b>\nCategories added by mods",
    "Some mods caused a 1500ms freeze!\nConsider disabling some mods for faster load times.",
]
dst = os.path.join(os.environ.get("PPG_GAME_DIR", r"D:\Steam\steamapps\common\People Playground"), "BepInEx", "ppgk_selftest_in.txt")
seen = set(); lines = []
for s in out:
    fs = flat(s)
    if fs not in seen:
        seen.add(fs); lines.append(fs)
open(dst, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
print(len(lines), "test lines")
