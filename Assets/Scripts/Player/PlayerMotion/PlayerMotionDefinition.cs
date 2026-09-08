using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
/// <summary>
/// 处理旋转方式
/// </summary>
public enum PlayerMotionRotationPolicy
{
    //动画期间不旋转
    KeepFacing = 0,
    //朝输入意图
    FaceDirection = 1,
    //使用动画旋转曲线
    ProfileYaw = 2
}
/// <summary>
/// Motion 被状态机打断后，决定是否仍解析源状态的退出表现
/// </summary>
public enum PlayerMotionInterruptedExitPolicy
{
    ResolveNormalTransitionMotion = 0,
    DirectToTargetPresentation = 1
}
/// <summary>
/// 动画数据定义
/// </summary>
[CreateAssetMenu(fileName = "PlayerMotionDefinition", menuName = "Player/Motion/Definition")]
public class PlayerMotionDefinition : ScriptableObject
{
    //用哪一份动画数据的轨迹
    [SerializeField] private PlayerMotionProfile profile;
    [SerializeField] private PlayerMotionProfile leftFootProfile;
    [SerializeField] private PlayerMotionProfile rightFootProfile;
    [SerializeField] private bool requiresFootProfiles;
    [SerializeField] private bool usePhaseFootSelection;
    [SerializeField, Range(0f, 1f)] private float nextPlantFootThreshold = 0.5f;
    [SerializeField] private PlayerMotionRotationPolicy rotationPolicy;
    //希望动画完成时间
    [Min(0f)] [SerializeField] private float durationOverride;
    //移动倍率
    [Min(0f)] [SerializeField] private float translationScale = 1f;
    //进入 Finite Motion 时从源地面循环移交到 Motion
    [Range(0f, 1f)] [SerializeField] private float entryHandoffEndProgress;
    [SerializeField] private AnimationCurve entryTranslationWeight = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    //Finite Motion 结束时从烘焙位移移交到目标状态
    [FormerlySerializedAs("handoffStartProgress")]
    [Range(0f, 1f)] [SerializeField] private float exitHandoffStartProgress = 0.8f;
    [FormerlySerializedAs("handoffEndProgress")]
    [Range(0f, 1f)] [SerializeField] private float exitHandoffEndProgress = 1f;
    //状态转换承诺窗口；窗口只约束状态机何时接受普通请求，不拥有转换裁决权
    [Range(0f, 1f)] [SerializeField] private float transitionLockEndProgress;
    [SerializeField] private PlayerMotionInterruptedExitPolicy interruptedExitPolicy;
    [FormerlySerializedAs("translationAuthority")]
    [SerializeField] private AnimationCurve exitTranslationAuthority = AnimationCurve.Linear(0f, 1f, 1f, 0f);
    //是否需要对应的动画表现
    [SerializeField] private bool requiresPresentation = true;

    public PlayerMotionProfile Profile => profile;
    public PlayerMotionProfile LeftFootProfile => leftFootProfile;
    public PlayerMotionProfile RightFootProfile => rightFootProfile;
    public bool RequiresFootProfiles => requiresFootProfiles;
    public bool UsePhaseFootSelection => usePhaseFootSelection;
    public float NextPlantFootThreshold => nextPlantFootThreshold;
    public PlayerMotionRotationPolicy RotationPolicy => rotationPolicy;
    //可控制动画播放时间，若没设设定使用默认的动画时长
    public float Duration => GetDuration(PlayerFoot.Unknown);
    public float TranslationScale => translationScale;
    public float EntryHandoffEndProgress => entryHandoffEndProgress;
    public float ExitHandoffStartProgress => exitHandoffStartProgress;
    public float ExitHandoffEndProgress => exitHandoffEndProgress;
    public bool HasEntryHandoff => entryHandoffEndProgress > 0f;
    public float TransitionLockEndProgress => transitionLockEndProgress;
    public PlayerMotionInterruptedExitPolicy InterruptedExitPolicy => interruptedExitPolicy;
    public bool RequiresPresentation => requiresPresentation;

    public PlayerMotionProfile ResolveProfile(PlayerFoot foot)
    {
        if (foot == PlayerFoot.Left && leftFootProfile != null) return leftFootProfile;
        if (foot == PlayerFoot.Right && rightFootProfile != null) return rightFootProfile;
        return profile;
    }
    /// <summary>
    /// 处理通过步幅选取动画的逻辑
    /// </summary>
    public PlayerFoot ResolveEntryFoot(PlayerLocomotionPhaseSnapshot phaseSnapshot)
    {
        if (!usePhaseFootSelection || !phaseSnapshot.HasPhase) return phaseSnapshot.LastPlantFoot;
        return phaseSnapshot.StepProgress < nextPlantFootThreshold ? phaseSnapshot.LastPlantFoot : phaseSnapshot.NextPlantFoot;
    }

    public float GetDuration(PlayerFoot foot) => durationOverride > 0f ? durationOverride : ResolveProfile(foot)?.Duration ?? 0f;
    public float GetDuration(PlayerMotionProfile selectedProfile) => durationOverride > 0f ? durationOverride : selectedProfile?.Duration ?? 0f;
    /// <summary>
    /// 将 Entry Handoff 的过程从 0-EntryEnd 重映射为 0-1
    /// </summary>
    public float CalculateEntryHandoffProgress(float motionProgress)
    {
        if (entryHandoffEndProgress <= 0f) return 1f;
        return Mathf.Clamp01(motionProgress / entryHandoffEndProgress);
    }
    /// <summary>
    /// 位移混合0为动画，1为motion
    /// </summary>
    public float EvaluateEntryTranslationWeight(float motionProgress)
    {
        float handoffProgress = CalculateEntryHandoffProgress(motionProgress);
        return Mathf.Clamp01(entryTranslationWeight == null ? handoffProgress : entryTranslationWeight.Evaluate(handoffProgress));
    }

    /// <summary>
    /// 将 Exit Handoff 的过程从 Start-End 重映射为 0-1
    /// </summary>
    public float CalculateExitHandoffProgress(float motionProgress)
    {
        if (exitHandoffEndProgress <= exitHandoffStartProgress) return motionProgress >= exitHandoffEndProgress ? 1f : 0f;
        //过程相对于整体0-1映射
        return Mathf.Clamp01((motionProgress - exitHandoffStartProgress) / (exitHandoffEndProgress - exitHandoffStartProgress));
    }

    public float EvaluateExitTranslationAuthority(float motionProgress) => EvaluateAuthority(exitTranslationAuthority, motionProgress);
    /// <summary>
    /// 数据校验
    /// </summary>
    public bool Validate(ICollection<string> errors)
    {
        bool valid = true;
        if (profile == null) { errors?.Add(name + ": 缺少 MotionProfile。"); return false; }
        valid &= profile.Validate(errors);
        if (leftFootProfile != null) valid &= leftFootProfile.Validate(errors);
        if (rightFootProfile != null) valid &= rightFootProfile.Validate(errors);
        if ((requiresFootProfiles || usePhaseFootSelection) && leftFootProfile == null) { errors?.Add(name + ": 缺少 Left Foot MotionProfile。"); valid = false; }
        if ((requiresFootProfiles || usePhaseFootSelection) && rightFootProfile == null) { errors?.Add(name + ": 缺少 Right Foot MotionProfile。"); valid = false; }
        if (usePhaseFootSelection) valid &= ValidatePhaseFootSelection(errors);
        if (float.IsNaN(Duration) || float.IsInfinity(Duration) || Duration <= 0f) { errors?.Add(name + ": Runtime Duration 必须是大于 0 的有限值。"); valid = false; }
        if (float.IsNaN(translationScale) || float.IsInfinity(translationScale)) { errors?.Add(name + ": TranslationScale 必须是有限值。"); valid = false; }
        bool progressValuesValid = true;
        if (!IsFiniteProgress(entryHandoffEndProgress)) { errors?.Add(name + ": EntryHandoffEndProgress 必须是 0 到 1 的有限值。"); valid = false; progressValuesValid = false; }
        if (!IsFiniteProgress(exitHandoffStartProgress)) { errors?.Add(name + ": ExitHandoffStartProgress 必须是 0 到 1 的有限值。"); valid = false; progressValuesValid = false; }
        if (!IsFiniteProgress(exitHandoffEndProgress)) { errors?.Add(name + ": ExitHandoffEndProgress 必须是 0 到 1 的有限值。"); valid = false; progressValuesValid = false; }
        if (progressValuesValid && (entryHandoffEndProgress > exitHandoffStartProgress || exitHandoffStartProgress > exitHandoffEndProgress))
        {
            errors?.Add(name + ": Handoff 进度必须满足 0 <= EntryHandoffEndProgress <= ExitHandoffStartProgress <= ExitHandoffEndProgress <= 1。");
            valid = false;
        }
        valid &= ValidateEntryTranslationWeight(errors);
        valid &= ValidateTrajectoryProfile(profile, errors);
        if (leftFootProfile != null) valid &= ValidateTrajectoryProfile(leftFootProfile, errors);
        if (rightFootProfile != null) valid &= ValidateTrajectoryProfile(rightFootProfile, errors);
        if (float.IsNaN(transitionLockEndProgress) || float.IsInfinity(transitionLockEndProgress) || transitionLockEndProgress < 0f || transitionLockEndProgress > 1f) { errors?.Add(name + ": TransitionLockEndProgress 必须是 0 到 1 的有限值。"); valid = false; }
        return valid;
    }

    private bool ValidateTrajectoryProfile(PlayerMotionProfile motionProfile, ICollection<string> errors)
    {
        bool valid = true;
        if (!motionProfile.HasPlanarPosition) { errors?.Add(name + ": " + motionProfile.name + " 需要有效 XZ channel。"); valid = false; }
        if (!motionProfile.HasYaw) { errors?.Add(name + ": " + motionProfile.name + " 需要有效 Yaw channel。"); valid = false; }
        return valid;
    }

    private bool ValidatePhaseFootSelection(ICollection<string> errors)
    {
        bool valid = true;
        if (!requiresFootProfiles) { errors?.Add(name + ": 启用 Phase Foot Selection 时必须启用 RequiresFootProfiles。"); valid = false; }
        if (float.IsNaN(nextPlantFootThreshold) || float.IsInfinity(nextPlantFootThreshold) || nextPlantFootThreshold < 0f || nextPlantFootThreshold > 1f)
        {
            errors?.Add(name + ": NextPlantFootThreshold 必须是 0 到 1 的有限值。");
            valid = false;
        }
        if (leftFootProfile != null) valid &= ValidateFirstPlantFoot(leftFootProfile, PlayerFoot.Right, "Left", errors);
        if (rightFootProfile != null) valid &= ValidateFirstPlantFoot(rightFootProfile, PlayerFoot.Left, "Right", errors);
        return valid;
    }

    private bool ValidateFirstPlantFoot(PlayerMotionProfile motionProfile, PlayerFoot expectedFoot, string label, ICollection<string> errors)
    {
        PlayerFoot firstFoot = PlayerFoot.Unknown;
        float firstTime = float.PositiveInfinity;
        for (int index = 0; index < motionProfile.PlantMarkers.Count; index++)
        {
            PlayerFootPlantMarker marker = motionProfile.PlantMarkers[index];
            if ((marker.Foot != PlayerFoot.Left && marker.Foot != PlayerFoot.Right) || float.IsNaN(marker.NormalizedTime) || float.IsInfinity(marker.NormalizedTime) || marker.NormalizedTime < 0f || marker.NormalizedTime > 1f || marker.NormalizedTime >= firstTime) continue;
            firstFoot = marker.Foot;
            firstTime = marker.NormalizedTime;
        }
        if (firstFoot == expectedFoot) return true;
        if (firstFoot == PlayerFoot.Unknown) errors?.Add(name + ": " + label + " Foot Profile 缺少有效的首个 Plant。");
        else errors?.Add(name + ": " + label + " Foot Profile 的首个真实 Plant 必须是 " + expectedFoot + "。");
        return false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Unity 编辑器中配置轨迹、状态锁承诺窗口和中断表现策略。
    /// </summary>
    public void Configure(PlayerMotionProfile motionProfile, PlayerMotionRotationPolicy rotation,
        float runtimeDuration, float scale, float exitHandoffStart, float exitHandoffEnd, bool presentation = true, float transitionLockEndProgress = 0f, PlayerMotionInterruptedExitPolicy interruptedExitPolicy = PlayerMotionInterruptedExitPolicy.ResolveNormalTransitionMotion, bool requireFootProfiles = false)
    {
        profile = motionProfile;
        rotationPolicy = rotation;
        durationOverride = runtimeDuration;
        translationScale = scale;
        entryHandoffEndProgress = 0f;
        entryTranslationWeight = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        exitHandoffStartProgress = exitHandoffStart;
        exitHandoffEndProgress = exitHandoffEnd;
        this.transitionLockEndProgress = transitionLockEndProgress;
        this.interruptedExitPolicy = interruptedExitPolicy;
        requiresPresentation = presentation;
        requiresFootProfiles = requireFootProfiles;
        exitTranslationAuthority = AnimationCurve.Linear(0f, 1f, 1f, 0f);
    }

    public void ConfigureEntryHandoff(float endProgress, AnimationCurve targetTranslationWeight = null)
    {
        entryHandoffEndProgress = endProgress;
        entryTranslationWeight = targetTranslationWeight ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    public void ConfigureFootProfiles(PlayerMotionProfile left, PlayerMotionProfile right, bool requireProfiles)
    {
        leftFootProfile = left;
        rightFootProfile = right;
        requiresFootProfiles = requireProfiles;
    }
#endif
    /// <summary>
    /// 依据过渡进程返回动画控制权重
    /// </summary>
    private float EvaluateAuthority(AnimationCurve curve, float motionProgress)
    {
        //零长度移交没有混合区间，由动画位移负责到完成帧结束
        if (exitHandoffEndProgress <= exitHandoffStartProgress) return 1f;
        //未开始时完全动画掌控
        if (motionProgress < exitHandoffStartProgress) return 1f;
        //完全结束后交给程序掌控
        if (motionProgress >= exitHandoffEndProgress) return 0f;
        //拿到0-1映射
        float handoff = CalculateExitHandoffProgress(motionProgress);
        
        return Mathf.Clamp01(curve == null ? 1f - handoff : curve.Evaluate(handoff));
    }

    private bool ValidateEntryTranslationWeight(ICollection<string> errors)
    {
        if (entryTranslationWeight == null)
        {
            errors?.Add(name + ": EntryTranslationWeight 曲线不能为空。");
            return false;
        }
        float start = entryTranslationWeight.Evaluate(0f);
        float end = entryTranslationWeight.Evaluate(1f);
        if (float.IsNaN(start) || float.IsInfinity(start) || !Mathf.Approximately(start, 0f) || float.IsNaN(end) || float.IsInfinity(end) || !Mathf.Approximately(end, 1f))
        {
            errors?.Add(name + ": EntryTranslationWeight 曲线端点必须为 0 和 1。");
            return false;
        }
        return true;
    }

    private static bool IsFiniteProgress(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    }
}
