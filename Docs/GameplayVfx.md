# 게임플레이 VFX 적용

적용일: 2026-11-11

연결 씬: `Assets/Scenes/Test/YoungSeok_TestScene.unity`

## 재생 조건

| 원본 프리팹 (`Assets/Fbx/VFX/`) | 동작 |
| --- | --- |
| `VFX_Boar Crash` | 멧돼지 공격으로 작물 HP가 0이 된 순간 한 번 재생. 일반 피해로 죽는 경우에는 발생하지 않음. |
| `VFX_Bug` | 살아 있는 작물에 벌레가 있으면 반복. 퇴치·기한 만료 시 새 방출을 멈추고 0.6초 동안 투명해진 뒤 제거. 작물 사망·수확·폐기는 즉시 제거. 서로 다른 밭의 같은 셀 좌표도 별도 관리. |
| `VFX_Leaf Crash` | 낫으로 잎 자르기가 성공했을 때 한 번 재생. 실패하거나 이미 자른 잎에는 발생하지 않음. |
| `VFX_Potato Crash` | 끌개로 ME/LE 상태의 감자를 최종 폐기하여 빈 밭이 될 때 한 번 재생. 정상 굴착과 상처 단계에는 발생하지 않음. |
| `VFX_Starch Water Drop` | 빨간 통에 첫 배치를 투입한 뒤 부분 채우기·추가 투입 대기·배출 중 반복. 배출 완료 시 종료하며 건조·포장 단계에는 발생하지 않음. |

## 구성

- `FarmingVfxController`: 기존 벌레 이벤트와 새 작업 성공 이벤트를 구독한다. 현재 씬과 `Assets/Prefabs/Farming/PotatoFarmingSystem.prefab`에 연결했다.
- `ParticleVfxInstance`: 원본의 회전·크기·하위 배치를 유지하고 실제 작업 위치에 생성한다. 일회성 효과는 모든 하위 파티클이 끝난 뒤 제거한다. 벌레 퇴치에는 기존 색상·수명별 알파를 유지하며 전체 알파를 줄이는 `FadeOut`을 사용하고, 설비 종료·작물 제거에는 즉시 정리한다.
- `StarchWaterVfxController`: 공정 상태와 실제 투입량으로 젖은 전분을 판정한다. `SieveFillCount`는 배출 후에도 5이므로 수량만으로 켜지 않는다.
- 빨간 통의 안쪽 애니메이션 본 아래 `StarchDripAnchor`를 추가했다. 본 포즈 적용 후 배출 위치를 따라가며, VFX 내부의 1.09m 소스 높이를 보정한다.
- 파괴·절단 프리팹은 하위 파티클까지 반복을 해제했다. 벌레·전분 프리팹은 반복을 유지하고, 모든 효과의 자동 시작을 해제하여 코드에서 재생한다.
- 저장 복원 시 벌레와 빨간 통의 젖은 전분 효과를 복원한다. 파괴·절단 효과는 저장 데이터에 포함하지 않으며 다시 재생하지 않는다.

위치 조정은 농사 오브젝트의 `FarmingVfxController`에 있는 네 가지 Offset과 설비의 `StarchDripAnchor`에서 할 수 있다.

## 플레이어 벌레 퇴치

벌레가 있는 살아 있는 감자를 조준하면 기존 농사 안내 UI에 **`[E] 벌레 쫓기`**를 표시하고, 대상 셀을 초록색으로 강조한다. 맨손과 네 가지 도구 슬롯 모두 E로 해당 셀의 벌레만 퇴치한다. 퇴치 판정과 안내는 즉시 갱신하고, 남아 있는 벌레 VFX는 0.6초 동안 점점 투명해진 뒤 제거한다. 새 파티클은 즉시 방출을 중단하며 퇴장 중 재호출해도 페이드 시간이 초기화되지 않는다.

페이드 길이는 농사 오브젝트의 `FarmingVfxController > Bug Fade Out Seconds`에서 조절할 수 있다. 저장 복원·비활성화 시에는 퇴장 중인 벌레도 정리한다. 실제 Play Mode에서 방출 중단·중간 투명도·시간 경과에 따른 알파 감소·최종 제거·이웃 작물 유지 등 **13개 검증을 통과**했다. 변경 후 기존 VFX 99개와 E 입력·UI 62개 검증도 다시 통과했다.

벌레 퇴치를 흙 덮기·수확·심기보다 먼저 처리하며, 같은 E 입력으로 두 작업을 실행하지 않는다. 거리 밖·장애물 뒤·입력이 차단된 상태에서는 퇴치하지 않는다. 설비 등 기존 상호작용 대상이 있으면 그 대상이 E를 우선 사용한다. 이미 만료되거나 퇴치된 벌레에는 안내를 표시하지 않는다.

`AgentScripts/BugChasePlayChecks.cs`에서 Input System의 E 키 이벤트로 **62개 검증을 통과**했다. 다섯 슬롯, 대상 한정 퇴치, VFX 종료, 안내 갱신, 거리·차폐·메뉴·다른 상호작용 우선순위와 후속 흙 덮기를 확인했다. 저장 쓰기는 `Temp/BugChasePlayChecks`로 분리하고 검증 후 Play Mode를 종료했다. 기존 VFX 99개와 상호작용 조준 7개 검증도 통과했다.

[벌레 퇴치 UI 확인 화면](../Screenshots/VFX/BugChasePrompt.png)

## 검증

- `Tools > Project Potato > Validate Gameplay VFX`: **99개 통과**. 서로 다른 밭의 벌레, 중복 방지, 제거·저장 복원, 원인별 파괴 판정, 실제 파티클 방출·수명, 실제 프랍을 사용하는 5회 전분 투입과 배출·건조 상태를 검사한다.
- 기존 멧돼지 **11개**, 수확 변경 **200개**, 전분 가공 **204개**와 벌레 검증을 통과했다.
- 현재 씬의 Play Mode에서 **66개 통과**. 실제 컴포넌트 API로 낫·끌개·멧돼지 공격·벌레 퇴치·전분 5회 투입을 실행했다. 부분 투입 상태 저장 복원, 아래로 떨어지는 전분, 화면 밖 일회성 효과의 자연 종료도 확인했다. 물리 키보드 입력 검증은 아니다.
- Play 검증의 저장 쓰기는 `Temp/GameplayVfxPlayChecks`로 분리했다. 검증 후 Play Mode를 종료하고 저장된 씬을 다시 열어 연결 유지와 컴포넌트 중복 없음을 확인했다.
- 스크립트 컴파일 성공. 기존 Visual Studio 연동 패키지의 초기화 오류는 별도로 남아 있으며 위 검증은 통과했다.

재연결 메뉴: `Tools > Project Potato > Connect Gameplay VFX`.

Play 검증 도구: `AgentScripts/GameplayVfxPlayChecks.cs`. Play Mode에서 `run_script`로 실행하고 검증 후 Play Mode를 종료한다.

확인 화면:

- [멧돼지 파괴](../Screenshots/VFX/BoarCrash.png)
- [벌레](../Screenshots/VFX/Bugs.png)
- [잎 절단](../Screenshots/VFX/LeafCrash.png)
- [감자 파괴](../Screenshots/VFX/PotatoCrash.png)
- [젖은 전분 낙하](../Screenshots/VFX/StarchWaterDrop.png)
