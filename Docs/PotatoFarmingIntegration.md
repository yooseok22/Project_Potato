# 타일 농사 연결

`PotatoFarmingSystem` 하나가 여러 타일의 감자 성장을 관리한다. 타일 키는 `(GroundGrid, Vector2Int)`이며 좌표는 월드 좌표가 아닌 그리드 셀 좌표다. 같은 그리드에는 하나의 시스템을 사용한다.

## 상호작용 담당자가 호출할 API

식재 상호작용 성공 시 다음을 호출한다. 도구/인벤토리/땅 상태 조건은 상호작용 담당자가 판단한다.

```csharp
bool started = farmingSystem.TryStartFarming(ground, cell);
// Inspector에 Ground와 Clock을 연결했다면 좌표만 전달 가능:
bool startedWithDefaults = farmingSystem.TryStartFarming(cell);
```

두 호출은 대안이다. 같은 식재에서 둘 다 호출하지 않는다. 중복 좌표, 범위 밖 좌표, null 의존성은 false를 반환하며 기존 성장 시간을 초기화하지 않는다. 이미 PotatoCovered로 바뀐 타일도 등록할 수 있다.

```csharp
if (farmingSystem.TryGetFarmingTile(ground, cell, out var tile))
{
    var stage = tile.Stage;
    var age = tile.AgeSeconds;
    var ready = tile.IsReadyToHarvest;
}

foreach (var tile in farmingSystem.GetFarmingTiles())
    Debug.Log($"{tile.Ground.name}: {tile.Cell}, {tile.Stage}");

farmingSystem.FarmingStageChanged += (grid, coordinate, stage) =>
    Debug.Log($"{coordinate}: {stage}");
```

이벤트는 식재 즉시 및 각 성장 단계 변경마다 발생한다. 초기 이벤트도 받으려면 시작 전에 구독한다. 수확/취소 처리가 끝나면 `StopFarming(ground, cell)`을 호출해 등록을 제거한다. 이 메서드는 타일 상태를 변경하지 않는다. 수확 가능 여부는 `tile.IsReadyToHarvest`로 조회한다.

## 타일 상태 전달

Inspector의 Stage Mappings에 지정한 단계에 진입하면 `ground.TrySetCellState(cell, mappedState)`를 호출한다. 현재 GroundCellState에는 성장별 값이 없으므로 기본 매핑은 `SoilMound → PotatoCovered` 하나다. 나머지 여섯 단계도 정상 성장하며 좌표와 함께 FarmingStageChanged 이벤트로 전달되지만, 대응하는 GroundCellState가 추가되고 매핑될 때까지 타일 상태는 유지된다.

타일 담당자가 새싹/잎 상태 enum 및 렌더링을 추가하면 Stage Mappings에 해당 값을 연결한다. enum 정수 값을 임의로 전달하거나 기존 흙 상태를 새싹 상태로 대신 사용하지 않는다.

이 시스템은 감자 GameObject나 컴포넌트를 생성하지 않는다. 각 타일은 좌표, 식재 시각(PlantedAt), 현재 단계(Stage)를 가진 순수 데이터다. 중앙 시스템은 공용 WorldClock의 TimeAdvanced 이벤트를 한 번 구독해 성장 중인 타일을 갱신한다. 성숙한 타일은 단계 계산을 건너뛰고 수확까지 등록을 유지한다. Inspector의 Clock을 연결하고, 기본 Ground도 연결하면 좌표만 전달할 수 있다. 명시적 초기화용 3인자 TryStartFarming(ground, cell, worldClock)도 유지하며, 같은 시스템에 다른 시계를 전달하면 false를 반환한다. 비활성화 중 진행된 시간은 재활성화 시 반영한다. 저장, 네트워크 동기화, 상호작용 입력 및 타일 렌더링은 포함하지 않는다.

검증 메뉴: `Tools > Project Potato > Validate Farming Tile Integration`.

