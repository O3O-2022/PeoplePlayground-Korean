"""번역 파일 검사기.

XUnity.AutoTranslator 가 번역 파일을 읽는 방식(TextHelper.ReadTranslationLineAndDecode)을 그대로 흉내 내어
깨진 줄, 잘못된 정규식, 그룹 이름 불일치를 찾습니다.

사용법: python tools/validate.py [번역 폴더]
기본 폴더: patch/BepInEx/Translation/ko/Text
"""
import glob
import os
import re
import sys

BS = "\\"


def decode(line):
    out = [None, None]; num = 0; flag = False; sb = []
    i = 0; n = len(line)
    while i < n:
        c = line[i]
        if flag:
            if c in "=" + BS: sb.append(c)
            elif c == "n": sb.append("\n")
            elif c == "r": sb.append("\r")
            elif c == "u": sb.append(chr(int(line[i + 1:i + 5], 16))); i += 4
            else: sb.append(BS); sb.append(c)
            flag = False; i += 1; continue
        if c == BS: flag = True
        elif c == "=":
            if num > 1: return None
            out[num] = "".join(sb); num += 1; sb = []
        elif c == "%" and line[i + 1:i + 3] == "3D": sb.append("="); i += 2
        elif c == "/" and line[i + 1:i + 2] == "/":
            out[num] = "".join(sb); num += 1
            return out if num == 2 else None
        else: sb.append(c)
        i += 1
    if num != 1: return None
    out[1] = "".join(sb)
    return out


def strip_quotes(s, prefix):
    if s.startswith(prefix): s = s[len(prefix):]
    a = s.find('"')
    if a != -1:
        s = s[a + 1:s.rfind('"')]
    return s


def to_py(pat):
    """.NET 정규식 문법을 파이썬 문법으로 (이름 있는 그룹, 역참조)."""
    pat = re.sub(r"\(\?<(\w+)>", r"(?P<\1>", pat)
    return re.sub(r"\\k<(\w+)>", r"(?P=\1)", pat)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, "patch", "BepInEx", "Translation", "ko", "Text")
    problems = 0
    for f in sorted(glob.glob(os.path.join(folder, "*.txt"))):
        name = os.path.basename(f); count = 0
        for ln, line in enumerate(open(f, encoding="utf-8").read().split("\n"), 1):
            if not line.strip() or line.lstrip().startswith("//"):
                continue
            r = decode(line)
            if r is None or not r[0] or not r[1]:
                print(f"깨진 줄 {name}:{ln}: {line[:100]}"); problems += 1; continue
            k, v = r; count += 1
            if k.startswith("r:") or k.startswith("sr:"):
                pat = strip_quotes(k, "sr:" if k.startswith("sr:") else "r:")
                rep = strip_quotes(v, "r:")
                try:
                    rx = re.compile(to_py(pat))
                except re.error as e:
                    print(f"정규식 오류 {name}:{ln}: {e}"); problems += 1; continue
                used = set(re.findall(r"\$\{(\w+)\}", rep))
                nums = [int(x) for x in re.findall(r"\$(\d+)", rep)]
                if used - set(rx.groupindex) or (nums and max(nums) > rx.groups):
                    print(f"그룹 불일치 {name}:{ln}: {rep}"); problems += 1
                if '"' in v and not (v.startswith('"') and v.endswith('"')):
                    print(f"값을 따옴표로 감싸야 함 {name}:{ln}"); problems += 1
        print(f"{name}: {count}줄")
    print("문제:", problems)
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
