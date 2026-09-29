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

성장 시간은 심은 시점이 아닌 **최초 물주기 알림을 받은 시점**부터 계산한다. 심은 뒤 물을 주기 전에는 PotatoCovered 상태로 계속 대기한다.

Inspector의 Stage Mappings에 지정한 단계에 진입하면 `ground.TrySetCellState(cell, mappedState)`를 호출한다. 기본 매핑: 0초 PotatoCovered, 40초 SproutFirst, 80초 SproutSecond, 150초 SproutThird, 220초 EarlyHarvest, 260초 ReadyToHarvest, 300초 LateHarvest.

`IsReadyToHarvest`는 260초부터 true다. `Progress`는 전체 300초 경과율이며, 적기에 도달해도 300초의 늦은 수확 상태까지 갱신한다. `BoarDugField`는 시간에 따른 성장 단계에 포함하지 않는다. 멧돼지 등으로 농사가 취소되면 이벤트 담당자가 `StopFarming`을 호출해야 한다.

이 시스템은 감자 GameObject나 컴포넌트를 생성하지 않는다. 각 타일은 좌표, 식재 시각(PlantedAt), 현재 단계(Stage)를 가진 순수 데이터다. 중앙 시스템은 공용 WorldClock의 TimeAdvanced 이벤트를 한 번 구독해 성장 중인 타일을 갱신한다. 성숙한 타일은 단계 계산을 건너뛰고 수확까지 등록을 유지한다. Inspector의 Clock을 연결하고, 기본 Ground도 연결하면 좌표만 전달할 수 있다. 명시적 초기화용 3인자 TryStartFarming(ground, cell, worldClock)도 유지하며, 같은 시스템에 다른 시계를 전달하면 false를 반환한다. 비활성화 중 진행된 시간은 재활성화 시 반영한다. 저장, 네트워크 동기화, 상호작용 입력 및 타일 렌더링은 포함하지 않는다.

검증 메뉴: `Tools > Project Potato > Validate Farming Tile Integration`.

## 물주기 상호작용 연결

Inspector에서 시스템의 Ground와 Clock을 연결한다. 상호작용 담당자는 실제 물주기 성공 후 해당 셀 좌표를 전달한다.

```csharp
// 1. 식재 성공 시 등록 (성장은 아직 대기).
farmingSystem.TryStartFarming(cell);

// 2. 이후 물주기 상호작용이 성공한 시점에 알림.
bool growthStarted = farmingSystem.TryWater(cell);
// Ground를 직접 지정하는 경우: farmingSystem.TryWater(ground, cell);
```

`TryWater`는 처음 물을 받은 등록 타일에 대해 true를 반환한다. 미등록 좌표, 범위 밖 좌표, 누락된 참조, 이미 물을 받은 타일에는 false를 반환한다. false는 실제 물주기 연출의 실패를 뜻하지 않고, 이번 호출로 새로 시작된 성장이 없다는 뜻이다. 중복 호출은 성장 시간을 초기화하지 않는다. 심기 전에 전달한 물주기는 저장하지 않으며, 재식재하면 다시 물주기가 필요하다.

이 메서드는 물 사용량, 애니메이션, 도구 입력 또는 GroundGrid의 물 상태를 변경하지 않는다. 다른 작업자가 그 상호작용을 처리하고 **성공했을 때만** 호출한다. 실제 물주기 메서드가 성공 여부를 반환한다면 다음처럼 연결한다.

```csharp
if (ground.TryWater(cell))
    farmingSystem.TryWater(ground, cell);
```

```csharp
if (farmingSystem.TryGetFarmingTile(cell, out var tile))
{
    bool waitingForWater = !tile.IsWatered;
    double plantedAt = tile.PlantedAt;         // 식재 월드 시각
    double? startedAt = tile.GrowthStartedAt;  // 물주기 전에는 null
    double growthSeconds = tile.AgeSeconds;   // 물주기 전에는 0
}
```

첫 물주기 후 40·80·150·220·260·300 게임 초에 각 다음 단계로 전환한다. 매 단계마다 다시 물을 줄 필요는 없다. 일시정지와 배속은 WorldClock을 따른다.

