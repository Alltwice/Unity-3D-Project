using System;
using UnityEngine;

/// <summary>
/// 以 Motor 的实际平面位移推进循环相位，并保留 Boundary Motion 的最后落地脚
/// </summary>
public class PlayerLocomotionPhaseRuntime
{
    private readonly PlayerMotionCatalog catalog;
    private PlayerLocomotionMode mode;
    private PlayerMotionProfile profile;
    //当前在使用什么脚的脚步资源，相当于标明资源语义
    private PlayerFoot variantFoot;
    private PlayerFoot lastPlantFoot;
    private float normalizedPhase;
    private ulong loopMotionInstanceId;
    private bool hasLoop;

    public PlayerLocomotionPhaseRuntime(PlayerMotionCatalog motionCatalog)
    {
        catalog = motionCatalog;
    }

    public PlayerLocomotionPhaseSnapshot Snapshot => BuildSnapshot();
    internal void InitializeFoot(PlayerFoot foot) { lastPlantFoot = foot; }

    /// <summary>
    /// 在一帧的最后，拿到motor的移动数据后反推动画相位来到了多少，然后将数据交由动画管理器由其推进动画
    /// </summary>
    public void Commit(PlayerLocomotionMode locomotionMode, PlayerMotorResult motorResult, PlayerMotionSnapshot motion, PlayerHandoffSnapshot handoff = default)
    {
        UpdateLastPlantFootFromBoundary(motion);
        if (handoff.HasTarget)
        {
            PlayerHandoffNodeSnapshot loop = !handoff.Target.Key.IsMotion ? handoff.Target : handoff.IsActive && !handoff.Source.Key.IsMotion ? handoff.Source : default;
            if (loop.InstanceId == 0 || !motorResult.IsGrounded || !PlayerLocomotionCycleDefinition.IsGroundLoopMode(loop.Key.Locomotion)) { PauseForBoundary(locomotionMode); return; }
            if (!hasLoop || mode != loop.Key.Locomotion) ActivateCycle(loop.Key.Locomotion);
            else AdvanceLoop(motorResult);
            return;
        }
        //是不是loop动画
        if (!PlayerLocomotionCycleDefinition.IsGroundLoopMode(locomotionMode) || !motorResult.IsGrounded)
        {
            CloseCycle(locomotionMode);
            return;
        }
        if (motion.IsActive)
        {
            PauseForBoundary(locomotionMode);
            return;
        }
        bool modeChanged = !hasLoop || mode != locomotionMode;
        if (modeChanged)
        {
            ActivateCycle(locomotionMode);
            loopMotionInstanceId = motion.ActiveDefinition != null ? motion.InstanceId : 0;
            return;
        }
        AdvanceLoop(motorResult);
    }

    private void ActivateCycle(PlayerLocomotionMode locomotionMode)
    {
        if (catalog == null || !catalog.TryGetCycle(locomotionMode, out PlayerLocomotionCycleDefinition definition)) throw new InvalidOperationException("PlayerMotionCatalog 缺少 " + locomotionMode + " 的 Locomotion Cycle。");
        if (!definition.TryResolveProfile(lastPlantFoot, out PlayerMotionProfile selectedProfile, out PlayerFoot selectedVariantFoot)) throw new InvalidOperationException(locomotionMode + " Locomotion Cycle 缺少 " + selectedVariantFoot + " Loop Profile。");
        profile = selectedProfile;
        variantFoot = selectedVariantFoot;
        mode = locomotionMode;
        normalizedPhase = 0f;
        hasLoop = true;
        ResolveCurrentPlantFeet(out PlayerFoot resolvedLastFoot, out _, out _);
        lastPlantFoot = resolvedLastFoot;
    }
    /// <summary>
    /// 处理最后落地脚
    /// </summary>
    private void UpdateLastPlantFootFromBoundary(PlayerMotionSnapshot motion)
    {
        if (motion.ActiveProfile == null) return;
        PlayerFoot fallback = motion.EntryLastPlantFoot == PlayerFoot.Unknown ? lastPlantFoot : motion.EntryLastPlantFoot;
        lastPlantFoot = motion.ActiveProfile.ResolveLastPlantFoot(motion.Progress, fallback);
    }

    private void PauseForBoundary(PlayerLocomotionMode locomotionMode)
    {
        mode = locomotionMode;
        profile = null;
        variantFoot = PlayerFoot.Unknown;
        normalizedPhase = 0f;
        loopMotionInstanceId = 0;
        hasLoop = false;
    }
    /// <summary>
    /// 结束地面循环
    /// </summary>
    private void CloseCycle(PlayerLocomotionMode locomotionMode)
    {
        mode = locomotionMode;
        profile = null;
        variantFoot = PlayerFoot.Unknown;
        normalizedPhase = 0f;
        loopMotionInstanceId = 0;
        hasLoop = false;
    }

    private void AdvanceLoop(PlayerMotorResult motorResult)
    {
        //开始计算当前在这个循环周期内的百分比
        normalizedPhase = Mathf.Repeat(normalizedPhase + motorResult.ActualPlanarDisplacement.magnitude / profile.CycleDistance, 1f);
        ResolveCurrentPlantFeet(out PlayerFoot resolvedLastFoot, out _, out _);
        lastPlantFoot = resolvedLastFoot;
    }

    private PlayerLocomotionPhaseSnapshot BuildSnapshot()
    {
        if (!hasLoop) return new PlayerLocomotionPhaseSnapshot(false, false, mode, PlayerFoot.Unknown, 0f, lastPlantFoot, PlayerFoot.Unknown, 0f);
        ResolveCurrentPlantFeet(out PlayerFoot resolvedLastFoot, out PlayerFoot nextFoot, out float stepProgress);
        return new PlayerLocomotionPhaseSnapshot(true, true, mode, variantFoot, normalizedPhase, resolvedLastFoot, nextFoot, stepProgress);
    }

    private void ResolveCurrentPlantFeet(out PlayerFoot resolvedLastFoot, out PlayerFoot nextFoot, out float stepProgress)
    {
        if (!profile.TryEvaluateLoopPhase(normalizedPhase, out resolvedLastFoot, out nextFoot, out stepProgress)) throw new InvalidOperationException(profile.name + " 的 Loop Phase 配置无效。");
    }
}
