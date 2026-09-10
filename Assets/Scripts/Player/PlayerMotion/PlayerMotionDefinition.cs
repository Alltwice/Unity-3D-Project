using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
/// <summary>
/// 最终应该怎么移动
/// </summary>
public enum PlayerMotionTranslationPolicy
{
    None = 0,
    //速度
    VelocityDriven = 1,
    //使用 Motion Begin 时捕获的方向和动画移动距离
    TravelAlongCapturedDirection = 2,
    //保留运动轨迹
    LocalTrajectory = 3,
    //使用当前 GameplayIntent 方向和动画移动距离
    TravelAlongDesiredDirection = 4,
    //保留局部轨迹，并跟随身体的额外转向修正
    SteeredLocalTrajectory = 5
}
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
/// 动画轨迹移动方向对应位置
/// </summary>
public enum PlayerMotionBasisPolicy
{
    //输入意图前方
    DesiredDirection,
    //输入瞬间朝向位置
    EntryVelocityDirection,
    //进入动画瞬间角色朝向
    EntryFacing
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
    [SerializeField] private PlayerMotionTranslationPolicy translationPolicy;
    [SerializeField] private PlayerMotionRotationPolicy rotationPolicy;
    [SerializeField] private PlayerMotionBasisPolicy basisPolicy;
    //希望动画完成时间
    [Min(0f)] [SerializeField] private float durationOverride;
    //移动倍率
    [Min(0f)] [SerializeField] private float translationScale = 1f;
    //状态转换承诺窗口；窗口只约束状态机何时接受普通请求，不拥有转换裁决权
    [Range(0f, 1f)] [SerializeField] private float transitionLockEndProgress;
    [SerializeField] private PlayerMotionInterruptedExitPolicy interruptedExitPolicy;
    //是否需要对应的动画表现
    [SerializeField] private bool requiresPresentation = true;
    [SerializeField] private PlayerHandoffEntrySettings handoffEntry = new PlayerHandoffEntrySettings();
    [SerializeField] private PlayerHandoffSuccessorSettings defaultSuccessor = new PlayerHandoffSuccessorSettings();

    public PlayerMotionProfile Profile => profile;
    public PlayerMotionProfile LeftFootProfile => leftFootProfile;
    public PlayerMotionProfile RightFootProfile => rightFootProfile;
    public bool RequiresFootProfiles => requiresFootProfiles;
    public bool UsePhaseFootSelection => usePhaseFootSelection;
    public float NextPlantFootThreshold => nextPlantFootThreshold;
    public PlayerMotionTranslationPolicy TranslationPolicy => translationPolicy;
    public PlayerMotionRotationPolicy RotationPolicy => rotationPolicy;
    public PlayerMotionBasisPolicy BasisPolicy => basisPolicy;
    //可控制动画播放时间，若没设设定使用默认的动画时长
    public float Duration => GetDuration(PlayerFoot.Unknown);
    public float TranslationScale => translationScale;
    public float TransitionLockEndProgress => transitionLockEndProgress;
    public PlayerMotionInterruptedExitPolicy InterruptedExitPolicy => interruptedExitPolicy;
    public bool RequiresPresentation => requiresPresentation;
    public PlayerHandoffEntrySettings HandoffEntry => handoffEntry;
    public PlayerHandoffSuccessorSettings DefaultSuccessor => defaultSuccessor;

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
        if (translationPolicy == PlayerMotionTranslationPolicy.SteeredLocalTrajectory)
        {
            if (rotationPolicy != PlayerMotionRotationPolicy.ProfileYaw || basisPolicy != PlayerMotionBasisPolicy.EntryFacing) { errors?.Add(name + ": SteeredLocalTrajectory 需要 ProfileYaw + EntryFacing。"); valid = false; }
            valid &= ValidateSteeredTrajectoryProfile(profile, errors);
            if (leftFootProfile != null) valid &= ValidateSteeredTrajectoryProfile(leftFootProfile, errors);
            if (rightFootProfile != null) valid &= ValidateSteeredTrajectoryProfile(rightFootProfile, errors);
        }
        if (float.IsNaN(transitionLockEndProgress) || float.IsInfinity(transitionLockEndProgress) || transitionLockEndProgress < 0f || transitionLockEndProgress > 1f) { errors?.Add(name + ": TransitionLockEndProgress 必须是 0 到 1 的有限值。"); valid = false; }
        if (rotationPolicy == PlayerMotionRotationPolicy.ProfileYaw && !profile.HasYaw) { errors?.Add(name + ": ProfileYaw 需要有效 Yaw channel。"); valid = false; }
        if (translationPolicy == PlayerMotionTranslationPolicy.LocalTrajectory && !profile.HasPlanarPosition) { errors?.Add(name + ": LocalTrajectory 需要有效 XZ channel。"); valid = false; }
        if ((translationPolicy == PlayerMotionTranslationPolicy.TravelAlongCapturedDirection || translationPolicy == PlayerMotionTranslationPolicy.TravelAlongDesiredDirection) && !profile.HasTravelDistance) { errors?.Add(name + ": TravelAlong 需要有效 Travel channel。"); valid = false; }
        return valid;
    }

    public bool ValidateHandoff(PlayerMotionNodeKey node, ICollection<string> errors)
    {
        bool valid = true;
        if (handoffEntry == null) { errors?.Add(name + ": 缺少进入混合配置。"); valid = false; }
        else valid &= handoffEntry.Validate(node, errors, name + ".Entry");
        if (defaultSuccessor == null) { errors?.Add(name + ": 缺少默认后继配置。"); valid = false; }
        else valid &= defaultSuccessor.Validate(node, errors, name + ".Successor");
        return valid;
    }

    private bool ValidateSteeredTrajectoryProfile(PlayerMotionProfile motionProfile, ICollection<string> errors)
    {
        bool valid = true;
        if (!motionProfile.HasPlanarPosition) { errors?.Add(name + ": SteeredLocalTrajectory 的 " + motionProfile.name + " 需要有效 XZ channel。"); valid = false; }
        if (!motionProfile.HasYaw) { errors?.Add(name + ": SteeredLocalTrajectory 的 " + motionProfile.name + " 需要有效 Yaw channel。"); valid = false; }
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
    public void Configure(PlayerMotionProfile motionProfile, PlayerMotionTranslationPolicy translation, PlayerMotionRotationPolicy rotation, PlayerMotionBasisPolicy basis, 
        float runtimeDuration, float scale, bool presentation = true, float transitionLockEndProgress = 0f, PlayerMotionInterruptedExitPolicy interruptedExitPolicy = PlayerMotionInterruptedExitPolicy.ResolveNormalTransitionMotion, bool requireFootProfiles = false)
    {
        profile = motionProfile;
        translationPolicy = translation;
        rotationPolicy = rotation;
        basisPolicy = basis;
        durationOverride = runtimeDuration;
        translationScale = scale;
        this.transitionLockEndProgress = transitionLockEndProgress;
        this.interruptedExitPolicy = interruptedExitPolicy;
        requiresPresentation = presentation;
        requiresFootProfiles = requireFootProfiles;
    }

    public void ConfigureFootProfiles(PlayerMotionProfile left, PlayerMotionProfile right, bool requireProfiles)
    {
        leftFootProfile = left;
        rightFootProfile = right;
        requiresFootProfiles = requireProfiles;
    }

    public void ConfigureHandoffEntry(PlayerHandoffEntrySettings entry)
    {
        handoffEntry = entry;
    }

    public void ConfigureDefaultSuccessor(PlayerHandoffSuccessorSettings successor)
    {
        defaultSuccessor = successor;
    }
#endif
}
