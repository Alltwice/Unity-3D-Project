using UnityEditor;
using UnityEngine;

internal static class PlayerHandoffInspectorUtility
{
    public static void DrawEntry(SerializedProperty entry, string label)
    {
        if (entry == null)
        {
            EditorGUILayout.HelpBox(label + " 配置缺失。", MessageType.Error);
            return;
        }
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("allowRequest"), new GUIContent("允许请求进入"));
        SerializedProperty defaultBlend = entry.FindPropertyRelative("defaultBlend");
        DrawBlend(defaultBlend, "默认混合", true);
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("sourceOverrides"), new GUIContent("来源覆盖"), true);
        DrawEntryDurationHint(defaultBlend);
    }

    public static void DrawSuccessor(SerializedProperty successor, string label)
    {
        if (successor == null)
        {
            EditorGUILayout.HelpBox(label + " 配置缺失。", MessageType.Error);
            return;
        }
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(successor.FindPropertyRelative("enabled"), new GUIContent("启用"));
        EditorGUILayout.PropertyField(successor.FindPropertyRelative("target"), new GUIContent("目标节点"), true);
        EditorGUILayout.PropertyField(successor.FindPropertyRelative("sourceTriggerProgress"), new GUIContent("源触发进度"));
        SerializedProperty blend = successor.FindPropertyRelative("blend");
        DrawBlend(blend, "混合", true);
        DrawSuccessorDurationHint(successor, blend);
    }

    public static void DrawBlend(SerializedProperty blend, string label, bool includeChildren)
    {
        if (blend == null)
        {
            EditorGUILayout.HelpBox(label + " 配置缺失。", MessageType.Error);
            return;
        }
        EditorGUILayout.PropertyField(blend, new GUIContent(label), includeChildren);
    }

    private static void DrawEntryDurationHint(SerializedProperty blend)
    {
        SerializedProperty mode = blend?.FindPropertyRelative("durationMode");
        if (mode == null) return;
        if (mode.enumValueIndex == (int)PlayerHandoffDurationMode.SourceMotionRatio)
            EditorGUILayout.HelpBox("默认进入可能来自 Loop，SourceMotionRatio 只能放在明确 Motion 来源的覆盖中。", MessageType.Warning);
        else if (mode.enumValueIndex == (int)PlayerHandoffDurationMode.TargetMotionRatio)
            EditorGUILayout.HelpBox("按目标 Motion 的实际 Profile Duration 换算；缺少实际 Profile 时无法确定秒数。", MessageType.Info);
        else
            EditorGUILayout.HelpBox("使用固定秒数。", MessageType.Info);
    }

    private static void DrawSuccessorDurationHint(SerializedProperty successor, SerializedProperty blend)
    {
        SerializedProperty mode = blend?.FindPropertyRelative("durationMode");
        SerializedProperty target = successor?.FindPropertyRelative("target");
        SerializedProperty kind = target?.FindPropertyRelative("kind");
        if (mode == null || kind == null) return;
        if (mode.enumValueIndex == (int)PlayerHandoffDurationMode.SourceMotionRatio)
            EditorGUILayout.HelpBox("按源 Motion 的实际 Profile Duration 换算。", MessageType.Info);
        else if (mode.enumValueIndex == (int)PlayerHandoffDurationMode.TargetMotionRatio)
            EditorGUILayout.HelpBox(kind.enumValueIndex == (int)PlayerMotionNodeKind.Motion ? "按目标 Motion 的实际 Profile Duration 换算。" : "TargetMotionRatio 只能用于 Motion 目标。", kind.enumValueIndex == (int)PlayerMotionNodeKind.Motion ? MessageType.Info : MessageType.Warning);
        else
            EditorGUILayout.HelpBox("使用固定秒数。", MessageType.Info);
    }
}
