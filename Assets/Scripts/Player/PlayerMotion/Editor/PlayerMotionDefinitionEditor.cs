using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerMotionDefinition))]
public class PlayerMotionDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.name == "m_Script" || iterator.name == "handoffEntry" || iterator.name == "defaultSuccessor") continue;
            EditorGUILayout.PropertyField(iterator, true);
        }
        EditorGUILayout.Space(6f);
        PlayerHandoffInspectorUtility.DrawEntry(serializedObject.FindProperty("handoffEntry"), "进入混合");
        EditorGUILayout.Space(4f);
        PlayerHandoffInspectorUtility.DrawSuccessor(serializedObject.FindProperty("defaultSuccessor"), "默认后继");
        serializedObject.ApplyModifiedProperties();
    }
}
