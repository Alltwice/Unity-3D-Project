using System;
using Animancer;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// 将 Gameplay、Motion 和 Simulation 相位事实表现为 Pose；不生产运动或脚步相位
/// </summary>
public sealed class PlayerAnimationController : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private AnimancerComponent animancer;
    [SerializeField] private PlayerAnimationSet animationSet;

    private AnimancerState boundaryState;
    private AnimancerState stableLoopState;
    private AnimancerState hardLandingState;
    private AnimancerState landingState;
    private AnimancerState dodgeState;
    private AnimancerState dodgeToIdleFadeSourceState;
    private AnimancerState dodgeToIdleFadeState;
    private ulong presentationSequence;
    private Type gameplayStateType;

    public float DebugBoundaryPhase { get; private set; }

    private void Awake()
    {
        if (animancer == null) animancer = GetComponent<AnimancerComponent>();
    }

    public void InitializeManualEvaluation()
    {
        animancer.Graph.UpdateMode = DirectorUpdateMode.Manual;
    }

    private readonly System.Collections.Generic.Dictionary<ulong, AnimancerState> groundStates = new System.Collections.Generic.Dictionary<ulong, AnimancerState>();
    private readonly System.Collections.Generic.List<ulong> releasedStates = new System.Collections.Generic.List<ulong>();

    public void Present(Type currentGameplayStateType, PlayerStateTransition? transition, PlayerMotionSnapshot motion, PlayerLocomotionPhaseSnapshot locomotionPhase, float stateProgress, PlayerLandingPresentationKey? landingPresentation, PlayerHandoffSnapshot handoff)
    {
        gameplayStateType = currentGameplayStateType;
        if (handoff.HasTarget)
        {
            landingState = null;
            PresentGround(handoff, locomotionPhase, transition);
            return;
        }
        ClearGroundStates();
        if (transition.HasValue) PlayStateTransition(transition.Value, locomotionPhase, landingPresentation);
        if (landingState != null && landingState.NormalizedTime >= landingState.NormalizedEndTime)
        {
            landingState = null;
            PlayStableLoop(gameplayStateType, locomotionPhase);
        }
        ApplyLoopPhase(locomotionPhase);
        if (gameplayStateType == typeof(PlayerDodgeState) && dodgeState != null) { dodgeState.Speed = 0f; dodgeState.NormalizedTime = stateProgress; }
        if (gameplayStateType == typeof(PlayerHardLandingState) && hardLandingState != null) { hardLandingState.Speed = 0f; hardLandingState.NormalizedTime = stateProgress; }
    }

    public void EvaluateGraph(float deltaTime) => animancer.Evaluate(Mathf.Max(0f, deltaTime));

    private void PresentGround(PlayerHandoffSnapshot handoff, PlayerLocomotionPhaseSnapshot phase, PlayerStateTransition? transition)
    {
        if (dodgeToIdleFadeState != null && (handoff.IsActive || !handoff.Target.Key.IsMotion || handoff.Target.Key.Motion != PlayerMotionId.DodgeToIdle)) CompleteDodgeToIdleFade();
        releasedStates.Clear();
        foreach (var pair in groundStates)
            if (pair.Key != handoff.Target.InstanceId && (!handoff.IsActive || pair.Key != handoff.Source.InstanceId)) releasedStates.Add(pair.Key);
        foreach (ulong id in releasedStates) { groundStates[id].Destroy(); groundStates.Remove(id); }
        AnimancerState target = ResolveGroundState(handoff.Target, phase);
        AnimancerState source = handoff.IsActive ? ResolveGroundState(handoff.Source, phase) : null;
        boundaryState = handoff.Target.Key.IsMotion ? target : null;
        DebugBoundaryPhase = handoff.Target.Motion.Progress;
        if (transition.HasValue && IsDodgeToIdleEntry(transition.Value) && dodgeState != null)
        {
            animationSet.TryGetBinding(handoff.Target.Motion.ActiveDefinition, handoff.Target.Motion.ActiveProfile, out _, out ClipTransition clip);
            dodgeToIdleFadeSourceState = dodgeState;
            dodgeToIdleFadeState = target;
            animancer.Play(target, clip.FadeDuration, FadeMode.FixedDuration);
        }
        if (dodgeToIdleFadeState != null)
        {
            if (handoff.IsActive || !dodgeToIdleFadeSourceState.IsActive) CompleteDodgeToIdleFade();
            else return;
        }
        AnimancerLayer layer = animancer.Layers[0];
        for (int i = layer.ActiveStates.Count - 1; i >= 0; i--)
        {
            AnimancerState state = layer.ActiveStates[i];
            state.CancelFade();
            if (state != source && state != target) state.Stop();
        }
        if (source != null) source.Weight = handoff.SourcePoseWeight;
        target.Weight = 1f - handoff.SourcePoseWeight;
    }

    private AnimancerState ResolveGroundState(PlayerHandoffNodeSnapshot node, PlayerLocomotionPhaseSnapshot phase)
    {
        if (node.Phase.HasLoop) phase = node.Phase;
        if (!groundStates.TryGetValue(node.InstanceId, out AnimancerState state))
        {
            ClipTransition clip;
            if (node.Key.IsMotion)
            {
                if (!animationSet.TryGetBinding(node.Motion.ActiveDefinition, node.Motion.ActiveProfile, out _, out clip)) throw new InvalidOperationException("Missing Motion animation: " + node.Key);
            }
            else
            {
                PlayerFoot foot = phase.HasLoop && phase.Mode == node.Key.Locomotion ? phase.VariantFoot : PlayerFoot.Unknown;
                if (!animationSet.TryResolveLoop(node.Key.Locomotion, foot, out PlayerAnimationSelection selection)) throw new InvalidOperationException("Missing Loop animation: " + node.Key);
                clip = selection.Transition;
            }
            state = animancer.Layers[0].CreateState(new object(), clip.Clip);
            clip.Apply(state);
            state.Play();
            groundStates.Add(node.InstanceId, state);
        }
        state.Speed = 0f;
        state.IsPlaying = false;
        if (node.Key.IsMotion) state.NormalizedTime = node.Motion.Progress;
        else if (phase.HasLoop && phase.Mode == node.Key.Locomotion) state.NormalizedTime = phase.NormalizedTime;
        else state.Time = node.ElapsedTime;
        return state;
    }

    private void ClearGroundStates()
    {
        ClearDodgeToIdleFade();
        foreach (AnimancerState state in groundStates.Values) state.Destroy();
        groundStates.Clear();
    }

    private void PlayStateTransition(PlayerStateTransition transition, PlayerLocomotionPhaseSnapshot locomotionPhase, PlayerLandingPresentationKey? landingPresentation)
    {
        landingState = null;
        ++presentationSequence;
        ClearDodgeToIdleFade();
        ClearBoundary();
        dodgeState = null;
        if (transition.CurrentStateType == typeof(PlayerDodgeState))
        {
            if (animationSet.TryResolveCue(PlayerAnimationCue.Dodge, out ClipTransition dodgeTransition))
            {
                dodgeState = animancer.Play(dodgeTransition);
                dodgeState.Speed = 0f;
                dodgeState.NormalizedTime = 0f;
            }
            return;
        }
        if (transition.CurrentStateType == typeof(PlayerHardLandingState))
        {
            hardLandingState = null;
            if (animationSet == null || !animationSet.TryResolveLandingPresentation(PlayerLandingPresentationKey.HardLand, out ClipTransition hardLandingTransition))
            {
                PlayStableLoop(typeof(PlayerHardLandingState), locomotionPhase);
                return;
            }
            hardLandingState = animancer.Play(hardLandingTransition);
            hardLandingState.Speed = 0f;
            hardLandingState.NormalizedTime = 0f;
            return;
        }
        if (transition.CurrentStateType == typeof(PlayerAirState))
        {
            if (transition.Reason == PlayerStateTransitionReason.Jumped && animationSet != null && animationSet.TryResolveCue(PlayerAnimationCue.JumpStart, out ClipTransition jumpStart)) PlayPresentationEdge(jumpStart, typeof(PlayerAirState), locomotionPhase, presentationSequence);
            else PlayStableLoop(typeof(PlayerAirState), locomotionPhase);
            return;
        }
        if (transition.PreviousStateType == typeof(PlayerAirState) && IsGroundState(transition.CurrentStateType) && landingPresentation.HasValue)
        {
            if (animationSet != null && animationSet.TryResolveLandingPresentation(landingPresentation.Value, out ClipTransition landing))
            {
                PlayLandingPresentation(landing);
                if (transition.CurrentStateType==typeof(PlayerIdleState))
                {
                    return;
                }
            }
            PlayStableLoop(transition.CurrentStateType, locomotionPhase);
            return;
        }
        PlayStableLoop(transition.CurrentStateType, locomotionPhase);
    }

    private void PlayLandingPresentation(ClipTransition landing)
    {
        landingState = animancer.Play(landing);
    }

    private void PlayPresentationEdge(ClipTransition edge, Type targetLoopStateType, PlayerLocomotionPhaseSnapshot locomotionPhase, ulong sequence)
    {
        if (edge == null || edge.Clip == null)
        {
            PlayStableLoop(targetLoopStateType, locomotionPhase);
            return;
        }
        AnimancerState state = animancer.Play(edge);
        state.Events(this).OnEnd = () =>
        {
            if (sequence == presentationSequence) PlayStableLoop(targetLoopStateType, locomotionPhase);
        };
    }

    private void PlayStableLoop(Type stateType, PlayerLocomotionPhaseSnapshot locomotionPhase)
    {
        if (!TryResolveLoop(stateType, locomotionPhase, out PlayerAnimationSelection selection, out bool manualSampling)) return;
        stableLoopState = animancer.Play(selection.Transition);
        if (manualSampling) ApplyLoopSample(stableLoopState, locomotionPhase);
    }

    private bool TryResolveLoop(Type stateType, PlayerLocomotionPhaseSnapshot locomotionPhase, out PlayerAnimationSelection selection, out bool manualSampling)
    {
        PlayerLocomotionMode stateMode = ResolveLocomotionMode(stateType);
        //是否是受Simulation控制的Loop
        manualSampling = locomotionPhase.HasLoop && PlayerLocomotionCycleDefinition.IsGroundLoopMode(stateMode) && locomotionPhase.Mode == stateMode;
        //决定使用哪个mode查询动画
        PlayerLocomotionMode resolveMode = manualSampling ? locomotionPhase.Mode : stateMode;
        //决定使用哪个脚步动画
        PlayerFoot resolveFoot = manualSampling ? locomotionPhase.VariantFoot : PlayerFoot.Unknown;
        if (animationSet != null && animationSet.TryResolveLoop(resolveMode, resolveFoot, out selection)) return true;
        selection = default;
        return false;
    }

    private void ApplyLoopPhase(PlayerLocomotionPhaseSnapshot locomotionPhase)
    {
        if (!locomotionPhase.HasLoop) return;
        if (stableLoopState != null) ApplyLoopSample(stableLoopState, locomotionPhase);
    }
    //从零状态开始播放，动画如何播放由外部数据提供，实际推进动画播放的位置
    private static void ApplyLoopSample(AnimancerState state, PlayerLocomotionPhaseSnapshot locomotionPhase)
    {
        state.Speed = 0f;
        state.IsPlaying = false;
        state.NormalizedTime = locomotionPhase.NormalizedTime;
    }
    /// <summary>
    /// 将状态转译为播放语义
    /// </summary>
    private static PlayerLocomotionMode ResolveLocomotionMode(Type stateType)
    {
        if (stateType == typeof(PlayerWalkState)) return PlayerLocomotionMode.Walk;
        if (stateType == typeof(PlayerRunState)) return PlayerLocomotionMode.Run;
        if (stateType == typeof(PlayerFastRunState)) return PlayerLocomotionMode.FastRun;
        if (stateType == typeof(PlayerAirState)) return PlayerLocomotionMode.Air;
        if (stateType == typeof(PlayerHardLandingState)) return PlayerLocomotionMode.HardLanding;
        if (stateType == typeof(PlayerDodgeState)) return PlayerLocomotionMode.Dodge;
        return PlayerLocomotionMode.Idle;
    }

    private static bool IsGroundState(Type stateType)
    {
        return stateType == typeof(PlayerIdleState) || stateType == typeof(PlayerWalkState) || stateType == typeof(PlayerRunState) || stateType == typeof(PlayerFastRunState);
    }

    private void ClearBoundary(bool clearLoop = true)
    {
        boundaryState = null;
        DebugBoundaryPhase = 0f;
        if (clearLoop)
        {
            stableLoopState = null;
        }
    }

    private static bool IsDodgeToIdleEntry(PlayerStateTransition transition)
    {
        return transition.PreviousStateType == typeof(PlayerDodgeState) && transition.CurrentStateType == typeof(PlayerIdleState) && transition.Reason == PlayerStateTransitionReason.DodgeCompleted;
    }

    private void CompleteDodgeToIdleFade()
    {
        AnimancerState sourceState = dodgeToIdleFadeSourceState;
        AnimancerState endState = dodgeToIdleFadeState;
        if (endState != null && endState.FadeGroup != null) endState.FadeGroup.Finish();
        dodgeToIdleFadeSourceState = null;
        dodgeToIdleFadeState = null;
        if (sourceState == dodgeState) dodgeState = null;
        if (sourceState != null && sourceState != boundaryState) sourceState.Stop();
    }

    private void ClearDodgeToIdleFade()
    {
        AnimancerState sourceState = dodgeToIdleFadeSourceState;
        AnimancerState endState = dodgeToIdleFadeState;
        dodgeToIdleFadeSourceState = null;
        dodgeToIdleFadeState = null;
        if (sourceState == dodgeState) dodgeState = null;
        if (endState != null) endState.Stop();
        if (sourceState != null && sourceState != endState) sourceState.Stop();
    }

}
