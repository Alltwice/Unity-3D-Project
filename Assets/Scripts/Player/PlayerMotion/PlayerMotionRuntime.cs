using UnityEngine;

/// <summary>单个 Motion 的本帧采样贡献，不包含交接权重</summary>
public struct PlayerMotionFrame
{
    public PlayerMotionDefinition Definition { get; }
    public PlayerMotionProfile Profile { get; }
    public PlayerFoot EntryLastPlantFoot { get; }
    public Vector3 AuthoredPlanarDisplacement { get; }
    public float AuthoredYawDelta { get; }
    public float RemainingAuthoredYaw { get; }
    public float PreviousProgress { get; }
    public float CurrentProgress { get; }
    public Vector3 AuthoredFacingBeforeStep { get; }
    public bool IsValid => Definition != null;
    public PlayerMotionFrame(PlayerMotionDefinition definition, Vector3 displacement, float yaw, float remainingYaw, float previous, float current)
        : this(definition, definition == null ? null : definition.Profile, PlayerFoot.Unknown, displacement, yaw, remainingYaw, previous, current, Vector3.zero) { }
    public PlayerMotionFrame(PlayerMotionDefinition definition, PlayerMotionProfile profile, PlayerFoot foot, Vector3 displacement, float yaw, float remainingYaw, float previous, float current, Vector3 facing = default)
    {
        Definition = definition; Profile = profile; EntryLastPlantFoot = foot;
        AuthoredPlanarDisplacement = displacement; AuthoredYawDelta = yaw; RemainingAuthoredYaw = remainingYaw;
        PreviousProgress = previous; CurrentProgress = current; AuthoredFacingBeforeStep = facing;
    }
}

/// <summary>单实例生命周期事实；只有 Handoff 目标向 Gameplay 暴露这些事实</summary>
public struct PlayerMotionSnapshot
{
    public PlayerMotionDefinition ActiveDefinition { get; }
    public PlayerMotionProfile ActiveProfile { get; }
    public PlayerFoot EntryLastPlantFoot { get; }
    public ulong InstanceId { get; }
    public float Progress { get; }
    public bool IsActive { get; }
    public bool JustCompleted { get; }
    public bool JustCancelled { get; }
    public bool IsTransitionLocked { get; }
    public PlayerMotionSnapshot(PlayerMotionDefinition definition, PlayerMotionProfile profile, PlayerFoot foot, ulong id, float progress, bool active, bool completed, bool cancelled, bool locked = false)
    {
        ActiveDefinition = definition; ActiveProfile = profile; EntryLastPlantFoot = foot; InstanceId = id;
        Progress = progress; IsActive = active; JustCompleted = completed; JustCancelled = cancelled; IsTransitionLocked = locked;
    }
}
public class PlayerMotionRuntime
{
    private PlayerMotionDefinition definition;
    private PlayerMotionProfile profile;
    private PlayerFoot entryLastPlantFoot;
    //消除角色动画影响转向世界位置
    private Quaternion basis = Quaternion.identity;
    //玩家移动数据
    private Vector3 travelDirection;
    private ulong sequence;
    private ulong instanceId;
    private float elapsedTime;
    private float duration;
    private float previousProgress;
    private float startYaw;
    private float currentProgress;
    private bool isActive;
    private bool justCompleted;
    private bool justCancelled;

    public PlayerMotionSnapshot Snapshot => BuildSnapshot();
    internal Vector3 AuthoredFacing => profile == null ? Vector3.forward : basis * (Quaternion.AngleAxis(profile.EvaluateYaw(currentProgress) - startYaw, Vector3.up) * Vector3.forward);
    /// <summary>
    /// 处理单帧事件例如跳跃开始结束等
    /// </summary>
    public void BeginFrame(bool retainCompleted = false)
    {
        if (!isActive && (justCancelled || (justCompleted && !retainCompleted)))
        {
            definition = null;
            profile = null;
            entryLastPlantFoot = PlayerFoot.Unknown;
            duration = 0f;
        }
        justCompleted = false;
        justCancelled = false;
    }
    /// <summary>
    /// 动画启动时的基础设定
    /// </summary>
    public ulong Begin(PlayerMotionDefinition nextDefinition, Vector3 basisDirection, Vector3 initialTravelDirection, float startProgress = 0f)
        => Begin(nextDefinition, nextDefinition == null ? null : nextDefinition.Profile, PlayerFoot.Unknown, basisDirection, initialTravelDirection, startProgress);

    public ulong Begin(PlayerMotionDefinition nextDefinition, PlayerMotionProfile selectedProfile, PlayerFoot selectedEntryLastPlantFoot, Vector3 basisDirection, Vector3 initialTravelDirection, float startProgress = 0f)
    {
        bool replaced = isActive;
        //切换动画数据
        definition = nextDefinition;
        profile = selectedProfile ?? (definition == null ? null : definition.ResolveProfile(selectedEntryLastPlantFoot));
        entryLastPlantFoot = selectedEntryLastPlantFoot;
        duration = definition == null ? 0f : definition.GetDuration(profile);
        instanceId = ++sequence;
        //当前开始动画执行时间
        elapsedTime = Mathf.Clamp01(startProgress) * duration;
        previousProgress = Mathf.Clamp01(startProgress);
        currentProgress = previousProgress;
        startYaw = profile == null ? 0f : profile.EvaluateYaw(currentProgress);
        //记录前方
        basisDirection = NormalizePlanar(basisDirection, Vector3.forward);
        //实际运动方向
        travelDirection = NormalizePlanar(initialTravelDirection, basisDirection);
        //创建面向玩家前方的旋转
        basis = Quaternion.LookRotation(basisDirection, Vector3.up);
        justCompleted = false;
        justCancelled = replaced;
        isActive = definition != null && profile != null && duration > 0f;
        //返回这一次的动画处理ID
        return instanceId;
    }

    public void Cancel()
    {
        if (!isActive) return;
        isActive = false;
        justCompleted = false;
        justCancelled = true;
    }
    /// <summary>
    /// 按固定间隔时间推进动画演进
    /// </summary>
    public PlayerMotionFrame Advance(float deltaTime, PlayerGameplayIntent intent)
    {
        if (definition == null) return default;
        if (!isActive) return new PlayerMotionFrame(definition, profile, entryLastPlantFoot, Vector3.zero, 0f, 0f, currentProgress, currentProgress, AuthoredFacing);
        if (definition.TranslationPolicy == PlayerMotionTranslationPolicy.TravelAlongDesiredDirection && intent.DesiredMoveDirection.sqrMagnitude > 0.0001f) travelDirection = NormalizePlanar(intent.DesiredMoveDirection, travelDirection);
        previousProgress = currentProgress;
        //推进deltatime的时间
        elapsedTime = Mathf.Min(duration, elapsedTime + Mathf.Max(0f, deltaTime));
        //计算进程
        currentProgress = duration > 0f ? Mathf.Clamp01(elapsedTime / duration) : 1f;
        PlayerMotionProfile activeProfile = profile;
        //拿到需要烘焙移动的位移数据
        Vector3 authoredTranslation = EvaluateTranslation(activeProfile, definition, previousProgress, currentProgress);
        //一帧要转多少度
        float authoredYaw = definition.RotationPolicy == PlayerMotionRotationPolicy.ProfileYaw ? activeProfile.EvaluateYaw(currentProgress) - activeProfile.EvaluateYaw(previousProgress) : 0f;
        //检查从当前开始距离旋转结束还差多少度
        float remainingAuthoredYaw = definition.RotationPolicy == PlayerMotionRotationPolicy.ProfileYaw ? activeProfile.EvaluateYaw(1f) - activeProfile.EvaluateYaw(currentProgress) : 0f;
        //拿到动画控制权重
        //产生这一帧等待消费的移动数据
        Vector3 authoredFacingBeforeStep = basis * (Quaternion.AngleAxis(activeProfile.EvaluateYaw(previousProgress) - startYaw, Vector3.up) * Vector3.forward);
        PlayerMotionFrame frame = new PlayerMotionFrame(definition, activeProfile, entryLastPlantFoot, authoredTranslation, authoredYaw, remainingAuthoredYaw, previousProgress, currentProgress, authoredFacingBeforeStep);
        if (currentProgress >= 1f)
        {
            isActive = false;
            justCompleted = true;
        }
        return frame;
    }
    /// <summary>
    /// 利用烘焙动画数据文件执行移动
    /// </summary>
    private Vector3 EvaluateTranslation(PlayerMotionProfile profile, PlayerMotionDefinition motionDefinition, float fromProgress, float toProgress)
    {
        switch (motionDefinition.TranslationPolicy)
        {
            case PlayerMotionTranslationPolicy.TravelAlongCapturedDirection:
            case PlayerMotionTranslationPolicy.TravelAlongDesiredDirection:
                //方向*（移动过程比例*整体缩放）可理解为速度
                return travelDirection * ((profile.EvaluateTravelDistance(toProgress) - profile.EvaluateTravelDistance(fromProgress)) * motionDefinition.TranslationScale);
            case PlayerMotionTranslationPolicy.LocalTrajectory:
            case PlayerMotionTranslationPolicy.SteeredLocalTrajectory:
                return basis * ((profile.EvaluatePlanarPosition(toProgress) - profile.EvaluatePlanarPosition(fromProgress)) * motionDefinition.TranslationScale);
            default:
                return Vector3.zero;
        }
    }
    /// <summary>
    /// 建立快照
    /// </summary>
    /// <returns></returns>
    private PlayerMotionSnapshot BuildSnapshot()
    {
        bool locked = definition != null && isActive && currentProgress < definition.TransitionLockEndProgress;
        return new PlayerMotionSnapshot(definition, profile, entryLastPlantFoot, instanceId, currentProgress, isActive, justCompleted, justCancelled, locked);
    }

    /// <summary>
    /// 去除y分量并将其向量化
    /// </summary>
    private static Vector3 NormalizePlanar(Vector3 value, Vector3 fallback)
    {
        value.y = 0f;
        if (value.sqrMagnitude > 0.0001f) return value.normalized;
        fallback.y = 0f;
        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.zero;
    }
}
