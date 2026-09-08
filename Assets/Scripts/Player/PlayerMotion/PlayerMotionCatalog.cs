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

[CreateAssetMenu(fileName = "PlayerMotionCatalog", menuName = "Player/Motion/Catalog")]
public class PlayerMotionCatalog : ScriptableObject
{
    [SerializeField] private List<PlayerMotionCatalogEntry> motions = new List<PlayerMotionCatalogEntry>();
    [SerializeField] private List<PlayerLocomotionCycleDefinition> locomotionCycles = new List<PlayerLocomotionCycleDefinition>();
    [Range(90f, 180f)] [SerializeField] private float turn180Threshold = 150f;

    public IReadOnlyList<PlayerMotionCatalogEntry> Motions => motions;
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

    public bool Validate(ICollection<string> errors)
    {
        bool valid = true;
        HashSet<PlayerMotionId> motionIds = new HashSet<PlayerMotionId>();
        for (int i = 0; i < motions.Count; i++)
        {
            PlayerMotionCatalogEntry entry = motions[i];
            if (!motionIds.Add(entry.Id)) { errors?.Add(name + ": MotionId " + entry.Id + " 重复。"); valid = false; }
            if (entry.Definition == null) { errors?.Add(name + ": MotionId " + entry.Id + " 缺少 Definition。"); valid = false; }
            else valid &= entry.Definition.Validate(errors);
        }
        HashSet<PlayerLocomotionMode> cycleModes = new HashSet<PlayerLocomotionMode>();
        for (int i = 0; i < locomotionCycles.Count; i++)
        {
            PlayerLocomotionCycleDefinition cycle = locomotionCycles[i];
            if (cycle == null) { errors?.Add(name + ": Locomotion Cycle " + i + " 缺失。"); valid = false; continue; }
            if (!cycleModes.Add(cycle.Mode)) { errors?.Add(name + ": Locomotion Cycle " + cycle.Mode + " 重复。"); valid = false; }
            valid &= cycle.Validate(errors);
        }
        PlayerLocomotionMode[] requiredModes = { PlayerLocomotionMode.Walk, PlayerLocomotionMode.Run, PlayerLocomotionMode.FastRun };
        for (int i = 0; i < requiredModes.Length; i++)
        {
            if (!cycleModes.Contains(requiredModes[i])) { errors?.Add(name + ": 缺少 " + requiredModes[i] + " Locomotion Cycle。"); valid = false; }
        }
        return valid;
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
#endif
}
