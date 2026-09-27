# 플레이어 시스템

## 조작

- 이동: `WASD`
- 앉기: `Ctrl` (걷기 속도의 0.6배)
- 달리기: `Shift` (걷기 속도의 1.8배)
- 점프: `Space` (기본 높이 1m)
- 상호작용: `E`
- 도구 전환: 마우스 휠. 손 → 괭이 → 물뿌리개 → 낫 → 감자 포크 순환
- 도구 직접 선택: `1` 손, `2` 괭이, `3` 물뿌리개, `4` 낫, `5` 감자 포크
- 드랍: `Q`
- 던지기: 마우스 우클릭 홀드 후 놓기, 홀드 중 좌클릭으로 취소
- 땅 덮기/파기: 기존 좌클릭/우클릭 유지
- 태블릿 상태 토글: `Tab`, 열린 상태에서는 `Esc`로 닫기

## 도구 동작

- 손: Rigidbody, Collider, `PickupItem`이 있는 물체를 보고 우클릭하면 집는다. 들고 있는 물체는 기존 방식대로 우클릭 홀드 후 놓아 던지고 `Q`로 내려놓는다.
- 괭이: 기본 흙에서 우클릭하면 밭으로, 감자가 없는 밭에서 우클릭하면 기본 흙으로 되돌린다. 빈 밭에서 좌클릭하면 심을 구멍을 만들고, 감자가 들어간 칸에서 좌클릭하면 흙 덮기 단계가 증가한다.
- 물뿌리개: 현재 테스트 단계에서는 물이 무한이다. 파인 타일을 조준하면 HUD에 `[ LMB ] WATER`가 표시되고 좌클릭하면 타일이 젖은 상태로 바뀌며 색이 진해진다. 이미 젖은 타일에는 안내가 다시 나오지 않는다. `PotatoGrowth`와 Collider가 있는 작물도 좌클릭으로 물을 줄 수 있다.
- 낫: 수확 가능한 `PotatoGrowth`를 보고 좌클릭하면 줄기를 제거한다.
- 감자 포크: 수확 가능하고 줄기가 제거된 `PotatoGrowth`를 보고 좌클릭하면 수확한다.
- 태블릿: 현재 UI 이전 단계라 열림/닫힘 상태만 관리하고 Console에 `[Tablet] 태블릿 열림/닫힘`을 출력한다.

바구니는 이번 단계에서 연결하지 않았다. 감자 심기 오브젝트가 추가되면 구멍 칸에 `GroundGrid.TryPlantPotato`를 호출하고, 수확 후에는 `TryRemovePotato`를 호출해 현재 토양 상태와 연결한다.

## 아이템 연결

새 아이템 컴포넌트는 `IPlayerHeldItem`을 구현하고 `PlayerItemController.RegisterItem`으로 해당 `PlayerItemSlot`에 등록한다. `Mass`, `CanDrop`, `CanThrow`, `TryDrop`, `TryThrow`를 구현하면 기존 입력에 바로 연결된다. 던지는 속도는 홀드 시간과 무관하고 질량의 제곱근에 반비례한다.

설정, 태블릿, 상호작용 홀드, 요리 상태를 열거나 닫는 시스템은 `PlayerActionBlocker.SetBlocked`를 호출한다. 이 상태에서는 드랍이 차단된다.

## 상호작용 연결

일반 오브젝트에는 Collider와 `InteractionTarget`을 추가한다. `Hold Duration`이 0이면 E 입력 즉시 실행하고, 0보다 크면 E 홀드 완료 시 UnityEvent를 실행한다. 바라보는 동안 `InteractionOutline`이 렌더러 외곽선을 표시한다.

## 기절 연결

- 멧돼지 프리팹의 AI는 돌진 시작/종료 때 `BoarChargeHitbox.SetCharging(true/false)`를 호출한다.
- 던질 감자와 돌 프리팹에는 Rigidbody, Collider, `ThrownStunProjectile`을 추가하고 종류를 Potato 또는 Rock으로 지정한다.
- 기절 시간은 3초이며 피격 방향 반대로 힘을 받는다.

현재 플레이어는 리깅된 캐릭터 모델이 없어 루트 캡슐 물리로 쓰러진다. 이후 휴머노이드 모델을 넣으면 `PlayerStunController`의 ragdoll bodies/colliders와 Animator 배열에 뼈를 연결해 관절 레그돌로 전환할 수 있다. 점프 중 1인칭 팔 벌림은 구현되어 있으며, 개구리 자세의 다리 표현도 플레이어 리그가 들어온 뒤 Animator에서 연결해야 한다.
