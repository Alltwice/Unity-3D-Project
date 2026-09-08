/// <summary>
/// Simulation 提交给表现层和 Motion Planner 的循环步态事实
/// </summary>
public struct PlayerLocomotionPhaseSnapshot
{
    public PlayerLocomotionPhaseSnapshot(bool hasLoop, bool hasPhase, PlayerLocomotionMode mode, PlayerFoot variantFoot, float normalizedTime, PlayerFoot lastPlantFoot, PlayerFoot nextPlantFoot, float stepProgress)
    {
        HasLoop = hasLoop;
        HasPhase = hasPhase;
        Mode = mode;
        VariantFoot = variantFoot;
        NormalizedTime = normalizedTime;
        LastPlantFoot = lastPlantFoot;
        NextPlantFoot = nextPlantFoot;
        StepProgress = stepProgress;
    }

    public bool HasLoop { get; }
    public bool HasPhase { get; }
    public PlayerLocomotionMode Mode { get; }
    public PlayerFoot VariantFoot { get; }
    public float NormalizedTime { get; }
    public PlayerFoot LastPlantFoot { get; }
    public PlayerFoot NextPlantFoot { get; }
    public float StepProgress { get; }
}
