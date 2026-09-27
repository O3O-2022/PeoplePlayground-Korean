# 개발 도구

번역을 고치거나 새 버전을 배포하는 사람을 위한 폴더입니다. 자세한 작업 흐름은 저장소 맨 위의 [CLAUDE.md](../CLAUDE.md)에 있습니다.

| 폴더 | 용도 |
|---|---|
| `translation/` | 번역 원본(JSON)과 사전 생성 스크립트 |
| `release/` | 배포 zip과 설치 도우미 exe 빌드 |
| `tour-plugin/` | 게임 안 자동 조작, 촬영, 전수 조사용 개발 플러그인 (배포하지 않음) |
| `extract/` | 게임 업데이트 뒤 번역 대상 문자열 다시 추출 |

## 준비물

- Python 3.10 이상
- Pillow: 비교 이미지용, `pip install pillow`
- UnityPy, TypeTreeGeneratorAPI: 에셋 추출용, `pip install UnityPy TypeTreeGeneratorAPI`
- .NET SDK 8 이상
- People Playground (BepInEx 설치된 상태)

게임 폴더가 기본값(`D:\Steam\steamapps\common\People Playground`)과 다르면 환경 변수 `PPG_GAME_DIR`나 `--game` 인수로 알려 주세요.

## 게임이 업데이트되면

1. `dotnet run -c Release --project dev/extract/strings-tool -- "<게임 폴더>" out` 을 실행하면 코드 문자열과 속성 문자열이 나옵니다.
2. `python dev/extract/extract_assets.py` 를 실행하면 에셋 텍스트(아이템, UI)가 나옵니다.
3. 새로 생긴 영어 문장을 `translation/` 원본에 추가하고 번역한 뒤, `build_xunity.py`를 실행합니다.
4. 게임 안에서 `sweep`(tour-plugin)으로 빠진 문장이 없는지 확인합니다.
