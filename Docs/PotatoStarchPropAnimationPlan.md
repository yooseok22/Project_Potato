# 믹서기·감자 전분 재배기 프랍 및 애니메이션 구현

수정일: 2026-10-13

## 1. 확정 사항과 구현 상태

사용자 확인 사항을 반영하여 코드와 현재 테스트 씬에 구현했다.

- 두 번째 손잡이는 실제 클립 `Anim_Starch Maker H2-1 Use`를 사용한다.
- 믹서기 본체와 버튼은 남기고 `FBX_Blender Head` 및 `FBX_Blender Lid`만 운반한다.
- 뚜껑·가동·부분 채우기·배출·손잡이·Drop은 원본 클립 길이와 속도로 진행한다.
- 모든 클립은 반복하지 않고 마지막 자세를 유지한다.
- 건조는 기존 150초를 유지한다.
- 감자 4개/배치, 재배기 용량 5배치, 완료 시 전분 5팩 획득 로그를 유지한다.
- 실제 인벤토리 차감·지급, 포장 아이템 생성, 저장, 멀티플레이 동기화는 이번 범위에 포함하지 않는다.

연결된 씬: `Assets/Scenes/Test/YoungSeok_TestScene.unity`.

`PotatoStarchTestSetup` 아래에서 기존 임시 모형을 제거하고 새 프랍을 연결했다. 새 프랍의 최초 배치는 플레이어와 약 1.3km 떨어져 있어 기존 테스트 설비의 앞쪽 위치로 옮기고 지면 높이에 맞췄다.

| 프랍 | 최초 교체 완료 시 위치(이후 Editor 배치에 따라 변경 가능) |
| --- | --- |
| `Potato Millionaire Strach Blender 1` | `(5.49, 0, 6.92)` |
| `Potato Millionaire Strach Maker 1` | `(7.09, 0, 6.92)` |
| 재배기 위 `DryingAnchor` | `(7.09, 2.04, 6.92)` |

원본 FBX 및 사용자가 추가한 Animator Controller 파일은 유지했다. 씬 인스턴스의 Animator는 전용 커브 드라이버와 중복 재생되지 않도록 비활성화했다.

## 2. 실제 클립과 제작 시간

파일명은 `Strach`, 클립명은 `Starch`라는 원본 표기를 사용한다. FBX 경로는 `Assets/Fbx/Food Processing/`이다.

| 역할 | 실제 AnimationClip 이름 | 원본 길이(초, 약) |
| --- | --- | ---: |
| 뚜껑 열기 | `Anim_Blender Lid Open` | 0.333 |
| 뚜껑 닫기 | `Anim_Blender Lid Close` | 0.333 |
| 믹서기 시작 | `Anim_Blender Start` | 1.533 |
| 분쇄 중 | `Anim_Blender Working` | 0.533 |
| 믹서기 종료 | `Anim_Blender End` | 1.533 |
| 빨간 통 채우기 | `Anim_Starch Water Basket Use` | 1.633 |
| 빨간 통 내용물 배출 | `Anim_Starch Water Basket Working` | 3.300 |
| 아래 틀에 전분 쌓기 | `Anim_Starch Dry Basket Use` | 3.233 |
| 첫 번째 손잡이 | `Anim_Starch Maker H1-1 Use` | 1.167 |
| 두 번째 손잡이 | `Anim_Starch Maker H2-1 Use` | 1.167 |
| 빨간 통 Drop | `Anim_Starch Water Basket Drop` | 0.533 |

믹서기 가동 시간은 Start + Working + End 합계인 약 **3.600초**다. 첫 뚜껑 작업은 별도로 약 0.667초다. 빨간 통 투입 구간은 한 번에 약 0.327초(전체 길이의 1/5)다. 전분 배출은 두 클립을 동시에 시작한 뒤 더 긴 클립까지 기다리므로 약 **3.300초**다.

길이를 코드에 고정하지 않고 연결된 원본 AnimationClip의 `length`를 사용한다.

## 3. 플레이 흐름

### 믹서기

```text
첫 [E]
  → Lid Open
  → 감자 4개 소비 로그(실제 차감 없음)
  → Lid Close 자동 재생
  → 닫힌 상태로 입력 대기
두 번째 [E]
  → Start → Working → End 각 1회
  → End 마지막 자세에서 입력 대기
세 번째 [E]
  → 통과 뚜껑 운반(본체는 남음)
재배기에 [E]
  → 1회분 투입, 빈 믹서기 통 운반 상태
남아 있는 믹서기 본체에 [E]
  → 통·뚜껑 원위치 및 초기 자세 복원
```

애니메이션 진행 중 중복 입력과 완료 전 운반은 차단한다. 투입 전에는 믹서기를 복귀할 수 없다. 5번째 투입 후에도 빈 믹서기를 먼저 복귀하여 아래 틀을 들 손 슬롯을 비워야 한다.

### 전분 재배기

```text
믹서기 1회 투입 → Basket Use 0%에서 20%까지 재생 후 유지
믹서기 2회 투입 → 20%에서 40%까지 재생 후 유지
믹서기 3회 투입 → 40%에서 60%까지 재생 후 유지
믹서기 4회 투입 → 60%에서 80%까지 재생 후 유지
믹서기 5회 투입 → 80%에서 100%까지 재생
  → Water Basket Working + Dry Basket Use 동시 시작
  → 두 클립 모두 완료, 아래 틀에 전분이 가득 찬 상태
아래 틀에 [E]
  → 전분이 찬 틀 운반
재배기에 [E]
  → H1-1 Use, 끝 자세 유지
재배기에 [E]
  → H2-1 Use, 첫 손잡이는 당겨진 상태 유지
  → 두 번째 손잡이 완료 시 Drop 자동 시작 + 두 손잡이 원위치
  → Drop 마지막 자세에서 입력 대기
재배기에 [E]
  → 위 DryingAnchor에 틀 배치, 손 슬롯 해제, 150초 건조
건조 완료 후 틀에 [E]
  → 전분 표시 제거
  → 전분 5팩 획득 Console 로그 1회
  → 빈 틀 아래 슬롯 복귀, 통·손잡이·투입량 초기화
```

`Working + Use` 동시 재생은 `Water Basket Working`과 `Dry Basket Use` 조합이다. 같은 빨간 통 내용물 본을 수정하는 `Water Basket Use`와 `Working`은 같은 시간에 합성하지 않는다.

부분 채우기, 손잡이 및 Drop 재생 중에는 다음 입력을 차단한다. 건조 중 틀 재운반과 다음 생산 사이클도 차단한다. 건조 완료 전 수거와 중복 수거를 막는다.

야외 바닥 배치 및 실외 판정은 제거했고 `OutdoorDryingSurface` 스크립트와 임시 바닥 오브젝트를 삭제했다. 틀은 재배기 위 고정 위치에서만 건조된다.

## 4. 애니메이션과 운반 구현

초기 계획의 Playables 대신, 확인된 11개 클립이 모두 Transform 위치·Quaternion 회전·크기 커브로 구성된 점을 이용해 **원본 커브를 부위별로 직접 평가**한다. Editor 연결 메뉴가 원본 커브와 본 참조를 직렬화하고, 런타임에는 Editor API 없이 평가한다.

- 뚜껑, 믹서기 가동 본, 빨간 통 내용물, 아래 틀 내용물, 두 손잡이, 빨간 통 Drop을 독립 채널로 관리한다.
- 클립 시간은 `0..length`로 제한하며 마지막 포즈를 계속 적용한다.
- 같은 채널은 다음 클립으로 교체하고 다른 채널의 포즈는 유지한다.
- `WorldClock`을 기준으로 일시정지·배속·`AdvanceBy`를 반영한다.
- 큰 시간 증가에는 자동 단계의 시간을 순서대로 소비하되, 다음 [E] 입력 대기 단계에서는 멈춘다.

`FBX_Starch Dry Basket`은 틀과 전분이 함께 들어 있는 SkinnedMeshRenderer다. 메시만 이동하지 않고 현재 포즈의 본을 복사하여 원래 렌더러의 `bones`와 `rootBone`을 운반용 본에 연결한다. 원본 본 경로는 재배기에 남겨 애니메이션 바인딩을 유지한다. 통·뚜껑에도 같은 방식을 적용한다.

복귀 시 원래 렌더러 부모·좌표·본 참조를 복원한다. 상단 건조 중에는 독립 본의 가득 찬 포즈를 유지하고, 수거 시 아래 슬롯으로 복귀한 뒤 빈 포즈를 적용해 전분만 없앤다.

현재 손 앵커는 `(0.09, 0.18, 0.09)` 크기의 손 프랍 아래에 있으므로 별도의 카메라 기준 운반 소켓으로 손 위치를 따라간다. 이를 통해 통·뚜껑과 틀이 찌그러지거나 지나치게 작아지는 것을 방지했다. 틀은 내부 전분을 볼 수 있도록 약간 기울여 든다.

본체와 아래 틀의 Collider를 분리하고, 아래 틀 앞쪽을 조준할 수 있도록 재배기 본체의 상호작용 영역을 상부에 맞췄다. 통·틀의 운반 Collider는 손에 들고 있는 동안 비활성화한다.

## 5. 변경한 파일

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/Processing/PotatoStarchProcessor.cs` | 전체 공정 상태, 클립 길이 기반 시간, 부분 투입·손잡이·상단 건조·수거 |
| `Assets/Scripts/Processing/StarchPropAnimationDriver.cs` | 독립 커브 채널, 부분 재생, 마지막 포즈 유지 |
| `Assets/Scripts/Processing/ProcessingCarryRig.cs` | 운반용 본 복사, 원래 SkinnedMeshRenderer 이동·복원 |
| `Assets/Scripts/Processing/ProcessingCarryItem.cs` | 손 슬롯, 크기 보정 소켓, 고정 상단 앵커 배치 |
| `Assets/Scripts/Processing/StarchBlender.cs` | 두 번의 시작 입력, 완료 후 운반·복귀 안내 및 입력 |
| `Assets/Scripts/Processing/StarchSieve.cs` | 투입·손잡이 순차 조작·상단 배치 안내 및 입력 |
| `Assets/Scripts/Processing/StarchDryingTray.cs` | 아래 틀 들기, 건조·수거 안내 및 입력 |
| `Assets/Editor/PotatoStarchProcessingChecks.cs` | 실제 프랍 연결, 커브 추출, Collider·앵커 설정 및 86개 검증 |
| `Assets/Scenes/Test/YoungSeok_TestScene.unity` | 새 프랍 연결 및 기존 임시 모형 교체 |

## 6. 검증 결과와 테스트 방법

스크립트 재컴파일 성공, 마지막 컴파일 결과 오류 없음.

`Tools > Project Potato > Validate Potato Starch Processing`에서 **86개 검증 통과**:

- 원본 제작 시간과 각 [E] 대기 단계, 완료 전 운반 차단.
- 5회 투입 및 각 구간의 중간·끝 시점, 중복 투입 차단.
- 두 배출 클립 완료 대기, 손잡이 끝 자세 유지, 자동 Drop 및 손잡이 복원.
- 150초 건조, 일시정지, 시간 건너뛰기, 중복 수거 방지 및 두 번째 사이클 시작.
- 운반 본 분리 전후 BakeMesh의 정점 포즈 일치와 원래 본·부모 복원.

실제 씬 Play Mode에서도 기존 플레이어의 손 슬롯 및 실제 상호작용 컴포넌트로 한 사이클을 실행해 확인했다. 이 검증은 컴포넌트의 `Interact(player)` 호출과 시계 진행 API를 사용했으며 물리 키보드 입력을 자동으로 누른 것은 아니다.

- 실제 믹서기 5회 운반·투입·복귀 및 운반 중 본체 위치 유지.
- 전분이 찬 틀 운반, 손잡이 조작, 상단 앵커 배치 및 손 슬롯 해제.
- 상단 틀의 Raycast 상호작용 대상 확인, 5팩 수거 및 전체 초기화.
- 아래 틀 앞쪽을 향하는 Raycast가 본체에 가려지지 않는 것을 별도로 확인.
- 게임 화면으로 통·뚜껑 운반, 전분이 찬 틀 운반·상단 배치, 수거 후 빈 틀을 확인.

최종 상태는 Play Mode를 종료하고 초기 상태의 씬을 저장했다. 직접 플레이할 때는 플레이어 앞쪽 새 믹서기에서 시작하면 된다. 테스트 메뉴를 다시 실행하면 현재 프랍을 기준으로 연결과 커브 데이터를 갱신할 수 있다.

검증 중 Unity Editor의 Visual Studio 패키지 초기화 및 Domain Reload Lifecycle 오류가 별도로 기록되었다. 가공 코드 컴파일과 실제 사이클 실행은 완료됐으며, 이 Editor 패키지 문제는 이번 변경 범위에 포함하지 않았다.

## 7. E 키 조준 대상 미선택 수정

사용자가 실제 E 키 입력에서 안내와 외곽선이 나타나지 않는 문제를 보고하여 입력 경로를 추가 검증했다.

원인은 화면 중앙 Ray가 플레이어 자신의 CharacterController에 먼저 닿는 것이었다. 재현 위치에서 플레이어 충돌은 약 0.016m, 믹서기 통 충돌은 약 2.173m였으며, 기존 단일 Physics.Raycast는 첫 충돌인 플레이어에서 탐색을 종료했다.

`PlayerInteractionController.cs`를 수정해 RaycastNonAlloc 결과에서 플레이어 자신과 자식 Collider를 제외하고 가장 가까운 외부 충돌을 선택한다. 외부 벽의 차폐, 상호작용 거리 및 레이어 제한은 유지하며 결과 버퍼가 가득 차면 확장한다.

수정 후 실제 테스트 씬에서 E 안내를 확인했다. Unity Input System에 임시 Keyboard 장치의 E 누름/해제 이벤트를 넣고 일반 MonoBehaviour Update 경로로 첫 E의 뚜껑 열기·닫기와 두 번째 E의 Start/Working/End 완료를 확인했다. 상호작용 함수를 직접 호출하지 않았으며, 물리 키보드를 누른 자동화와는 구분된다. 임시 입력 장치를 제거하고 테스트 중 변경한 플레이어 위치와 시선은 Play 종료로 복원했다.

`Tools > Project Potato > Validate Player Interaction Targeting`의 7개 회귀 검증도 통과했다. 자신/자식 제외, 외부 벽 차폐, 거리·레이어 제한, 다수 충돌 시 최근접 선택과 버퍼 확장을 검사한다. 컴파일 오류 없음.

실제 플레이에서는 Game 화면을 클릭해 커서를 잠근 상태로 프랍을 조준하고 E를 누른다.
