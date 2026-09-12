using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>节点退出时的固定秒数与统一位移/姿态曲线</summary>
[Serializable]
public struct PlayerHandoffSettings
{
    [SerializeField, Min(0f)] private float duration;
    [SerializeField] private AnimationCurve curve;
    public float Duration => duration;
    public AnimationCurve Curve => curve;
    public PlayerHandoffSettings(float seconds, AnimationCurve blendCurve)
    {
        duration = seconds;
        curve = blendCurve;
    }
    public static PlayerHandoffSettings Default() => new PlayerHandoffSettings(0f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
    public float Evaluate(float progress) => Mathf.Clamp01(curve.Evaluate(Mathf.Clamp01(progress)));
    public bool Validate(ICollection<string> errors, string label)
    {
        bool valid = IsFinite(duration) && duration >= 0f;
        if (!valid) errors?.Add(label + ": Duration 必须是非负有限秒数。");
        if (curve == null) { errors?.Add(label + ": 缺少 Curve。"); return false; }
        if (!Mathf.Approximately(curve.Evaluate(0f), 0f) || !Mathf.Approximately(curve.Evaluate(1f), 1f)) { errors?.Add(label + ": Curve 端点必须为 0 → 1。"); valid = false; }
        foreach (Keyframe key in curve.keys)
        {
            if (IsFinite(key.time) && IsFinite(key.value) && IsFinite(key.inTangent) && IsFinite(key.outTangent) && IsFinite(key.inWeight) && IsFinite(key.outWeight)) continue;
            errors?.Add(label + ": Curve 包含非有限数值。");
            valid = false;
        }
        return valid;
    }
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
