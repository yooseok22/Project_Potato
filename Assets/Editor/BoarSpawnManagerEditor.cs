using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BoarSpawnManager))]
public sealed class BoarSpawnManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox(
            "Play 모드에서 멧돼지 스폰 버튼을 누르세요. 농사 중인 감자가 있는 해금된 밭이 필요하며 출몰 확률과 대기시간은 적용되지 않습니다.",
            MessageType.Info);

        var manager = (BoarSpawnManager)target;
        using (new EditorGUI.DisabledScope(!Application.isPlaying || manager.ActiveBoar))
        {
            if (GUILayout.Button("멧돼지 스폰") && !manager.TrySpawnBoar())
                Debug.LogWarning("[Boar] 테스트 스폰 실패: 프리팹, 농사 시스템, 해금된 밭과 살아 있는 감자를 확인하세요.", manager);
        }

        if (manager.ActiveBoar)
            EditorGUILayout.ObjectField("현재 멧돼지", manager.ActiveBoar, typeof(BoarController), true);
    }
}
