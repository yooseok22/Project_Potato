# 저장 / 불러오기

## 사용 방법

농사 씬을 Play하면 `SaveGameController`가 자동 생성됩니다. 씬에 직접 배치해도 됩니다.
컨트롤러 컴포넌트의 Inspector 메뉴에서 `Save/Slot 1`, `Save/Slot 2`, `Load/Slot 1`, `Load/Slot 2`를 실행합니다.
게임 UI에서는 아래 API를 호출하면 됩니다. 이번 작업에는 슬롯 선택 화면은 포함하지 않았습니다.

```csharp
bool saved = SaveGameController.Instance.Save(1);
bool loaded = SaveGameController.Instance.Load(1);
string error = SaveGameController.Instance.LastError;
```

슬롯 번호는 **1, 2**입니다. `Saved`, `Loaded` 이벤트로 UI를 갱신할 수 있습니다.
파일은 `Application.persistentDataPath/Saves/slot-1.json`, `slot-2.json`에 저장됩니다.
정상 덮어쓰기 시 이전 파일을 `.bak`에 보관합니다. 손상 파일을 불러오면 현재 상태를 유지하고 실패를 반환합니다.
이전 저장으로 돌아가려면 `LoadBackup(1)` 또는 Inspector의 `Load Backup/Slot 1`을 실행합니다.
체크섬은 우발적 손상 탐지용이며 암호화나 조작 방지 기능은 아닙니다.

저장한 농사 씬들을 먼저 연 뒤 불러오세요. 씬 로딩 및 동적 오브젝트 생성은 호출하는 쪽의 책임입니다.
현재는 오프라인 저장입니다. 네트워크 세션 중에는 호스트를 포함하여 저장·불러오기를 거부합니다.
호스트 농장과 각 플레이어 인벤토리를 저장하려면 권한·플레이어 식별·클라이언트 복원 동기화 설계가 추가로 필요합니다.

## 농사 저장 범위

- 모든 로드된 `GroundGrid`: 땅 상태, 빈 경작지, 구멍, 흙 덮기 횟수, 물주기 상태, 수확 중간 상태.
- `PotatoFarmingSystem`: 각 감자의 좌표, 심은 시각, 최초 물준 시각, 성장 정지 시각과 누적 정지 시간, 성장 단계, 현재/최대 체력.
- `PotatoHarvestCoordinator`: 낫질 당시 수확 시기, 흙 덮기 추적 정보. 불러온 뒤 쇠스랑 작업과 수확을 이어갈 수 있습니다.
- `WorldClock`: 경과 시간, 달력 설정, 속도, 일시정지.

불러오기는 기존 농사를 전부 교체합니다. 심기나 낫질을 재실행하지 않으므로 피해와 수확 페널티를 중복 적용하지 않습니다.
시간은 저장 시점으로 복원하며 게임을 꺼둔 동안의 현실 시간은 성장에 더하지 않습니다.
멧돼지, 플레이어 위치, 월드 드롭 아이템 및 독립 샘플 컴포넌트 `PotatoGrowth`는 이번 저장 범위에 포함하지 않습니다.

오브젝트는 `SaveIdentity`의 영구 ID로 식별할 수 있습니다. 컴포넌트를 추가하면 ID가 생성됩니다.
복제한 오브젝트에는 별도의 ID를 지정해야 하며, 런타임 생성 오브젝트는 매 실행마다 같은 논리 ID를 부여해야 합니다.
현재 영구 ID가 없는 씬 오브젝트에는 `씬 경로 + 계층 이름 + 컴포넌트 타입`을 사용합니다.
이 경우 이름/계층/씬 경로를 바꾸면 기존 저장을 읽을 수 없습니다. 출시용 씬에는 영구 ID를 부여하고 씬/프리팹에 저장하는 것을 권장합니다.
중복 ID, 누락된 오브젝트, 달라진 그리드 크기·위치·셀 크기는 복원 전에 거부합니다.

## 인벤토리: 플레이어 구현과 독립된 계약

`InventoryData`는 `ownerId`와 아이템 목록을 가집니다. `ownerId`에는 저장 사이에 유지되는 플레이어 ID를 사용합니다.
실행마다 달라지는 Netcode ClientId나 Unity InstanceID를 사용하지 마세요.

| 필드 | 역할 |
| --- | --- |
| `itemId` | 아이템 카탈로그의 안정적인 ID. 이름, 프리팹 참조 대신 저장 |
| `quantity` | 같은 속성의 스택 수량. 1 이상 |
| `instanceId` | 내구도 등 개별 상태가 있는 아이템의 선택적 고유 ID. 지정 시 수량은 1 |
| `slot` | 슬롯을 보존할 때 지정. 슬롯 개념이 없으면 -1 |
| `propertiesVersion` | 개별 속성 JSON의 버전 |
| `propertiesJson` | 등급, 내구도 등 선택적인 속성 JSON |

아이템 정의(이름/아이콘/설명/프리팹)는 저장하지 않습니다. 플레이어 담당 코드가 `itemId`로 기존 카탈로그를 조회하여 복원합니다.
동일 ID여도 등급/속성이 다르면 별도 항목으로 저장하고, 완전히 같은 스택만 플레이어 인벤토리 쪽에서 합칩니다.
알 수 없는 아이템 ID의 처리(실패, 대체 아이템 등)도 플레이어 어댑터의 검증 정책으로 정합니다.

플레이어 코드는 `IInventorySaveTarget`을 구현합니다.

```csharp
public interface IInventorySaveTarget
{
    string OwnerId { get; }
    InventoryData CaptureInventory();
    void ValidateInventory(InventoryData data);
    void RestoreInventory(InventoryData data);
}

// 플레이어 인벤토리 초기화 이후
SaveGameController.Instance.Inventories.Bind(playerInventoryAdapter);
// 플레이어 제거 이전: 현재 내용을 버퍼에 보관하고 연결 해제
SaveGameController.Instance.Inventories.Unbind(playerInventoryAdapter);
```

`CaptureInventory()`는 현재 내용을 데이터로 반환합니다.
`ValidateInventory()`는 아이템 ID, 슬롯 용량, 속성 버전 등을 검사하고 불가능하면 예외를 던집니다. 실제 인벤토리를 변경하면 안 됩니다.
`RestoreInventory()`는 기존 내용 전체를 교체해야 합니다. 추가 방식으로 구현하면 중복 아이템이 생깁니다.
연결된 플레이어가 저장 파일에 없으면 빈 인벤토리로 교체합니다.
불러오기 시 플레이어가 아직 생성되지 않았으면 데이터를 버퍼에 유지하고, 이후 `Bind()` 시 적용합니다.
연결되지 않은 소유자의 인벤토리도 다시 저장되므로 데이터가 유실되지 않습니다.

플레이어 코드가 없어도 다음처럼 저장 데이터 흐름을 시험할 수 있습니다.

```csharp
var data = new InventoryData { ownerId = "local-player" };
data.items.Add(new InventoryItemData { itemId = "potato", quantity = 12 });
SaveGameController.Instance.Inventories.SetUnboundInventory(data);
SaveGameController.Instance.Save(1);
SaveGameController.Instance.Load(1);
InventoryData restored = SaveGameController.Instance.Inventories.GetInventory("local-player");
```

기존 `PlayerItemController`의 도구 참조나 들고 있는 GameObject를 임의로 인벤토리로 간주하지 않습니다.
실제 아이템 생성·지급은 향후 플레이어 어댑터가 담당합니다.

## 확장과 효율

`SaveSlotService`는 저장 내용을 알지 못하고 `ISaveSection`만 다룹니다.
농사는 `farming`, 인벤토리는 `inventory` 키로 분리되며 각자 버전을 가집니다.
퀘스트나 재화 등을 추가하려면 별도 `ISaveSection`을 구현하여 `Slots.Register(section)`하면 됩니다.
`PrepareRestore(version, json)`에서 구버전 데이터를 현재 형식으로 변환한 뒤 검증하도록 확장할 수 있습니다.
현재 두 모듈은 버전 1을 지원하고, 미지원 버전은 조용히 초기화하지 않고 오류로 반환합니다.
새 필수 모듈을 추가할 때 기존 파일에 해당 모듈이 없는 경우의 초기값/마이그레이션 정책도 구현해야 합니다.
불러온 파일에서 현재 등록되지 않은 모듈은 다음 저장에도 보존합니다.

땅은 기본 흙이 아닌 변경된 셀만 기록합니다. 인벤토리는 객체 전체 대신 ID·스택 수량·선택적 속성을 기록합니다.
따라서 저장 크기는 변경된 셀 수와 실제 인벤토리 항목 수에 비례합니다.
현재는 모든 셀을 순회해 변경 여부를 확인하고, 복원 뒤 전체 셀에 렌더 갱신을 알립니다.
저장은 명시적 호출 시 메인 스레드에서 동기 실행합니다. 현재 50×50 그리드의 수동 저장에 맞춘 구현이며 대규모 월드에는 변경 셀 추적·비동기 파일 쓰기 등을 추가할 수 있습니다.

모든 모듈을 검증한 다음 복원하며 적용 중 예외가 발생하면 이전 스냅샷으로 롤백을 시도합니다.
어댑터가 롤백에서도 예외를 던지거나 이벤트에서 외부 효과를 발생시키면 완전한 트랜잭션을 보장할 수 없으므로 검증과 교체 메서드는 명확히 분리해야 합니다.
파일은 임시 파일에 기록하고 flush한 뒤 교체합니다. 파일 교체를 지원하지 않는 플랫폼은 오류를 반환하므로 타깃 플랫폼 빌드에서도 확인하세요.

## 검증

Unity 메뉴 `Tools/Project Potato/Validate Save and Load`에서 격리된 임시 씬·디렉터리로 검증합니다.
슬롯 분리, 정확한 상태 복원, 반복 로드, 손상 파일, 백업, 사전 검증, 롤백, 플레이어 지연 연결, 알 수 없는 모듈 보존, 새로 생성한 씬 오브젝트 복원 및 수확 재개를 검사합니다.
실제 플레이어 저장 파일은 건드리지 않습니다.
