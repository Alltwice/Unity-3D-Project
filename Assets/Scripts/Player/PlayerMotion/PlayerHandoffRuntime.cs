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
    private PlayerHandoffDefinition relation;
    private ulong sequence;
    //交接时钟
    private float elapsed;
    private float duration;
    private float initialPose = 1f;
    private float initialTranslation = 1f;
    private readonly List<PlayerHandoffStep> steps = new List<PlayerHandoffStep>();
    public PlayerHandoffRuntime(PlayerMotionCatalog motionCatalog) { catalog = motionCatalog; }
    private float Progress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
    //记录源权重
    private float PoseWeight => source == null ? 0f : initialPose * (1f - relation.EvaluatePose(Progress));
    private float MoveWeight => source == null ? 0f : initialTranslation * (1f - relation.EvaluateTranslation(Progress));
    public PlayerHandoffSnapshot Snapshot => new PlayerHandoffSnapshot { HasTarget = target != null, IsActive = source != null, Source = source == null ? default : source.Snapshot, Target = target == null ? default : target.Snapshot, SourcePoseWeight = PoseWeight, SourceTranslationWeight = MoveWeight };
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
    public void Clear() { source = null; target = null; relation = null; steps.Clear(); }
    public void SetImmediate(PlayerMotionNodeKey key, PlayerFoot foot, Vector3 facing, Vector3 desired, Vector3 velocity)
    {
        Clear();
        target = CreateNode(key, foot, facing, desired, velocity);
    }
    /// <summary>
    /// 混合事件触发入口
    /// </summary>
    public void Request(PlayerMotionNodeKey key, PlayerFoot foot, Vector3 facing, Vector3 desired, Vector3 velocity)
    {
        if (target == null) { SetImmediate(key, foot, facing, desired, velocity); return; }
        if (target.Key.Equals(key)) return;
        //没有交接保留target，存在交接保留source
        Node retained = source ?? target;
        float pose = source == null ? 1f : PoseWeight;
        float move = source == null ? 1f : MoveWeight;
        if (retained.Key.Equals(key))
        {
            // 反向交接复用原来源，以旧目标作为淡出端
            PlayerHandoffDefinition reverse = catalog.GetHandoff(target.Key, key, PlayerHandoffTriggerMode.Request);
            source = target;
            CaptureSourceFacing(source, facing);
            target = retained;
            StartBlend(reverse, 1f - pose, 1f - move);
            return;
        }
        PlayerHandoffDefinition next = catalog.GetHandoff(retained.Key, key, PlayerHandoffTriggerMode.Request);
        if (source == null) retained.Velocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (source == null) CaptureSourceFacing(retained, facing);
        source = retained;
        target = CreateNode(key, foot, facing, desired, velocity);
        StartBlend(next, pose, move);
    }
    private void StartBlend(PlayerHandoffDefinition next, float pose, float move)
    {
        relation = next;
        initialPose = pose;
        initialTranslation = move;
        elapsed = 0f;
        duration = next.ResolveDuration(source.Duration, target.Duration);
        if (duration <= 0f) source = null;
    }
    private Node CreateNode(PlayerMotionNodeKey key, PlayerFoot foot, Vector3 facing, Vector3 desired, Vector3 velocity)
    {
        Node node = new Node { Key = key, Id = ++sequence, Velocity = velocity };
        if (!key.IsMotion)
        {
            node.Phase = new PlayerLocomotionPhaseRuntime(catalog);
            node.Phase.InitializeFoot(foot);
            return node;
        }
        if (!catalog.TryGet(key.Motion, out PlayerMotionDefinition definition)) throw new InvalidOperationException("Missing Motion: " + key);
        PlayerMotionProfile profile = definition.ResolveProfile(foot);
        Vector3 basis = definition.BasisPolicy == PlayerMotionBasisPolicy.DesiredDirection ? desired : definition.BasisPolicy == PlayerMotionBasisPolicy.EntryVelocityDirection && velocity.sqrMagnitude > 0.0001f ? velocity : facing;
        node.Duration = definition.GetDuration(profile);
        node.Motion = new PlayerMotionRuntime();
        node.Motion.Begin(definition, profile, foot, basis, desired);
        return node;
    }
    /// <summary>
    /// 混合工作入口
    /// </summary>
    public IReadOnlyList<PlayerHandoffStep> Advance(float deltaTime, PlayerGameplayIntent intent, Vector3 facing, PlayerFoot foot)
    {
        steps.Clear();
        float remaining = Mathf.Max(0f, deltaTime);
        while (target != null)
        {
            PlayerHandoffDefinition successor = target.Key.IsMotion && !target.SuccessorConsumed ? catalog.GetSuccessor(target.Key) : null;
            //检查距离事件触发点的时间
            float untilTrigger = successor == null ? float.PositiveInfinity : Mathf.Max(0f, successor.SourceTriggerProgress * target.Duration - target.Elapsed);
            if (untilTrigger <= 0f)
            {
                //避免同一个实例反复触发
                target.SuccessorConsumed = true;
                if (source != null && source.Key.Equals(successor.Target))
                {
                    Request(successor.Target, foot, facing, intent.DesiredMoveDirection, source.Velocity);
                    continue;
                }
                Node retained = source ?? target;
                float pose = source == null ? 1f : PoseWeight;
                float move = source == null ? 1f : MoveWeight;
                PlayerHandoffDefinition next = retained == target ? successor : catalog.GetHandoff(retained.Key, successor.Target, PlayerHandoffTriggerMode.Request);
                source = retained;
                if (retained == target) CaptureSourceFacing(source, facing);
                PlayerMotionSnapshot completedSource = target.Motion.Snapshot;
                PlayerFoot successorFoot = completedSource.ActiveProfile.ResolveLastPlantFoot(completedSource.Progress, completedSource.EntryLastPlantFoot);
                target = CreateNode(successor.Target, successorFoot, facing, intent.DesiredMoveDirection, retained.Velocity);
                StartBlend(next, pose, move);
                continue;
            }
            if (remaining <= 0f) break;
            float step = Mathf.Min(remaining, untilTrigger);
            if (source != null) step = Mathf.Min(step, duration - elapsed);
            float startWeight = MoveWeight;
            //两端采样，但只有目标接收输入
            PlayerMotionFrame a = AdvanceNode(source, step, default);
            if (source != null && a.IsValid && a.Definition.TranslationPolicy == PlayerMotionTranslationPolicy.SteeredLocalTrajectory)
                a = new PlayerMotionFrame(a.Definition, a.Profile, a.EntryLastPlantFoot, source.SourceCorrection * a.AuthoredPlanarDisplacement, a.AuthoredYawDelta, a.RemainingAuthoredYaw, a.PreviousProgress, a.CurrentProgress, a.AuthoredFacingBeforeStep);
            PlayerMotionFrame b = AdvanceNode(target, step, intent);
            elapsed += step;
            steps.Add(new PlayerHandoffStep { DeltaTime = step, Source = a, Target = b, HasSource = source != null, SourceIsMotion = source != null && source.Key.IsMotion, TargetIsMotion = target.Key.IsMotion, SourceVelocity = source == null ? Vector3.zero : source.Velocity, SourceWeight = (startWeight + MoveWeight) * 0.5f });
            remaining = Mathf.Max(0f, remaining - step);
            if (source != null && elapsed >= duration) source = null;
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
