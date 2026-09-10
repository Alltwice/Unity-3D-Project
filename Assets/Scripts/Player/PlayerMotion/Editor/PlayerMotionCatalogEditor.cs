using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerMotionCatalog))]
public class PlayerMotionCatalogEditor : Editor
{
    private int previewSourceIndex;
    private int previewTargetIndex = 1;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.name == "m_Script" || iterator.name == "loopHandoffEntries") continue;
            EditorGUILayout.PropertyField(iterator, true);
        }
        EditorGUILayout.Space(6f);
        DrawLoopEntries(serializedObject.FindProperty("loopHandoffEntries"));
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.Space(8f);
        DrawResolutionPreview((PlayerMotionCatalog)target);
    }

    private static void DrawLoopEntries(SerializedProperty entries)
    {
        if (entries == null)
        {
            EditorGUILayout.HelpBox("Loop 进入配置缺失。", MessageType.Error);
            return;
        }
        entries.isExpanded = EditorGUILayout.Foldout(entries.isExpanded, "Loop 进入混合", true);
        if (!entries.isExpanded) return;
        EditorGUI.indentLevel++;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty element = entries.GetArrayElementAtIndex(i);
            SerializedProperty mode = element.FindPropertyRelative("mode");
            string label = mode == null ? "Loop " + i : ((PlayerLocomotionMode)mode.enumValueIndex).ToString();
            EditorGUILayout.PropertyField(element, new GUIContent(label), true);
        }
        EditorGUI.indentLevel--;
    }

    private void DrawResolutionPreview(PlayerMotionCatalog catalog)
    {
        EditorGUILayout.LabelField("Handoff 解析预览", EditorStyles.boldLabel);
        List<NodeOption> options = BuildNodeOptions(catalog);
        if (options.Count < 2)
        {
            EditorGUILayout.HelpBox("至少需要两个可解析节点才能预览 Request。", MessageType.Info);
            return;
        }
        previewSourceIndex = Mathf.Clamp(previewSourceIndex, 0, options.Count - 1);
        previewTargetIndex = Mathf.Clamp(previewTargetIndex, 0, options.Count - 1);
        string[] labels = new string[options.Count];
        for (int i = 0; i < options.Count; i++) labels[i] = options[i].Label;
        previewSourceIndex = EditorGUILayout.Popup("来源", previewSourceIndex, labels);
        previewTargetIndex = EditorGUILayout.Popup("目标", previewTargetIndex, labels);
        PlayerMotionNodeKey source = options[previewSourceIndex].Node;
        PlayerMotionNodeKey target = options[previewTargetIndex].Node;
        PlayerHandoffResolution resolution = catalog.ResolveRequest(source, target);
        if (!resolution.IsValid)
        {
            EditorGUILayout.HelpBox(resolution.Error, MessageType.Error);
            return;
        }
        EditorGUILayout.LabelField("配置来源", ConfigurationSourceLabel(resolution.ConfigurationSource));
        EditorGUILayout.LabelField("时长模式", resolution.Blend.DurationMode.ToString());
        EditorGUILayout.LabelField("时长值", resolution.Blend.DurationValue.ToString("0.########", CultureInfo.InvariantCulture));
        DrawResolvedDuration(catalog, resolution);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.CurveField("姿态曲线", resolution.Blend.PoseCurve);
            EditorGUILayout.CurveField("平移曲线", resolution.Blend.TranslationCurve);
        }
    }

    private static void DrawResolvedDuration(PlayerMotionCatalog catalog, PlayerHandoffResolution resolution)
    {
        if (resolution.Blend.DurationMode == PlayerHandoffDurationMode.Seconds)
        {
            EditorGUILayout.LabelField("解析秒数", resolution.Blend.DurationValue.ToString("0.########", CultureInfo.InvariantCulture));
            return;
        }
        PlayerMotionNodeKey reference = resolution.Blend.DurationMode == PlayerHandoffDurationMode.SourceMotionRatio ? resolution.Source : resolution.Target;
        if (!reference.IsMotion || !catalog.TryGetNodeDuration(reference, PlayerFoot.Unknown, out float duration))
        {
            EditorGUILayout.LabelField("解析秒数", "未确定（缺少实际 Profile）");
            return;
        }
        EditorGUILayout.LabelField("解析秒数", (resolution.Blend.DurationValue * duration).ToString("0.########", CultureInfo.InvariantCulture));
        EditorGUILayout.LabelField("引用 Profile Duration", duration.ToString("0.########", CultureInfo.InvariantCulture));
    }

    private static List<NodeOption> BuildNodeOptions(PlayerMotionCatalog catalog)
    {
        List<NodeOption> options = new List<NodeOption>();
        PlayerLocomotionMode[] modes = { PlayerLocomotionMode.Idle, PlayerLocomotionMode.Walk, PlayerLocomotionMode.Run, PlayerLocomotionMode.FastRun };
        for (int i = 0; i < modes.Length; i++) options.Add(new NodeOption(PlayerMotionNodeKey.ForLoop(modes[i]), "Loop/" + modes[i]));
        if (catalog?.Motions != null)
        {
            for (int i = 0; i < catalog.Motions.Count; i++)
            {
                PlayerMotionCatalogEntry entry = catalog.Motions[i];
                if (entry.Definition != null) options.Add(new NodeOption(PlayerMotionNodeKey.ForMotion(entry.Id), "Motion/" + entry.Id));
            }
        }
        return options;
    }

    private static string ConfigurationSourceLabel(PlayerHandoffConfigurationSource source)
    {
        switch (source)
        {
            case PlayerHandoffConfigurationSource.TargetDefaultEntry: return "目标默认进入";
            case PlayerHandoffConfigurationSource.TargetSourceOverride: return "目标来源覆盖";
            case PlayerHandoffConfigurationSource.SourceDefaultSuccessor: return "源默认后继";
            default: return source.ToString();
        }
    }

    private struct NodeOption
    {
        public PlayerMotionNodeKey Node;
        public string Label;

        public NodeOption(PlayerMotionNodeKey node, string label)
        {
            Node = node;
            Label = label;
        }
    }
}
