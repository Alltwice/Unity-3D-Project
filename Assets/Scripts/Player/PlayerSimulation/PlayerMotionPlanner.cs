using System;
using UnityEngine;

/// <summary>
/// 将 Gameplay transition/intent 解析成唯一 MotionDefinition，不接触动画或 CharacterController
/// </summary>
public class PlayerMotionPlanner : MonoBehaviour
{
    [SerializeField] private PlayerMotionCatalog catalog;

    private readonly PlayerMotionRuntime runtime = new PlayerMotionRuntime();
    private PlayerLocomotionPhaseRuntime phaseRuntime;

    public PlayerMotionCatalog Catalog => catalog;
    public PlayerMotionSnapshot Snapshot => runtime.Snapshot;
    public PlayerLocomotionPhaseSnapshot PhaseSnapshot => phaseRuntime.Snapshot;

    private void Awake()
    {
        phaseRuntime = new PlayerLocomotionPhaseRuntime(catalog);
    }

    public void BeginFrame() => runtime.BeginFrame();

    public void HandleStateTransition(PlayerStateTransition transition, PlayerGameplayIntent intent, PlayerMotorResult motorResult)
    {
        if (TryResolveTargetTransitionMotion(transition, intent, out PlayerMotionDefinition definition))
        {
            Begin(definition, motorResult);
            return;
        }
        PlayerMotionSnapshot motion = runtime.Snapshot;
        //如果动画被锁定runtime模拟停止
        if (motion.IsActive && motion.ActiveDefinition != null && motion.ActiveDefinition.InterruptedExitPolicy == PlayerMotionInterruptedExitPolicy.DirectToTargetPresentation)
        {
            runtime.Cancel();
            return;
        }
        if (TryResolveSourceExitMotion(transition, out definition))
        {
            Begin(definition, motorResult);
            return;
        }
        if (runtime.Snapshot.IsActive) runtime.Cancel();
    }
    /// <summary>
    /// 处理了左右转向的动画
    /// </summary>
    public void ResolveContinuousMotion(Type stateType, PlayerGameplayIntent intent, PlayerMotorResult motorResult)
    {
        if (runtime.Snapshot.IsActive || intent.DesiredMoveDirection.sqrMagnitude < 0.0001f) return;
        PlayerMotionId left;
        PlayerMotionId right;
        if (stateType == typeof(PlayerWalkState))
        {
            left = PlayerMotionId.WalkTurn180Left;
            right = PlayerMotionId.WalkTurn180Right;
        }
        else if (stateType == typeof(PlayerRunState))
        {
            left = PlayerMotionId.RunTurn180Left;
            right = PlayerMotionId.RunTurn180Right;
        }
        else if (stateType == typeof(PlayerFastRunState))
        {
            left = PlayerMotionId.FastRunTurn180Left;
            right = PlayerMotionId.FastRunTurn180Right;
        }
        else return;
        Vector3 reference = motorResult.HorizontalVelocity.sqrMagnitude > 0.0001f ? motorResult.HorizontalVelocity : transform.forward;
        float signedAngle = SignedPlanarAngle(reference, intent.DesiredMoveDirection);
        if (Mathf.Abs(signedAngle) < catalog.Turn180Threshold) return;
        if (catalog.TryGet(signedAngle < 0f ? left : right, out PlayerMotionDefinition definition)) Begin(definition, motorResult);
    }

    public PlayerMotionFrame Advance(float deltaTime)
    {
        return runtime.Advance(deltaTime);
    }
    /// <summary>
    /// 这里planner通过移动数据驱动phaseRuntime
    /// </summary>
    public void CommitLocomotionPhase(PlayerLocomotionMode locomotionMode, PlayerMotorResult motorResult)
    {
        phaseRuntime.Commit(locomotionMode, motorResult, runtime.Snapshot);
    }
    /// <summary>
    /// 先解析目标进入 Motion，再按当前 Motion 的中断策略处理源状态退出 Motion
    /// </summary>
    private bool TryResolveTargetTransitionMotion(PlayerStateTransition transition, PlayerGameplayIntent intent, out PlayerMotionDefinition definition)
    {
        Type previous = transition.PreviousStateType;
        Type current = transition.CurrentStateType;
        PlayerMotionId id;
        if (previous == typeof(PlayerIdleState) && current == typeof(PlayerWalkState)) id = ResolveStartId(PlayerMotionId.IdleToWalk, PlayerMotionId.WalkStart180Left, PlayerMotionId.WalkStart180Right, intent);
        else if (previous == typeof(PlayerIdleState) && current == typeof(PlayerRunState)) id = ResolveStartId(PlayerMotionId.IdleToRun, PlayerMotionId.RunStart180Left, PlayerMotionId.RunStart180Right, intent);
        else { definition = null; return false; }
        return catalog.TryGet(id, out definition);
    }

    /// <summary>
    /// 解析源状态的停止表现；DirectToTargetPresentation 会在调用方中跳过此分支
    /// </summary>
    private bool TryResolveSourceExitMotion(PlayerStateTransition transition, out PlayerMotionDefinition definition)
    {
        Type previous = transition.PreviousStateType;
        Type current = transition.CurrentStateType;
        PlayerMotionId id;
        if (previous == typeof(PlayerWalkState) && current == typeof(PlayerIdleState)) id = PlayerMotionId.WalkToIdle;
        else if (previous == typeof(PlayerRunState) && current == typeof(PlayerIdleState)) id = PlayerMotionId.RunToIdle;
        else if (previous == typeof(PlayerFastRunState) && current == typeof(PlayerIdleState)) id = PlayerMotionId.FastRunToIdle;
        else if (previous == typeof(PlayerDodgeState) && current == typeof(PlayerIdleState) && transition.Reason == PlayerStateTransitionReason.DodgeCompleted) id = PlayerMotionId.DodgeToIdle;
        else { definition = null; return false; }
        return catalog.TryGet(id, out definition);
    }
    /// <summary>
    /// 处理当前运动状态id
    /// </summary>
    private PlayerMotionId ResolveStartId(PlayerMotionId standard, PlayerMotionId left, PlayerMotionId right, PlayerGameplayIntent intent)
    {
        float signedAngle = SignedPlanarAngle(transform.forward, intent.DesiredMoveDirection);
        //输入角度不满转向条件就不转向
        if (Mathf.Abs(signedAngle) < catalog.Turn180Threshold) return standard;
        //满足条件判断左右
        PlayerMotionId turnId = signedAngle < 0f ? left : right;
        return catalog.TryGet(turnId, out _) ? turnId : standard;
    }

    private void Begin(PlayerMotionDefinition definition, PlayerMotorResult motorResult)
    {
        PlayerFoot entryFoot = definition.ResolveEntryFoot(PhaseSnapshot);
        PlayerMotionProfile selectedProfile = definition.ResolveProfile(entryFoot);
        runtime.Begin(definition, selectedProfile, entryFoot, ResolveEntrySource(definition, motorResult), transform.forward);
    }

    private PlayerMotionEntrySource ResolveEntrySource(PlayerMotionDefinition definition, PlayerMotorResult motorResult)
    {
        PlayerLocomotionPhaseSnapshot phase = PhaseSnapshot;
        if (definition == null || !definition.HasEntryHandoff || !phase.HasLoop || !PlayerLocomotionCycleDefinition.IsGroundLoopMode(phase.Mode)) return default;
        return new PlayerMotionEntrySource(phase.Mode, motorResult.HorizontalVelocity);
    }
    /// <summary>
    /// 角度计算
    /// </summary>
    private static float SignedPlanarAngle(Vector3 from, Vector3 to)
    {
        from.y = 0f;
        to.y = 0f;
        return from.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f ? 0f : Vector3.SignedAngle(from, to, Vector3.up);
    }
}
