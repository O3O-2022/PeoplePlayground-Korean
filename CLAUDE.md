# CLAUDE.md — 작업 인수인계

이 저장소는 **People Playground 비공식 한국어 패치**다. 여러 기기에서 이어서 작업하므로, 필요한 것은 모두 이 저장소에 있어야 한다.

## 작업 규칙

- 사용자와는 **한국어**로 대화한다. 번역 품질(자연스러움)이 속도보다 중요하다.
- 사용자가 쓸 도구는 한국어 GUI로, 단계가 단순하고 진행률이 보이게 만든다.
- **이 폴더가 없는 기기라면** `git clone https://github.com/O3O-2022/PeoplePlayground-Korean` 으로 받아서 그대로 이어서 작업한다.
- 게임을 자동으로 조작할 때(아이템 생성, 촬영, 전수 조사)는 **게임 시간을 멈추고 소리를 끈다**. 렉과 소음이 생기지 않게 하고, 끝나면 생성물을 지운다.
- 화면 캡처는 데스크톱 전체가 아니라 **게임 화면만** 찍는다 (`dev/tour-plugin`의 `shot`). 사용자의 다른 창이 찍히면 안 된다.
- 공개 문서에는 사용자 개인 정보(사용자 폴더 경로 등)나 특정 모드를 겨냥한 내용을 쓰지 않는다.
- 게임을 끄고 켜야 할 때는 프로세스가 완전히 끝난 뒤에 파일을 바꾼다 (`WaitForExit`). 실행 중에는 DLL이 잠겨 있다.

## 구조

| 경로 | 내용 |
|---|---|
| `patch/BepInEx/Translation/ko/Text/` | XUnity 번역 사전 (배포되는 결과물) |
| `patch/.../90_manual.txt` | **사람이 직접 고치는** 조합 문장 규칙 (sr:/r:) |
| `dev/translation/ko/*.json` | **번역 원본**. 아이템·UI·코드·설정 번역은 여기서 고친다 |
| `dev/translation/*.json` | 게임에서 추출한 영어 원문 목록 (번역 원본의 키) |
| `dev/translation/build_xunity.py` | 번역 원본으로 `patch/` 사전 파일을 생성한다 |
| `dev/translation/simulate.py` | XUnity 조회를 흉내 내 조합 문장을 시험한다 |
| `plugin/` | 한글 폰트 플러그인 (맑은 고딕을 TMP 대체 폰트로 등록, 미번역 수집, 자가 검사) |
| `installer/` | 설치 도우미 GUI (WinForms, .NET Framework 4.8, 배포 zip을 exe에 넣음) |
| `dev/release/build_release.py` | 배포 zip + 설치 도우미 exe를 처음부터 만든다 |
| `dev/tour-plugin/` | 개발용 BepInEx 플러그인: 스크립트로 게임 UI 조작, 촬영, 전수 조사 (배포 안 함) |
| `dev/extract/` | 게임 업데이트 뒤 번역 대상 문자열을 다시 뽑는 도구 |
| `tools/validate.py` | 사전 파일 검사 (XUnity 파서와 같은 방식) |

## 자주 하는 작업

**번역 고치기**

```bash
# 1) dev/translation/ko/*.json 또는 patch/.../90_manual.txt 수정
python dev/translation/build_xunity.py
python tools/validate.py
# 2) 게임에 반영: patch/BepInEx/Translation/ko/Text/*.txt 를 게임폴더\BepInEx\Translation\ko\Text\ 에 복사 → 게임에서 ALT+R
```

**배포**

```bash
python dev/release/build_release.py --version 0.3 --game "<게임 폴더>"
```

- 이 명령이 `.build/PeoplePlayground-Korean-v0.3.zip`과 `.build/installer/PeoplePlayground-Korean-Installer.exe`를 만든다.
- 커밋하고 푸시한 뒤, GitHub에서 Releases → Draft a new release로 태그를 만들고 zip과 exe를 첨부한다.
- 브라우저 업로드 도구는 세션이 읽을 수 있는 폴더의 파일만 올릴 수 있으니, 먼저 작업 폴더로 복사한다.
- 설치 도우미의 버전 표시는 `--version` 값에서 자동으로 들어간다.

**게임 안 검증** (`dev/tour-plugin`)

- 빌드한 `PPGKoreanTour.dll`을 `게임폴더\BepInEx\plugins\PPGKoreanTour\`에 넣는다.
- `게임폴더\BepInEx\ppgk_tour.txt`에 명령을 적고, `ppgk_tour.flag` 파일을 만들면 실행된다. 결과는 `BepInEx\tour\`, 기록은 `ppgk_tour_log.txt`에 남는다.
- 명령 목록:

| 명령 | 동작 |
|---|---|
| `click <글자>` | 화면 글자로 버튼 클릭 |
| `click name:<오브젝트>` | 오브젝트 이름으로 버튼 클릭 |
| `hover` | 마우스 올리기 |
| `spawnc <dx> <dy> <아이템>` | 카메라 기준 위치에 아이템 생성 |
| `select <이름>` | 물체 선택 |
| `context <x> <y>` | 우클릭 메뉴 열기 |
| `scrollmenu 0` | 우클릭 메뉴 맨 아래로 스크롤 |
| `category <n>` | n번째 카테고리 선택 |
| `lang en\|ko` | 번역 끄기/켜기 (ALT+T) |
| `shot <이름>` | 게임 화면 캡처 |
| `sweep` | 모든 툴팁과 우클릭 메뉴 전수 조사 → `sweep.tsv` |
| `unpause` | 시간 정지 풀기 |
| `reload` | 번역 다시 불러오기 |
| `call <타입> <메서드> [인수]` | 게임 메서드 호출 |

- **sweep 전에는 반드시 시간을 멈추고 음소거한다.**
- `dev/tour-plugin/make_compare.py`는 README용 전/후 비교 이미지를, `make_selftest.py`는 자가 검사 입력을 만든다. 폰트 플러그인에 `ppgk_selftest.flag`를 주면 XUnity로 전체를 번역해 본다.

## 꼭 알아야 할 함정

- **툴팁 마침표**: 게임(`HasTooltipBehaviour.AppendPunctuation`)이 툴팁을 Trim한 뒤, 끝에 문장부호가 없으면 `.`를 붙인다. 그래서 `build_xunity.py`가 모든 문장의 `.`판을 자동으로 추가한다.
- **XUnity 파일 형식**:
  - 한 줄에 `키=값`이다. `=`는 `\=`, 줄바꿈은 `\n`으로 적는다.
  - `//`부터는 주석이라 번역문에 `//`를 쓰지 않는다.
  - 값에 이스케이프 안 된 `=`가 있으면 그 줄 전체가 무시된다.
  - 정규식 줄의 값은 반드시 `"..."`로 감싼다.
- **분할 정규식(sr:)**: 나중에 정의된 것이 먼저 검사된다. 그래서 범용 줄 분할은 `90_manual.txt` 맨 위에, 구체적인 규칙은 아래에 둔다. 분할된 조각은 사전에서 먼저 찾는다(정규식은 재귀로 적용).
- **Windows 줄바꿈**: 게임의 `AppendLine`과 `Environment.NewLine`은 `\r\n`이므로 정규식에서는 `\r?\n`을 쓴다.
- **Bash 히어독 역슬래시**: 역슬래시가 줄어들 수 있다. 정규식이나 C# 파일은 파일 쓰기 도구로 직접 작성한다.
- **폰트**: TMP 3.0은 OS 폰트를 직접 못 읽는다. 플러그인이 `FontEngine.LoadFontFace(Font)`를 가로채 malgun.ttf 바이트를 넘긴다. 한글 줄바꿈은 `TMP_Settings.useModernHangulLineBreakingRules = true`로 단어 단위로 한다.
- **안전 모드**: 게임 1.27.18 빌드는 코드에 안전 모드가 고정되어 있어 창작마당 모드가 불러와지지 않는다. 우리 패치는 BepInEx라 영향이 없다.
- **릴리스 DLL**: `-p:DebugType=none --no-incremental`로 빌드해야 PDB 경로(사용자 이름)가 안 들어간다. `build_release.py`가 이것까지 검사한다.

## 현재 상태

- v0.2 배포됨. 아이템 290여 개의 툴팁과 우클릭 메뉴 약 4,400개를 게임 안에서 전수 조사해, 고유명사 말고는 모두 한국어인 것을 확인했다.
- README에 전/후 비교 10장, 설치 도우미 GUI가 있다.

## 다음 작업 후보 (사용자 요청)

1. **AI 건축 대결 모드** (새 저장소로)
   - 플레이어와 AI가 같은 과제(예: 벽돌 멀리 날리기, 높이 올리기, 사람 보호)에 맞춰 기계를 만들고, 동시에 작동시켜 점수를 비교한다.
   - AI는 부품 조합을 게임 물리로 직접 시험해 가장 좋은 것을 고르는 방식을 제안했고, 사용자 확인을 기다리는 중이다.
   - BepInEx 플러그인으로 만든다 (게임 안전 모드 때문).
2. **물리 최적화 모드**
   - `Physics2D.jobOptions.useMultithreading`은 Unity 2020.3에 있지만 게임이 켜지 않는다. 켜는 작은 모드를 만들고, 물체 수별로 FPS를 측정해 효과를 확인한다.
   - GPU 물리엔진으로 교체하는 것은 불가능에 가깝다.
3. **창작마당 모드 텍스트 번역**: XUnity CustomTranslate 엔드포인트와 로컬 LLM을 연결하는 아이디어.
