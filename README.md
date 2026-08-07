# 이 멋진 적에게 축복을 (Overbless)

> **강한 적을 쓰러뜨리는 게임이 아니라, 적을 강하게 만든 책임을 지는 게임.**

직접 공격할 수 없는 축복술사가 적을 실제로 강화하고, 강해진 적의 공격을 다른 적과 환경으로 유도해 살아남는 **2D 탑다운 액션 퍼즐**.

| 항목 | 내용 |
|---|---|
| 장르 | 2D 탑다운 액션 퍼즐 |
| 플랫폼 | PC 데스크톱 웹 브라우저 (Unity WebGL) |
| 플레이 인원 | 싱글 플레이 |
| 목표 플레이 시간 | 10~15분 |
| 기준 해상도 | 1920×1080 (최소 1280×720), 16:9 고정 |
| 엔진 | Unity 6000.0.72f1, URP 2D |

---

## 📦 NAN 2026 제출물

| # | 제출물 | 형태 | 위치 / 링크 | 상태 |
|---|---|---|---|---|
| 1 | 플레이 가능한 빌드 및 소스 코드 | 웹(브라우저 실행) + GitHub 전체 소스 | 플레이: `https://digdagdac.github.io/KONOSUBA/` · 소스: 이 저장소 | 빌드는 `gh-pages` 브랜치 배포 후 활성화 |
| 2 | 플레이 동영상 (30~60초) | YouTube 링크 | *(제출 전 기입)* | 실제 플레이 화면 녹화 예정 |
| 3 | 게임 소개 및 설명 문서 | PDF | 원문: [`Docs/Submission/GAME_OVERVIEW_KO.md`](Docs/Submission/GAME_OVERVIEW_KO.md) | PDF 변환 후 제출 |
| 4 | AI 활용 기술 문서 | PDF | 원문: [`Docs/Submission/AI_USAGE_TECH_KO.md`](Docs/Submission/AI_USAGE_TECH_KO.md) | PDF 변환 후 제출 |
| 5 | 팀원 롤 기술서 | PDF | **개인 참여 — 제출 생략** | 해당 없음 |

- 저장소는 전체 소스 코드와 커밋 기록을 그대로 포함한다. 비공개 유지 시 심사 계정(`dl_gameai_reviewer@nhn.com`)을 초대한다.
- 심사자는 별도 유료 라이선스 없이 링크 클릭만으로 브라우저에서 플레이할 수 있다. PC 실행 파일(.exe)은 제공하지 않는다.
- 제출 배포 승인 기록: [`Docs/Decisions/CONTEST_SUBMISSION_APPROVAL.json`](Docs/Decisions/CONTEST_SUBMISSION_APPROVAL.json)

### 제출용 웹 빌드 배포 절차

제출 빌드(`Overbless.Editor.Build.ContestWebGLBuilder`, Release + Brotli)는 `Builds/Overbless_Web`에 생성되고, `gh-pages` 브랜치로 발행한다.

```powershell
# 제출용 Release WebGL 빌드 생성 후
python Tools/publish_gh_pages.py --build Builds/Overbless_Web
git push origin gh-pages
```

GitHub 저장소 Settings → Pages에서 `gh-pages` 브랜치 루트를 선택하면 `https://digdagdac.github.io/KONOSUBA/`에서 플레이할 수 있다.

---

## 🎮 게임 방법

### 목표

플레이어는 적에게 **피해를 줄 수 없다.** 공격 버튼이 없다. 할 수 있는 일은 적을 **더 강하게 만드는 것**뿐이다.

축복을 받은 적은 실제로 더 빠르고, 더 크고, 더 넓게 공격한다. 강해진 그 공격이 **다른 적에게 맞도록** 위치를 잡고, 마지막 순간에 비켜서면 적이 서로를 처치한다. 적이 쓰러지면 영혼 조각이 떨어진다.

한 방을 통과하는 조건:

1. 영혼 조각 **3개**를 모은다. 영혼은 적이 서로를 처치했을 때만 나온다.
2. 열린 출구로 들어간다.

방은 총 **3개**이며, 방을 지날수록 배우는 것이 늘어난다.

| 방 | 새로 배우는 것 |
|---|---|
| ROOM 01 | 축복 두 종류(과속·거대화)와 유도의 기본 |
| ROOM 02 | 메아리 축복 — 고정된 공격을 잠시 뒤 한 번 그대로 반복 |
| ROOM 03 | 시야를 끊는 고정 기둥 — 경로 선택과 메아리를 함께 사용 |

### 조작

| 입력 | 기능 |
|---|---|
| `W` `A` `S` `D` / 방향키 | 이동 |
| `Space` | 대시 |
| `1` / `2` / `3` | 과속 / 거대화 / 메아리 축복 선택 (메아리는 ROOM 02부터) |
| 마우스 이동 | 적 확인 및 대상 지정 |
| 좌클릭 | 선택한 축복을 지목한 적에게 적용 |
| 우클릭 | 선택 취소 |
| `R` | 현재 방 재시작 |
| `Esc` | 선택 취소 또는 일시정지 |

### 종료 조건

| 결과 | 조건 | 이후 |
|---|---|---|
| 방 통과 | 영혼 3개 수집 후 출구 진입 | 다음 방으로 이동. ROOM 03 통과 시 결과 화면 |
| 완주 | ROOM 03 통과 | `RUN COMPLETE` 결과 화면 → 클릭하면 타이틀로 |
| 패배 | 체력 6 소진 | `R`로 그 방을 처음부터 다시 시작 (횟수 제한 없음) |

---

## 🚀 실행 방법

### 플레이어 (심사자)

별도 설치·계정·유료 라이선스가 필요 없다.

1. 플레이 링크를 최신 데스크톱 **Chrome 또는 Edge**에서 연다.
2. 로딩이 끝나면 타이틀 화면이 나온다. **아무 곳이나 클릭**하면 첫 방으로 들어간다.
3. 방에 들어가면 `CLICK TO BEGIN` 안내가 나온다. 한 번 더 클릭하면 게임이 시작된다.
   (브라우저 정책상 사용자의 실제 입력 전에는 소리·타이머를 시작할 수 없어 방마다 한 번 확인한다.)

권장 환경: 데스크톱 Chrome/Edge 최신 버전, 1280×720 이상, WebGL2 지원. 모바일 브라우저는 대상이 아니다.

### 개발자 (소스에서 빌드)

- Unity Editor `6000.0.72f1` + URP 2D
- 입력: **Input System만** 사용. 레거시 Input Manager와 `UnityEngine.Input` 직접 호출 금지
- Addressables 미사용
- 오디오: 저장소 안의 **절차 합성만** 사용 (외부 녹음·AI 오디오 에셋 금지)
- 개발용 WebGL은 로컬 **Development Build**로 실행하며 `file://`로 열지 않는다

<details>
<summary>전체 검증·로컬 WebGL 실행 스크립트 (PowerShell)</summary>

아래 예시는 `$env:UNITY_EDITOR`에 Unity `6000.0.72f1`의 `Unity.exe` 경로가 설정되어 있다고 가정한다.

```powershell
$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path .).Path
$verificationDirectory = Join-Path $projectPath "Evidence\Verification"
$editModeResults = Join-Path $verificationDirectory "editmode-results.xml"
$playModeResults = Join-Path $verificationDirectory "playmode-results.xml"
$buildOutput = Join-Path $projectPath "Builds\M1_GuidedValidation_WebGL"
$buildManifest = Join-Path ($buildOutput + ".sealed") "build-manifest.json"

function Invoke-UnityChecked {
    param([string[]]$UnityArguments)
    & $env:UNITY_EDITOR @UnityArguments
    if ($LASTEXITCODE -ne 0) { throw "Unity failed with exit code $LASTEXITCODE." }
}

function Assert-FreshFile {
    param([string]$Path, [datetime]$NotBeforeUtc, [string]$Label)
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.LastWriteTimeUtc -lt $NotBeforeUtc) {
        throw "$Label was not created by this command sequence: $Path"
    }
}

# 절차 오디오·생성 콘텐츠를 테스트보다 먼저 만든다.
Invoke-UnityChecked @(
    "-batchmode", "-nographics", "-quit", "-projectPath", $projectPath,
    "-executeMethod", "Overbless.Editor.Audio.ProceduralAudioGenerator.GenerateAll"
)
Invoke-UnityChecked @(
    "-batchmode", "-nographics", "-quit", "-projectPath", $projectPath,
    "-executeMethod", "Overbless.Editor.Bootstrap.M1ContentBootstrap.CreateForBatchMode"
)

# EditMode / PlayMode 테스트
New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
Remove-Item -LiteralPath $editModeResults, $playModeResults -Force -ErrorAction SilentlyContinue
$testsStartedAt = [DateTime]::UtcNow
Invoke-UnityChecked @(
    "-batchmode", "-nographics", "-projectPath", $projectPath, "-runTests",
    "-testPlatform", "EditMode", "-testResults", $editModeResults
)
Assert-FreshFile $editModeResults $testsStartedAt "EditMode test result"
Invoke-UnityChecked @(
    "-batchmode", "-nographics", "-projectPath", $projectPath, "-runTests",
    "-testPlatform", "PlayMode", "-testResults", $playModeResults
)
Assert-FreshFile $playModeResults $testsStartedAt "PlayMode test result"

# 개발용 WebGL 빌드 후 로컬 HTTP 서버 실행
Remove-Item -LiteralPath $buildOutput, ($buildOutput + ".sealed") -Recurse -Force -ErrorAction SilentlyContinue
$buildStartedAt = [DateTime]::UtcNow
Invoke-UnityChecked @(
    "-batchmode", "-nographics", "-quit", "-projectPath", $projectPath,
    "-executeMethod", "Overbless.Editor.Build.DevelopmentWebGLBuilder.BuildForBatchMode"
)
Assert-FreshFile $buildManifest $buildStartedAt "Development WebGL build manifest"

python Tools/serve_webgl.py $buildOutput --port 8000
```

</details>

---

## 🤖 AI 활용 요약

상세 내역은 [`Docs/Submission/AI_USAGE_TECH_KO.md`](Docs/Submission/AI_USAGE_TECH_KO.md)에 정리되어 있다.

| 영역 | AI 사용 | 결과물 |
|---|---|---|
| 코드 구현 | 대화형 코딩 에이전트로 설계·구현·테스트·검증 | 런타임 43개 소스, 에디터 도구 24개, 자동 테스트 75건 |
| 이미지 | 텍스트-이미지 생성 모델 | 8방향 픽셀 애니메이션, 스프라이트, UI, 텔레그래프 |
| 오디오 | **AI 미사용** — 저장소 안의 결정론적 절차 합성만 사용 | 기능음 10종 WAV |
| 문서·설계 | 에이전트가 PRD 기반 설계 문서·평가·튜닝 가설 작성 | 설계 헌장, 시스템 평가, 튜닝 가설 61행 |

**출처 원칙:** 모든 생성물은 프롬프트 원문·도구·시드·생성 시각·SHA-256·수정 내역·검토 상태를 [`Docs/AI_Usage/`](Docs/AI_Usage/)에 파일로 남긴다. 외부 스토어·라이브러리 에셋은 **0건**이며, 외부 에셋·오픈소스 출처는 AI 활용 기술 문서 5장에 명시했다.

---

## 📁 저장소 구조

| 경로 | 내용 |
|---|---|
| `Assets/_Project/` | 게임 런타임·에디터 소스, 테스트, 생성 에셋 |
| `Docs/Submission/` | 제출 문서 원문 (게임 소개, AI 활용 기술 문서, 타이틀 아트 사양) |
| `Docs/AI_Usage/` | AI 생성 증거: 프롬프트, 생성 기록, 수정 내역, 에셋 매니페스트 |
| `Docs/Design/` | 게임 설계 헌장, 시스템 평가, 튜닝 가설, 플레이테스트 팩 |
| `Docs/Decisions/` | 사용자 승인 결정 기록 (JSON, 승인 후 불변) |
| `Docs/Production/PROJECT_RULES.md` | 구현·레이어·증거 계약 |
| `Docs/Style/STYLE_BIBLE.md` | 시각 기준 |
| `Tools/` | 빌드 서빙, gh-pages 발행, 아트 파이프라인, 브라우저 자동 검증 스크립트 (Python 표준 라이브러리만 사용) |
| `overbless_prd_v0.2_ko.md` | 승인 원문 PRD |
| `overbless_ai_resource_production_checklist_v0.1_ko.md` | AI 리소스 제작 체크리스트 승인 원문 |

승인 원문 해시:
- `overbless_prd_v0.2_ko.md` — `21f953935801cb873e8d34dd81bf4bfa35ad1cf840e8d3ed3359b16a9dd8136c`
- `overbless_ai_resource_production_checklist_v0.1_ko.md` — `b1b79d6111ce8dd436a6a42b4de5bc4eeba8b63790238ba28736d2e66639708b`

---

## ✅ 검증 현황

| 항목 | 결과 |
|---|---|
| EditMode 테스트 | 49건 통과 |
| PlayMode 테스트 | 26건 통과 |
| 제출 WebGL 빌드 | Release, Brotli 압축, 총 20.7MB |
| 브라우저 실행 검증 | headless Chrome에서 타이틀 → 첫 방 → 입력 반영까지 4단계 스크린샷 확인 |

수치 밸런스는 아직 플레이테스트 전 가설이며, `Docs/Design/M1_TUNING_HYPOTHESES.csv`에 근거·시험 범위·파손 조건·측정 방법을 기록해 두었다.

## ⚠️ 유의사항 (원작 관계)

저장소 이름은 초기 습작 흔적으로 `KONOSUBA`이지만, 게임의 제목·설정·캐릭터·아트는 기존 상업 작품과 무관한 **창작물**이다. 모든 이미지 프롬프트에 기존 캐릭터·의상·엠블럼·구도의 모방 금지를 명시했고, 캐릭터 4인(리벨라·베라·루메·모코)의 설정은 `Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json`에 원본 설정으로 기록되어 있다.
