# 프로젝트 규칙

## 1. 기준과 범위

- Unity Editor는 `6000.0.72f1`로 고정한다. 렌더 파이프라인은 URP 2D이며 입력은 Input System만 사용한다. Addressables는 도입하지 않는다.
- 오디오는 저장소 안의 절차 합성만 사용한다. 외부 녹음, 외부 생성 오디오, AI 오디오 에셋은 허용하지 않는다.
- **M0**은 프로젝트 설정·문서·승인 원문·증거 계약을 고정한다. **M1**은 직접 공격 없는 유도 전투 슬라이스, 대표 시청각 표현, 로컬 Development WebGL 검증을 구현한다.
- `M2EntryGate`의 최종 결정은 Unity의 `Overbless/M2 Entry Gate/Record Human Gate Decision` 대화형 창에서 사용자가 후보·결정·UTC·사용자 증언에 결합된 canonical payload를 준비한 뒤, Unity/저장소 밖에서 보관하는 RSA 개인 키로 그 정확한 UTF-8 바이트를 서명하고 detached signature를 붙여 기록한다. validator는 후보 밖 환경의 `OVERBLESS_M2_GATE_TRUST_ANCHOR`와 `OVERBLESS_M2_GATE_TRUSTED_PUBLIC_KEY_SPKI_BASE64`를 신뢰 기준으로 사용하며, 키가 없거나 서명이 맞지 않으면 fail-closed 한다.
- 일반 Editor 자동화에는 PASS를 생성하거나 개인 키를 읽는 API가 없다. `gate-decision.json`은 candidate ID, evidence/validator 해시, `PASS|REWORK`, 결정 UTC, 사용자 증언을 RSA-SHA256 서명으로 인증하므로 공개 증거만 읽는 자동화는 사용자를 사칭하거나 증언을 바꿀 수 없다. 개인 키는 저장소, 후보, Unity 환경 변수에 두지 않는다. 사용자가 외부 서명 후 대화형 확인을 완료하기 전에는 machine validator가 준비 상태여도 최종 PASS가 아니다.

## 2. 폴더와 이름

`Assets/_Project/` 아래에 프로젝트 소유 파일을 둔다. 용도는 `Runtime/`, `Editor/`, `Data/`, `Scenes/`, `Prefabs/`, `Art/`, `Audio/`로 분리한다. 외부 패키지 파일이나 Unity 생성 폴더에 게임 코드를 넣지 않는다.

| 대상 | 규칙 |
|---|---|
| C# 파일·형식 | PascalCase, 파일명과 주 형식명을 일치시킨다. 런타임 네임스페이스는 `Overbless.Runtime`이며 `Overbless.Runtime.asmdef`의 root namespace도 동일하게 설정한다. 에디터 네임스페이스는 `Overbless.Editor` 하위다. |
| Scene | `M1_GuidedValidation.unity`처럼 마일스톤과 목적을 드러낸다. |
| Prefab | `Player.prefab`, `Dasher.prefab`처럼 런타임 역할과 일치시킨다. |
| ScriptableObject 형식·에셋 | 형식과 에셋 이름이 역할을 명시해야 하며 런타임에서 원본을 변경하지 않는다. |
| 소스 아트·오디오 | M1 대표 에셋은 역할·이벤트 이름을 사용하고, provenance manifest에 실제 경로와 SHA-256을 기록한다. |

## 3. 데이터와 런타임 소유권

- `Data/`의 ScriptableObject는 설계 원본이며 런타임에서 변경하지 않는다. 인스펙터 원본, 배열, 중첩 값에 쓰기하지 않는다.
- 체력, 쿨다운, 대상, 공격 방향, 축복 적용 상태처럼 변하는 값은 일반 C# 런타임 상태가 소유한다. 원본 설정에서 값을 읽어 초기화할 뿐, 원본을 상태 저장소로 쓰지 않는다.
- 입력은 `PlayerInputRouter`만 읽고, 이동은 `PlayerController`, 대시는 `DashAbility`, 공격 상태는 각 적/공격 소유자, 피해·사망 결과는 명시적 이벤트 소유자가 처리한다. 한 시스템이 다른 시스템의 내부 상태를 직접 수정하지 않는다.
- 공격 판정은 물리 힘에 맡기지 않는다. 돌진·투사체 이동, 방향 저장, 밀치기, 피해와 사망은 코드·명시적 이벤트로 처리한다.

## 4. 공격·정렬·물리 계약

### 공격 단계

모든 주요 공격은 `경고 → 방향/범위 고정 → 실행 → 회복` 순서를 지킨다. 고정 이후에는 플레이어를 재추적하지 않으며, 취소·사망·방 재시작 때 고정 컨텍스트를 폐기한다.

### Sorting Layer

아래 순서는 낮은 쪽에서 높은 쪽이다. 게임 오브젝트는 `Default` Sorting Layer를 사용하지 않는다.

`Background < World < Actors < VFX < Telegraph < UI`

`Telegraph`는 배경·VFX보다 항상 앞에 있어야 한다. 색만으로 공격을 구분하지 않고 선·면·아이콘 등의 형태를 함께 사용한다.

### Physics 2D Layer

게임 물리 레이어는 `Player`, `EnemyBody`, `EnemyAttack`, `Projectile`, `World`, `Pickup`, `Exit`만 사용한다. 충돌을 허용하는 쌍은 다음뿐이다.

- `Player ↔ World`, `EnemyBody`, `EnemyAttack`, `Projectile`, `Pickup`, `Exit`
- `EnemyBody ↔ World`, `Player`, `EnemyBody`, `EnemyAttack`, `Projectile`
- `EnemyAttack ↔ Player`, `EnemyBody`
- `Projectile ↔ Player`, `EnemyBody`, `World`
- `Pickup ↔ Player`, `Exit ↔ Player`

그 밖의 모든 쌍은 비활성화한다. 접촉 자체는 피해를 주지 않으며, self-hit와 공격 소유자 면역은 런타임 피해 계약이 처리한다.

## 5. 증거와 출처

- 승인된 원문과 결정 파일은 쓰기 한 번의 증거다. 승인 뒤 내용을 수정·덮어쓰기·재생성하지 않는다.
- 최종 `gate-decision.json`은 같은 후보 디렉터리의 고유 임시 파일에 기록하고 `FileOptions.WriteThrough`와 `Flush(true)`로 flush한 뒤, 기존 파일을 덮어쓰지 않는 같은 디렉터리 promotion으로만 만든다. 최종 이름만 결정으로 간주한다. 중단된 임시 파일은 최종 결정을 오염시키지 않으며 재시도는 새 임시 이름을 사용한다.
- detached signature는 `candidateId`, `evidenceManifestSha256`, `validatorReportSha256`, `decision`, `decidedUtc`, `userAttestation`의 canonical JSON에 결합한다. `trustAnchor`, `signatureAlgorithm: RSA-SHA256`, canonical Base64 signature가 스키마와 외부 공개 키 검증을 모두 통과해야 한다.
- 정정 또는 재검증은 새 파일과 새 식별자/시각/해시로 남기고, 이전 증거와 대체 관계를 참조한다. 이전 파일은 보존한다.
- 에셋·오디오 증거는 입력 원본, 생성기·도구·버전, 설정 또는 지시, 시드, 생성 시각, 원본/최종 경로와 SHA-256, 순서가 있는 수정 내역, 검토자를 기록한다. 해시는 실제 바이트를 기준으로 한다.
- 증거에 라이선스나 출처를 추정해 채우지 않는다.

## 6. M2+ 경계

유효한 사용자 `PASS` 전 금지: 메아리, 골렘, 절벽·함정·파괴물, 잔향, 최종전, `Room_02`, `Room_03`, `Room_Final`과 이를 선결정하는 범용 framework. M1의 플레이어·돌진수·궁수·추종자·과속·거대화·friendly fire·영혼·출구·HUD·기능 오디오는 승인된 현재 범위다.
예외: `Docs/Decisions/M2_ASSET_PRODUCTION_APPROVAL.json`에 따라 M2 이미지의 오프라인 생성·투명화·시트 패킹·Unity 임포트 설정·출처 기록은 허용한다. 해당 에셋을 씬·프리팹·게임플레이 데이터·런타임 코드에 연결하거나 M2 기능을 활성화하는 작업은 유효한 `M2EntryGate PASS` 전까지 금지한다.

사용자 `PASS` 뒤에도 M2+는 별도 계획과 승인을 거친다. 플레이어 직접 공격, 디버프·독·저주형 축복, 메타 진행, 모바일/온라인 기능, 절차적 던전, 대형 스크롤 맵은 현재 허용 목록에 없다.
