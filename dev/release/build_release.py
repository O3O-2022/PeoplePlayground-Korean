"""배포 파일을 처음부터 만든다: 배포용 zip + 설치 도우미 exe.

사용법 (저장소 루트에서):
    python dev/release/build_release.py --version 0.2 --game "D:\\Steam\\steamapps\\common\\People Playground"

필요: Python 3, .NET SDK(8 이상), 게임 폴더(플러그인 빌드에 게임 DLL 참조가 필요)
결과: .build/PeoplePlayground-Korean-v<버전>.zip, .build/installer/PeoplePlayground-Korean-Installer.exe
"""
import argparse, os, shutil, subprocess, sys, urllib.request, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BUILD = os.path.join(ROOT, ".build")
CACHE = os.path.join(BUILD, "download")
STAGE = os.path.join(BUILD, "zip")

THIRD_PARTY = [
    ("BepInEx_win_x64_5.4.23.5.zip",
     "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip"),
    ("XUnity.AutoTranslator-BepInEx-5.6.2.zip",
     "https://github.com/bbepis/XUnity.AutoTranslator/releases/download/v5.6.2/XUnity.AutoTranslator-BepInEx-5.6.2.zip"),
]


def rmtree(path):
    """읽기 전용 파일(원드라이브/압축 해제 파일 등)도 지운다."""
    import stat
    def onerror(func, p, _):
        os.chmod(p, stat.S_IWRITE)
        func(p)
    if os.path.exists(path):
        shutil.rmtree(path, onerror=onerror)


def run(cmd):
    print(">", " ".join(cmd))
    subprocess.run(cmd, check=True, cwd=ROOT)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True, help="예: 0.2")
    ap.add_argument("--game", default=os.environ.get("PPG_GAME_DIR", r"D:\Steam\steamapps\common\People Playground"))
    a = ap.parse_args()
    ver = a.version.lstrip("v")

    # 1) 외부 프로그램 받기 (공식 GitHub 릴리스)
    os.makedirs(CACHE, exist_ok=True)
    for name, url in THIRD_PARTY:
        dst = os.path.join(CACHE, name)
        if not os.path.exists(dst):
            print("download", url)
            urllib.request.urlretrieve(url, dst)

    # 2) 스테이징: BepInEx + XUnity + 패치
    rmtree(STAGE)
    os.makedirs(STAGE)
    for name, _ in THIRD_PARTY:
        with zipfile.ZipFile(os.path.join(CACHE, name)) as z:
            z.extractall(STAGE)
    shutil.copytree(os.path.join(ROOT, "patch", "BepInEx"), os.path.join(STAGE, "BepInEx"), dirs_exist_ok=True)

    # 3) 한글 폰트 플러그인 빌드 (디버그 경로가 남지 않게)
    plugin_out = os.path.join(BUILD, "plugin")
    run(["dotnet", "build", "plugin", "-c", "Release", "--no-incremental", "-p:GameDir=" + a.game,
         "-p:DebugType=none", "-p:DebugSymbols=false", "-p:Deterministic=true", "-o", plugin_out])
    dst = os.path.join(STAGE, "BepInEx", "plugins", "PPGKorean")
    os.makedirs(dst, exist_ok=True)
    shutil.copy2(os.path.join(plugin_out, "PPGKoreanFont.dll"), dst)

    # 4) 라이선스, 설치 안내
    lic = os.path.join(STAGE, "licenses")
    os.makedirs(lic, exist_ok=True)
    for f in os.listdir(os.path.join(ROOT, "licenses")):
        shutil.copy2(os.path.join(ROOT, "licenses", f), lic)
    shutil.copy2(os.path.join(ROOT, "LICENSE"), lic)
    shutil.copy2(os.path.join(ROOT, "THIRD_PARTY_NOTICES.md"), lic)
    readme = open(os.path.join(ROOT, "dev", "release", "release_readme.txt"), encoding="utf-8").read()
    open(os.path.join(STAGE, "설치방법.txt"), "w", encoding="utf-8").write(readme.replace("{version}", "v" + ver))

    # 5) zip (한글 파일 이름은 UTF-8 표시로 저장된다)
    zpath = os.path.join(BUILD, "PeoplePlayground-Korean-v%s.zip" % ver)
    if os.path.exists(zpath):
        os.remove(zpath)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(STAGE):
            for f in files:
                full = os.path.join(root, f)
                z.write(full, os.path.relpath(full, STAGE).replace(os.sep, "/"))
    print("zip:", zpath, os.path.getsize(zpath), "bytes")

    # 6) 설치 도우미 (zip을 exe 안에 넣는다)
    run(["dotnet", "build", "installer", "-c", "Release", "-p:PatchVersion=" + ver, "-p:Payload=" + zpath,
         "-o", os.path.join(BUILD, "installer")])
    exe = os.path.join(BUILD, "installer", "PeoplePlayground-Korean-Installer.exe")

    # 7) 개인 정보(사용자 폴더 경로)가 들어가지 않았는지 검사
    user = os.path.basename(os.path.expanduser("~")).encode()
    for path in [zpath, exe, os.path.join(dst, "PPGKoreanFont.dll")]:
        if user and user in open(path, "rb").read():
            sys.exit("경고: %s 안에 사용자 이름이 들어 있습니다" % path)
    print("installer:", exe, os.path.getsize(exe), "bytes")
    print("완료. GitHub 릴리스에 zip과 exe를 올리세요.")


if __name__ == "__main__":
    main()
