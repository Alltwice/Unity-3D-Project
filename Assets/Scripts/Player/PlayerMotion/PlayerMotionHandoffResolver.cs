/// <summary>代码中的自然后继触发事实，不包含混合参数</summary>
public struct PlayerMotionHandoffTrigger
{
    public PlayerMotionNodeKey Target;
    public float Progress;
    public PlayerMotionHandoffTrigger(PlayerLocomotionMode mode, float progress)
    {
        Target = PlayerMotionNodeKey.ForLoop(mode);
        Progress = progress;
    }
}

/// <summary>集中维护自然后继与地面停止目标，手感参数由来源 Definition 提供</summary>
public static class PlayerMotionHandoffResolver
{
    public static bool TryGetSuccessor(PlayerMotionNodeKey source, out PlayerMotionHandoffTrigger trigger)
    {
        trigger = default;
        if (!source.IsMotion) return false;
        switch (source.Motion)
        {
            case PlayerMotionId.IdleToWalk:
            case PlayerMotionId.WalkStart180Left:
            case PlayerMotionId.WalkStart180Right:
            case PlayerMotionId.WalkTurn180Left:
            case PlayerMotionId.WalkTurn180Right:
                trigger = new PlayerMotionHandoffTrigger(PlayerLocomotionMode.Walk, 0.7f);
                return true;
            case PlayerMotionId.IdleToRun:
            case PlayerMotionId.RunStart180Left:
            case PlayerMotionId.RunStart180Right:
            case PlayerMotionId.RunTurn180Left:
            case PlayerMotionId.RunTurn180Right:
                trigger = new PlayerMotionHandoffTrigger(PlayerLocomotionMode.Run, 0.7f);
                return true;
            case PlayerMotionId.FastRunTurn180Left:
            case PlayerMotionId.FastRunTurn180Right:
                trigger = new PlayerMotionHandoffTrigger(PlayerLocomotionMode.FastRun, 0.7f);
                return true;
            case PlayerMotionId.WalkToIdle:
            case PlayerMotionId.RunToIdle:
            case PlayerMotionId.FastRunToIdle:
                trigger = new PlayerMotionHandoffTrigger(PlayerLocomotionMode.Idle, 0.7f);
                return true;
            case PlayerMotionId.DodgeToIdle:
                trigger = new PlayerMotionHandoffTrigger(PlayerLocomotionMode.Idle, 0.8f);
                return true;
            default: return false;
        }
    }

    public static bool TryGetStop(PlayerLocomotionMode mode, out PlayerMotionId motion)
    {
        switch (mode)
        {
            case PlayerLocomotionMode.Walk: motion = PlayerMotionId.WalkToIdle; return true;
            case PlayerLocomotionMode.Run: motion = PlayerMotionId.RunToIdle; return true;
            case PlayerLocomotionMode.FastRun: motion = PlayerMotionId.FastRunToIdle; return true;
            case PlayerLocomotionMode.Dodge: motion = PlayerMotionId.DodgeToIdle; return true;
            default: motion = default; return false;
        }
    }
}
