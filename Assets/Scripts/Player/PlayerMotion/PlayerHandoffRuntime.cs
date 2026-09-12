using System;
using System.Collections.Generic;
using UnityEngine;

public struct PlayerHandoffNodeSnapshot
{
    public PlayerMotionNodeKey Key;
    public ulong InstanceId;
    public PlayerMotionSnapshot Motion;
    public float ElapsedTime;
    public PlayerLocomotionPhaseSnapshot Phase;
}

public struct PlayerHandoffSnapshot
{
    public bool HasTarget;
    public bool IsActive;
    public PlayerHandoffNodeSnapshot Source;
    public PlayerHandoffNodeSnapshot Target;
    public float SourcePoseWeight;
    public float SourceTranslationWeight;
}
/// <summary>
/// 供Composer消费的一帧混合数据
/// </summary>
public struct PlayerHandoffStep
{
    public float DeltaTime;
    public PlayerMotionFrame Source;
    public PlayerMotionFrame Target;
    public bool HasSource;
    public bool SourceIsMotion;
    public bool TargetIsMotion;
    public Vector3 SourceVelocity;
    public float SourceWeight;
}

/// <summary>最多保留两个采样实例；只有目标可以触发后继或向 Gameplay 提供事实</summary>
public class PlayerHandoffRuntime
{
    //正在运行的节点
    private class Node
    {
        //具体动画语义
        public PlayerMotionNodeKey Key;
        public ulong Id;
        public PlayerMotionRuntime Motion;
        public PlayerHandoffSettings ExitHandoff;
        public float Duration;
        public float Elapsed;
        public Vector3 Velocity;
        public PlayerLocomotionPhaseRuntime Phase;
        public bool SuccessorConsumed;
        public Quaternion SourceCorrection = Quaternion.identity;
        public PlayerHandoffNodeSnapshot Snapshot
        {
            get
            {
                PlayerMotionSnapshot motion = Motion == null ? default : Motion.Snapshot;
                if (Motion != null) motion = new PlayerMotionSnapshot(motion.ActiveDefinition, motion.ActiveProfile, motion.EntryLastPlantFoot, Id, motion.Progress, motion.IsActive, motion.JustCompleted, motion.JustCancelled, motion.IsTransitionLocked);
                return new PlayerHandoffNodeSnapshot { Key = Key, InstanceId = Id, Motion = motion, ElapsedTime = Elapsed, Phase = Phase == null ? default : Phase.Snapshot };
            }
        }
    }
    private PlayerMotionCatalog catalog;
    private Node source;
    private Node target;
    private PlayerHandoffSettings settings;
    private ulong sequence;
    //交接时钟
    private float elapsed;
    private float duration;
    private float initialWeight = 1f;
    private readonly List<PlayerHandoffStep> steps = new List<PlayerHandoffStep>();
    public PlayerHandoffRuntime(PlayerMotionCatalog motionCatalog) { catalog = motionCatalog; }
    private float Progress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
    //记录源权重
    private float SourceWeight => source == null ? 0f : initialWeight * (1f - settings.Evaluate(Progress));
    public PlayerHandoffSnapshot Snapshot => new PlayerHandoffSnapshot { HasTarget = target != null, IsActive = source != null, Source = source == null ? default : source.Snapshot, Target = target == null ? default : target.Snapshot, SourcePoseWeight = SourceWeight, SourceTranslationWeight = SourceWeight };
    public PlayerMotionSnapshot MotionSnapshot => target == null ? default : target.Snapshot.Motion;
    public void CommitPhase(PlayerMotorResult result)
    {
        if (source?.Phase != null) source.Phase.Commit(source.Key.Locomotion, result, default);
        if (target?.Phase != null) target.Phase.Commit(target.Key.Locomotion, result, default);
    }
    public void BeginFrame()
    {
        source?.Motion?.BeginFrame(true);
        target?.Motion?.BeginFrame(true);
    }
    public void Clear() { source = null; target = null; settings = default; steps.Clear(); }
    public void SetImmediate(PlayerMotionNodeKey key, PlayerFoot foot, Vector3 facing, Vector3 desired, Vector3 velocity)
    {
        Clear();
        target = CreateNode(key, foot, facing, desired, velocity);
    }
    /// <summary>
    /// Gameplay 与自然后继共用的入口；中断时保留来源进度/权重，替换旧目标
    /// </summary>
    public void Request(PlayerMotionNodeKey key, PlayerFoot foot, Vector3 facing, Vector3 desired, Vector3 velocity)
    {
        if (target == null) { SetImmediate(key, foot, facing, desired, velocity); return; }
        if (target.Key.Equals(key)) return;
        //没有交接保留target，存在交接保留source
        Node retained = source ?? target;
        float weight = source == null ? 1f : SourceWeight;
        if (retained.Key.Equals(key))
        {
            // 反向交接复用原来源，以旧目标作为淡出端
            source = target;
            CaptureSourceFacing(source, facing);
            target = retained;
            StartBlend(1f - weight);
            return;
        }
        if (source == null) retained.Velocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (source == null) CaptureSourceFacing(retained, facing);
        source = retained;
        target = CreateNode(key, foot, facing, desired, velocity);
        StartBlend(weight);
    }
    private void StartBlend(float weight)
    {
        settings = source.ExitHandoff;
        initialWeight = weight;
        elapsed = 0f;
        duration = settings.Duration;
        if (duration <= 0f) source = null;
    }

    private Node CreateNode(PlayerMotionNodeKey key, PlayerFoot foot, Vector3 facing, Vector3 desired, Vector3 velocity)
    {
        Node node = new Node { Key = key, Id = ++sequence, Velocity = velocity };
        if (!key.IsMotion)
        {
            if (!catalog.TryGetLocomotion(key.Locomotion, out PlayerLocomotionDefinition locomotion)) throw new InvalidOperationException("Missing Locomotion: " + key);
            node.ExitHandoff = locomotion.ExitHandoff;
            node.Phase = new PlayerLocomotionPhaseRuntime(catalog);
            node.Phase.InitializeFoot(foot);
            return node;
        }
        if (!catalog.TryGet(key.Motion, out PlayerMotionDefinition definition)) throw new InvalidOperationException("Missing Motion: " + key);
        PlayerMotionProfile profile = definition.ResolveProfile(foot);
        Vector3 basis = definition.BasisPolicy == PlayerMotionBasisPolicy.DesiredDirection ? desired : definition.BasisPolicy == PlayerMotionBasisPolicy.EntryVelocityDirection && velocity.sqrMagnitude > 0.0001f ? velocity : facing;
        node.ExitHandoff = definition.ExitHandoff;
        node.Duration = definition.GetDuration(profile);
        node.Motion = new PlayerMotionRuntime();
        node.Motion.Begin(definition, profile, foot, basis, desired);
        return node;
    }
    /// <summary>
    /// 混合工作入口
    /// </summary>
    public IReadOnlyList<PlayerHandoffStep> Advance(float deltaTime, PlayerGameplayIntent intent, Vector3 facing)
    {
        steps.Clear();
        float remaining = Mathf.Max(0f, deltaTime);
        bool reachedTrigger = false;
        while (target != null)
        {
            PlayerMotionHandoffTrigger successor = default;
            bool hasSuccessor = target.Key.IsMotion && !target.SuccessorConsumed && PlayerMotionHandoffResolver.TryGetSuccessor(target.Key, out successor);
            //检查距离事件触发点的时间
            float untilTrigger = !hasSuccessor ? float.PositiveInfinity : Mathf.Max(0f, successor.Progress * target.Duration - target.Elapsed);
            if (reachedTrigger || untilTrigger <= 0f)
            {
                reachedTrigger = false;
                //避免同一个实例反复触发
                target.SuccessorConsumed = true;
                PlayerMotionSnapshot completedSource = target.Motion.Snapshot;
                PlayerFoot successorFoot = completedSource.ActiveProfile.ResolveLastPlantFoot(completedSource.Progress, completedSource.EntryLastPlantFoot);
                Request(successor.Target, successorFoot, facing, intent.DesiredMoveDirection, (source ?? target).Velocity);
                continue;
            }
            if (remaining <= 0f) break;
            float step = Mathf.Min(remaining, untilTrigger);
            float untilBlendEnd = source == null ? float.PositiveInfinity : Mathf.Max(0f, duration - elapsed);
            step = Mathf.Min(step, untilBlendEnd);
            //直接消费本段选中的边界，避免浮点累加不再推进时反复采样
            reachedTrigger = hasSuccessor && step >= untilTrigger;
            bool reachedBlendEnd = source != null && step >= untilBlendEnd;
            float startWeight = SourceWeight;
            //两端采样，但只有目标接收输入
            PlayerMotionFrame a = AdvanceNode(source, step, default);
            if (source != null && a.IsValid && a.Definition.TranslationPolicy == PlayerMotionTranslationPolicy.SteeredLocalTrajectory)
                a = new PlayerMotionFrame(a.Definition, a.Profile, a.EntryLastPlantFoot, source.SourceCorrection * a.AuthoredPlanarDisplacement, a.AuthoredYawDelta, a.RemainingAuthoredYaw, a.PreviousProgress, a.CurrentProgress, a.AuthoredFacingBeforeStep);
            PlayerMotionFrame b = AdvanceNode(target, step, intent);
            if (reachedTrigger) target.Elapsed = successor.Progress * target.Duration;
            elapsed = reachedBlendEnd ? duration : elapsed + step;
            steps.Add(new PlayerHandoffStep { DeltaTime = step, Source = a, Target = b, HasSource = source != null, SourceIsMotion = source != null && source.Key.IsMotion, TargetIsMotion = target.Key.IsMotion, SourceVelocity = source == null ? Vector3.zero : source.Velocity, SourceWeight = (startWeight + SourceWeight) * 0.5f });
            remaining = Mathf.Max(0f, remaining - step);
            if (reachedBlendEnd) source = null;
        }
        return steps;
    }
    private static PlayerMotionFrame AdvanceNode(Node node, float deltaTime, PlayerGameplayIntent intent)
    {
        if (node == null) return default;
        node.Elapsed = node.Key.IsMotion ? Mathf.Min(node.Duration, node.Elapsed + deltaTime) : node.Elapsed + deltaTime;
        return node.Motion == null ? default : node.Motion.Advance(deltaTime, intent);
    }
    private static void CaptureSourceFacing(Node node, Vector3 facing)
    {
        if (node.Motion != null) node.SourceCorrection = Quaternion.AngleAxis(Vector3.SignedAngle(node.Motion.AuthoredFacing, Vector3.ProjectOnPlane(facing, Vector3.up), Vector3.up), Vector3.up);
    }
}
