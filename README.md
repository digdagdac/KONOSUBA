# KONOSUBA
## 개발 기준

- Unity Editor: `6000.0.72f1`
- 렌더링: URP 2D
- 입력: Input System만 사용한다. 레거시 Input Manager와 `UnityEngine.Input` 직접 호출은 사용하지 않는다.
- Addressables: 사용하지 않는다.
- 오디오: 저장소 안의 절차 합성만 사용한다. 외부 녹음·외부 생성 오디오·AI 오디오 에셋은 사용하지 않는다.
- 목표 플랫폼: PC 데스크톱 WebGL. 개발 중 WebGL은 로컬 **Development Build**로 실행하며 `file://`로 열지 않는다.

승인 원문은 `overbless_prd_v0.2_ko.md`
(`21f953935801cb873e8d34dd81bf4bfa35ad1cf840e8d3ed3359b16a9dd8136c`)와
`overbless_ai_resource_production_checklist_v0.1_ko.md`
(`b1b79d6111ce8dd436a6a42b4de5bc4eeba8b63790238ba28736d2e66639708b`)다.

## 범위

| 마일스톤 | 포함 | 제외 |
|---|---|---|
| M0 | Unity/URP 2D/Input System 고정 설정, 제작 규칙, 승인 원문·스타일·오디오 결정, 증거 계약 | 원격 배포와 최종 에셋 |
| M1 | 직접 공격 없는 유도 전투 슬라이스: 이동·대시, 돌진수·궁수·추종자, 과속·거대화, 적 friendly fire, 영혼·출구, 대표 pixel art, 절차 합성 기능 오디오, 로컬 Development WebGL | 메아리·골렘·환경 기믹·잔향·최종전·추가 방 |
| M2+ | 유효한 동일 후보의 사용자 `PASS` 뒤 별도 계획으로 진행 | 사용자 승인 전 구현·활성화·우회 검증 |

`M2EntryGate`는 사용자만 완료할 수 있다. 대화형 창이 준비한 canonical payload를 저장소 밖의 RSA 개인 키로 외부 서명한 뒤 detached signature를 붙여야 하며, 에이전트와 자동 validator는 `PASS`를 대신 기록할 수 없다. 상세 경계는 `Docs/Production/PROJECT_RULES.md`를 따른다.

## 조작 계약

| 입력 | 기능 |
|---|---|
| `WASD` / 방향키 | 이동 |
| `Space` | 대시 |
| `1` / `2` | 과속 / 거대화 축복 선택 |
| 마우스 이동 | 적 확인·대상 지정 |
| 좌클릭 / 우클릭 | 축복 적용 / 선택 취소 |
| `Esc` | 선택 취소 또는 일시정지 |
| `R` | 현재 방 재시작 |

## 프로젝트 열기·검증·로컬 WebGL

아래 PowerShell 예시는 `$env:UNITY_EDITOR`에 Unity `6000.0.72f1`의 `Unity.exe` 경로가 설정되어 있다고 가정한다.

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
    if ($LASTEXITCODE -ne 0) {
        throw "Unity failed with exit code $LASTEXITCODE."
    }
}

function Assert-FreshFile {
    param([string]$Path, [datetime]$NotBeforeUtc, [string]$Label)

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.LastWriteTimeUtc -lt $NotBeforeUtc) {
        throw "$Label was not created by this command sequence: $Path"
    }
}

# 프로젝트 열기
Invoke-UnityChecked @("-projectPath", $projectPath)

# EditMode / PlayMode 테스트: 이전 결과를 제거하고 이번 실행이 만든 XML만 허용한다.
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

# 이전 빌드를 먼저 제거한다. 이번 빌드가 쓴 manifest가 없으면 서버를 시작하지 않는다.
Remove-Item -LiteralPath $buildOutput, ($buildOutput + ".sealed") -Recurse -Force -ErrorAction SilentlyContinue
$buildStartedAt = [DateTime]::UtcNow
Invoke-UnityChecked @(
    "-batchmode", "-nographics", "-quit", "-projectPath", $projectPath,
    "-executeMethod", "Overbless.Editor.Audio.ProceduralAudioGenerator.GenerateAll"
)
Invoke-UnityChecked @(
    "-batchmode", "-nographics", "-quit", "-projectPath", $projectPath,
    "-executeMethod", "Overbless.Editor.Bootstrap.M1ContentBootstrap.CreateForBatchMode"
)
Invoke-UnityChecked @(
    "-batchmode", "-nographics", "-quit", "-projectPath", $projectPath,
    "-executeMethod", "Overbless.Editor.Build.DevelopmentWebGLBuilder.BuildForBatchMode"
)
Assert-FreshFile $buildManifest $buildStartedAt "Development WebGL build manifest"

python Tools/serve_webgl.py $buildOutput --port 8000
if ($LASTEXITCODE -ne 0) {
    throw "Local WebGL server exited with code $LASTEXITCODE."
}
```

WebGL 개발 빌드는 `Builds/M1_GuidedValidation_WebGL`에 생성된다. `file://`로 열지 말고 위 로컬 HTTP 서버를 사용한다. 첫 trusted 입력 전에는 게임·타이머·오디오가 시작되지 않으며, 포커스 복귀 뒤에도 새 입력 gesture가 필요하다.

시각 기준과 M3 이전 아트 제한은 `Docs/Style/STYLE_BIBLE.md`를, 구현·레이어·증거 계약은 `Docs/Production/PROJECT_RULES.md`를 따른다.

게임 시스템 설계·평가는 `Docs/Design/GAME_DESIGN_CHARTER_KO.md`,
`Docs/Design/M1_GAME_SYSTEM_EVALUATION_KO.md`,
`Docs/Design/M1_TUNING_HYPOTHESES.csv`에서 관리한다. 모든 M1 튜닝 수치는 fresh 플레이테스트 전까지 `[PLACEHOLDER]` 가설이며, 이 평가는 사용자 소유 `M2EntryGate` 결정을 대신하지 않는다.