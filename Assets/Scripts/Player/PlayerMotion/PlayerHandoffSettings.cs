using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerHandoffTriggerMode { Request, SourceProgress }
public enum PlayerHandoffDurationMode { Seconds, SourceMotionRatio, TargetMotionRatio }
public enum PlayerHandoffConfigurationSource { TargetDefaultEntry, TargetSourceOverride, SourceDefaultSuccessor }

[Serializable]
public struct PlayerHandoffBlendSettings
{
    [SerializeField] private PlayerHandoffDurationMode durationMode;
    [SerializeField, Min(0f)] private float durationValue;
    [SerializeField] private AnimationCurve poseCurve;
    [SerializeField] private AnimationCurve translationCurve;

    public PlayerHandoffDurationMode DurationMode => durationMode;
    public float DurationValue => durationValue;
    public AnimationCurve PoseCurve => poseCurve;
    public AnimationCurve TranslationCurve => translationCurve;

    public PlayerHandoffBlendSettings(PlayerHandoffDurationMode mode, float value, AnimationCurve pose, AnimationCurve translation)
    {
        durationMode = mode;
        durationValue = value;
        poseCurve = pose;
        translationCurve = translation;
    }

    public static PlayerHandoffBlendSettings Default()
    {
        return new PlayerHandoffBlendSettings
        {
            durationMode = PlayerHandoffDurationMode.Seconds,
            durationValue = 0f,
            poseCurve = LinearCurve(),
            translationCurve = LinearCurve()
        };
    }

    public float ResolveDuration(float sourceDuration, float targetDuration)
    {
        return durationValue * (durationMode == PlayerHandoffDurationMode.SourceMotionRatio ? sourceDuration : durationMode == PlayerHandoffDurationMode.TargetMotionRatio ? targetDuration : 1f);
    }

    public float EvaluatePose(float progress) => Mathf.Clamp01(poseCurve.Evaluate(progress));
    public float EvaluateTranslation(float progress) => Mathf.Clamp01(translationCurve.Evaluate(progress));

    public PlayerHandoffBlendSettings Clone()
    {
        PlayerHandoffBlendSettings clone = this;
        clone.poseCurve = CloneCurve(poseCurve);
        clone.translationCurve = CloneCurve(translationCurve);
        return clone;
    }

    public bool Validate(ICollection<string> errors, string label, PlayerMotionNodeKey source, PlayerMotionNodeKey target, bool sourceMayBeLoop)
    {
        bool valid = !float.IsNaN(durationValue) && !float.IsInfinity(durationValue) && durationValue >= 0f;
        valid &= Enum.IsDefined(typeof(PlayerHandoffDurationMode), durationMode);
        if (durationMode == PlayerHandoffDurationMode.SourceMotionRatio && (sourceMayBeLoop || !source.IsMotion))
        {
            errors?.Add(label + ": SourceMotionRatio 只能用于明确的 Motion 来源。");
            valid = false;
        }
        if (durationMode == PlayerHandoffDurationMode.TargetMotionRatio && !target.IsMotion)
        {
            errors?.Add(label + ": TargetMotionRatio 只能用于 Motion 目标。");
            valid = false;
        }
        valid &= ValidCurve(poseCurve, label + ".PoseCurve", errors);
        valid &= ValidCurve(translationCurve, label + ".TranslationCurve", errors);
        return valid;
    }

    public static bool AreEquivalent(PlayerHandoffBlendSettings left, PlayerHandoffBlendSettings right)
    {
        return left.durationMode == right.durationMode && left.durationValue.Equals(right.durationValue) && AreCurvesEquivalent(left.poseCurve, right.poseCurve) && AreCurvesEquivalent(left.translationCurve, right.translationCurve);
    }

    public static bool AreCurvesEquivalent(AnimationCurve left, AnimationCurve right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.preWrapMode != right.preWrapMode || left.postWrapMode != right.postWrapMode) return false;
        Keyframe[] leftKeys = left.keys;
        Keyframe[] rightKeys = right.keys;
        if (leftKeys.Length != rightKeys.Length) return false;
        for (int i = 0; i < leftKeys.Length; i++)
        {
            Keyframe a = leftKeys[i];
            Keyframe b = rightKeys[i];
            if (!a.time.Equals(b.time) || !a.value.Equals(b.value) || !a.inTangent.Equals(b.inTangent) || !a.outTangent.Equals(b.outTangent)) return false;
#pragma warning disable CS0618
            if (a.tangentMode != b.tangentMode) return false;
#pragma warning restore CS0618
            if (a.weightedMode != b.weightedMode || !a.inWeight.Equals(b.inWeight) || !a.outWeight.Equals(b.outWeight)) return false;
        }
        return true;
    }

    public static AnimationCurve CloneCurve(AnimationCurve curve)
    {
        if (curve == null) return null;
        AnimationCurve clone = new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        return clone;
    }

    private static bool ValidCurve(AnimationCurve curve, string label, ICollection<string> errors)
    {
        if (curve == null)
        {
            errors?.Add(label + ": 曲线缺失。");
            return false;
        }
        bool valid = Mathf.Approximately(curve.Evaluate(0f), 0f) && Mathf.Approximately(curve.Evaluate(1f), 1f);
        if (!valid) errors?.Add(label + ": 曲线端点必须为 0 → 1。");
        Keyframe[] keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
        {
            Keyframe key = keys[i];
            bool keyValid = IsFinite(key.time) && IsFinite(key.value) && IsFinite(key.inTangent) && IsFinite(key.outTangent) && IsFinite(key.inWeight) && IsFinite(key.outWeight);
            if (!keyValid)
            {
                errors?.Add(label + ": Key " + i + " 包含非有限数值。");
                valid = false;
            }
        }
        return valid;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static AnimationCurve LinearCurve() => AnimationCurve.Linear(0f, 0f, 1f, 1f);
}

[Serializable]
public struct PlayerHandoffSourceOverride
{
    [SerializeField] private PlayerMotionNodeKey source;
    [SerializeField] private PlayerHandoffBlendSettings blend;

    public PlayerMotionNodeKey Source => source;
    public PlayerHandoffBlendSettings Blend => blend;

    public PlayerHandoffSourceOverride(PlayerMotionNodeKey sourceNode, PlayerHandoffBlendSettings blendSettings)
    {
        source = sourceNode;
        blend = blendSettings;
    }
}

[Serializable]
public class PlayerHandoffEntrySettings
{
    [SerializeField] private bool allowRequest;
    [SerializeField] private PlayerHandoffBlendSettings defaultBlend = default;
    [SerializeField] private List<PlayerHandoffSourceOverride> sourceOverrides = new List<PlayerHandoffSourceOverride>();

    public bool AllowRequest => allowRequest;
    public PlayerHandoffBlendSettings DefaultBlend => defaultBlend;
    public IReadOnlyList<PlayerHandoffSourceOverride> SourceOverrides => sourceOverrides;

    public PlayerHandoffEntrySettings()
    {
        defaultBlend = PlayerHandoffBlendSettings.Default();
        sourceOverrides = new List<PlayerHandoffSourceOverride>();
    }

    public bool Validate(PlayerMotionNodeKey target, ICollection<string> errors, string label)
    {
        if (sourceOverrides == null)
        {
            errors?.Add(label + ": 来源覆盖列表缺失。");
            return false;
        }
        bool valid = defaultBlend.Validate(errors, label + ".Default", default, target, true);
        HashSet<PlayerMotionNodeKey> sources = new HashSet<PlayerMotionNodeKey>();
        for (int i = 0; i < sourceOverrides.Count; i++)
        {
            PlayerHandoffSourceOverride sourceOverride = sourceOverrides[i];
            if (!sourceOverride.Source.IsValid() || sourceOverride.Source.Equals(target))
            {
                errors?.Add(label + ": 来源覆盖 " + i + " 的来源节点无效。");
                valid = false;
            }
            if (!sources.Add(sourceOverride.Source))
            {
                errors?.Add(label + ": 来源覆盖不能重复 " + sourceOverride.Source + "。");
                valid = false;
            }
            valid &= sourceOverride.Blend.Validate(errors, label + ".Override[" + i + "]", sourceOverride.Source, target, false);
        }
        return valid;
    }

#if UNITY_EDITOR
    public void Configure(bool allow, PlayerHandoffBlendSettings blend, IEnumerable<PlayerHandoffSourceOverride> overrides)
    {
        allowRequest = allow;
        defaultBlend = blend.Clone();
        sourceOverrides = new List<PlayerHandoffSourceOverride>();
        if (overrides != null) foreach (PlayerHandoffSourceOverride sourceOverride in overrides) sourceOverrides.Add(new PlayerHandoffSourceOverride(sourceOverride.Source, sourceOverride.Blend.Clone()));
    }
#endif
}

[Serializable]
public class PlayerHandoffSuccessorSettings
{
    [SerializeField] private bool enabled;
    [SerializeField] private PlayerMotionNodeKey target;
    [SerializeField, Range(0f, 1f)] private float sourceTriggerProgress;
    [SerializeField] private PlayerHandoffBlendSettings blend = default;

    public bool Enabled => enabled;
    public PlayerMotionNodeKey Target => target;
    public float SourceTriggerProgress => sourceTriggerProgress;
    public PlayerHandoffBlendSettings Blend => blend;

    public PlayerHandoffSuccessorSettings()
    {
        blend = PlayerHandoffBlendSettings.Default();
    }

    public bool Validate(PlayerMotionNodeKey source, ICollection<string> errors, string label)
    {
        if (!enabled) return true;
        bool valid = source.IsMotion;
        if (!valid) errors?.Add(label + ": 默认后继来源必须是 Motion。");
        if (!target.IsValid() || target.Equals(source))
        {
            errors?.Add(label + ": 默认后继目标节点无效。");
            valid = false;
        }
        if (float.IsNaN(sourceTriggerProgress) || float.IsInfinity(sourceTriggerProgress) || sourceTriggerProgress < 0f || sourceTriggerProgress > 1f)
        {
            errors?.Add(label + ": SourceTriggerProgress 必须是 0 到 1 的有限值。");
            valid = false;
        }
        valid &= blend.Validate(errors, label + ".Blend", source, target, false);
        return valid;
    }

#if UNITY_EDITOR
    public void Configure(bool isEnabled, PlayerMotionNodeKey successorTarget, float triggerProgress, PlayerHandoffBlendSettings blendSettings)
    {
        enabled = isEnabled;
        target = successorTarget;
        sourceTriggerProgress = triggerProgress;
        blend = blendSettings.Clone();
    }
#endif
}

public struct PlayerHandoffResolution
{
    public PlayerMotionNodeKey Source;
    public PlayerMotionNodeKey Target;
    public PlayerHandoffTriggerMode TriggerMode;
    public float SourceTriggerProgress;
    public PlayerHandoffBlendSettings Blend;
    public PlayerHandoffConfigurationSource ConfigurationSource;
    public bool IsValid;
    public string Error;

    public static PlayerHandoffResolution Invalid(PlayerMotionNodeKey source, PlayerMotionNodeKey target, string error)
    {
        return new PlayerHandoffResolution { Source = source, Target = target, TriggerMode = PlayerHandoffTriggerMode.Request, ConfigurationSource = PlayerHandoffConfigurationSource.TargetDefaultEntry, IsValid = false, Error = error };
    }
}
