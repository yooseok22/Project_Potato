# 감자 수확 시스템 구현 계획 — 작업 소유권 분리본

## 1. 작업 원칙

이 문서는 우리 쪽 코드와 친구분 코드를 분리한 구현 계획이다.

- 우리 쪽에서는 친구분 파일을 수정하지 않는다.
- 성장 진행, 성장 정지, 감자 체력 원본 데이터는 친구분 시스템의 책임으로 둔다.
- 우리 쪽에서는 타일 상태, 도구 상호작용, 수확 시기·손상 상태 판정, 보상 계산과 콘솔 출력만 담당한다.
- 친구분 시스템과는 공개 API만 사용해 연결한다.
- 필요한 공개 API가 아직 없다면 친구분이 구현할 때까지 해당 연결 작업을 보류한다.

## 2. Git 기록 기준 코드 소유권

### 친구분 영역 — 우리 쪽 수정 금지

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/Farming/PotatoFarmingSystem.cs` | 성장 및 체력 데이터 중앙 관리 |
| `Assets/Scripts/Farming/PotatoGrowthRules.cs` | 성장 단계와 시간 규칙 |
| `Assets/Scripts/Combat/IDamageable.cs` | 피해 인터페이스 |
| `Assets/Editor/PotatoFarmingChecks.cs` | 친구분 농사 시스템 검증 |

위 파일은 우리 쪽 구현에서 직접 수정하지 않는다.

### 우리 쪽 작업 가능 영역

| 파일 | 담당 작업 |
| --- | --- |
| `Assets/Scripts/Farming/GroundGrid.cs` | 타일 상태 전이, 수확 시기 및 포크 결과 판정 |
| `Assets/Scripts/Farming/FarmingTargetSelector.cs` | 낫·포크·맨손 E 상호작용 연결 |
| `Assets/Scripts/Farming/SickleHarvestPreviewController.cs` | 낫·포크 사용 가능 타일의 초록색 표시 |
| `Assets/Scripts/Player/PlayerToolController.cs` | 입력 전달과 수확 프롬프트 |
| 새 수확 전용 스크립트 | 수확 메타데이터, 보상 계산, 콘솔 출력 |
| 새 검증 스크립트 | 우리 쪽 기능만 독립 검증 |

소유자가 불분명한 다른 파일은 확인 없이 수정하지 않는다.

## 3. 역할 분담 요약

| 기능 | 우리 쪽 | 친구분 |
| --- | --- | --- |
| GN/GB 003~005 낫 상호작용 | 구현 | — |
| 낫 사용 가능 초록색 표시 | 구현 | — |
| 낫 사용 시 수확 시기 판정 | 구현 | — |
| 낫 사용 후 타일을 GN_F/GB_F로 변경 | 구현 | — |
| 개별 감자의 성장 정지 | 정지 API 호출만 담당 | 실제 정지 로직 구현 |
| 성장 정지 후 체력 보존 | 결과 사용 | 데이터 보존 구현 |
| 수확 시기·흙 미덮음 피해 요청 | 기존 `TryDamage` 호출 | 피해 적용 및 HP 보관 |
| 감자 포크 타일 전이 | 구현 | — |
| 정상/상처 입은 감자 판정 | 구현 | — |
| E 수확 입력과 G_001 전환 | 구현 | 완료 시 HP 반환 API 제공 |
| 체력별 개수·등급 계산 | 구현 | — |
| 수확 결과 콘솔 출력 | 구현 | — |
| 벌레·밟힘·멧돼지 피해 발생 판정 | 해당 시스템 소유자 | 해당 시스템 소유자 |
| 피해량의 실제 HP 반영 | API 호출 | `PotatoFarmingSystem`에서 처리 |

## 4. 친구분이 해야 하는 작업

친구분은 `PotatoFarmingSystem` 안에서 성장 정지만 담당한다.

### 필요한 최소 공개 API

정확한 메서드명은 친구분이 결정할 수 있지만, 다음 계약을 만족하는 API가 하나 필요하다.

```csharp
public sealed class PotatoFarmingSystem : MonoBehaviour, IPotatoGrowthStopService
{
    public bool TryFreezeGrowthForHarvest(GroundGrid targetGround, Vector2Int cell);
}
```

`IPotatoGrowthStopService` 인터페이스는 우리 쪽 `PotatoHarvestCoordinator.cs`에 정의한다. 친구분은 기존 클래스에 인터페이스와 메서드를 구현하기만 하면 된다.

### API 동작 조건

- 지정한 한 셀의 감자 성장만 정지한다.
- `FarmingTile` 등록 정보는 삭제하지 않는다.
- 현재 HP를 그대로 보존한다.
- 성장 정지 이후에도 `TryDamage(ground, cell, amount)` 호출은 정상 동작해야 한다.
- 성장 정지 이후 `TryCompleteHarvest(ground, cell, out potatoHp)`가 현재 HP를 반환하고 등록을 제거해야 한다.
- `RefreshGrowth()`는 성장 정지된 타일의 성장 단계를 더 이상 진행하지 않는다.
- 존재하지 않는 타일, 이미 수확 완료된 타일, 중복 정지 요청에는 `false`를 반환한다.

### 친구분 검증 범위

친구분 소유 테스트에서 다음만 검증하면 된다.

- 감자 하나만 정지되고 다른 감자는 계속 성장하는지
- 정지된 감자의 HP가 유지되는지
- 정지 후에도 `TryDamage`가 동작하는지
- `TryCompleteHarvest`가 HP를 반환하고 데이터를 한 번만 제거하는지

수확 시기, 타일 분기, 보상 확률, 콘솔 문구는 친구분 담당이 아니다.

## 5. 우리 쪽에서 할 수 있는 작업

### 5.1 새 수확 전용 데이터

친구분 코드 바깥에 새 타입을 만든다.

- `PotatoHarvestTiming`: Early, Optimal, Late
- `PotatoDigCondition`: Normal, Injured
- `PotatoHarvestGrade`: OneStar, TwoStar, ThreeStar
- `PotatoHarvestResult`: 셀, 수확 시기, 손상 상태, HP, 개수, 각 감자의 등급
- `PotatoHarvestCoordinator`: 낫·포크·E 수확 흐름을 조정하는 우리 쪽 컴포넌트

`PotatoHarvestCoordinator`는 수확 시기를 자체 딕셔너리에 저장한다. 친구분의 `FarmingTile`에 수확 시기 필드를 추가하지 않는다.

키는 기존 시스템과 동일하게 `(GroundGrid, Vector2Int)`를 사용한다.

### 5.2 낫 사용 시 수확 시기 판정

| 낫 사용 전 타일 | 수확 시기 | 체력 피해 |
| --- | --- | ---: |
| `Ground GB_003`, `Ground GN_003` | 이른 수확 | -20 |
| `Ground GB_004`, `Ground GN_004` | 적기 수확 | 0 |
| `Ground GB_005`, `Ground GN_005` | 늦은 수확 | -40 |

추가 규칙:

- GB 계열에서 낫을 사용하면 흙 미덮음 피해 -10을 한 번 추가한다.
- GN 계열은 흙 미덮음 피해를 받지 않는다.
- 수확 시기와 GB/GN 여부는 낫을 사용하기 전 타일에서 판정한다.
- 성공 후 GB 계열은 `GB_F`, GN 계열은 `GN_F`로 전환한다.
- 타일이 이미 F 상태라면 낫 상호작용을 다시 허용하지 않는다.

우리 쪽 처리 순서:

1. `GroundGrid`에서 현재 타일이 낫 사용 가능한지 검사한다.
2. 현재 타일로 수확 시기와 GB/GN 여부를 계산한다.
3. 친구분의 성장 정지 API를 호출한다.
4. 성장 정지가 성공한 경우에만 타일을 GB_F 또는 GN_F로 변경한다.
5. 이른/늦은 수확 피해와 흙 미덮음 피해를 기존 `TryDamage`로 요청한다.
6. 수확 시기를 우리 쪽 `PotatoHarvestCoordinator`에 저장한다.

친구분의 성장 정지 API가 준비되지 않았다면 3번 이후 연결은 진행하지 않는다.

### 5.3 감자 포크 상태 전이

기존 타일 흐름은 우리 쪽 `GroundGrid`에서 처리한다.

- GB_F/GN_F → HY 또는 HN
- HY → ME
- HN → MY 또는 MN
- MY → LE
- MN → LY
- ME/LE/LY에서 감자 포크를 한 번 더 사용하면 G_001

맨손 E 수확 가능 상태와 감자 수식어:

| 타일 상태 | 감자 수식어 |
| --- | --- |
| HY, MY, LY | 정상 감자 |
| ME, LE | 상처 입은 감자 |

GB/GN 접두어와 관계없이 같은 규칙을 적용한다.

ME/LE/LY에서 포크를 한 번 더 사용해 G_001로 만든 경우에는 보상을 지급하지 않는다. 대신 기존 `StopFarming(ground, cell)` 공개 API를 호출해 남은 재배 데이터만 정리한다. 이를 위해 포크 상호작용 연결부가 `PotatoFarmingSystem` 참조를 받도록 우리 쪽 코드만 변경한다.

### 5.4 맨손 E 수확

E 수확은 우리 쪽 조정 컴포넌트에서 다음 순서로 처리한다.

1. 현재 타일이 HY/MY/ME/LY/LE인지 검사한다.
2. 타일 상태에서 정상/상처 입은 수식어를 판정한다.
3. 우리 쪽 저장소에서 이른/적기/늦은 수확 정보를 조회한다.
4. 기존 `TryCompleteHarvest(ground, cell, out potatoHp)`를 호출한다.
5. 성공하면 타일을 G_001로 전환한다.
6. 반환된 HP로 수확 개수와 각 감자의 별 등급을 계산한다.
7. 결과를 콘솔에 한 번 출력한다.
8. 우리 쪽에 저장한 수확 시기 데이터를 삭제한다.

`TryCompleteHarvest`가 실패하면 타일 변경, 보상 계산, 로그 출력을 하지 않는다.

## 6. 체력 피해 연결

우리 쪽에서는 HP를 직접 수정하지 않고 친구분이 이미 제공한 API만 호출한다.

```csharp
farmingSystem.TryDamage(ground, affectedCell, damageAmount);
```

| 피해 요인 | 피해량 | 담당 |
| --- | ---: | --- |
| 이른 수확 | 20 | 우리 쪽에서 낫 성공 시 API 호출 |
| 적기 수확 | 0 | 호출 없음 |
| 늦은 수확 | 40 | 우리 쪽에서 낫 성공 시 API 호출 |
| 흙 안 덮음 | 10 | 우리 쪽에서 GB 낫 성공 시 API 호출 |
| 벌레 안 쫓음 | 10 | 벌레 시스템 소유자가 API 호출 |
| 발로 밟힘 | 10 | 밟힘 시스템 소유자가 API 호출 |
| 멧돼지 공격 | 35 | 멧돼지 시스템 소유자가 API 호출 |

발로 밟힘 시스템에는 다음 호출만 전달한다.

```csharp
farmingSystem.TryDamage(ground, steppedCell, trampleDamage);
```

해당 스크립트가 친구분 소유라면 우리 쪽에서는 수정하지 않고 호출 규약만 전달한다. 매 프레임 중복 피해 방지는 밟힘 시스템 소유자가 셀 진입 또는 재피해 대기시간으로 처리한다.

## 7. 체력별 보상 계산 — 우리 쪽 담당

| 최종 체력 | 수확 개수 | 개별 감자 등급 확률 |
| --- | --- | --- |
| 100 | 2~3개 | 1성 70%, 2성 20%, 3성 10% |
| 90~99 | 1~3개 | 1성 85%, 2성 15% |
| 80~89 | 1~2개 | 1성 100% |
| 1~79 | 1개 | 1성 100% |
| 0 | 0개 | 파헤쳐진 감자, 보상 없음 |

- 개수 범위는 양 끝을 포함한다.
- 여러 개가 나오면 각 감자의 별 등급을 독립적으로 추첨한다.
- HP는 계산 전에 0~100으로 제한한다.
- 보상 계산은 친구분 코드와 분리된 순수 함수로 만든다.
- 현 단계에서는 실제 인벤토리를 변경하지 않고 콘솔 출력만 한다.

## 8. 콘솔 출력

정상 예시:

```text
[Potato Harvest] Cell=(12, 8), 수확시기=적기 수확, 감자상태=정상, HP=100/100, 수확개수=3, 등급=[1성, 2성, 1성]
```

상처 입은 감자 예시:

```text
[Potato Harvest] Cell=(12, 8), 수확시기=이른 수확, 감자상태=상처 입은, HP=70/100, 수확개수=1, 등급=[1성]
```

체력 0 예시:

```text
[Potato Harvest] Cell=(12, 8), 수확시기=늦은 수확, 감자상태=상처 입은, HP=0/100, 수확개수=0, 결과=파헤쳐진 감자
```

## 9. 테스트 소유권 분리

### 우리 쪽 새 검증 파일

기존 친구분의 `PotatoFarmingChecks.cs`를 수정하지 않고, 별도 파일을 새로 만든다.

예정 파일:

`Assets/Editor/YoungSeokPotatoHarvestChecks.cs`

검증 항목:

- GB/GN 003~005의 이른/적기/늦은 판정
- GB 계열 흙 미덮음 피해 요청값
- HY/MY/LY 정상, ME/LE 상처 입은 판정
- 포크 상태 전이
- 체력 경계값 100, 99, 90, 89, 80, 79, 1, 0의 보상 규칙
- E 수확 결과 로그 데이터 생성
- 중복 수확 차단
- 포크로 G_001 전환 시 보상 없음

### 친구분 검증 파일

친구분은 본인 테스트에서 성장 정지와 HP 보존만 검증한다. 우리 쪽에서 친구분 테스트 파일을 수정하지 않는다.

## 10. 구현 순서

1. 친구분에게 성장 정지 API 계약을 전달한다.
2. 우리 쪽 수확 시기·손상 상태·보상 결과 타입을 새 파일로 만든다.
3. `GroundGrid`의 타일 판정 및 포크 전이를 정리한다.
4. 별도 `PotatoHarvestCoordinator`를 구현한다.
5. 낫 상호작용에서 친구분 성장 정지 API를 호출하도록 연결한다.
6. 기존 `TryDamage`와 `TryCompleteHarvest`만 사용해 HP 및 완료 처리를 연결한다.
7. E 수확 결과를 콘솔에 출력한다.
8. 우리 쪽 전용 검증 파일을 새로 추가한다.
9. 친구분의 성장 정지 구현이 합쳐진 뒤 통합 플레이 테스트를 진행한다.

## 11. 완료 조건

- 친구분 소유 파일을 우리 쪽에서 수정하지 않는다.
- GN/GB 003, 004, 005가 각각 이른/적기/늦은 수확으로 기록된다.
- 친구분 API를 통해 성장만 멈추고 HP는 E 수확까지 보존된다.
- HY/MY/LY는 정상, ME/LE는 상처 입은 감자로 판정된다.
- E 수확 시 HP에 맞는 개수와 별 등급이 콘솔에 한 번 출력된다.
- 체력 0이면 보상 0개와 파헤쳐진 감자 결과가 출력된다.
- 수확 후 타일은 G_001이며 같은 감자를 다시 수확할 수 없다.
- 기존 초록색 상호작용 표시가 낫, 포크, 맨손 E 조건과 일치한다.

## 12. 현재 작업 트리 주의사항

`PotatoFarmingSystem.cs`와 `PotatoFarmingChecks.cs`에는 이전 작업에서 우리 쪽이 잘못 추가한 미커밋 변경이 남아 있다. 이 재작성 과정에서는 두 파일을 더 수정하거나 원복하지 않았다.

그 변경을 제거할 때는 친구분의 원본 전체를 되돌리지 않는다. 별도 승인 후 `PotatoFarmingSystem.cs`의 `ForwardStage()` 흙덮기 성장 분기와 `PotatoFarmingChecks.cs`에 추가한 타일 상호작용 검사만 정확히 원복해야 한다.
