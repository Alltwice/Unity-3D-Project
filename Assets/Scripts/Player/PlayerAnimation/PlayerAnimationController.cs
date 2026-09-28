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
    private AnimancerState dodgeExitFadeSourceState;
    private AnimancerState dodgeExitFadeTargetState;
    private ulong dodgeExitFadeTargetInstanceId;
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

    private const float GroundStateSwitchDuration = 0.12f;
    private class GroundPresentation
    {
        public AnimancerState Current;
        public AnimancerState Previous;
        public bool IsDirectional;
        public float SwitchElapsed = GroundStateSwitchDuration;
    }

    private readonly System.Collections.Generic.Dictionary<ulong, GroundPresentation> groundStates = new System.Collections.Generic.Dictionary<ulong, GroundPresentation>();
    private readonly System.Collections.Generic.List<ulong> releasedStates = new System.Collections.Generic.List<ulong>();

    public void Present(Type currentGameplayStateType, PlayerStateTransition? transition, PlayerMotionSnapshot motion, PlayerLocomotionPhaseSnapshot locomotionPhase, float stateProgress, PlayerLandingPresentationKey? landingPresentation, PlayerHandoffSnapshot handoff, PlayerDirectionalMovementSnapshot directionalMovement, float deltaTime)
    {
        gameplayStateType = currentGameplayStateType;
        if (handoff.HasTarget)
        {
            landingState = null;
            PresentGround(handoff, locomotionPhase, transition, directionalMovement, deltaTime);
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

    private void PresentGround(PlayerHandoffSnapshot handoff, PlayerLocomotionPhaseSnapshot phase, PlayerStateTransition? transition, PlayerDirectionalMovementSnapshot direction, float deltaTime)
    {
        if (dodgeExitFadeTargetState != null && (handoff.IsActive || dodgeExitFadeTargetInstanceId != handoff.Target.InstanceId)) CompleteDodgeExitFade();
        releasedStates.Clear();
        foreach (var pair in groundStates)
            if (pair.Key != handoff.Target.InstanceId && (!handoff.IsActive || pair.Key != handoff.Source.InstanceId)) releasedStates.Add(pair.Key);
        foreach (ulong id in releasedStates) { DestroyGroundPresentation(groundStates[id]); groundStates.Remove(id); }
        GroundPresentation target = ResolveGroundState(handoff.Target, phase, direction, deltaTime, out ITransition targetTransition);
        GroundPresentation source = handoff.IsActive ? ResolveGroundState(handoff.Source, phase, direction, deltaTime, out _) : null;
        boundaryState = handoff.Target.Key.IsMotion ? target.Current : null;
        DebugBoundaryPhase = handoff.Target.Motion.Progress;
        if (transition.HasValue && IsDodgeExitEntry(transition.Value, handoff.Target) && dodgeState != null && dodgeExitFadeTargetState == null)
        {
            BeginDodgeExitFade(target.Current, handoff.Target.InstanceId, targetTransition.FadeDuration);
        }
        if (dodgeExitFadeTargetState != null)
        {
            if (handoff.IsActive || dodgeExitFadeTargetInstanceId != handoff.Target.InstanceId) CompleteDodgeExitFade();
            else if (dodgeExitFadeSourceState != null && dodgeExitFadeSourceState.IsActive) return;
            else CompleteDodgeExitFade();
        }
        AnimancerLayer layer = animancer.Layers[0];
        for (int i = layer.ActiveStates.Count - 1; i >= 0; i--)
        {
            AnimancerState state = layer.ActiveStates[i];
            state.CancelFade();
            if (!ContainsGroundState(source, state) && !ContainsGroundState(target, state)) state.Stop();
        }
        if (source != null) ApplyGroundWeight(source, handoff.SourcePoseWeight);
        ApplyGroundWeight(target, 1f - handoff.SourcePoseWeight);
    }

    private GroundPresentation ResolveGroundState(PlayerHandoffNodeSnapshot node, PlayerLocomotionPhaseSnapshot phase, PlayerDirectionalMovementSnapshot direction, float deltaTime, out ITransition transition)
    {
        if (node.Phase.HasLoop) phase = node.Phase;
        if (!TryResolveGroundTransition(node, phase, direction, out transition, out bool directional)) throw new InvalidOperationException("Missing ground animation: " + node.Key);
        if (!groundStates.TryGetValue(node.InstanceId, out GroundPresentation presentation))
        {
            presentation = new GroundPresentation { Current = CreateGroundState(transition), IsDirectional = directional };
            groundStates.Add(node.InstanceId, presentation);
        }
        else if (presentation.IsDirectional != directional)
        {
            if (dodgeExitFadeTargetState == presentation.Current) CompleteDodgeExitFade();
            presentation.Previous?.Destroy();
            presentation.Previous = presentation.Current;
            presentation.Current = CreateGroundState(transition);
            presentation.IsDirectional = directional;
            presentation.SwitchElapsed = 0f;
        }
        if (directional) ((DirectionalMixerState)presentation.Current).Parameter = direction.LocalDirection;
        float normalizedTime = node.Key.IsMotion ? node.Motion.Progress : phase.HasLoop && phase.Mode == node.Key.Locomotion ? phase.NormalizedTime : 0f;
        SampleGroundState(presentation.Current, node, phase, normalizedTime);
        if (presentation.Previous != null)
        {
            SampleGroundState(presentation.Previous, node, phase, normalizedTime);
            presentation.SwitchElapsed += Mathf.Max(0f, deltaTime);
            if (presentation.SwitchElapsed >= GroundStateSwitchDuration)
            {
                presentation.Previous.Destroy();
                presentation.Previous = null;
            }
        }
        return presentation;
    }

    private AnimancerState CreateGroundState(ITransition transition)
    {
        AnimancerState state = transition.CreateState();
        state.Key = new object();
        state.SetParent(animancer.Layers[0]);
        transition.Apply(state);
        state.Play();
        return state;
    }

    private static void SampleGroundState(AnimancerState state, PlayerHandoffNodeSnapshot node, PlayerLocomotionPhaseSnapshot phase, float normalizedTime)
    {
        state.Speed = 0f;
        state.IsPlaying = false;
        if (node.Key.IsMotion || phase.HasLoop && phase.Mode == node.Key.Locomotion) state.NormalizedTime = normalizedTime;
        else state.Time = node.ElapsedTime;
    }

    private static bool ContainsGroundState(GroundPresentation presentation, AnimancerState state)
    {
        return presentation != null && (presentation.Current == state || presentation.Previous == state);
    }

    private static void ApplyGroundWeight(GroundPresentation presentation, float weight)
    {
        float progress = presentation.Previous == null ? 1f : Mathf.Clamp01(presentation.SwitchElapsed / GroundStateSwitchDuration);
        presentation.Current.Weight = weight * progress;
        if (presentation.Previous != null) presentation.Previous.Weight = weight * (1f - progress);
    }

    private static void DestroyGroundPresentation(GroundPresentation presentation)
    {
        presentation.Current.Destroy();
        presentation.Previous?.Destroy();
    }   
    ///<summary>用于处理地面八向移动动画解析</summary>
    private bool TryResolveGroundTransition(PlayerHandoffNodeSnapshot node, PlayerLocomotionPhaseSnapshot phase, PlayerDirectionalMovementSnapshot direction, out ITransition transition, out bool directional)
    {
        directional = false;
        if (node.Phase.HasLoop) phase = node.Phase;
        if (node.Key.IsMotion)
        {
            bool found = animationSet.TryGetBinding(node.Motion.ActiveDefinition, node.Motion.ActiveProfile, out _, out ClipTransition motionTransition);
            transition = motionTransition;
            return found;
        }
        if (direction.IsActive && direction.Mode == node.Key.Locomotion && animationSet.TryResolveDirectionalLoop(node.Key.Locomotion, out MixerTransition2D mixer))
        {
            transition = mixer;
            directional = true;
            return true;
        }
        PlayerFoot foot = phase.HasLoop && phase.Mode == node.Key.Locomotion ? phase.VariantFoot : PlayerFoot.Unknown;
        if (animationSet.TryResolveLoop(node.Key.Locomotion, foot, out PlayerAnimationSelection selection))
        {
            transition = selection.Transition;
            return true;
        }
        transition = null;
        return false;
    }

    private void ClearGroundStates()
    {
        ClearDodgeExitFade();
        foreach (GroundPresentation presentation in groundStates.Values) DestroyGroundPresentation(presentation);
        groundStates.Clear();
    }

    private void PlayStateTransition(PlayerStateTransition transition, PlayerLocomotionPhaseSnapshot locomotionPhase, PlayerLandingPresentationKey? landingPresentation)
    {
        landingState = null;
        ++presentationSequence;
        ClearDodgeExitFade();
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
        manualSampling = locomotionPhase.HasLoop && PlayerLocomotionDefinition.IsGroundLoopMode(stateMode) && locomotionPhase.Mode == stateMode;
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

    private static bool IsDodgeExitEntry(PlayerStateTransition transition, PlayerHandoffNodeSnapshot target)
    {
        if (transition.PreviousStateType != typeof(PlayerDodgeState) || transition.Reason != PlayerStateTransitionReason.DodgeCompleted) return false;
        if (transition.CurrentStateType == typeof(PlayerIdleState)) return target.Key.IsMotion && target.Key.Motion == PlayerMotionId.DodgeToIdle;
        return transition.CurrentStateType == typeof(PlayerFastRunState) && !target.Key.IsMotion && target.Key.Locomotion == PlayerLocomotionMode.FastRun;
    }

    private void BeginDodgeExitFade(AnimancerState target, ulong targetInstanceId, float fadeDuration)
    {
        AnimancerState source = dodgeState;
        dodgeExitFadeSourceState = source;
        dodgeExitFadeTargetState = target;
        dodgeExitFadeTargetInstanceId = targetInstanceId;
        // 完成转换先于本帧表现，补齐 Dodge 最后姿态再从零权重淡入地面姿态
        source.Speed = 0f;
        source.IsPlaying = false;
        source.NormalizedTime = 1f;
        target.CancelFade();
        target.Weight = 0f;
        if (fadeDuration <= 0f)
        {
            target.Weight = 1f;
            CompleteDodgeExitFade();
            return;
        }
        animancer.Play(target, fadeDuration, FadeMode.FixedDuration);
    }

    private void CompleteDodgeExitFade()
    {
        AnimancerState sourceState = dodgeExitFadeSourceState;
        AnimancerState endState = dodgeExitFadeTargetState;
        if (endState != null)
        {
            if (endState.FadeGroup != null) endState.FadeGroup.Finish();
            endState.Weight = 1f;
        }
        dodgeExitFadeSourceState = null;
        dodgeExitFadeTargetState = null;
        dodgeExitFadeTargetInstanceId = 0;
        if (sourceState == dodgeState) dodgeState = null;
        if (sourceState != null && sourceState != boundaryState) sourceState.Stop();
    }

    private void ClearDodgeExitFade()
    {
        AnimancerState sourceState = dodgeExitFadeSourceState;
        AnimancerState endState = dodgeExitFadeTargetState;
        dodgeExitFadeSourceState = null;
        dodgeExitFadeTargetState = null;
        dodgeExitFadeTargetInstanceId = 0;
        if (sourceState == dodgeState) dodgeState = null;
        if (endState != null)
        {
            endState.CancelFade();
            endState.Stop();
        }
        if (sourceState != null && sourceState != endState) sourceState.Stop();
    }

}
