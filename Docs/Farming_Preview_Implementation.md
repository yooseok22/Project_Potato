# 농작지 미리보기 구현 결과

## 실행

`Assets/Scenes/YoungSeok.unity`를 열고 Play를 누른다. Player 앞의 Ground를 가리키는 중앙 크로스헤어와 1×1칸 농작지 후보가 표시된다.

- 초록색: 선택 셀이 Ground 내부이고 Player에서 수평 거리 4단위 이내.
- 빨간색: 선택 셀이 거리 제한 또는 바닥 경계 조건을 충족하지 못함.
- 숨김: Ground 미적중 또는 장애물에 가림.

WASD로 카메라 기준 이동, 마우스로 좌우·상하 시점 회전, Shift로 달리기를 지원한다. 캐릭터는 WASD의 실제 이동 방향으로 돌아서 이동하며, 정지 중 마우스 회전은 캐릭터 방향을 바꾸지 않는다. Esc로 커서를 해제하고 Game 화면을 클릭하면 다시 조작한다. 포커스를 잃으면 입력을 해제한다. 농작지 확정·저장은 아직 포함하지 않았다. Play 중 Inspector 조정은 종료하면 복원되므로 영구 설정은 Edit 모드에서 변경한다.

## 조절 위치

- Main Camera / QuarterViewCamera: Pitch, Yaw, Distance, Pivot Height, Shoulder Offset.
- Main Camera / QuarterViewCamera: Mouse Sensitivity, Minimum/Maximum Pitch로 마우스 감도와 상하 회전 범위 조절.
- Player / ThirdPersonPlayerController: Walk Speed(3), Sprint Speed(6), Acceleration(20), Turn Speed(720), Gravity(-20).
- Player / FarmingTargetSelector: Interaction Range, Ray Distance, Ray Mask.
- Ground / GroundGrid: Cell Size.
- FarmingPreview / FarmingPreviewView: 유효·무효 색상, 면 투명도, 바닥 오프셋.

팰월드식 조작 수정에 따라 카메라를 캐릭터 중심의 어깨 너머 구도로 변경했다. 초기 Pitch 30도, Distance 5, Pivot Height 1.5, Shoulder Offset 1.1이다. 마우스 상하 회전으로 경작지 조준 거리를 변경한다. Ground는 회전하지 않은 수평 Plane을 전제로 한다. Player 레이어는 조준 Ray에서 제외하고, Ground 외 장애물은 최초 충돌에서 조준을 차단한다.

## 검증

Unity Play 모드에서 다음 10개 검증을 통과했다.

1. 음수 월드 좌표의 셀 변환.
2. 정확한 셀 경계 좌표 변환.
3. Ground 최소·최대 유효 셀 및 외부 셀 판정.
4. 초기 조준 위치의 유효 타일 선택.
5. 거리 초과 시 무효 판정.
6. 무효 상태의 빨간 외곽선.
7. 임시 Cube 장애물에 의한 조준 차단.
8. Ground 미적중 시 숨김.
9. Player 위치 변경에 따른 인접 타일 선택.
10. 화면 중앙 앵커와 위치가 유지되는 크로스헤어 설정.

Game 화면에서 쿼터뷰 구도, 반투명 면과 초록색 외곽선도 확인했다. 검증용 장애물과 임시 설정은 복원했다.

## 이동 기능 추가 검증

Play 모드에서 카메라 90도 회전 후 전진 방향 변경, 대각선 속도 정규화, 상하 시점 제한, 1초 보행 거리, 지면 접촉, 달리기 속도, 벽 충돌, 회전 후 농작지 선택을 포함한 9개 검증을 통과했다. 컴파일 및 콘솔 오류·경고는 없었다. CharacterController가 충돌을 담당하며 기존 CapsuleCollider는 중복 충돌을 막기 위해 비활성화했다. 점프·앉기·카메라 장애물 회피는 이번 이동 기능에 포함하지 않았다.

