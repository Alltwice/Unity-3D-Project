using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 注册并按输入转换、状态 Tick、结果转换三个阶段更新唯一的玩家 Gameplay 状态
/// </summary>
public class PlayerStateController : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig movementConfig;

    private readonly Dictionary<Type, PlayerStateBase> states = new Dictionary<Type, PlayerStateBase>();
    private PlayerJump playerJump;
    private PlayerDodge playerDodge;
    private PlayerContext context;
    private PlayerStateBase currentState;

    public PlayerStateBase CurrentState => currentState;
    public PlayerLocomotionMode CurrentLocomotionMode => currentState?.LocomotionMode ?? PlayerLocomotionMode.Idle;
    public PlayerLocomotionMode TargetGroundMode => context?.TargetGroundMode ?? PlayerLocomotionMode.Idle;
    public bool HasGroundMoveContinuationIntent => context?.HasGroundMoveContinuationIntent ?? false;
    public float CurrentPresentationProgress => currentState?.PresentationProgress ?? 0f;

    private void Awake()
    {
        playerJump = GetComponent<PlayerJump>();
        playerDodge = GetComponent<PlayerDodge>();
    }
    /// <summary>
    /// 状态机的初始化设定
    /// </summary>
    public PlayerStateTransition Initialize(IPlayerInputSource inputSource, IPlayerActionBuffer inputActionBuffer)
    {
        context = new PlayerContext(playerJump, playerDodge, inputSource, inputActionBuffer, movementConfig);
        RegisterStates();
        TryChangeState(new PlayerStateTransitionRequest(typeof(PlayerIdleState), PlayerStateTransitionReason.Initialized), out PlayerStateTransition transition);
        return transition;
    }
    /// <summary>
    /// 设置移动数据和事实
    /// </summary>
    public void SetSimulationFacts(PlayerMotorResult motorResult, PlayerMotionSnapshot motionSnapshot, PlayerLandingSnapshot landingSnapshot)
    {
        context.SetSimulationFacts(motorResult, motionSnapshot, landingSnapshot);
    }

    public void UpdateLocomotionIntent(float deltaTime)
    {
        context.UpdateLocomotionIntent(deltaTime);
    }

    public PlayerStateTransition? ProcessPreTickTransition()
    {
        return TryChangeState(currentState?.EvaluateInputTransition(), out PlayerStateTransition transition) ? transition : (PlayerStateTransition?)null;
    }

    public void Tick(float deltaTime, ref PlayerGameplayIntent intent)
    {
        currentState?.Tick(deltaTime, ref intent);
    }

    public PlayerStateTransition? ProcessPostTickTransition()
    {
        return TryChangeState(currentState?.EvaluateResultTransition(), out PlayerStateTransition transition) ? transition : (PlayerStateTransition?)null;
    }

    private void RegisterStates()
    {
        AddState(new PlayerIdleState(context));
        AddState(new PlayerWalkState(context));
        AddState(new PlayerRunState(context));
        AddState(new PlayerFastRunState(context));
        AddState(new PlayerDodgeState(context));
        AddState(new PlayerAirState(context));
        AddState(new PlayerHardLandingState(context));
    }

    private bool TryChangeState(PlayerStateTransitionRequest request, out PlayerStateTransition transition)
    {
        transition = default;
        if (request == null)
        {
            return false;
        }
        //锁定期只拒绝本次普通候选；下一帧仍由当前状态按最新事实重新评估
        if (context.MotionSnapshot.IsTransitionLocked && !IsForcedTransition(request.Reason))
        {
            return false;
        }
        if (!states.TryGetValue(request.TargetStateType, out PlayerStateBase nextState))
        {
            return false;
        }
        if (currentState == nextState && !request.AllowReentry)
        {
            return false;
        }

        transition = new PlayerStateTransition(currentState?.GetType(), nextState.GetType(), request.Reason);
        currentState?.Exit(transition);
        currentState = nextState;
        currentState.Enter(transition);
        return true;
    }

    private static bool IsForcedTransition(PlayerStateTransitionReason reason)
    {
        return reason == PlayerStateTransitionReason.Initialized || reason == PlayerStateTransitionReason.Fell || reason == PlayerStateTransitionReason.Landed || reason == PlayerStateTransitionReason.HardLanded;
    }

    private void AddState<TState>(TState state) where TState : PlayerStateBase
    {
        Type stateType = state.GetType();
        if (!states.ContainsKey(stateType))
        {
            states.Add(stateType, state);
        }
    }
}
