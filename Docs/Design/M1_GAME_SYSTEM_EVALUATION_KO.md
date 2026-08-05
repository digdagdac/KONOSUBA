# 《이 멋진 적에게 축복을》 M1 게임 시스템 평가

- 문서 버전: 1.4
- 평가 기준: `Docs/Design/GAME_DESIGN_CHARTER_KO.md`
- 대상: M0+M1 유도형 WebGL 수직 슬라이스
- 판정 범위: 게임 디자인 품질 평가이며 `M2EntryGate PASS|REWORK`를 대신하지 않는다.
- 수치 상태: 아래 게임플레이 튜닝 현재값은 fresh 플레이테스트 전 `[PLACEHOLDER]` 가설이다. 단, `Minimum Warning Duration=0.55초`는 공격 가독성 하한, `Performance Floor FPS=45`는 증거 수용 하한인 고정 `ACCEPTANCE` 계약이므로 튜닝 범위에서 제외한다.

## 설계 기둥

1. **적을 강화하는 역설적 선택**: 플레이어는 약화가 아니라 적의 속도·크기·공격 범위를 키워 위험과 기회를 동시에 만든다.
2. **읽히는 인과관계**: 경고→고정→실행→회복, 축복 표식, 공격 궤적, 피격·영혼·출구 피드백으로 “내 선택 때문에 일어났다”가 보여야 한다.
3. **직접 공격 없는 공간 퍼즐 액션**: 이동과 대시로 위치를 조정해 적의 공격이 다른 적을 맞히게 한다.
4. **짧은 실패, 즉시 재학습**: 사망·실패 후 R 재시작으로 동일한 고정 배치를 빠르게 반복한다.

**재미 가설**: “강해진 적의 더 위험한 공격을 마지막 순간에 피하고 다른 적에게 적중시키는 순간”이 반복할 가치가 있는 만족을 제공한다.

## 종이 프로토타입

아래 수치와 배치 벡터는 `Minimum Warning Duration`의 고정 `ACCEPTANCE` 하한을 제외하면 모두 fresh 플레이테스트 전 `[PLACEHOLDER]`다. 단일 값은 `M1_TUNING_HYPOTHESES.csv`의 동명 행을 사용하며, 좌표 벡터는 한 세트로 기록·교체한다. 종이판 1칸은 월드 1단위다.

### 초기 상태

| 구성물 | 시작 상태 |
|---|---|
| 보드 | `Room Width=16`, `Room Height=9`이므로 x=`[-8,8]`, y=`[-4.5,4.5]`인 격자 `[PLACEHOLDER]` |
| 플레이어 1 | `(0,-2.5)`, HP=`Player HP`, Haste/Giant 카드 각 1장, 대시 준비 |
| Dasher 101 | `(0,3)`, 남향, HP=`Dasher HP` |
| Archer 102 | `(0,-0.5)`, 남향, HP=`Archer HP` |
| Archer 103 | `(-4,1.5)`, 동향, HP=`Archer HP` |
| Minion 104 | `(4,1.5)`, HP=`Minion HP` |
| Minion 105 | `(4,-1.5)`, HP=`Minion HP` |
| 출구 | `(7,-3.5)`, 잠김 |
| 공용 상태 | `Enemy Count=5`, 영혼 0/`Souls Required`, 출구 잠김, 모든 적 Idle, active/queued context 없음, damage ledger 비움, 축복 배정·return 타이머 없음, 모든 공격·Warning·Recovery·dash 타이머 0, 종이 실행 최초 전역 attack ID 1, 경과 시간 0 |

### 공간 템플릿과 수치 카드

- 한 종이 tick은 `T=Paper Tick Duration`이다. 위치·거리·남은 시간은 소수로 기록하며 보드 경계에서 자른다. 지속 시간 `D`는 `ceil(D/T)`개의 판정 tick으로 환산하되, 남은 시간은 매 tick 끝에 `max(0, 남은 시간-T)`로 갱신한다. 새로 시작한 타이머도 그 tick 끝부터 감소한다.
- Dasher Line은 잠긴 중심선 길이=`Dasher Line Length`, 전체 폭=`Dasher Line Width`다. Archer Line은 길이=`Archer Line Length`, 전체 폭=`Archer Line Width`다.
- Minion Circle은 잠긴 origin에서 잠긴 방향으로 `Minion Attack Range/2`만큼 떨어진 점이 중심이고, 반지름은 `max(Minion Attack Range/2, Minion Attack Width/2)`다.
- 비공격 이동은 아래 3단계의 아키타입 규칙만 따른다. Dasher는 고정되고, Archer는 `Archer Move Speed`로 `Archer Preferred Distance`를 유지하며, Minion은 `Minion Move Speed`로 플레이어를 추적한다.
- Line 실행은 이전 위치부터 새 위치까지 전체 폭의 swept capsule로 판정한다. 끝점만 검사하지 않는다. `벽까지 거리`는 중심선 방향으로 반폭 `width/2`인 capsule이 보드 경계에 처음 닿는 거리, 즉 경계를 `width/2`만큼 안쪽으로 확장한 선까지의 거리다. Minion Circle은 실행 tick에 한 번 중심점 overlap을 판정한다.
- 종이판의 플레이어·적 말은 피격 판정에서 중심점으로 취급하고 서로의 이동을 막지 않는다. Giant 크기·질량은 표시와 상태 카드에는 반영하지만 종이 충돌 반경에는 넣지 않으며, 실제 collider 체감은 빌드 플레이테스트 항목으로 분리한다.
- `Player Collision Radius`는 종이 공격 피격이 아니라 영혼·출구와의 이동 segment overlap 확장에만 쓴다. 영혼 trigger 반경은 `Soul Pickup Radius`다. 출구 trigger는 중심 `(7,-3.5)`, 반너비=`Exit Trigger Half Width`, 반높이=`Exit Trigger Half Height`인 축 정렬 사각형이다.
- 접촉은 sweep상의 이른 시각, 동률이면 target entity ID 오름차순으로 처리한다. 첫 접촉에서 `(attackInstanceId,targetEntityId)`를 기록한 뒤 피해 수락 여부를 판정하므로 무적에 막힌 대상도 같은 공격에서 다시 맞지 않는다. 공격자 자신은 제외한다.
- 기본 공격 피해는 `Enemy Damage`; 적 HP·쿨다운·Warning·Recovery는 튜닝 표의 아키타입별 행을 사용한다.
- 상태 카드 매핑은 Dasher=`Dasher Engagement Range`/`Dasher Warning`/`Dasher Recovery`/`Dasher Attack Cooldown`, Archer=`Archer Engagement Range`/`Archer Warning`/`Archer Recovery`/`Archer Attack Cooldown`, Minion=`Minion Attack Range`/`Minion Warning`/`Minion Recovery`/`Minion Attack Cooldown`이다.

### tick 진행과 우선순위

1. **선택**: 플레이어는 Haste/Giant 중 사용 가능한 카드 0–1장을 유효한 살아 있는 적에게 적용하거나 취소한다. Warning/Locked/Executing/Recovery 중인 적도 대상이 될 수 있으며, 아래 스냅숏 계약을 따른다. 종이 선택·취소는 tick 경계의 즉시 결정이라 시간이 흐르지 않는다. `Selection Time Scale`은 빌드 체감 플레이테스트 전용이다.
2. **플레이어 행동**: 대시 중이 아니면 대기, 정규 이동, 준비된 대시 중 하나를 고른다. 정규 이동은 8방향의 정규화 벡터로 `Player Move Speed×T`만큼 이동한다. 대기는 이동하지 않는다. 대시 시작 시 방향을 고정하고 남은 Dash Distance=`Dash Distance`, 남은 dash time=`Dash Duration`, 남은 invulnerability=`Dash Invulnerability`, 남은 cooldown=`Dash Cooldown`으로 설정한다. 시작 tick부터 대시 중인 각 tick에는 다른 이동을 받지 않고 `min((Dash Distance/Dash Duration)×T, 남은 Dash Distance)`만큼 요청한 뒤 실제 이동 거리만큼 남은 Dash Distance를 줄인다. 수집·출구 판정을 위해 이번 tick의 시작점→끝점 이동 segment를 저장한다.
3. **적 이동**: 살아 있는 적을 entity ID 오름차순으로 처리한다. Dasher는 Idle/Warning/Recovery에서 이동하지 않는다. Archer는 Idle/Warning에서 플레이어와의 거리 `d`가 upper=`Archer Preferred Distance+Archer Distance Tolerance`보다 크면 플레이어 쪽으로 `min(Archer Move Speed×Haste Move Multiplier×T, d)`만큼 이동하고, `d`가 lower=`Archer Preferred Distance-Archer Distance Tolerance`보다 작으면 플레이어 반대쪽으로 정확히 `Archer Move Speed×Haste Move Multiplier×T`만큼 이동한다. 허용대 `[lower,upper]`에서는 정지한다. `d=0`일 때 반대 방향은 동쪽이며 모든 결과는 보드 경계에서 자른다. 이 규칙은 허용대 경계를 강제 clamp하지 않으므로 왕복 진동 여부도 플레이테스트 측정 대상이다. Minion은 Idle에서 `d>Minion Attack Range`이면 플레이어 쪽으로 `min(Minion Move Speed×Haste Move Multiplier×T, d)`만큼 이동한다. 다른 상태의 적은 정지한다. Warning telegraph는 이동 후 현재 적→현재 플레이어 벡터로 다시 그리지만 저장하지 않는다.
4. **적 상태 전이·ID 발급**: tick 시작부터 Executing이었던 공격 목록을 먼저 저장한 뒤 **살아 있는 적만** entity ID 오름차순으로 처리한다. 상태 분기는 상호 배타적이다. `state==Warning && Warning remaining==0`이면 현재 위치를 origin으로 하고 `player position-origin`을 정규화해 방향을 잠근다. 이 벡터가 0이면 Dasher/Archer는 공격을 취소해 Idle로 돌아가고 Minion만 남향 `(0,-1)`을 사용한다. 유효한 lock마다 다음 양의 전역 attack ID를 발급하고 1 증가시킨다. target mask는 플레이어 1과 적 101–105 중 attacker 자신을 제외한 집합이며, 현재 축복으로 나머지 context를 잠근다. 새 Executing 공격은 이번 tick 실행 목록에는 넣지 않는다. `state==Recovery && Recovery remaining==0`이면 Idle로 돌아가 현재 Haste 여부로 매핑된 Attack Cooldown을 설정한다. `state==Idle && cooldown==0`이면 Dasher는 `Dasher Engagement Range`, Archer는 `Archer Engagement Range`, Minion은 `Minion Attack Range` 이내에 플레이어가 있을 때 매핑된 Warning을 시작해 한 번 저장한다. Executing과 아직 양수 타이머인 상태는 바꾸지 않는다.
5. **공격·접촉·피해 단일 loop**: 4단계에 저장한 공격자를 attacker entity ID 오름차순으로 처리한다. 시작할 때 attacker가 죽었거나 context가 취소됐으면 건너뛴다. Dasher는 `min(Dasher Charge Speed×현재 Haste Move Multiplier×T, 잠긴 남은 길이, 벽까지 거리)`, Archer 투사체는 `min(Archer Projectile Speed×현재 Haste Projectile Multiplier×T, 잠긴 남은 길이, 벽까지 거리)`의 sweep을 만들고, Minion은 잠긴 Circle을 한 번 만든다. 각 contact를 이른 시각→target entity ID 순으로 즉시 처리한다: ledger 중복이면 건너뛰고, 아니면 먼저 `(attackInstanceId,targetEntityId)`를 기록한다. target이 플레이어 1이고 Dash Invulnerability 남은 시간이 양수면 피해만 거부한다. 그 외 살아 있는 target에는 즉시 `HP=max(0, HP-Enemy Damage)`를 commit한다. 플레이어 1이 HP 0이면 영혼을 만들지 않고 즉시 **실패**를 확정하며 남은 contact·attacker와 6단계를 모두 건너뛴다. 적이 HP 0이면 `Alive→Dead` 상태, context 취소, transition roster 제외, 정확히 하나의 `{sourceEnemyId, center=그 적의 commit된 현재 중심점}` 영혼 생성을 한 commit으로 처리하고 점유 축복마다 남은 `Blessing Slot Return` 타이머를 만든다. 그래서 먼저 죽은 뒤 attacker는 차례가 와도 건너뛴다. 실패가 없으면 contact loop 뒤 Line은 몸/투사체를 끝점으로 옮기고 잠긴 길이를 모두 이동하거나 벽에 닿았을 때 Recovery 타이머를 만들며, Circle은 한 번 판정 후 바로 Recovery 타이머를 만든다.
6. **수집·출구**: 5단계에서 실패가 확정되지 않았을 때만 수행한다. tick 시작에 존재한 영혼은 플레이어 이동 segment와 영혼 중심의 최단거리가 `Player Collision Radius+Soul Pickup Radius` 이하이면 수집하며, 이른 접촉→영혼 `sourceEnemyId` 순이다. 5단계에서 새로 생긴 영혼은 플레이어 최종점이 같은 반경 합 안에 있을 때만 수집한다. 누적 수집 수가 `Souls Required`에 도달하면 출구를 연다. tick 시작부터 열려 있던 출구는 플레이어 segment가 `Player Collision Radius`만큼 확장한 출구 사각형과 교차하면 **성공**을 확정한다. 이번 단계 수집으로 출구가 새로 열리면 과거 segment를 소급하지 않고 플레이어 최종점이 확장 사각형 안에 있을 때만 성공이다. 성공/실패는 한 번만 확정하며 같은 tick에는 플레이어 실패가 출구 성공보다 우선한다.
7. **tick 종료**: Warning, Recovery, Attack Cooldown, dash, dash invulnerability, dash cooldown, Blessing Slot Return의 양수인 남은 시간을 각각 `T`만큼 감소시킨다. return 타이머가 0이 된 축복 슬롯은 다음 tick 1단계부터 사용 가능하다. 대시는 이동 거리 또는 Dash Duration이 0일 때 끝나고, 다음 2단계 시작에 cooldown이 0일 때만 다시 준비된다. 성공/실패가 아니면 경과 시간을 `T` 더한다. 플레이어 HP가 0 이하이면 0으로 저장하고 실패다.
8. **재시작**: 실패 또는 R 선택 시 런타임 상태를 한 commit으로 복원한다. 모든 적을 Idle로 되돌리고 active/queued context와 damage ledger를 비우며, 말·HP·카드·축복 배정·return 포함 모든 타이머·영혼·출구를 초기화한다. 전역 attack ID 발급기는 되감지 않고 다음 양의 ID에서 계속해 재시작 전후에도 ID가 중복되지 않는다.

### 축복 계산

- 같은 종류의 중복 적용은 거부한다. Haste+Giant는 같은 적에 동시 적용 가능하며 두 카드가 각각 잠긴다.
- 재계산은 기본 카드에서 시작해 Haste와 Giant의 존재 여부를 한 번씩 적용한다. 입력 순서와 무관하며 출력 순서는 `Haste→Giant`다.
- Haste는 Archer 거리 유지와 Minion 추적 속도 및 Dasher charge 속도에 `Haste Move Multiplier`, Warning에 `max(기본 Warning / Haste Attack Speed Multiplier, Minimum Warning Duration)`, 공격 쿨다운에 `Haste Cooldown Multiplier`, Archer 투사체 속도에 `Haste Projectile Multiplier`를 적용한다. `Minimum Warning Duration=0.55초`는 튜닝 가설이 아니라 가독성을 위한 `ACCEPTANCE` 하한이다.
- Giant 적용 계수 `G`는 Giant가 있으면 `Giant Range Multiplier`, 없으면 1이다. 유효 교전 범위=`기본 Engagement Range×G`, 유효 공격 range `R=기본 Attack Range×G`, 유효 width `W=기본 Attack Width`다. Dasher/Archer Line은 길이 `R`·폭 `W`, Minion Circle은 center offset `R/2`·radius `max(R/2,W/2)`로 해석한 뒤 lock에 `R`과 `W`를 저장한다. 새 최대 HP는 `min(int.MaxValue, ceil(기본 최대 HP×Giant HP Multiplier))`다. 살아 있는 대상의 적용 직전 비율 `r=현재 HP/기존 최대 HP`를 보존해 새 현재 HP를 `clamp(Mathf.RoundToInt(새 최대 HP×r), 1, 새 최대 HP)`로 정한다. 여기서 `Mathf.RoundToInt`는 가장 가까운 정수로 반올림하고 정확한 `.5` 동률은 짝수 쪽을 택한다. 이미 사망한 대상은 0을 유지한다. 말 크기에는 `Giant Scale Multiplier`, 질량에는 `Giant Mass Multiplier`를 적용한다.
- **축복 스냅숏 계약**: Warning 시작은 그 순간의 `max(기본 Warning/Haste Attack Speed Multiplier, Minimum Warning Duration)`만 저장하며 이후 축복으로 남은 Warning을 다시 계산하지 않는다. Lock은 `attackInstanceId`, attacker, `lockedAt=현재 경과 시간`, origin, 정규화 방향, shape, range, width, damage, target mask를 저장하고 이 필드는 종료까지 불변이다. Archer launch 위치는 잠긴 origin이다. Lock 뒤 Haste 적용은 잠긴 공격 형상·피해를 바꾸지 않지만 5단계의 live charge/projectile 속도와 Recovery 완료 시 설정할 cooldown에는 반영된다. Lock 뒤 Giant 적용도 잠긴 형상을 바꾸지 않고 대상 HP·크기·질량과 다음 공격에만 반영된다.

### 종이 단계 성공 조건

- 축복하지 않기, Haste, Giant가 서로 다른 위치 결정을 만든다.
- 공격하지 않아도 유도→friendly fire→영혼→출구의 인과를 위 절차만으로 재현할 수 있다.
- 동일 공격 중복 피해, 자기 피해, 사망 후 슬롯 영구 점유, 재시작 후 토큰 잔류가 발생하지 않는다.

### 종이 단계 파손 조건

- 항상 같은 축복/위치가 우월하거나 축복 선택이 결과를 바꾸지 않는다.
- 생존 최적 행동과 friendly fire 유도 행동이 항상 같아 위험/보상 선택이 사라진다.
- 영혼 수급이 막혀 출구를 열 수 없거나 한 사망에서 영혼을 반복 생성할 수 있다.

## Core Loop

### Moment-to-Moment (0–30초)

- **행동**: 이동 → 적 관찰 → Haste/Giant 선택 → 대상 지정 → 경고 궤적 유도 → 대시 회피
- **즉시 피드백**: 선택 감속, 대상 하이라이트, 독립 축복 표식, 형태별 경고선/원, 고정 색상, 기능성 효과음
- **보상**: 적 체력 감소, 적 사망, 영혼 생성이라는 명확한 인과 보상
- **결정**: 어느 적을 얼마나 위험하게 강화할지, 어느 방향에서 공격을 잠글지, 대시를 생존과 유도 중 어디에 쓸지

### Session Loop (5–30분)

- **목표**: friendly fire로 적을 처치하고 영혼 3개를 모아 출구를 연다.
- **긴장**: 직접 공격 불가, 플레이어 HP 6, 1.2초 대시 쿨다운, 축복 슬롯의 사망 후 0.5초 반환
- **해결**: 출구 진입 성공 또는 사망 후 동일 배치 재시작
- **M1 성공 기준**: fresh tester가 외부 코칭 없이 5회 이내 완주

### Long-Term Loop (수 시간–수 주)

M1에는 장기 성장이나 유지 장치가 없다. 이는 누락이 아니라 의도된 범위 제한이다. M2 이후 후보는 적 조합·방 구조·축복 상호작용 확장이지만, 코어 루프 재미가 먼저 검증되어야 한다.

## 메커니즘 명세

### 이동과 대시

- **목적**: 공격 방향을 조절하고 마지막 순간에 생존하는 핵심 공간 동사
- **플레이어 판타지**: 공격하지 않고 전장을 조종하는 민첩한 유도자
- **입력**: WASD/방향키, Space
- **출력**: 이동, 방향 고정 대시, 일시 무적, 쿨다운
- **성공 조건**: 입력 방향과 이동이 일치하고, 대시가 경계 밖으로 나가지 않으며, 일시정지·포커스 게이트 중 시작되지 않는다.
- **실패 상태**: 무적 고착, 벽 관통, 정지 중 대시 예약, 서로 다른 입력 소유자가 상태를 덮어씀
- **엣지 케이스**: 사망·포커스 손실·일시정지·재시작 동시 발생, 벽 근접 대시
- **튜닝 레버**: 이동속도 5, 거리 3.5, 지속 0.18초, 무적 0.22초, 쿨다운 1.2초
- **의존성**: Input System, Health, WebStartGate, PauseController, 월드 경계

### Haste/Giant 적 축복

- **목적**: 적의 위협을 기회로 바꾸는 핵심 위험/보상 선택
- **플레이어 판타지**: 적을 직접 때리지 않고 규칙을 비틀어 승리하는 축복술사
- **입력**: 1 또는 2 → 포인터 대상 → 좌클릭, 우클릭/Escape 취소
- **출력**: Haste 또는 Giant 런타임 스탯 재계산, 대상 표식, 슬롯 잠금
- **성공 조건**: 살아 있고 유효한 현재 hover 대상에만 적용된다. 같은 종류 중복은 거부하고 Haste+Giant 교차 중첩은 허용하며, 입력 순서와 무관하게 `Haste→Giant` 순서로 산출하고 두 슬롯을 각각 잠근다.
- **실패 상태**: 이전 hover 대상 오적용, 파괴된 대상이 슬롯 점유, 재시작 후 스탯 잔류
- **엣지 케이스**: 선택 중 포커스 손실, 대상 사망, Haste+Giant 중첩, fake-null Unity 객체
- **튜닝 레버**: Haste 이동×1.5·공격속도×1.35·쿨다운×0.75(쿨다운 25% 짧음)·투사체×1.35; Giant 크기×1.35·HP×1.75·범위×1.4·질량×1.5
- **의존성**: BlessingTargeting/System/Slot, EnemyRuntimeStats, Health, HUD, 표식

### 적 공격과 friendly fire

- **목적**: 적의 공격을 플레이어의 간접 공격 수단으로 전환
- **플레이어 판타지**: 무기를 들지 않고 적의 힘과 각도를 역이용해 전장을 지휘하는 책략가
- **입력**: 적 AI의 목표 추적과 경고 종료 시점의 lock snapshot
- **출력**: Line 또는 Circle 공격, `(attackInstanceId,targetEntityId)` 단위 피해 1회
- **성공 조건**: Warning→Locked→Executing→Recovery 순서, 전역 양수 공격 ID, 자기 피해 거부, 서로 다른 대상에는 같은 공격 1회씩 허용
- **실패 상태**: 중복 피해, 고정 후 궤적 변경, 정지 중 공격 진행, 관찰자 예외로 상태 고착
- **엣지 케이스**: 공격 중 사망·취소, 벽 충돌, 같은 프레임 다중 적중, 재진입 이벤트
- **튜닝 레버**: 적별 경고·범위·폭·속도·회복·쿨다운
- **의존성**: AttackStateMachine/Context, DamageLedger, Health, AI, 텔레그래프, 기능성 오디오

### 영혼과 출구

- **목적**: 전투 성과를 공간 목표로 전환해 세션을 닫는다.
- **플레이어 판타지**: 위험한 유도를 눈에 보이는 전리품과 탈출로 바꾸는 생존자
- **입력**: 적 사망, 플레이어 영혼 접촉
- **출력**: 적당 영혼 1개, 수집 수 증가, 3개에서 출구 개방
- **성공 조건**: 사망 토큰당 1개, 수집 1회, 상태 커밋 후 모든 관찰자 통지
- **실패 상태**: 중복 영혼, 관찰자 예외로 출구/오디오 불일치, 재시작 후 잔류
- **엣지 케이스**: 세 번째 영혼과 출구 이벤트 동시 발생, 수집 callback 예외, 재시작
- **튜닝 레버**: 요구 영혼 3개
- **의존성**: M1RoomLifecycle, SoulFragment, ExitGate, HUD, 오디오

### Web 시작·포커스·일시정지·재시작

- **목적**: 브라우저 정책을 플레이 규칙과 일치시키고 반복 학습을 안전하게 만든다.
- **분류/플레이어 경험 목표**: 전투 메커니즘이 아닌 플랫폼 지원 시스템이다. 플레이어는 브라우저 정책 때문에 입력을 잃었다고 느끼지 않고, 중단·복귀·재시작을 예측 가능하게 통제한다고 느껴야 한다.
- **입력**: 신뢰 가능한 키/마우스 제스처, 포커스 변화, Escape, R
- **출력**: 시간·입력·오디오 소유권 claim, 상태 복구, 결정론적 재시작
- **성공 조건**: 첫 제스처 전 게임/타이머/오디오 정지, 포커스 복귀에 재무장, 소유자별 claim 대칭 해제
- **실패 상태**: stale 입력 복구, 일시정지 중 상태 진행, 부분 재시작
- **엣지 케이스**: 사망 상태 포커스 복귀와 R 동시 입력, 선택 중 Escape, observer 예외
- **튜닝 레버**: 없음—정책·원자성 계약
- **의존성**: PlayerInputRouter, GameplayTimeScaleCoordinator, AudioListener, FunctionalAudioEmitter

## 시스템 상호작용 매트릭스

| 시스템 쌍 | 의도 | 허용/버그 기준 |
|---|---|---|
| Haste × Dasher | 이동·live charge 속도 증가, Warning 단축, 쿨다운 단축 | lock 뒤 방향·range·width·damage 변경은 버그이며, 실행 중 charge 속도가 현재 Haste를 따라가는 것은 의도 |
| Haste × Archer | 이동·live 투사체 속도 증가, Warning·쿨다운 단축 | lock 뒤 방향·range·width·damage 변경은 버그이며, 실행 중 투사체 속도가 현재 Haste를 따라가는 것은 의도 |
| Haste × Minion | 이동 증가, Warning·쿨다운 단축 | 사용하지 않는 투사체 multiplier가 Circle 범위를 바꾸면 버그 |
| Giant × Dasher | HP·교전/Line 길이·크기·질량 증가 | 벽 속 고착 또는 Line 폭까지 암묵 증가하면 버그 |
| Giant × Archer | HP·교전/Line 길이·크기·질량 증가 | 투사체 속도까지 증가하면 버그 |
| Giant × Minion | HP·교전/Circle 반지름·크기·질량 증가 | 접촉만으로 피해가 생기면 버그 |
| Haste × Giant | 같은 적에 허용, 기본값에서 각 multiplier를 한 번씩 적용, Haste→Giant 정렬, 두 슬롯 독립 잠금 | 입력 순서별 결과 차이, 같은 종류 중복, 한 슬롯만 잠김은 버그 |
| Dash × Enemy Attack | 회피·유도 | 대시 자체 공격 판정은 버그 |
| Pause/Focus × AI | 완전 정지 | deltaTime 0에서 피해·상태 전이는 버그 |
| Enemy Death × Blessing Slot | `Blessing Slot Return` `[PLACEHOLDER]` 후 반환 | baseline 복원 전 반환 또는 파괴 대상의 영구 점유는 버그 |
| Required Soul × Exit | 같은 커밋에서 개방 | 한 observer 예외로 후속 통지가 사라지면 버그 |
| Restart × 모든 런타임 | 동일 초기 상태 복원 | 토큰, 오디오, 축복, 시간 claim 잔류는 버그 |

## 온보딩 평가

- [x] 첫 화면에 이동, 대시, 1/2 축복 키와 목표가 표시된다.
- [x] 핵심 동사인 축복 선택을 30초 안에 시도할 수 있는 입력 경로가 제공된다.
- [ ] **위험**: 첫 성공이 보장되지 않는다. 첫 공격 유도 전에 플레이어가 연속 피격될 수 있다.
- [ ] **검증 가설**: 공격 경고와 고정 색이 fresh tester에게 적 공격의 인과를 시각적으로 학습하게 한다.
- [ ] **검증 가설**: 영혼과 잠긴 출구가 fresh tester에게 세션 목표를 이해하게 한다.
- [ ] **tester별 온보딩 완료 체크리스트**: (a) trusted gesture 후 이동과 대시를 각각 수행, (b) 30초 안에 의도한 적에게 축복 1회 적용, (c) 첫 friendly fire 관찰 후 “내 위치/축복 때문에 적이 맞았다”를 코칭 없이 설명, (d) 영혼 1개를 수집하고 영혼→출구 목표를 설명. 네 항목을 모두 만족해야 1명 완료로 센다.
- [ ] **M1 파일럿 게이트**: fresh tester 3명은 3/3 모두 위 체크리스트를 완료하고 외부 전략 코칭 없이 각자 5회 이내 방을 완주해야 한다.
- [ ] **출시 전 지표**: 별도 fresh cohort `N≥20`에서 완료자/전체 비율 `>90%`를 요구한다. 3명 파일럿의 3/3을 “90%”로 환산하지 않는다.
- [ ] **검증 가설**: 출구 개방이 첫 세션의 명확한 마침표와 다음 콘텐츠 기대를 만든다.

## 밸런스 평가

상세 수치는 `Docs/Design/M1_TUNING_HYPOTHESES.csv`에서 관리한다.

### 현재 강점

- Haste와 Giant가 속도 위험/공간 위험이라는 서로 다른 결정을 만든다.
- 직접 공격이 없어 위치 선정과 lock 유도가 모든 전투 의사결정의 중심에 남는다.
- 고정 배치와 짧은 재시작은 원인 학습에 적합하다.

### 파손 조건

- 30초 안에 축복을 한 번도 사용하지 않으면 온보딩 실패
- 첫 사망 원인을 경고/고정/피격 중 하나로 설명하지 못하면 가독성 실패
- 동일 전략만으로 5회 연속 무위험 완주하면 위험/보상 실패
- 5회 안에 한 번도 완주하지 못하면 난이도 또는 가르침 실패
- Haste와 Giant 중 한 축복 선택률이 20% 미만이면 선택 가치 실패
- 적 공격이 다른 적에게 맞았음을 3명 중 2명 이상 인지하지 못하면 핵심 인과 피드백 실패

### 1차 플레이테스트 가설

1. HP 6/피해 1은 여섯 번의 실수를 허용하지만, 다섯 적의 동시 압박 때문에 체감은 더 가혹할 수 있다.
2. 대시 쿨다운 1.2초가 개별 공격 경고 0.7–0.75초보다 길도록 설정한 의도는 위치 선정을 압박하는 것이다. 매 공격에 대시를 쓸 수 있는지는 공격당 대시 준비 상태/사용량 또는 전체 조우 공격 케이던스를 측정해 검증한다. 위치 선정을 만들면 성공, 기다림만 만들면 실패다.
3. Haste의 이동×1.5와 투사체×1.35는 인과를 읽기 전에 피격을 만들 위험이 있다.
4. Giant의 범위×1.4는 friendly fire 성공 면적을 넓히지만 벽 근처 이동 자유를 과도하게 줄일 수 있다.

## 몬스터 방향 애니메이션 v002 런타임 계약

- Dasher·Archer·Minion의 이동 표현은 transform 변화량을 추측하지 않고, 각 AI가 명시적으로 소유하는 `LocomotionMode`(`Idle`/`Walk`/`Run`)와 정규화된 `IntendedFacing`을 사용한다. 벽·기둥 충돌로 실제 이동이 막혀도 이 의도는 바뀌지 않으므로 idle 프레임이 미끄러지는 것처럼 보이거나 방향이 튀지 않아야 한다.
- 역할별 기본 속도는 Dasher Walk/Run=`1.0/1.5`, Archer=`1.0/1.5`, Minion=`0.8333333/1.25`다. Archer는 v003 보행 리듬과 실제 접근 체감이 일치하도록 Walk/Run을 2배로 조정했으며, Haste는 두 속도를 같은 이동 배율로 다시 계산한다. `RunSpeed`는 항상 `WalkSpeed`보다 커야 하며 Dasher의 실행 이동은 별도 `ChargeSpeed` 계약을 유지한다.
- 공격 Warning 중에는 최신 목표 방향을 의도로 갱신하고, Lock 이후에는 잠긴 방향이 AttackCharge·AttackExecute·Recover 표현을 우선한다. 공격이 Idle로 돌아오거나 취소되면 최신 유효 `IntendedFacing`이 다시 보인다.
- 공격 판정과 상태 전이는 계속 게임플레이 로직이 소유한다. `AttackPhase.Executing` 진입과 `AttackExecute` 프레임 0 적용은 같은 tick에 동기화하지만, 애니메이션 프레임이 판정·투사체·회복 전이를 요청하지 않는다. 논리 구간이 길면 비반복 Charge/Execute/Recover는 마지막 프레임을 유지한다.
- Minion은 판정 tick에 피해를 정확히 한 번 적용하고, 논리 소유 `Executing`을 0.25초 유지한다. 표현은 독립적으로 설정된 24fps의 6개 Execute 프레임을 한 번 재생한 뒤 Recover로 전환한다. 다음 공격 가능 시각은 기존 케이던스를 보존하도록 `판정 시각 + RecoveryDuration + AttackCooldown`의 절대 시각에 고정하며, 0.25초 표현 구간을 다시 더하지 않는다.
- M1과 M2의 Dasher·Archer·Minion 프리팹은 동일한 세 v002 `DirectionalAnimationSet` 자산을 공유하지만, 프리팹과 씬의 소유권·토폴로지는 계속 분리한다.
- 이 계약이 게임플레이 타이밍을 바꾸지 않았음은 v002 이전 커밋(`dd7ad95`)과 후보(`4ef7d30`)에서 동일한 하네스(`Assets/Tests/PlayMode/MonsterLocomotionTimingTests.cs`, 캡처 스텝을 0.02초 고정 스텝에 정렬)로 7개 지표를 계측해 확인했다. 추격·후퇴·준비 시간은 동일하고, 케이던스 4개 지표만 정확히 2프레임(0.04초, -0.78~-1.18%) 빨라진다. 이는 v002가 다음 공격 가능 시각을 회복 완료 시점이 아니라 판정 시각 기준 절대 시각으로 고정하기 때문이며, 전부 ±10% 허용 범위 안이다. 측정 원본과 해시는 `Docs/AI_Usage/edits/monster_directional_animation_live_review_v002.json`에 있다.
## 플레이테스트 계획

- 대상: 빌드 사전 노출이 없는 fresh tester 3명
- 코칭: 조작표 외 전략 설명 금지
- 관찰: 첫 축복 시각, 첫 friendly fire 시각, 첫 영혼 수집, 사망 원인 진술, 완주 시도 수, 축복 선택 비율
- 성공: M1 파일럿 3/3이 tester별 온보딩 체크리스트를 모두 충족하고 각자 5회 이내 완주. `>90%`는 `N≥20`의 별도 fresh cohort에서만 계산
- 오디오 블라인드: DasherReady/ArcherReady/ExitOpened 순서를 각 tester가 식별하고 중복·누락이 없어야 한다.
- 성능: 브라우저 매트릭스 각 셀에서 반열린 [t, t+1초) 1초 버킷 60개를 수집하며, 각 버킷은 45 FPS 이상이어야 한다.

## 종합 판정

**구조적 판정: M1 코어 루프는 구현·시험 가능한 형태로 성립한다.** 적 강화가 단순 버프가 아니라 공격 유도 레버로 연결되고, 영혼·출구가 세션 목표를 닫는다. 다만 첫 성공 보장, 텔레그래프 인지, 두 축복의 선택률, 다섯 적 동시 압박은 사람 플레이테스트 전까지 검증되지 않은 핵심 위험이다. 따라서 이 문서는 M2 진입을 승인하지 않으며, fresh tester와 사용자 소유 게이트 판단을 요구한다.

## 변경 이력

| 버전 | 날짜 | 변경 |
|---|---|---|
| 1.0 | 2026-07-13 | M1 루프, 메커니즘, 상호작용, 온보딩, 밸런스 가설, 파손 조건, 플레이테스트 기준 최초 평가 |
| 1.1 | 2026-07-13 | `[PLACEHOLDER]` 범위를 게임플레이 튜닝으로 한정하고 고정 45 FPS 증거 수용 하한을 제외했으며, 온보딩 가설·대시 압박 측정·Haste 쿨다운 해석·1초 버킷 성능 계약을 정정 |
| 1.2 | 2026-07-13 | 코드와 독립적으로 실행 가능한 토큰·카드 기반 종이 프로토타입, 성공 조건, 파손 조건을 추가 |
| 1.3 | 2026-07-13 | 실행 가능한 종이판 초기 상태·공간/initiative/전투/종료 규칙, 누락된 플레이어 판타지, 축복×아키타입/교차 중첩 계약, 3/3 파일럿과 N≥20 온보딩 지표를 명확화 |
| 1.4 | 2026-07-13 | 아키타입 이동·telegraph·lock/ID·sweep·즉시 피해 절차, 대기·대시 tick, 영혼/출구 trigger, 슬롯 반환, 공격 스냅숏, Haste Warning 하한과 Giant HP 공식을 실행 가능한 런타임 대응 규칙으로 명확화 |
| 1.5 | 2026-07-31 | v002 로코모션 계약의 baseline(`dd7ad95`)·candidate(`4ef7d30`) 타이밍 계측 결과를 추가하고, 케이던스 2프레임 차이의 원인과 ±10% 판정을 기록 |
