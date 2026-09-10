using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerMotionId
{
    IdleToWalk,
    WalkToIdle,
    IdleToRun,
    RunToIdle,
    FastRunToIdle,
    WalkStart180Left,
    WalkStart180Right,
    RunStart180Left,
    RunStart180Right,
    WalkTurn180Left,
    WalkTurn180Right,
    RunTurn180Left,
    RunTurn180Right,
    FastRunTurn180Left,
    FastRunTurn180Right,
    DodgeToIdle = 19
}
/// <summary>
/// 组织每一份动画数据
/// </summary>
[Serializable]
public struct PlayerMotionCatalogEntry
{
    [SerializeField] private PlayerMotionId id;
    [SerializeField] private PlayerMotionDefinition definition;

    public PlayerMotionCatalogEntry(PlayerMotionId motionId, PlayerMotionDefinition motionDefinition)
    {
        id = motionId;
        definition = motionDefinition;
    }

    public PlayerMotionId Id => id;
    public PlayerMotionDefinition Definition => definition;
}

[Serializable]
public struct PlayerLocomotionHandoffEntry
{
    [SerializeField] private PlayerLocomotionMode mode;
    [SerializeField] private PlayerHandoffEntrySettings entry;

    public PlayerLocomotionMode Mode => mode;
    public PlayerHandoffEntrySettings Entry => entry;

    public PlayerLocomotionHandoffEntry(PlayerLocomotionMode locomotionMode, PlayerHandoffEntrySettings entrySettings)
    {
        mode = locomotionMode;
        entry = entrySettings;
    }
}

[CreateAssetMenu(fileName = "PlayerMotionCatalog", menuName = "Player/Motion/Catalog")]
public class PlayerMotionCatalog : ScriptableObject
{
    [SerializeField] private List<PlayerMotionCatalogEntry> motions = new List<PlayerMotionCatalogEntry>();
    [SerializeField] private List<PlayerLocomotionHandoffEntry> loopHandoffEntries = new List<PlayerLocomotionHandoffEntry>();
    public PlayerMotionId GetId(PlayerMotionDefinition definition)
    {
        foreach (PlayerMotionCatalogEntry entry in motions) if (entry.Definition == definition) return entry.Id;
        throw new InvalidOperationException("Motion Definition 不在 Catalog 中。");
    }
    [SerializeField] private List<PlayerLocomotionCycleDefinition> locomotionCycles = new List<PlayerLocomotionCycleDefinition>();
    [Range(90f, 180f)] [SerializeField] private float turn180Threshold = 150f;

    public IReadOnlyList<PlayerMotionCatalogEntry> Motions => motions;
    public IReadOnlyList<PlayerLocomotionHandoffEntry> LoopHandoffEntries => loopHandoffEntries;
    public IReadOnlyList<PlayerLocomotionCycleDefinition> LocomotionCycles => locomotionCycles;
    public float Turn180Threshold => turn180Threshold;

    public bool TryGet(PlayerMotionId id, out PlayerMotionDefinition definition)
    {
        for (int i = 0; i < motions.Count; i++)
        {
            if (motions[i].Id != id) continue;
            definition = motions[i].Definition;
            return definition != null;
        }
        definition = null;
        return false;
    }
    /// <summary>
    /// 供外部拿到脚步相位数据
    /// </summary>
    public bool TryGetCycle(PlayerLocomotionMode locomotionMode, out PlayerLocomotionCycleDefinition definition)
    {
        for (int i = 0; i < locomotionCycles.Count; i++)
        {
            PlayerLocomotionCycleDefinition candidate = locomotionCycles[i];
            if (candidate == null || candidate.Mode != locomotionMode) continue;
            definition = candidate;
            return true;
        }
        definition = null;
        return false;
    }

    public PlayerHandoffResolution ResolveRequest(PlayerMotionNodeKey source, PlayerMotionNodeKey target)
    {
        if (!source.IsValid() || !target.IsValid() || source.Equals(target)) return PlayerHandoffResolution.Invalid(source, target, "Handoff 请求的来源或目标节点无效。");
        if (!ContainsNode(source)) return PlayerHandoffResolution.Invalid(source, target, "缺少 Handoff 请求来源节点: " + source);
        if (!TryResolveEntry(target, out PlayerHandoffEntrySettings entry)) return PlayerHandoffResolution.Invalid(source, target, "缺少目标节点进入配置: " + target);
        if (entry == null) return PlayerHandoffResolution.Invalid(source, target, "目标节点进入配置缺失: " + target);
        if (!entry.AllowRequest) return PlayerHandoffResolution.Invalid(source, target, "目标节点已禁用请求进入: " + target);
        IReadOnlyList<PlayerHandoffSourceOverride> overrides = entry.SourceOverrides;
        if (overrides == null) return PlayerHandoffResolution.Invalid(source, target, "目标节点来源覆盖列表缺失: " + target);
        for (int i = 0; i < overrides.Count; i++)
        {
            if (!overrides[i].Source.Equals(source)) continue;
            return new PlayerHandoffResolution
            {
                Source = source,
                Target = target,
                TriggerMode = PlayerHandoffTriggerMode.Request,
                SourceTriggerProgress = 0f,
                Blend = overrides[i].Blend,
                ConfigurationSource = PlayerHandoffConfigurationSource.TargetSourceOverride,
                IsValid = true
            };
        }
        return new PlayerHandoffResolution
        {
            Source = source,
            Target = target,
            TriggerMode = PlayerHandoffTriggerMode.Request,
            SourceTriggerProgress = 0f,
            Blend = entry.DefaultBlend,
            ConfigurationSource = PlayerHandoffConfigurationSource.TargetDefaultEntry,
            IsValid = true
        };
    }

    public bool TryGetSuccessor(PlayerMotionNodeKey source, out PlayerHandoffResolution resolution)
    {
        resolution = default;
        if (!source.IsMotion) return false;
        if (!TryGet(source.Motion, out PlayerMotionDefinition definition))
        {
            resolution = PlayerHandoffResolution.Invalid(source, default, "缺少默认后继来源 Motion: " + source);
            return true;
        }
        PlayerHandoffSuccessorSettings successor = definition.DefaultSuccessor;
        if (successor == null)
        {
            resolution = PlayerHandoffResolution.Invalid(source, default, "缺少默认后继配置: " + source);
            return true;
        }
        if (!successor.Enabled) return false;
        resolution = new PlayerHandoffResolution
        {
            Source = source,
            Target = successor.Target,
            TriggerMode = PlayerHandoffTriggerMode.SourceProgress,
            SourceTriggerProgress = successor.SourceTriggerProgress,
            Blend = successor.Blend,
            ConfigurationSource = PlayerHandoffConfigurationSource.SourceDefaultSuccessor,
            IsValid = true
        };
        return true;
    }

    public bool TryGetNodeDuration(PlayerMotionNodeKey node, PlayerFoot foot, out float duration)
    {
        if (node.IsMotion)
        {
            if (!TryGet(node.Motion, out PlayerMotionDefinition definition)) { duration = 0f; return false; }
            PlayerMotionProfile profile = definition.ResolveProfile(foot);
            duration = definition.GetDuration(profile);
            return profile != null && duration > 0f;
        }
        if (!TryGetCycle(node.Locomotion, out PlayerLocomotionCycleDefinition cycle) || !cycle.TryResolveProfile(foot, out PlayerMotionProfile loopProfile, out _)) { duration = 0f; return false; }
        duration = loopProfile.Duration;
        return duration > 0f;
    }

    private bool TryResolveEntry(PlayerMotionNodeKey target, out PlayerHandoffEntrySettings entry)
    {
        if (target.IsMotion)
        {
            if (TryGet(target.Motion, out PlayerMotionDefinition definition))
            {
                entry = definition.HandoffEntry;
                return true;
            }
            entry = null;
            return false;
        }
        for (int i = 0; i < loopHandoffEntries.Count; i++)
        {
            if (loopHandoffEntries[i].Mode != target.Locomotion) continue;
            entry = loopHandoffEntries[i].Entry;
            return true;
        }
        entry = null;
        return false;
    }

    public bool Validate(ICollection<string> errors)
    {
        bool valid = true;
        HashSet<PlayerMotionId> motionIds = new HashSet<PlayerMotionId>();
        for (int i = 0; i < motions.Count; i++)
        {
            PlayerMotionCatalogEntry entry = motions[i];
            if (!motionIds.Add(entry.Id)) { errors?.Add(name + ": MotionId " + entry.Id + " 重复。"); valid = false; }
            if (entry.Definition == null) { errors?.Add(name + ": MotionId " + entry.Id + " 缺少 Definition。"); valid = false; }
            else
            {
                valid &= entry.Definition.Validate(errors);
                valid &= entry.Definition.ValidateHandoff(PlayerMotionNodeKey.ForMotion(entry.Id), errors);
            }
        }
        HashSet<PlayerLocomotionMode> cycleModes = new HashSet<PlayerLocomotionMode>();
        for (int i = 0; i < locomotionCycles.Count; i++)
        {
            PlayerLocomotionCycleDefinition cycle = locomotionCycles[i];
            if (cycle == null) { errors?.Add(name + ": Locomotion Cycle " + i + " 缺失。"); valid = false; continue; }
            if (!cycleModes.Add(cycle.Mode)) { errors?.Add(name + ": Locomotion Cycle " + cycle.Mode + " 重复。"); valid = false; }
            valid &= cycle.Validate(errors);
        }
        HashSet<PlayerLocomotionMode> entryModes = new HashSet<PlayerLocomotionMode>();
        for (int i = 0; i < loopHandoffEntries.Count; i++)
        {
            PlayerLocomotionHandoffEntry entry = loopHandoffEntries[i];
            PlayerMotionNodeKey node = PlayerMotionNodeKey.ForLoop(entry.Mode);
            if (!entryModes.Add(entry.Mode)) { errors?.Add(name + ": Loop 进入配置 " + entry.Mode + " 重复。"); valid = false; }
            if (entry.Mode != PlayerLocomotionMode.Idle && !PlayerLocomotionCycleDefinition.IsGroundLoopMode(entry.Mode)) { errors?.Add(name + ": Loop 进入配置模式无效 " + entry.Mode + "。"); valid = false; }
            if (entry.Entry == null) { errors?.Add(name + ": 缺少 Loop 进入配置 " + entry.Mode + "。"); valid = false; continue; }
            valid &= entry.Entry.Validate(node, errors, name + "." + entry.Mode + ".Entry");
            valid &= ValidateEntrySources(entry.Entry, node, errors);
        }
        PlayerLocomotionMode[] requiredModes = { PlayerLocomotionMode.Idle, PlayerLocomotionMode.Walk, PlayerLocomotionMode.Run, PlayerLocomotionMode.FastRun };
        for (int i = 0; i < requiredModes.Length; i++)
        {
            if (!entryModes.Contains(requiredModes[i])) { errors?.Add(name + ": 缺少 " + requiredModes[i] + " Loop 进入配置。"); valid = false; }
        }
        PlayerLocomotionMode[] requiredCycleModes = { PlayerLocomotionMode.Walk, PlayerLocomotionMode.Run, PlayerLocomotionMode.FastRun };
        for (int i = 0; i < requiredCycleModes.Length; i++)
        {
            if (!cycleModes.Contains(requiredCycleModes[i])) { errors?.Add(name + ": 缺少 " + requiredCycleModes[i] + " Locomotion Cycle。"); valid = false; }
        }
        for (int i = 0; i < motions.Count; i++)
        {
            PlayerMotionCatalogEntry entry = motions[i];
            if (entry.Definition == null) continue;
            valid &= ValidateSuccessor(entry.Definition.DefaultSuccessor, PlayerMotionNodeKey.ForMotion(entry.Id), errors);
        }
        return valid;
    }

    private bool ValidateEntrySources(PlayerHandoffEntrySettings entry, PlayerMotionNodeKey target, ICollection<string> errors)
    {
        bool valid = true;
        IReadOnlyList<PlayerHandoffSourceOverride> overrides = entry.SourceOverrides;
        if (overrides == null) return false;
        for (int i = 0; i < overrides.Count; i++)
        {
            if (!ContainsNode(overrides[i].Source)) { errors?.Add(name + ": 来源覆盖节点缺失 " + overrides[i].Source + "。"); valid = false; }
        }
        return valid;
    }

    private bool ValidateSuccessor(PlayerHandoffSuccessorSettings successor, PlayerMotionNodeKey source, ICollection<string> errors)
    {
        if (successor == null || !successor.Enabled) return true;
        return ContainsNode(successor.Target) ? true : AddMissingNodeError(successor.Target, errors);
    }

    private bool AddMissingNodeError(PlayerMotionNodeKey node, ICollection<string> errors)
    {
        errors?.Add(name + ": 默认后继目标节点缺失 " + node + "。");
        return false;
    }

    private bool ContainsNode(PlayerMotionNodeKey node)
    {
        if (!node.IsValid()) return false;
        return node.IsMotion ? TryGet(node.Motion, out _) : node.Locomotion == PlayerLocomotionMode.Idle || TryGetCycle(node.Locomotion, out _);
    }

#if UNITY_EDITOR
    public void Configure(IEnumerable<PlayerMotionCatalogEntry> entries, float turnThreshold)
    {
        motions.Clear();
        motions.AddRange(entries);
        turn180Threshold = turnThreshold;
    }

    public void Configure(IEnumerable<PlayerMotionCatalogEntry> entries, IEnumerable<PlayerLocomotionCycleDefinition> cycles, float turnThreshold)
    {
        motions.Clear();
        motions.AddRange(entries);
        locomotionCycles.Clear();
        locomotionCycles.AddRange(cycles);
        turn180Threshold = turnThreshold;
    }

    public void ConfigureLoopHandoffEntries(IEnumerable<PlayerLocomotionHandoffEntry> entries)
    {
        loopHandoffEntries.Clear();
        if (entries != null) loopHandoffEntries.AddRange(entries);
    }
#endif
}
