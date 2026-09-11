using System;
using UnityEngine;

/// <summary>
/// 将 Gameplay transition/intent 解析成目标节点，统一运行时执行交接，不接触动画或 CharacterController
/// </summary>
public class PlayerMotionPlanner : MonoBehaviour
{
    [SerializeField] private PlayerMotionCatalog catalog;

    private PlayerHandoffRuntime runtime;
    private PlayerFacingMode facingMode = PlayerFacingMode.MovementAligned;
    public PlayerHandoffSnapshot HandoffSnapshot => runtime.Snapshot;
    private PlayerLocomotionPhaseRuntime phaseRuntime;

    public PlayerMotionCatalog Catalog => catalog;
    public PlayerMotionSnapshot Snapshot => runtime.MotionSnapshot;
    public PlayerLocomotionPhaseSnapshot PhaseSnapshot
    {
        get
        {
            PlayerHandoffSnapshot handoff = runtime.Snapshot;
            if (handoff.Target.Phase.HasLoop) return handoff.Target.Phase;
            if (handoff.IsActive && handoff.Source.Phase.HasLoop) return handoff.Source.Phase;
            return phaseRuntime.Snapshot;
        }
    }

    private void Awake()
    {
        phaseRuntime = new PlayerLocomotionPhaseRuntime(catalog);
        runtime = new PlayerHandoffRuntime(catalog);
    }

    public void BeginFrame() => runtime.BeginFrame();

    /// <summary>
    /// 在写入本帧 Motion facts 前清理进入 Independent 后不再使用的地面 Motion
    /// </summary>
    public void SynchronizeFacingMode(PlayerFacingMode nextFacingMode, PlayerLocomotionMode currentLocomotionMode, PlayerMotorResult motorResult)
    {
        if (facingMode == nextFacingMode) return;
        bool enteringIndependent = nextFacingMode == PlayerFacingMode.Independent;
        facingMode = nextFacingMode;
        if (!enteringIndependent || !IsGroundState(currentLocomotionMode)) return;

        PlayerHandoffSnapshot handoff = runtime.Snapshot;
        if (!handoff.HasTarget || (!handoff.Source.Key.IsMotion && !handoff.Target.Key.IsMotion)) return;
        PlayerFoot foot = PhaseSnapshot.LastPlantFoot;
        runtime.SetImmediate(PlayerMotionNodeKey.ForLoop(currentLocomotionMode), foot, transform.forward, transform.forward, motorResult.HorizontalVelocity);
    }

    public void HandleStateTransition(PlayerStateTransition transition, PlayerGameplayIntent intent, PlayerMotorResult motorResult)
    {
        if (transition.PreviousStateType == typeof(PlayerAirState) || transition.PreviousStateType == typeof(PlayerHardLandingState))
        {
            runtime.Clear();
            return;
        }
        if (transition.CurrentStateType == typeof(PlayerAirState) || transition.CurrentStateType == typeof(PlayerDodgeState) || transition.CurrentStateType == typeof(PlayerHardLandingState)) { runtime.Clear(); return; }
        if (facingMode == PlayerFacingMode.Independent && IsGroundState(intent.LocomotionMode))
        {
            RequestLoop(intent, motorResult);
            return;
        }
        if (TryResolveTargetTransitionMotion(transition, intent, out PlayerMotionDefinition definition))
        {
            Begin(definition, intent, motorResult);
            return;
        }
        PlayerMotionSnapshot motion = runtime.MotionSnapshot;
        //中断策略只选择目标表现，不绕过状态层的锁定裁决
        if (motion.IsActive && motion.ActiveDefinition != null && motion.ActiveDefinition.InterruptedExitPolicy == PlayerMotionInterruptedExitPolicy.DirectToTargetPresentation)
        {
            RequestLoop(intent, motorResult);
            return;
        }
        if (TryResolveSourceExitMotion(transition, out definition))
        {
            Begin(definition, intent, motorResult);
            return;
        }
        RequestLoop(intent, motorResult);
    }
    /// <summary>
    /// 处理普通模式的 180° 转向 Motion；Independent 使用速度移动，不进入方向差 Motion
    /// </summary>
    public void ResolveContinuousMotion(Type stateType, PlayerGameplayIntent intent, PlayerMotorResult motorResult)
    {
        if (facingMode == PlayerFacingMode.Independent) return;
        if (runtime.MotionSnapshot.IsActive || intent.DesiredMoveDirection.sqrMagnitude < 0.0001f) return;
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
        if (catalog.TryGet(signedAngle < 0f ? left : right, out PlayerMotionDefinition definition)) Begin(definition, intent, motorResult);
    }

    public System.Collections.Generic.IReadOnlyList<PlayerHandoffStep> Advance(float deltaTime, PlayerGameplayIntent intent)
    {
        return runtime.Advance(deltaTime, intent, transform.forward, PhaseSnapshot.LastPlantFoot);
    }
    /// <summary>
    /// 这里planner通过移动数据驱动phaseRuntime
    /// </summary>
    public void CommitLocomotionPhase(PlayerLocomotionMode locomotionMode, PlayerMotorResult motorResult)
    {
        PlayerHandoffSnapshot handoff = runtime.Snapshot;
        PlayerMotionSnapshot footSource = handoff.Target.Key.IsMotion ? runtime.MotionSnapshot : handoff.Source.Motion;
        phaseRuntime.Commit(locomotionMode, motorResult, footSource, handoff);
        runtime.CommitPhase(motorResult);
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
    /// <summary>
    /// 请求进入运行时
    /// </summary>
    private void Begin(PlayerMotionDefinition definition, PlayerGameplayIntent intent, PlayerMotorResult motorResult)
    {
        PlayerFoot foot = definition.ResolveEntryFoot(PhaseSnapshot);
        if (!runtime.Snapshot.HasTarget && catalog.GetId(definition) != PlayerMotionId.DodgeToIdle)
            runtime.SetImmediate(PlayerMotionNodeKey.ForLoop(PhaseSnapshot.HasLoop ? PhaseSnapshot.Mode : PlayerLocomotionMode.Idle), foot, transform.forward, intent.DesiredMoveDirection, motorResult.HorizontalVelocity);
        runtime.Request(PlayerMotionNodeKey.ForMotion(catalog.GetId(definition)), foot, transform.forward, intent.DesiredMoveDirection, motorResult.HorizontalVelocity);
    }

    private void RequestLoop(PlayerGameplayIntent intent, PlayerMotorResult motorResult)
    {
        if (intent.LocomotionMode > PlayerLocomotionMode.FastRun) { runtime.Clear(); return; }
        runtime.Request(PlayerMotionNodeKey.ForLoop(intent.LocomotionMode), PhaseSnapshot.LastPlantFoot, transform.forward, intent.DesiredMoveDirection, motorResult.HorizontalVelocity);
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

    private static bool IsGroundState(PlayerLocomotionMode mode)
    {
        return mode == PlayerLocomotionMode.Idle || PlayerLocomotionCycleDefinition.IsGroundLoopMode(mode);
    }
}
