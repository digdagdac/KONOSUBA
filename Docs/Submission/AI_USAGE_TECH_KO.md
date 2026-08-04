# AI 활용 기술 문서

《이 멋진 적에게 축복을》 / Overbless — NAN 2026 제출용

이 문서는 이 프로젝트에서 AI를 **어디에, 어떤 방식으로, 어떤 증거를 남기며** 사용했는지 기술한다. 저장소 안의 기계 검증 가능한 기록을 그대로 인용하며, 문서에서 주장하는 내용은 모두 저장소에서 확인할 수 있다.

---

## 1. 요약

| 영역 | AI 사용 | 결과물 |
|---|---|---|
| 코드 구현 | 대화형 코딩 에이전트로 설계·구현·테스트·검증 수행 | 런타임 43개 소스, 에디터 도구 24개, 자동 테스트 75건 |
| 이미지 | 텍스트-이미지 생성 모델로 픽셀 스프라이트·8방향 애니메이션·UI·VFX 원본 생성 | 캐릭터 8방향 모션 시트, 스프라이트, 아이콘, 텔레그래프 |
| 오디오 | **AI 미사용.** 저장소 안의 결정론적 절차 합성만 사용 | 기능음 10종 WAV |
| 문서·설계 | 에이전트가 PRD 기반으로 설계 문서·평가·튜닝 가설 작성 | 설계 헌장, 시스템 평가, 튜닝 가설 61행 |

핵심 원칙은 두 가지였다.

1. **생성물은 전부 출처를 남긴다.** 프롬프트 원문, 도구·버전, 시드, 생성 시각, 원본·최종 경로와 실제 바이트의 SHA-256, 순서가 있는 수정 내역, 검토 상태를 파일로 기록한다.
2. **사람이 판단할 것을 AI가 대신 기록하지 않는다.** 사람의 승인·검토가 필요한 항목은 `reviewer: null` 또는 `pending-user-gate`로 남아 있다.

---

## 2. 코드에서의 AI 활용 구조

### 2.1 작업 방식

대화형 에이전트가 다음 순환을 반복했다.

```
승인 원문(PRD) 확인 → 계약 정의 → 테스트 작성 → 구현
  → Unity 배치 모드로 EditMode/PlayMode 실행 → 실패 원인 분석 → 수정
  → 증거 기록(XML·해시·스크린샷) → 문서 갱신 → 커밋
```

검증은 사람의 눈이 아니라 **실행 결과**를 기준으로 삼았다. 예를 들어 다음 명령이 매 변경마다 실행됐다.

```powershell
Unity.exe -batchmode -nographics -projectPath . -runTests `
  -testPlatform EditMode -testResults Evidence\Verification\editmode-results.xml
```

### 2.2 AI가 만든 구조 중 설명이 필요한 것

**생성 콘텐츠 부트스트랩.** 씬·프리팹·데이터 에셋을 손으로 만들지 않고 코드가 만든다. `Assets/_Project/Editor/Bootstrap/M1ContentBootstrap.cs`가 5개 씬 전체를 결정론적으로 생성한다. 씬을 재현 가능한 산출물로 두면, AI가 만든 배치가 사람이 읽을 수 있는 코드로 남고 재생성으로 검증된다.

**공격 계약.** 모든 적 공격은 `예고 → 방향·범위 고정 → 실행 → 회복`을 지키고, 고정 후에는 재추적하지 않는다(`AttackStateMachine`). 이 계약이 게임의 유도 플레이가 성립하는 근거이며, `MinimumWarningDuration = 0.55초`는 가독성 하한으로 코드에 고정돼 있다.

**적 AI.** 세 종류의 적이 각자 습관을 가진다. 돌진수는 직선 가속 후 조향하지 않고(`DasherAI`), 궁수는 거리를 유지하며 고정된 레인으로 투사체를 보내고(`ArcherAI`), 추종자는 짧은 예고로 근접 타격한다(`MinionAI`). 세 AI 모두 공통 기반(`EnemyBase`)의 장애물 스윕·피해 스윕을 공유하며, 프레임당 힙 할당이 없도록 버퍼를 재사용한다.

**소유권 분리.** 입력은 `PlayerInputRouter`만 읽고, 이동·대시·공격 상태·피해·사망은 각각 명시적 소유자가 처리한다. 한 시스템이 다른 시스템의 내부 상태를 직접 수정하지 않는다. 이 규칙이 AI가 코드를 확장할 때 회귀를 만들지 않는 방어선이었다.

**검증 자동화.** 브라우저 실행 검증까지 자동화했다. `Tools/capture_webgl_visuals.py`와 `Tools/verify_submission_run.py`는 표준 라이브러리만으로 Chrome DevTools Protocol을 직접 구현해(수동 WebSocket 프레이밍 포함) 로컬 서버 → headless Chrome → 신뢰 클릭 → 입력 주입 → 스크린샷·해시까지 수행한다. 즉 "브라우저에서 실제로 돌아간다"는 주장도 기계 증거로 남는다.

### 2.3 AI 사용에서 실제로 문제가 됐던 것

문서화 가치가 있는 실패 사례를 남긴다.

| 증상 | 원인 | 대응 |
|---|---|---|
| 타이밍 계측이 실행마다 약 20% 흔들림 | 캡처 스텝(1/60초)이 물리 고정 스텝(0.02초)과 어긋나 `Rigidbody2D` 위치가 `transform`에 6프레임마다 1번 늦게 반영 | 캡처 스텝을 고정 스텝에 정렬해 결정론 확보 |
| 카드 UI가 첫 프레임에 잘못 표시 | 씬 로드 프레임의 `Time.deltaTime`이 이미 확정된 뒤 시작 게이트가 `timeScale`을 0으로 만들어, 적이 1틱 실행됨 | 표시를 신뢰 입력 게이트 이후로 제한 |
| 프레젠터가 적 상태를 읽지 못함 | 적이 `Awake`에서 공격 상태 기계를 만드는데 프레젠터가 같은 시점에 참조 | 초기화를 `Start`로 이동 |
| 해시 기반 출처 검증이 브랜치 전환마다 깨짐 | `core.autocrlf=true`가 LF 블롭을 CRLF로 풀어 바이트가 달라짐 | `checkout-index`로 바이트 복원 절차 정립 |

---

## 3. 이미지 생성

### 3.1 사용 도구

| 도구 | 버전 | 용도 |
|---|---|---|
| `god-tibo-imagen` | 0.3.1 | 픽셀 스프라이트, 8방향 모션 시트, UI·VFX 원본 |
| `gpt-5.4` | — | 프롬프트 구성 및 결과 판정 보조 |
| `private-codex` | — | 생성 요청 실행 제공자 |

### 3.2 프롬프트 원문 보관

제출한 프롬프트는 요약하지 않고 원문 그대로 저장한다.

| 파일 | 내용 |
|---|---|
| `Docs/AI_Usage/prompts/monster_directional_animation_prompts_v002.json` | 3종 몬스터 × 5방향 모션 시트 |
| `Docs/AI_Usage/prompts/m1_unity_sprite_prompts_v001.json` | M1 런타임 스프라이트 |
| `Docs/AI_Usage/prompts/m1_directional_animation_prompts_v001.json` | 초기 방향 애니메이션 |
| `Docs/AI_Usage/prompts/m1_combat_visual_prompts_v001.json` | 전투 시각 요소 |
| `Docs/AI_Usage/prompts/m2_image_resource_prompts_v001.json` | M2 이미지 자원 |
| `Docs/AI_Usage/prompts/m2_runtime_visual_prompts_v002.json` | M2 런타임 비주얼 |
| `Docs/AI_Usage/prompts/m2_character_appeal_prompts_v002.json` | 캐릭터 초상화·픽셀 시트(미생성) |
| `Docs/AI_Usage/prompts/visual_reference_prompts_v001.json` | 스타일 참조 보드 |

### 3.3 대표 프롬프트

모든 프롬프트에 공통으로 넣은 원본성 제약이다.

```
Original artwork only. Do not reproduce, imitate, or evoke any existing commercial
character, costume, emblem, weapon silhouette, logo, or key-visual composition.
Reject any result whose hair, costume, and colour blocking jointly resemble an
existing published character. No text, no watermark, no signature.
```

결정론적 추출을 위해 배경 규약을 프롬프트에 강제했다.

```
Production-ready 2D pixel art for a fixed-camera three-quarter top-down dungeon game.
Four-head-tall adult proportions, one 128x128 source frame, crisp hard pixel clusters,
dark navy outline, no antialiasing, no blur, full body centered on flat pure magenta
#ff00ff with generous border. Separate every panel with wide flat pure magenta gutters.
```

마젠타 거터 규약 덕분에 후처리(`Tools/process_*.py`)가 패널 경계를 추정하지 않고 정확히 잘라낸다.

### 3.4 후처리 파이프라인

```
프롬프트 → 생성 원본 PNG(Docs/AI_Usage/sources/**)
  → 마젠타 거터 기준 패널 분할 · 투명화 · 색 수 검사(Tools/process_*.py)
  → Assets/_Project/Art/** 배치 및 임포터 설정 고정(Editor/Bootstrap/*)
  → 인덱스 색·토폴로지·프레임 수를 EditMode 테스트로 검증
```

원본과 최종 파일을 모두 보존하고 각각의 SHA-256을 기록한다. 검증은 눈이 아니라 테스트가 한다. 예를 들어 몬스터 애니메이션은 프레임 수, 초당 프레임, 인덱스 색상 수, 아틀라스 토폴로지까지 단정한다.

### 3.5 아직 생성되지 않은 이미지

정직하게 남긴다. 아래 두 항목은 프롬프트와 배선 절차까지 준비됐지만 **이미지가 없다.**

| 항목 | 현재 대역 | 문서 |
|---|---|---|
| 캐릭터 셀 초상화 시트 | 각 캐릭터의 픽셀 전투 스프라이트 | `Docs/AI_Usage/prompts/m2_character_appeal_prompts_v002.json` |
| 타이틀 키 비주얼 | 단색 플레이트 + 플레이어 스프라이트 | `Docs/Submission/TITLE_ART_SPEC_KO.md` |

두 경우 모두 **데이터가 대역임을 스스로 선언**하고, 실제 아트가 들어오면 EditMode 테스트가 전환을 강제한다(`portraitSource: RepresentativeCombatSprite` → `CelPortraitSheet`).

---

## 4. 오디오: AI를 쓰지 않았다

오디오는 외부 녹음·외부 생성 오디오·AI 오디오 에셋을 모두 금지하고, 저장소 안의 결정론적 절차 합성만 사용한다(`Docs/Decisions/OPEN_A3_AUDIO.json`).

`Assets/_Project/Editor/Audio/ProceduralAudioGenerator.cs`가 사인파와 시드 고정 노이즈로 10종의 기능음을 합성한다. 각 음은 시드가 고정돼 있어 재생성하면 **바이트까지 동일**하다.

| 이벤트 | 이벤트 | 이벤트 |
|---|---|---|
| `DasherReady` | `ArcherReady` | `AttackLocked` |
| `PlayerHit` | `SoulCollected` | `ExitOpened` |
| `BlessingApplied` | `BlessingRejected` | `EnemyDefeated` |
| `FriendlyFireKill` | | |

각 WAV마다 `Docs/AI_Usage/generations/<이벤트>.json`에 생성기 소스 SHA-256, Unity 버전, 시드, 파라미터, 원본·최종 경로와 해시, 수정 내역, 검토자 상태를 기록한다.

---

## 5. 외부 에셋 및 오픈소스 출처

### 5.1 엔진과 패키지

| 항목 | 버전 | 라이선스·근거 |
|---|---|---|
| Unity Editor | 6000.0.72f1 | Unity 이용약관에 따른 개인/무료 라이선스 사용. 빌드 산출물에 Unity 로고·"Made with Unity" 표기 유지 |
| Universal Render Pipeline (URP 2D) | Unity 패키지 | Unity Companion License |
| Input System | Unity 패키지 | Unity Companion License |
| Test Framework | Unity 패키지 | Unity Companion License |
| `LegacyRuntime.ttf` | Unity 내장 | Unity가 엔진과 함께 제공하는 내장 폰트. 별도 파일을 저장소에 포함하지 않음 |
| Unity WebGL 템플릿 | Unity 내장 | 제출 빌드에서 페이지 스타일과 표기만 수정 |

### 5.2 게임 에셋

| 종류 | 출처 | 비고 |
|---|---|---|
| 캐릭터·환경·UI·VFX 이미지 | 본 프로젝트에서 AI로 생성 | 프롬프트·시드·해시 전량 기록 |
| 오디오 | 본 프로젝트에서 절차 합성 | 외부 오디오 0건 |
| 폰트 | Unity 내장 | 외부 폰트 0건 |
| 스토어·라이브러리 에셋 | **없음** | 외부 유료·무료 에셋 미사용 |

### 5.3 도구 코드

`Tools/*.py`는 모두 이 프로젝트에서 작성했고 Python 표준 라이브러리만 사용한다. 외부 파이썬 패키지 의존이 없다(WebSocket 클라이언트도 직접 구현).

### 5.4 원작 관계 명시

저장소 이름은 초기 습작 흔적으로 `KONOSUBA`지만, 게임의 제목·설정·캐릭터·아트는 기존 상업 작품과 무관한 창작물이다. 모든 이미지 프롬프트에 기존 캐릭터·의상·엠블럼·구도의 모방 금지를 명시했고, 캐릭터 4인(리벨라·베라·루메·모코)의 설정은 `Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json`에 원본 설정으로 기록돼 있다.

---

## 6. 증거 체계

AI 사용 내역을 사후에 검증할 수 있게 만든 장치다.

| 위치 | 내용 |
|---|---|
| `Docs/AI_Usage/prompts/` | 제출한 프롬프트 원문 |
| `Docs/AI_Usage/generations/` | 생성 기록: 도구·버전·시드·시각·경로·SHA-256·수정 내역·검토 상태 |
| `Docs/AI_Usage/edits/` | 검토 기록: 무엇을 기계로 확인했고 무엇이 사람 판단으로 남았는지 |
| `Docs/AI_Usage/asset_manifest.csv` | 에셋 목록과 출처 필드 |
| `Docs/AI_Usage/sources/` | 생성 원본 PNG |
| `Docs/AI_Usage/reviews/` | 리뷰용 게임플레이 스크린샷 |
| `Docs/Decisions/` | 사용자 결정 파일. 승인 후 수정하지 않고, 정정은 새 파일로 남긴다 |
| `Assets/_Project/Editor/Evidence/` | 증거 스키마·검증기. 18개 수용 기준을 코드로 고정 |

기계가 대신할 수 없는 항목은 그대로 열려 있다. 사람의 시각·오디오 승인, 무보조 플레이테스트 관측, 그리고 외부 RSA 서명이 필요한 `M2EntryGate`는 자동화가 기록할 수 없도록 설계했다.

---

## 7. 자동 검증 현황

제출 시점 기준.

| 항목 | 결과 |
|---|---|
| EditMode 테스트 | 49건 통과 |
| PlayMode 테스트 | 26건 통과 |
| 제출 WebGL 빌드 | Release, Brotli 압축, 총 20.7MB |
| 브라우저 실행 검증 | headless Chrome에서 타이틀 → 첫 방 → 입력 반영까지 4단계 스크린샷 확인 |

빌드 산출물의 파일별 크기·SHA-256은 `Builds/Overbless_Web.provenance/submission-build-manifest.json`에 기록된다.
