using System;
using UnityEngine;

public enum PlayerMotionNodeKind { Locomotion, Motion }

[Serializable]
public struct PlayerMotionNodeKey : IEquatable<PlayerMotionNodeKey>
{
    [SerializeField] private PlayerMotionNodeKind kind;
    [SerializeField] private PlayerMotionId motion;
    [SerializeField] private PlayerLocomotionMode locomotion;

    public PlayerMotionNodeKind Kind => kind;
    public PlayerMotionId Motion => motion;
    public PlayerLocomotionMode Locomotion => locomotion;
    public bool IsMotion => kind == PlayerMotionNodeKind.Motion;

    public static PlayerMotionNodeKey ForMotion(PlayerMotionId id) => new PlayerMotionNodeKey { kind = PlayerMotionNodeKind.Motion, motion = id };
    public static PlayerMotionNodeKey ForLoop(PlayerLocomotionMode mode) => new PlayerMotionNodeKey { kind = PlayerMotionNodeKind.Locomotion, locomotion = mode };
    public bool Equals(PlayerMotionNodeKey other) => kind == other.kind && (IsMotion ? motion == other.motion : locomotion == other.locomotion);
    public override bool Equals(object obj) => obj is PlayerMotionNodeKey other && Equals(other);
    public override int GetHashCode() => ((int)kind * 397) ^ (IsMotion ? (int)motion : (int)locomotion);
    public override string ToString() => IsMotion ? motion.ToString() : locomotion + "Loop";

    public bool IsValid()
    {
        return Enum.IsDefined(typeof(PlayerMotionNodeKind), kind) && (IsMotion ? Enum.IsDefined(typeof(PlayerMotionId), motion) : Enum.IsDefined(typeof(PlayerLocomotionMode), locomotion));
    }

    public static int Compare(PlayerMotionNodeKey left, PlayerMotionNodeKey right)
    {
        int kind = ((int)left.kind).CompareTo((int)right.kind);
        if (kind != 0) return kind;
        return left.IsMotion ? ((int)left.motion).CompareTo((int)right.motion) : ((int)left.locomotion).CompareTo((int)right.locomotion);
    }
}
