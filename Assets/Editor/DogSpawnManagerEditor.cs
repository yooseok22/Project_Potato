using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DogSpawnManager))]
public sealed class DogSpawnManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Play 모드에서 강아지 스폰 버튼을 누르세요. 획득 조건은 적용되지 않습니다.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
            if (GUILayout.Button("강아지 스폰")) ((DogSpawnManager)target).SpawnDog();
        var dog = ((DogSpawnManager)target).ActiveDog;
        if (dog) EditorGUILayout.ObjectField("현재 강아지", dog, typeof(DogHelperController), true);
    }
}
