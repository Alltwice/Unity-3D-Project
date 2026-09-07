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
    private AnimancerState exitHandoffLoopState;
    private AnimancerState entrySourceLoopState;
    private AnimancerState stableLoopState;
    private AnimancerState hardLandingState;
    private AnimancerState dodgeState;
    private AnimancerState dodgeToIdleFadeSourceState;
    private AnimancerState dodgeToIdleFadeState;
    private PlayerMotionAnimationBinding activeBinding;
    private float entryPoseEndProgress;
    private bool entrySourceUsesLocomotionPhase;
    private ulong presentedMotionInstanceId;
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

    public void Present(Type currentGameplayStateType, PlayerStateTransition? transition, PlayerMotionSnapshot motion, PlayerLocomotionPhaseSnapshot locomotionPhase, float stateProgress, PlayerLandingPresentationKey? landingPresentation)
    {
        gameplayStateType = currentGameplayStateType;
        bool newMotion = motion.ActiveDefinition != null && motion.InstanceId != presentedMotionInstanceId;
        bool motionCancelled = motion.JustCancelled && motion.InstanceId == presentedMotionInstanceId;
        if (newMotion)
        {
            if (transition.HasValue && IsDodgeToIdleEntry(transition.Value)) PlayDodgeToIdleMotion(motion, locomotionPhase);
            else PlayMotion(motion, locomotionPhase);
        }
        else if (motionCancelled)
        {
            ClearDodgeToIdleFade();
            ClearBoundary();
        }
        if (transition.HasValue && !newMotion) PlayStateTransition(transition.Value, locomotionPhase, landingPresentation);
        else if (!newMotion && !motionCancelled && motion.ActiveDefinition != null && motion.InstanceId == presentedMotionInstanceId) UpdateBoundaryMotion(motion, locomotionPhase);
        else if (!newMotion && !transition.HasValue && motionCancelled) PlayStableLoop(gameplayStateType, locomotionPhase);
        ApplyLoopPhase(locomotionPhase);
        if (gameplayStateType == typeof(PlayerDodgeState) && dodgeState != null)
        {
            dodgeState.Speed = 0f;
            dodgeState.NormalizedTime = stateProgress;
        }
        if (gameplayStateType == typeof(PlayerHardLandingState) && hardLandingState != null)
        {
            hardLandingState.Speed = 0f;
            hardLandingState.NormalizedTime = stateProgress;
        }
    }

    public void EvaluateGraph(float deltaTime)
    {
        animancer.Evaluate(Mathf.Max(0f, deltaTime));
    }

    /// <summary>
    /// 边界 Motion 始终由 MotionSnapshot.Progress 手动采样
    /// </summary>
    private void PlayMotion(PlayerMotionSnapshot motion, PlayerLocomotionPhaseSnapshot locomotionPhase)
    {
        ClearDodgeToIdleFade();
        ++presentationSequence;
        presentedMotionInstanceId = motion.InstanceId;
        if (animationSet == null || !animationSet.TryGetBinding(motion.ActiveDefinition, motion.ActiveProfile, out PlayerMotionAnimationBinding binding, out ClipTransition transition))
        {
            ClearBoundary();
            PlayStableLoop(gameplayStateType, locomotionPhase);
            return;
        }
        bool useMotionEntry = motion.HasEntrySource && motion.EntryHandoffActive;
        AnimancerState sourceLoop = stableLoopState;
        float motionDuration = motion.ActiveDefinition.GetDuration(motion.ActiveProfile);
        float fallbackEntryEndProgress = motionDuration > 0f ? Mathf.Clamp01(transition.FadeDuration / motionDuration) : 0f;
        if (!useMotionEntry && fallbackEntryEndProgress <= 0f) sourceLoop = null;
        ClearEntrySourceLoop(sourceLoop);
        StopUnownedActiveStates(sourceLoop);
        entrySourceLoopState = sourceLoop;
        entrySourceUsesLocomotionPhase = useMotionEntry;
        entryPoseEndProgress = useMotionEntry ? motion.ActiveDefinition.EntryHandoffEndProgress : fallbackEntryEndProgress;
        boundaryState = null;
        exitHandoffLoopState = null;
        stableLoopState = null;
        activeBinding = binding;
        hardLandingState = null;
        boundaryState = PlayManual(transition);
        boundaryState.Speed = 0f;
        boundaryState.IsPlaying = false;
        boundaryState.NormalizedTime = motion.Progress;
        DebugBoundaryPhase = motion.Progress;
        if (motion.ExitHandoffActive || motion.JustCompleted) EnsureExitHandoffLoop(locomotionPhase);
        ApplyMotionPoseWeights(motion);
    }

    private void PlayDodgeToIdleMotion(PlayerMotionSnapshot motion, PlayerLocomotionPhaseSnapshot locomotionPhase)
    {
        ClearDodgeToIdleFade();
        ++presentationSequence;
        presentedMotionInstanceId = motion.InstanceId;
        if (animationSet == null || !animationSet.TryGetBinding(motion.ActiveDefinition, motion.ActiveProfile, out PlayerMotionAnimationBinding binding, out ClipTransition transition) || dodgeState == null)
        {
            PlayMotion(motion, locomotionPhase);
            return;
        }
        AnimancerState sourceState = dodgeState;
        ClearBoundary();
        StopUnownedActiveStates(sourceState);
        entrySourceLoopState = null;
        entrySourceUsesLocomotionPhase = false;
        entryPoseEndProgress = 0f;
        exitHandoffLoopState = null;
        stableLoopState = null;
        activeBinding = binding;
        hardLandingState = null;
        float motionDuration = motion.ActiveDefinition.GetDuration(motion.ActiveProfile);
        float timeUntilExitHandoff = motionDuration > 0f ? Mathf.Max(0f, (motion.ActiveDefinition.ExitHandoffStartProgress - motion.Progress) * motionDuration) : 0f;
        float fadeDuration = Mathf.Min(Mathf.Max(0f, transition.FadeDuration), timeUntilExitHandoff);
        AnimancerState endState = animancer.Play(transition, fadeDuration, FadeMode.FixedDuration);
        endState.Speed = 0f;
        endState.IsPlaying = false;
        endState.NormalizedTime = motion.Progress;
        boundaryState = endState;
        dodgeToIdleFadeSourceState = sourceState;
        dodgeToIdleFadeState = endState;
        DebugBoundaryPhase = motion.Progress;
        if (fadeDuration <= 0f)
        {
            CompleteDodgeToIdleFade();
            if (motion.ExitHandoffActive || motion.JustCompleted) EnsureExitHandoffLoop(locomotionPhase);
            ApplyMotionPoseWeights(motion);
            if (motion.JustCompleted) ClearBoundary(false);
        }
    }

    private void UpdateBoundaryMotion(PlayerMotionSnapshot motion, PlayerLocomotionPhaseSnapshot locomotionPhase)
    {
        if (boundaryState == null || activeBinding == null) return;
        boundaryState.Speed = 0f;
        boundaryState.IsPlaying = false;
        boundaryState.NormalizedTime = motion.Progress;
        DebugBoundaryPhase = motion.Progress;
        if (dodgeToIdleFadeSourceState != null && (!dodgeToIdleFadeSourceState.IsActive || motion.ExitHandoffActive || motion.JustCompleted)) CompleteDodgeToIdleFade();
        if (!IsEntryPoseActive(motion)) ClearEntrySourceLoop();
        if (motion.ExitHandoffActive || motion.JustCompleted)
        {
            EnsureExitHandoffLoop(locomotionPhase);
        }
        ApplyMotionPoseWeights(motion);
        if (motion.JustCancelled && !motion.IsActive)
        {
            ClearBoundary();
            PlayStableLoop(gameplayStateType, locomotionPhase);
        }
        else if (motion.JustCompleted)
        {
            ClearBoundary(false);
        }
    }
    /// <summary>
    /// 计算动画混合权重
    /// </summary>
    /// <param name="motion"></param>
    private void ApplyMotionPoseWeights(PlayerMotionSnapshot motion)
    {
        if (boundaryState == null || activeBinding == null || dodgeToIdleFadeSourceState != null) return;
        float entryTargetWeight = entrySourceLoopState == null ? 1f : activeBinding.EvaluateEntryPoseWeight(ResolveEntryPoseProgress(motion));
        float exitTargetWeight = exitHandoffLoopState != null && (motion.ExitHandoffActive || motion.JustCompleted) ? motion.JustCompleted ? 1f : activeBinding.EvaluateExitPoseWeight(motion.ExitHandoffProgress) : 0f;
        float sourceWeight = 1f - entryTargetWeight;
        float boundaryWeight = entryTargetWeight * (1f - exitTargetWeight);
        float targetLoopWeight = entryTargetWeight * exitTargetWeight;
        ResetMotionStateWeights();
        AddStateWeight(entrySourceLoopState, sourceWeight);
        AddStateWeight(boundaryState, boundaryWeight);
        AddStateWeight(exitHandoffLoopState, targetLoopWeight);
    }

    private void EnsureExitHandoffLoop(PlayerLocomotionPhaseSnapshot locomotionPhase)
    {
        if (exitHandoffLoopState != null) return;
        if (!TryResolveLoop(gameplayStateType, locomotionPhase, out PlayerAnimationSelection selection, out bool manualSampling)) return;
        exitHandoffLoopState = PlayManual(selection.Transition);
        stableLoopState = exitHandoffLoopState;
        if (manualSampling) ApplyLoopSample(exitHandoffLoopState, locomotionPhase);
    }

    private AnimancerState PlayManual(ClipTransition transition)
    {
        AnimancerState state = animancer.Layers[0].GetOrCreateState(transition);
        transition.Apply(state);
        state.CancelFade();
        state.Play();
        return state;
    }

    private bool IsEntryPoseActive(PlayerMotionSnapshot motion)
    {
        if (entrySourceLoopState == null || !motion.IsActive) return false;
        if (entrySourceUsesLocomotionPhase) return motion.HasEntrySource && motion.EntryHandoffActive;
        return motion.Progress < entryPoseEndProgress;
    }

    private float ResolveEntryPoseProgress(PlayerMotionSnapshot motion)
    {
        if (entrySourceUsesLocomotionPhase) return motion.EntryHandoffProgress;
        return entryPoseEndProgress > 0f ? Mathf.Clamp01(motion.Progress / entryPoseEndProgress) : 1f;
    }

    private void ResetMotionStateWeights()
    {
        if (entrySourceLoopState != null) entrySourceLoopState.Weight = 0f;
        if (boundaryState != null && boundaryState != entrySourceLoopState) boundaryState.Weight = 0f;
        if (exitHandoffLoopState != null && exitHandoffLoopState != entrySourceLoopState && exitHandoffLoopState != boundaryState) exitHandoffLoopState.Weight = 0f;
    }

    private static void AddStateWeight(AnimancerState state, float weight)
    {
        if (state != null && weight > 0f) state.Weight += weight;
    }

    private void StopUnownedActiveStates(AnimancerState retainedState)
    {
        AnimancerLayer layer = animancer.Layers[0];
        if (retainedState != null) retainedState.CancelFade();
        for (int i = layer.ActiveStates.Count - 1; i >= 0; i--)
        {
            AnimancerState state = layer.ActiveStates[i];
            if (state != retainedState) state.Stop();
        }
    }

    private void PlayStateTransition(PlayerStateTransition transition, PlayerLocomotionPhaseSnapshot locomotionPhase, PlayerLandingPresentationKey? landingPresentation)
    {
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
        if ((transition.PreviousStateType == typeof(PlayerWalkState) && transition.CurrentStateType == typeof(PlayerRunState)) || (transition.PreviousStateType == typeof(PlayerRunState) && transition.CurrentStateType == typeof(PlayerWalkState)))
        {
            PlayStableLoopWithFade(transition.CurrentStateType, locomotionPhase);
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
            if (IsLandingMotion(landingPresentation.Value))
            {
                PlayStableLoop(transition.CurrentStateType, locomotionPhase);
                return;
            }
            if (animationSet != null && animationSet.TryResolveLandingPresentation(landingPresentation.Value, out ClipTransition landing))
            {
                PlayPresentationEdge(landing, transition.CurrentStateType, locomotionPhase, presentationSequence);
                return;
            }
            PlayStableLoop(transition.CurrentStateType, locomotionPhase);
            return;
        }
        PlayStableLoop(transition.CurrentStateType, locomotionPhase);
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

    private void PlayStableLoopWithFade(Type stateType, PlayerLocomotionPhaseSnapshot locomotionPhase)
    {
        if (!TryResolveLoop(stateType, locomotionPhase, out PlayerAnimationSelection selection, out bool manualSampling)) return;
        stableLoopState = animancer.Play(selection.Transition, selection.Transition.FadeDuration, FadeMode.FixedDuration);
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
        if (exitHandoffLoopState != null && exitHandoffLoopState != stableLoopState) ApplyLoopSample(exitHandoffLoopState, locomotionPhase);
        if (entrySourceUsesLocomotionPhase && entrySourceLoopState != null && entrySourceLoopState != stableLoopState) ApplyLoopSample(entrySourceLoopState, locomotionPhase);
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

    private static bool IsLandingMotion(PlayerLandingPresentationKey presentation)
    {
        return presentation == PlayerLandingPresentationKey.LandWalk || presentation == PlayerLandingPresentationKey.LandRun || presentation == PlayerLandingPresentationKey.LandRoll;
    }

    private void ClearBoundary(bool clearLoop = true)
    {
        boundaryState = null;
        activeBinding = null;
        DebugBoundaryPhase = 0f;
        ClearEntrySourceLoop();
        if (clearLoop)
        {
            exitHandoffLoopState = null;
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

    private void ClearEntrySourceLoop(AnimancerState retainedState = null)
    {
        entryPoseEndProgress = 0f;
        entrySourceUsesLocomotionPhase = false;
        if (entrySourceLoopState == null) return;
        AnimancerState sourceLoop = entrySourceLoopState;
        entrySourceLoopState = null;
        if (sourceLoop == retainedState || sourceLoop == boundaryState || sourceLoop == exitHandoffLoopState || sourceLoop == stableLoopState) return;
        sourceLoop.Stop();
    }
}
