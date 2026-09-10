using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerMotionNodeKind { Locomotion, Motion }
public enum PlayerHandoffTriggerMode { Request, SourceProgress }
public enum PlayerHandoffDurationMode { Seconds, SourceMotionRatio, TargetMotionRatio }

[Serializable]
public struct PlayerMotionNodeKey : IEquatable<PlayerMotionNodeKey>
{
    [SerializeField] private PlayerMotionNodeKind kind;
    [SerializeField] private PlayerMotionId motion;
    [SerializeField] private PlayerLocomotionMode locomotion;
    public PlayerMotionNodeKind Kind => kind;
    public PlayerMotionId Motion => motion;
    public PlayerLocomotionMode Locomotion => locomotion;
    public bool IsMotion => kind == PlayerMotionNodeKind.Motion;
    public static PlayerMotionNodeKey ForMotion(PlayerMotionId id) => new PlayerMotionNodeKey { kind = PlayerMotionNodeKind.Motion, motion = id };
    public static PlayerMotionNodeKey ForLoop(PlayerLocomotionMode mode) => new PlayerMotionNodeKey { locomotion = mode };
    public bool Equals(PlayerMotionNodeKey other) => kind == other.kind && (IsMotion ? motion == other.motion : locomotion == other.locomotion);
    public override bool Equals(object obj) => obj is PlayerMotionNodeKey other && Equals(other);
    public override int GetHashCode() => ((int)kind * 397) ^ (IsMotion ? (int)motion : (int)locomotion);
    public override string ToString() => IsMotion ? motion.ToString() : locomotion + "Loop";
}

/// <summary>关系拥有触发条件和混合配置，节点本身不执行进入或退出混合</summary>
[CreateAssetMenu(fileName = "PlayerHandoffDefinition", menuName = "Player/Motion/Handoff")]
public class PlayerHandoffDefinition : ScriptableObject
{
    [SerializeField] private PlayerMotionNodeKey source;
    [SerializeField] private PlayerMotionNodeKey target;
    [SerializeField] private PlayerHandoffTriggerMode triggerMode;
    [SerializeField, Range(0f, 1f)] private float sourceTriggerProgress;
    [SerializeField] private PlayerHandoffDurationMode durationMode;
    [SerializeField, Min(0f)] private float durationValue;
    [SerializeField] private AnimationCurve poseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve translationCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public PlayerMotionNodeKey Source => source;
    public PlayerMotionNodeKey Target => target;
    public PlayerHandoffTriggerMode TriggerMode => triggerMode;
    public float SourceTriggerProgress => sourceTriggerProgress;
    public float ResolveDuration(float sourceDuration, float targetDuration) => durationValue * (durationMode == PlayerHandoffDurationMode.SourceMotionRatio ? sourceDuration : durationMode == PlayerHandoffDurationMode.TargetMotionRatio ? targetDuration : 1f);
    public float EvaluatePose(float progress) => Mathf.Clamp01(poseCurve.Evaluate(progress));
    public float EvaluateTranslation(float progress) => Mathf.Clamp01(translationCurve.Evaluate(progress));
    public bool Validate(ICollection<string> errors)
    {
        bool valid = !float.IsNaN(durationValue) && !float.IsInfinity(durationValue) && durationValue >= 0f;
        valid &= Enum.IsDefined(typeof(PlayerHandoffTriggerMode), triggerMode) && Enum.IsDefined(typeof(PlayerHandoffDurationMode), durationMode);
        valid &= Enum.IsDefined(typeof(PlayerMotionNodeKind), source.Kind) && Enum.IsDefined(typeof(PlayerMotionNodeKind), target.Kind) && !source.Equals(target);
        valid &= !float.IsNaN(sourceTriggerProgress) && sourceTriggerProgress >= 0f && sourceTriggerProgress <= 1f;
        valid &= triggerMode != PlayerHandoffTriggerMode.SourceProgress || source.IsMotion;
        valid &= durationMode != PlayerHandoffDurationMode.SourceMotionRatio || source.IsMotion;
        valid &= durationMode != PlayerHandoffDurationMode.TargetMotionRatio || target.IsMotion;
        valid &= ValidCurve(poseCurve) && ValidCurve(translationCurve);
        if (!valid) errors?.Add(name + ": Handoff 时长、触发节点或曲线端点无效。");
        return valid;
    }
    private static bool ValidCurve(AnimationCurve curve)
    {
        if (curve == null || !Mathf.Approximately(curve.Evaluate(0f), 0f) || !Mathf.Approximately(curve.Evaluate(1f), 1f)) return false;
        foreach (Keyframe key in curve.keys)
            if (float.IsNaN(key.time) || float.IsInfinity(key.time) || float.IsNaN(key.value) || float.IsInfinity(key.value) || float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent)) return false;
        return true;
    }
#if UNITY_EDITOR
    public void Configure(PlayerMotionNodeKey from, PlayerMotionNodeKey to, PlayerHandoffTriggerMode trigger, float progress, PlayerHandoffDurationMode mode, float value, AnimationCurve pose, AnimationCurve translation)
    {
        source = from;
        target = to;
        triggerMode = trigger;
        sourceTriggerProgress = progress;
        durationMode = mode;
        durationValue = value;
        poseCurve = pose;
        translationCurve = translation;
    }
#endif
}
