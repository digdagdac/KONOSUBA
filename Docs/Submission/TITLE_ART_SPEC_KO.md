# 타이틀 화면 키 비주얼 사양

이 문서는 타이틀 화면의 **메인 이미지가 아직 없는 상태**에서 작성됐다. 화면은 이미지 없이도 동작하도록 이미 구현돼 있고, 이미지가 생기면 이 문서의 절차만 따라 교체하면 된다.

- 승인 근거: `Docs/Decisions/CONTEST_SUBMISSION_APPROVAL.json`, `Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json`
- 시각 기준: `Docs/Style/STYLE_BIBLE.md`
- 원문 제목·한 줄 정의: `overbless_prd_v0.2_ko.md`

---

## 1. 현재 상태와 교체 대상

| 슬롯 | 현재 | 교체 후 |
|---|---|---|
| `Title/KeyVisual` | 단색 패널(짙은 남색) | 1920×1080 키 비주얼 1장 |
| `Title/RepresentativePortrait` | 플레이어 픽셀 스프라이트 대역 | 키 비주얼이 들어오면 자동으로 사라진다 |
| 제목 텍스트 | 엔진 텍스트 `OVERBLESS` | 키 비주얼에 한글 로고타입을 넣었다면 숨길 수 있다 |
| 조작 안내·시작 안내 | 엔진 텍스트(영문 대문자) | 그대로 유지 |

`CharacterIdentityCatalog`의 초상화와 같은 원칙이다. 지금은 **대역**임을 코드와 테스트가 명시하고 있고, 실제 아트가 오면 그 상태가 자동으로 바뀐다.

### 한글 글리프 제약 (중요)

엔진 UI는 Unity 내장 `LegacyRuntime.ttf`를 쓴다. 이 폰트에는 **한글 글리프가 없다.** 화면 안의 텍스트를 한글로 바꾸면 두부(□)로 렌더된다. 해결 경로는 세 가지이며 권장 순서대로 적는다.

1. **키 비주얼 이미지에 한글 로고타입을 그려 넣는다(권장).** 폰트 라이선스 문제가 없고, 로고타입은 원래 아트 영역이다.
2. 브라우저가 렌더하는 영역에 한글을 둔다. 제출 빌더가 `index.html`의 `<title>`과 우측 하단 빌드 표기를 《이 멋진 적에게 축복을》로 바꾼다. 브라우저 폰트로 렌더되므로 한글이 정상 표시된다. **이미 적용돼 있다.**
3. 한글 폰트를 저장소에 추가한다. **비권장.** 외부 에셋이 되어 라이선스 명시 의무가 생기고 WebGL 용량도 늘어난다.

---

## 2. 화면 구성 계약

기준 해상도 1920×1080, 최소 1280×720, 16:9 고정. 캔버스는 방 HUD와 동일하게 `ScreenSpaceCamera` + `ScaleWithScreenSize(1920×1080, match 0.5)`다.

```
┌──────────────────────────────────────────────────────────────┐
│  (키 비주얼 전면 배치, 1920×1080, 화면 전체를 덮는다)          │
│                                                              │
│   ┌───────────────────────────┐                              │
│   │ 제목 로고 영역             │   ← 좌측 상단 안전 영역       │
│   │ 부제 한 줄                 │      (x 96~1200, y 120~340) │
│   └───────────────────────────┘                              │
│                                                              │
│                                    ┌───────────────────────┐ │
│                                    │ 인물·장면 초점 영역     │ │
│                                    │ (우측 45~95% 구간)     │ │
│                                    └───────────────────────┘ │
│   ┌──────────────────────────────────────────────┐           │
│   │ 조작 안내 4행 (엔진 텍스트, 반투명 패널 위)   │           │
│   └──────────────────────────────────────────────┘           │
│              CLICK OR PRESS ANY KEY TO START                 │
└──────────────────────────────────────────────────────────────┘
```

이미지 제작 시 반드시 지켜야 하는 사항:

- **좌측 상단 x 96~1200, y 120~340 영역은 저채도·저디테일로 비워둔다.** 제목과 부제가 올라간다. 로고타입을 이미지에 직접 그려 넣는 경우 이 영역 안에 배치한다.
- **하단 y 830~1010 전체 폭은 어둡게 유지한다.** 조작 안내 패널과 시작 안내가 올라간다.
- 인물·초점은 **우측 45~95% 구간**에 둔다.
- 1280×720으로 축소해도 로고타입이 읽히는 굵기를 유지한다.
- 텍스트·로고·워터마크를 임의로 넣지 않는다. 예외는 위에서 허용한 한글 로고타입 하나뿐이다.

---

## 3. 이미지 생성 지시문

`Docs/AI_Usage/prompts/` 규약에 맞춰 프롬프트 원문을 그대로 보존한다. 아래 프롬프트를 수정 없이 사용하고, 수정했다면 수정본을 프롬프트 파일에 새로 기록한다.

### 3.1 공통 제약

```
Original artwork only. Do not reproduce, imitate, or evoke any existing commercial
character, costume, emblem, weapon silhouette, logo, or key-visual composition.
Reject any result whose hair, costume, and colour blocking jointly resemble an
existing published character. No text, no watermark, no signature, no UI mockup.
Output exactly 1920x1080, 16:9, full-bleed illustration.
```

### 3.2 키 비주얼 프롬프트 (`title_key_visual_v001`)

```
Create the title key visual for an original 2D top-down action puzzle game about a
saint who cannot attack and instead overblesses her enemies until they destroy each
other.

Composition: a dungeon hall seen from a slightly high three-quarter angle, cool
dark-navy stone and dust, one warm amber light source low on the right. The left
upper third stays deliberately quiet and low-contrast for a logotype. The lower
strip across the full width falls into shadow.

Subject on the right: Rivella, age 22, an original cynical former saint. Asymmetric
cyan-black hair, amber eyes, a dry half-smile, a broken-halo talisman floating
behind one shoulder, a long black-and-cyan coat with split tails. Her hands are
empty and open in a seal-casting gesture, no weapon. She is calm, not heroic.

Behind and below her, three original silhouetted rivals are mid-attack against each
other rather than against her: a red-lightning charge knight leaning into a straight
dash, a violet eclipse archer holding a crescent greatbow along a horizontal firing
lane, and a small lime-eyed cursed-doll swarm creature. Their attack telegraphs read
as shapes, not colour alone: a thick straight line, a thin dotted line, a circular
area.

Mood: the moment just before her own blessing backfires. Cool palette with cyan,
violet and ember-red accents against dark navy. Painterly cel-anime illustration,
clean readable shapes, restrained dungeon fantasy, no gore.
```

### 3.3 한글 로고타입 포함본 (선택, `title_key_visual_logotype_v001`)

3.2의 결과를 채택한 뒤 로고타입을 요청할 경우에만 사용한다.

```
Add an original Korean logotype reading exactly "이 멋진 적에게 축복을" inside the
quiet upper-left area. Sharp brush-cut strokes with a faint cyan inner glow and a
broken-halo motif replacing one stroke terminal. Keep every glyph legible when the
image is scaled to 1280x720. Do not add any other text, mark, or signature.
```

한글 자형을 정확히 그려내지 못하는 도구가 많다. 결과의 글자 형태가 어긋나면 **채택하지 말고** 3.2의 무텍스트 버전을 쓰고 로고는 별도 작업으로 만든다.

---

## 4. 생성 후 처리 절차

### 4.1 원본 보존

```
Docs/AI_Usage/sources/title_v001/title_key_visual_v001_source.png
```

원본은 그대로 둔다. 크기·색·압축을 바꾸지 않는다. 다른 소스 세트와 같은 규약이다.

### 4.2 런타임 에셋 배치

```
Assets/_Project/Art/M1Production/UI/ui_title_key_visual_a_v001.png
```

| 항목 | 값 |
|---|---|
| 해상도 | 1920×1080 정확히 |
| 형식 | PNG, 8bit/채널, 불투명 |
| 색공간 | sRGB |
| 파일명 | `ui_title_key_visual_a_v001.png` (개정 시 `_v002`) |

### 4.3 임포트 설정

메뉴 `Overbless/Contest/Import Title Key Visual`(`TitleArtBootstrap`)을 실행하면 아래가 자동 적용되고, 1920×1080이 아니면 실패한다. 잘못된 크기가 조용히 들어가는 경로는 없다.

| 설정 | 값 | 이유 |
|---|---|---|
| Texture Type | Sprite (2D and UI) | UI `Image`에 물린다 |
| Sprite Mode | Single | 시트가 아니다 |
| Pixels Per Unit | 1920 | 화면 채움 기준 |
| Filter Mode | Bilinear | 픽셀 아트가 아닌 일러스트다. 방 안의 스프라이트만 Point를 쓴다 |
| Compression | Uncompressed | 그라디언트 밴딩 방지 |
| Generate Mip Maps | 끔 | 화면 고정 배치 |
| Max Size | 2048 | 1920 원본 보존 |
| Alpha Is Transparency | 끔 | 불투명 배경 |

### 4.4 출처 기록 (필수)

`Docs/Production/PROJECT_RULES.md` 5절에 따라 실제 바이트 기준으로 기록한다. 새 파일을 만들고 기존 기록은 건드리지 않는다.

1. `Docs/AI_Usage/prompts/title_visual_prompts_v001.json` — 제출한 프롬프트 원문, 도구 이름·버전, 응답 ID, 요청 해상도.
2. `Docs/AI_Usage/generations/title_key_visual_v001.json` — 아래 항목을 실제 값으로 채운다. 추정치를 넣지 않는다.

```json
{
  "asset": "title_key_visual_v001",
  "toolName": "<도구 이름>",
  "toolVersion": "<버전>",
  "provider": "<제공자>",
  "responseId": "<응답 ID>",
  "prompt": "<제출 원문 그대로>",
  "generationUtc": "<ISO8601 UTC>",
  "requestedDimensions": "1920x1080",
  "actualDimensions": "<실제 결과 크기>",
  "originalPath": "Docs/AI_Usage/sources/title_v001/title_key_visual_v001_source.png",
  "originalSha256": "<실제 바이트 SHA-256>",
  "modifications": [
    { "order": 1, "operation": "<예: resize 2048x1152 to 1920x1080 (Lanczos)>" }
  ],
  "finalPath": "Assets/_Project/Art/M1Production/UI/ui_title_key_visual_a_v001.png",
  "finalSha256": "<실제 바이트 SHA-256>",
  "reviewer": null,
  "reviewState": "review-required"
}
```

3. `Docs/AI_Usage/asset_manifest.csv` — `ui_title_key_visual` 행 추가.
4. `reviewer`는 **사용자만** 채운다. 에이전트는 null로 남긴다.

해시는 다음으로 얻는다.

```powershell
Get-FileHash -Algorithm SHA256 "Docs\AI_Usage\sources\title_v001\title_key_visual_v001_source.png" |
  Select-Object -ExpandProperty Hash
```

### 4.5 배선·검증·재배포

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.72f1\Editor\Unity.exe"
function Run-Unity([string[]]$extra) {
  Start-Process -FilePath $unity -Wait -NoNewWindow -ArgumentList (@(
    "-batchmode","-nographics","-projectPath","." ) + $extra)
}

# 1. 키 비주얼 임포트 설정 적용
Run-Unity @("-quit","-executeMethod","Overbless.Editor.Bootstrap.TitleArtBootstrap.ImportForBatchMode")

# 2. 타이틀·결과 화면 재생성 (키 비주얼이 있으면 자동으로 물린다)
Run-Unity @("-quit","-executeMethod","Overbless.Editor.Bootstrap.M1ContentBootstrap.CreateFlowScreensForBatchMode")

# 3. 검증 (타이틀 대역 상태 단정이 실제 아트 상태 단정으로 자동 전환된다)
Run-Unity @("-runTests","-testPlatform","EditMode","-testResults","Evidence\Verification\editmode-title.xml")

# 4. 제출 빌드 재생성
Run-Unity @("-quit","-executeMethod","Overbless.Editor.Build.ContestWebGLBuilder.BuildForBatchMode")

# 5. 브라우저 실행 확인
python Tools\verify_submission_run.py --build Builds/Overbless_Web --port 8100 `
  --output-directory Evidence/Verification/submission-run

# 6. Pages 브랜치로 재배포
python Tools\publish_gh_pages.py --build Builds/Overbless_Web
git push origin gh-pages
```

### 4.6 대역 상태 해제

`M1ContentBootstrap.CreateOrUpdateFlowScreens`가 키 비주얼 에셋의 존재로 대역 여부를 판단한다. 손으로 바꿀 플래그는 없다.

- 에셋 없음 → 단색 플레이트 + 플레이어 스프라이트 대역
- 에셋 있음 → 키 비주얼 전면 배치, 대역 스프라이트 제거
- 로고타입을 이미지에 넣었다면 `M1ContentBootstrap.HideEngineTitleWhenLogotypeBaked`를 `true`로 바꿔 엔진 제목 텍스트를 숨긴다

EditMode 테스트 `ContestSubmissionTests.TitleScreenStandsInUntilTheKeyVisualIsDelivered`가 두 상태를 모두 검증한다. 이미지를 넣고 재생성하지 않으면 테스트가 실패하므로, 어긋난 상태로 제출되지 않는다.

---

## 5. 아직 남아 있는 아트 항목

| 항목 | 프롬프트 | 출력 경로 |
|---|---|---|
| 리벨라·베라·루메·모코·아트라 초상화 시트 5종, 픽셀 시트 3종 | `Docs/AI_Usage/prompts/m2_character_appeal_prompts_v002.json` | `Docs/AI_Usage/sources/m2_character_appeal_v002/` |
| 타이틀 키 비주얼 | 이 문서 3절 | `Docs/AI_Usage/sources/title_v001/` |

초상화 시트가 들어오면 `CharacterIdentityCatalog`의 `portraitSource`가 `CelPortraitSheet`로 올라가야 하며, `M2CharacterIdentityTests`가 그 전환을 강제한다.
