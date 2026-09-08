using UnityEngine;

/// <summary>
/// 在一帧中提供 Finite Motion 的原始位移、Yaw 与 Handoff 数据
/// </summary>
public struct PlayerMotionFrame
{
    public PlayerMotionFrame(PlayerMotionDefinition definition, PlayerMotionProfile profile, PlayerFoot entryLastPlantFoot, Vector3 authoredPlanarDisplacement, float authoredYawDelta, float remainingAuthoredYaw, float previousProgress, float currentProgress, float exitTranslationAuthority, bool entryHandoffActive, float entryTargetTranslationWeight, Vector3 entrySourcePlanarVelocity, Vector3 authoredFacingBeforeStep)
    {
        Definition = definition;
        Profile = profile;
        EntryLastPlantFoot = entryLastPlantFoot;
        AuthoredPlanarDisplacement = authoredPlanarDisplacement;
        AuthoredYawDelta = authoredYawDelta;
        AuthoredFacingBeforeStep = authoredFacingBeforeStep;
        RemainingAuthoredYaw = remainingAuthoredYaw;
        PreviousProgress = previousProgress;
        CurrentProgress = currentProgress;
        ExitTranslationAuthority = exitTranslationAuthority;
        EntryHandoffActive = entryHandoffActive;
        EntryTargetTranslationWeight = entryTargetTranslationWeight;
        EntrySourcePlanarVelocity = entrySourcePlanarVelocity;
    }

    public PlayerMotionDefinition Definition { get; }
    public PlayerMotionProfile Profile { get; }
    public PlayerFoot EntryLastPlantFoot { get; }
    public Vector3 AuthoredPlanarDisplacement { get; }
    public float AuthoredYawDelta { get; }
    public Vector3 AuthoredFacingBeforeStep { get; }
    public float RemainingAuthoredYaw { get; }
    public float PreviousProgress { get; }
    public float CurrentProgress { get; }
    public float ExitTranslationAuthority { get; }
    public bool EntryHandoffActive { get; }
    public float EntryTargetTranslationWeight { get; }
    public Vector3 EntrySourcePlanarVelocity { get; }
    public bool IsValid => Definition != null;
}
/// <summary>
/// 供外部获取的 Motion 状态快照，其中同时包含供 AnimationController 消费的 Handoff 数据
/// </summary>
public struct PlayerMotionSnapshot
{
    public PlayerMotionSnapshot(PlayerMotionDefinition activeDefinition, ulong instanceId, float progress, float exitHandoffProgress, bool exitHandoffActive, bool isActive, bool justCompleted, bool justCancelled, bool isTransitionLocked = false)
        : this(activeDefinition, activeDefinition == null ? null : activeDefinition.Profile, PlayerFoot.Unknown, instanceId, progress, exitHandoffProgress, exitHandoffActive, false, false, 0f, PlayerLocomotionMode.Idle, isActive, justCompleted, justCancelled, isTransitionLocked)
    {
    }

    public PlayerMotionSnapshot(PlayerMotionDefinition activeDefinition, ulong instanceId, float progress, float exitHandoffProgress, bool exitHandoffActive, bool hasEntrySource, bool entryHandoffActive, float entryHandoffProgress, PlayerLocomotionMode entrySourceLocomotionMode, bool isActive, bool justCompleted, bool justCancelled, bool isTransitionLocked = false)
        : this(activeDefinition, activeDefinition == null ? null : activeDefinition.Profile, PlayerFoot.Unknown, instanceId, progress, exitHandoffProgress, exitHandoffActive, hasEntrySource, entryHandoffActive, entryHandoffProgress, entrySourceLocomotionMode, isActive, justCompleted, justCancelled, isTransitionLocked)
    {
    }

    public PlayerMotionSnapshot(PlayerMotionDefinition activeDefinition, PlayerMotionProfile activeProfile, PlayerFoot entryLastPlantFoot, ulong instanceId, float progress, float exitHandoffProgress, bool exitHandoffActive, bool isActive, bool justCompleted, bool justCancelled, bool isTransitionLocked = false)
        : this(activeDefinition, activeProfile, entryLastPlantFoot, instanceId, progress, exitHandoffProgress, exitHandoffActive, false, false, 0f, PlayerLocomotionMode.Idle, isActive, justCompleted, justCancelled, isTransitionLocked)
    {
    }

    public PlayerMotionSnapshot(PlayerMotionDefinition activeDefinition, PlayerMotionProfile activeProfile, PlayerFoot entryLastPlantFoot, ulong instanceId, float progress, float exitHandoffProgress, bool exitHandoffActive, bool hasEntrySource, bool entryHandoffActive, float entryHandoffProgress, PlayerLocomotionMode entrySourceLocomotionMode, bool isActive, bool justCompleted, bool justCancelled, bool isTransitionLocked = false)
    {
        ActiveDefinition = activeDefinition;
        ActiveProfile = activeProfile;
        EntryLastPlantFoot = entryLastPlantFoot;
        InstanceId = instanceId;
        Progress = progress;
        ExitHandoffProgress = exitHandoffProgress;
        ExitHandoffActive = exitHandoffActive;
        HasEntrySource = hasEntrySource;
        EntryHandoffActive = entryHandoffActive;
        EntryHandoffProgress = entryHandoffProgress;
        EntrySourceLocomotionMode = entrySourceLocomotionMode;
        IsActive = isActive;
        JustCompleted = justCompleted;
        JustCancelled = justCancelled;
        IsTransitionLocked = isTransitionLocked;
    }

    public PlayerMotionDefinition ActiveDefinition { get; }
    public PlayerMotionProfile ActiveProfile { get; }
    public PlayerFoot EntryLastPlantFoot { get; }
    public ulong InstanceId { get; }
    public float Progress { get; }
    public float ExitHandoffProgress { get; }
    public bool ExitHandoffActive { get; }
    public bool HasEntrySource { get; }
    public bool EntryHandoffActive { get; }
    public float EntryHandoffProgress { get; }
    public PlayerLocomotionMode EntrySourceLocomotionMode { get; }
    public bool IsActive { get; }
    public bool JustCompleted { get; }
    public bool JustCancelled { get; }
    public bool IsTransitionLocked { get; }
}
public class PlayerMotionRuntime
{
    private PlayerMotionDefinition definition;
    private PlayerMotionProfile profile;
    private PlayerFoot entryLastPlantFoot;
    private PlayerMotionEntrySource entrySource;
    //以 Motion 进入时的角色朝向作为局部轨迹基准
    private Quaternion basis = Quaternion.identity;
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
    /// <summary>
    /// 处理单帧事件例如跳跃开始结束等
    /// </summary>
    public void BeginFrame()
    {
        if (!isActive && (justCompleted || justCancelled))
        {
            definition = null;
            profile = null;
            entryLastPlantFoot = PlayerFoot.Unknown;
            entrySource = default;
            duration = 0f;
        }
        justCompleted = false;
        justCancelled = false;
    }
    /// <summary>
    /// 启动 Motion 并记录进入朝向
    /// </summary>
    public ulong Begin(PlayerMotionDefinition nextDefinition, Vector3 entryFacing, float startProgress = 0f)
    {
        return Begin(nextDefinition, nextDefinition == null ? null : nextDefinition.Profile, PlayerFoot.Unknown, default, entryFacing, startProgress);
    }

    public ulong Begin(PlayerMotionDefinition nextDefinition, PlayerMotionEntrySource source, Vector3 entryFacing, float startProgress = 0f)
    {
        return Begin(nextDefinition, nextDefinition == null ? null : nextDefinition.Profile, PlayerFoot.Unknown, source, entryFacing, startProgress);
    }

    public ulong Begin(PlayerMotionDefinition nextDefinition, PlayerMotionProfile selectedProfile, PlayerFoot selectedEntryLastPlantFoot, Vector3 entryFacing, float startProgress = 0f)
    {
        return Begin(nextDefinition, selectedProfile, selectedEntryLastPlantFoot, default, entryFacing, startProgress);
    }

    public ulong Begin(PlayerMotionDefinition nextDefinition, PlayerMotionProfile selectedProfile, PlayerFoot selectedEntryLastPlantFoot, PlayerMotionEntrySource source, Vector3 entryFacing, float startProgress = 0f)
    {
        bool replaced = isActive;
        //切换动画数据
        definition = nextDefinition;
        profile = selectedProfile ?? (definition == null ? null : definition.ResolveProfile(selectedEntryLastPlantFoot));
        entryLastPlantFoot = selectedEntryLastPlantFoot;
        entrySource = definition != null && definition.HasEntryHandoff && source.IsValid ? NormalizeEntrySource(source) : default;
        duration = definition == null ? 0f : definition.GetDuration(profile);
        instanceId = ++sequence;
        //当前开始动画执行时间
        elapsedTime = Mathf.Clamp01(startProgress) * duration;
        previousProgress = Mathf.Clamp01(startProgress);
        currentProgress = previousProgress;
        startYaw = profile == null ? 0f : profile.EvaluateYaw(currentProgress);
        entryFacing = NormalizePlanar(entryFacing, Vector3.forward);
        basis = Quaternion.LookRotation(entryFacing, Vector3.up);
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
    /// 按固定间隔时间推进 Motion 演进
    /// </summary>
    public PlayerMotionFrame Advance(float deltaTime)
    {
        if (!isActive || definition == null) return default;
        previousProgress = currentProgress;
        //推进deltatime的时间
        elapsedTime = Mathf.Min(duration, elapsedTime + Mathf.Max(0f, deltaTime));
        //计算进程
        currentProgress = duration > 0f ? Mathf.Clamp01(elapsedTime / duration) : 1f;
        PlayerMotionProfile activeProfile = profile;
        //拿到需要烘焙移动的位移数据
        Vector3 authoredTranslation = EvaluateTranslation(activeProfile, definition, previousProgress, currentProgress);
        float authoredYaw = activeProfile.EvaluateYaw(currentProgress) - activeProfile.EvaluateYaw(previousProgress);
        float remainingAuthoredYaw = activeProfile.EvaluateYaw(1f) - activeProfile.EvaluateYaw(currentProgress);
        //拿到动画控制权重
        float exitTranslationAuthority = definition.EvaluateExitTranslationAuthority(currentProgress);
        bool entryHandoffActive = HasEntrySource && currentProgress < definition.EntryHandoffEndProgress;
        float entryTargetTranslationWeight = HasEntrySource ? definition.EvaluateEntryTranslationWeight(currentProgress) : 1f;
        Vector3 authoredFacingBeforeStep = basis * (Quaternion.AngleAxis(activeProfile.EvaluateYaw(previousProgress) - startYaw, Vector3.up) * Vector3.forward);
        PlayerMotionFrame frame = new PlayerMotionFrame(definition, activeProfile, entryLastPlantFoot, authoredTranslation, authoredYaw, remainingAuthoredYaw, previousProgress, currentProgress, exitTranslationAuthority, entryHandoffActive, entryTargetTranslationWeight, entrySource.PlanarVelocity, authoredFacingBeforeStep);
        if (currentProgress >= 1f)
        {
            isActive = false;
            justCompleted = true;
        }
        return frame;
    }
    /// <summary>
    /// 采样以进入朝向为基准的原始局部 XZ 轨迹
    /// </summary>
    private Vector3 EvaluateTranslation(PlayerMotionProfile profile, PlayerMotionDefinition motionDefinition, float fromProgress, float toProgress)
    {
        return basis * ((profile.EvaluatePlanarPosition(toProgress) - profile.EvaluatePlanarPosition(fromProgress)) * motionDefinition.TranslationScale);
    }
    /// <summary>
    /// 建立快照
    /// </summary>
    /// <returns></returns>
    private PlayerMotionSnapshot BuildSnapshot()
    {
        bool hasEntrySource = HasEntrySource;
        float exitHandoffProgress = definition == null ? 0f : definition.CalculateExitHandoffProgress(currentProgress);
        bool exitHandoffActive = definition != null && currentProgress >= definition.ExitHandoffStartProgress;
        float entryHandoffProgress = hasEntrySource ? definition.CalculateEntryHandoffProgress(currentProgress) : 0f;
        bool entryHandoffActive = hasEntrySource && isActive && currentProgress < definition.EntryHandoffEndProgress;
        //这里处理动画锁
        bool isTransitionLocked = definition != null && isActive && currentProgress < definition.TransitionLockEndProgress;
        return new PlayerMotionSnapshot(definition, profile, entryLastPlantFoot, instanceId, currentProgress, exitHandoffProgress, exitHandoffActive, hasEntrySource, entryHandoffActive, entryHandoffProgress, hasEntrySource ? entrySource.LocomotionMode : PlayerLocomotionMode.Idle, isActive, justCompleted, justCancelled, isTransitionLocked);
    }

    private bool HasEntrySource => definition != null && definition.HasEntryHandoff && entrySource.IsValid;

    private static PlayerMotionEntrySource NormalizeEntrySource(PlayerMotionEntrySource source)
    {
        source.PlanarVelocity.y = 0f;
        return source;
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
