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
    [SerializeField] private List<PlayerLocomotionDefinition> locomotions = new List<PlayerLocomotionDefinition>();
    [Range(90f, 180f)] [SerializeField] private float turn180Threshold = 150f;
    public IReadOnlyList<PlayerMotionCatalogEntry> Motions => motions;
    public IReadOnlyList<PlayerLocomotionDefinition> Locomotions => locomotions;
    public float Turn180Threshold => turn180Threshold;
    public PlayerMotionId GetId(PlayerMotionDefinition definition)
    {
        foreach (PlayerMotionCatalogEntry entry in motions) if (entry.Definition == definition) return entry.Id;
        throw new InvalidOperationException("Motion Definition 不在 Catalog 中。");
    }
    public bool TryGet(PlayerMotionId id, out PlayerMotionDefinition definition)
    {
        foreach (PlayerMotionCatalogEntry entry in motions)
        {
            if (entry.Id != id) continue;
            definition = entry.Definition;
            return definition != null;
        }
        definition = null;
        return false;
    }
    public bool TryGetLocomotion(PlayerLocomotionMode mode, out PlayerLocomotionDefinition definition)
    {
        foreach (PlayerLocomotionDefinition candidate in locomotions)
        {
            if (candidate == null || candidate.Mode != mode) continue;
            definition = candidate;
            return true;
        }
        definition = null;
        return false;
    }
    public bool Validate(ICollection<string> errors)
    {
        bool valid = true;
        HashSet<PlayerMotionId> ids = new HashSet<PlayerMotionId>();
        foreach (PlayerMotionCatalogEntry entry in motions)
        {
            if (!Enum.IsDefined(typeof(PlayerMotionId), entry.Id) || !ids.Add(entry.Id)) { errors?.Add(name + ": MotionId 无效或重复: " + entry.Id); valid = false; }
            if (entry.Definition == null) { errors?.Add(name + ": 缺少 Motion Definition: " + entry.Id); valid = false; }
            else valid &= entry.Definition.Validate(errors);
            if (PlayerMotionHandoffResolver.TryGetSuccessor(PlayerMotionNodeKey.ForMotion(entry.Id), out PlayerMotionHandoffTrigger trigger) && !TryGetLocomotion(trigger.Target.Locomotion, out _)) { errors?.Add(name + ": 缺少代码后继节点 " + trigger.Target); valid = false; }
        }
        HashSet<PlayerLocomotionMode> modes = new HashSet<PlayerLocomotionMode>();
        foreach (PlayerLocomotionDefinition definition in locomotions)
        {
            if (definition == null) { errors?.Add(name + ": 缺少 Locomotion Definition。"); valid = false; continue; }
            if (!modes.Add(definition.Mode)) { errors?.Add(name + ": Locomotion 重复: " + definition.Mode); valid = false; }
            valid &= definition.Validate(errors);
        }
        foreach (PlayerLocomotionMode mode in new[] { PlayerLocomotionMode.Idle, PlayerLocomotionMode.Walk, PlayerLocomotionMode.Run, PlayerLocomotionMode.FastRun })
            if (!modes.Contains(mode)) { errors?.Add(name + ": 缺少 Locomotion: " + mode); valid = false; }
        return valid;
    }
#if UNITY_EDITOR
    public void Configure(IEnumerable<PlayerMotionCatalogEntry> entries, float turnThreshold)
    {
        motions.Clear();
        motions.AddRange(entries);
        turn180Threshold = turnThreshold;
    }
    public void Configure(IEnumerable<PlayerMotionCatalogEntry> entries, IEnumerable<PlayerLocomotionDefinition> definitions, float turnThreshold)
    {
        Configure(entries, turnThreshold);
        locomotions.Clear();
        locomotions.AddRange(definitions);
    }
#endif
}
