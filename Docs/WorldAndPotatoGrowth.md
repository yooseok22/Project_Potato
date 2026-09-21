# 월드 시간과 감자 성장

## 사용

- 현재 작업 씬: `Assets/Scenes/Yuseok/Yuseok.unity`.
- `Assets/Prefabs/World/WorldSystem.prefab`: 씬에 하나 배치한다. 시작 06:00, 하루 600초, 낮 06:00~18:00. Inspector에서 설정 가능.
- `Assets/Prefabs/Farming/PotatoGrowth.prefab`: 타일이나 렌더러가 없는 독립 감자. 기본적으로 Play 시작 시 자동 식재하며 씬의 활성 WorldClock이 하나일 때 자동 연결한다. 여러 개라면 Clock을 명시적으로 지정한다.
- Console에서 `[World]`, `[Potato]` 로그 확인. 단계 변경 로그와 함께 기본 5 게임 초마다 성장 시간, 다음 단계까지 남은 시간, 월드 시각을 출력한다. PotatoGrowth의 Progress Log Interval을 0으로 설정하면 주기 로그를 끈다.
- Player Settings의 Run In Background를 켜 두었으므로 다른 창을 보는 동안에도 진행한다. 에디터 Pause 또는 WorldClock의 Paused를 켜면 멈춘다.
- Play 중 WorldClock의 Speed로 배속, Paused로 공용 시간 정지. 컴포넌트 컨텍스트 메뉴의 Debug에서 40초 건너뛰기, 일시정지, 수확 및 재식재 가능.

## 시간 규칙

게임 시간 1초는 기본적으로 플레이 1초다. Unity Time.timeScale 및 WorldClock.Speed를 함께 반영한다. WorldClock의 하루 길이는 달력 변환에만 사용하며 감자 성장 속도와 독립적이다. 밤에도 동일하게 성장한다.

단계 시작 시각: 흙 언덕 0초, 새싹 40초, 큰 잎 2개 80초, 큰 잎 4개 150초, 큰 잎 6개 220초, 싱싱한 큰 잎 6개 260초, 시든 잎 6개 300초. 시든 잎은 수확 가능 상태이며 시간이 지나도 유지된다.

`AdvanceBy(seconds)`는 수면 등 명시적인 시간 건너뛰기용으로, 일시정지 여부나 배속과 무관하게 지정한 게임 초를 더한다. 감자는 건너뛴 성장 단계 이벤트를 순서대로 발생시킨다. 월드의 낮/밤 이벤트는 건너뛰기 후 최종 상태가 바뀌었을 때만 발생한다.

## 타일 시스템 연결

자동 식재가 필요 없으면 PotatoGrowth의 Plant On Start를 끈다. 타일 측에서 프리팹을 생성한 뒤 `potato.Plant(worldClock)`를 호출한다. 진행 중 중복 식재는 false를 반환한다. `StageChanged`, `HarvestReady` 이벤트로 그래픽/UI를 연결하고 `TryHarvest()` 성공 시 타일과 인벤토리를 갱신한다. 성장 컴포넌트는 타일이나 인벤토리를 직접 참조하지 않는다.

비활성화 중에도 월드 시간이 흐르면 다시 활성화할 때 성장 상태가 따라잡는다. 씬을 새로 로드하면 시간도 새로 시작한다. 저장/불러오기, 오프라인 성장, 씬 간 시간 유지, 멀티플레이 시간 동기화 및 실제 조명 변화는 이번 구현 범위에 포함하지 않았다.

검증: Unity 메뉴 `Tools > Project Potato > Validate World and Potato Growth`. 별도 Preview Scene에서 경계 시각, 일시정지, 배속, 건너뛰기, 수확, 재식재, 비활성화 후 복귀, 하루 길이 독립성을 검사한다.
