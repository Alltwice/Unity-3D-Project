using UnityEngine;
/// <summary>
/// 移动模式
/// </summary>
public enum PlayerLocomotionMode
{
    Idle,
    Walk,
    Run,
    FastRun,
    Dodge,
    Air,
    HardLanding
}
/// <summary>
/// 实际速度还是烘焙路径
/// </summary>
public enum PlayerMotorTranslationMode
{
    VelocityDriven,
    DisplacementDriven,
    // 直接采用指定速度，仅在命令的有效时间内积分
    ImmediateVelocityDriven
}
/// <summary>
/// 旋转模式
/// </summary>
public enum PlayerMotorRotationMode
{
    None,
    //平滑
    FaceDirection,
    //直接
    YawDelta
}

/// <summary>
/// 玩家输入意图
/// </summary>
public struct PlayerGameplayIntent
{
    public PlayerLocomotionMode LocomotionMode;
    public Vector3 DesiredMoveDirection;
    public Vector3 DesiredFacingDirection;
    public float VerticalImpulse;
    public bool HasVerticalImpulse;
    public bool HasPlanarVelocityOverride;
    public Vector3 PlanarVelocityOverride;
    public float PlanarVelocityDuration;
    /// <summary>
    /// 建立输入意图
    /// </summary>
    public static PlayerGameplayIntent Create(Vector3 desiredMoveDirection, Vector3 currentFacing)
    {
        desiredMoveDirection.y = 0f;
        currentFacing.y = 0f;
        if (desiredMoveDirection.sqrMagnitude > 1f) desiredMoveDirection.Normalize();
        return new PlayerGameplayIntent
        {
            //返回的是默认安全值
            LocomotionMode = PlayerLocomotionMode.Idle,
            DesiredMoveDirection = desiredMoveDirection,
            DesiredFacingDirection = desiredMoveDirection.sqrMagnitude > 0.0001f ? desiredMoveDirection.normalized : currentFacing.normalized
        };
    }
    /// <summary>
    /// 覆盖水平速度，duration 为本帧有效移动时间
    /// </summary>
    public void RequestPlanarVelocity(Vector3 velocity, float duration)
    {
        HasPlanarVelocityOverride = true;
        PlanarVelocityOverride = velocity;
        PlanarVelocityDuration = duration;
    }

    /// <summary>
    /// 请求一次垂直冲量
    /// </summary>
    public void RequestVerticalImpulse(float impulse)
    {
        VerticalImpulse = impulse;
        HasVerticalImpulse = true;
    }
}
/// <summary>
/// 玩家最终执行命令快照
/// </summary>
public struct PlayerMotorCommand
{
    public PlayerMotorCommand(PlayerMotorTranslationMode translationMode, Vector3 targetPlanarVelocity, float planarAcceleration, Vector3 planarDisplacement, PlayerMotorRotationMode rotationMode, Vector3 desiredFacingDirection, float yawDelta, bool hasVerticalImpulse, float verticalImpulse, float planarVelocityDuration = 0f)
    {
        TranslationMode = translationMode;
        TargetPlanarVelocity = targetPlanarVelocity;
        PlanarAcceleration = planarAcceleration;
        //直接驱动位移
        PlanarDisplacement = planarDisplacement;
        RotationMode = rotationMode;
        DesiredFacingDirection = desiredFacingDirection;
        YawDelta = yawDelta;
        HasVerticalImpulse = hasVerticalImpulse;
        VerticalImpulse = verticalImpulse;
        PlanarVelocityDuration = planarVelocityDuration;
    }

    public PlayerMotorTranslationMode TranslationMode { get; }
    public Vector3 TargetPlanarVelocity { get; }
    public float PlanarAcceleration { get; }
    public float PlanarVelocityDuration { get; }
    public Vector3 PlanarDisplacement { get; }
    public PlayerMotorRotationMode RotationMode { get; }
    public Vector3 DesiredFacingDirection { get; }
    public float YawDelta { get; }
    public bool HasVerticalImpulse { get; }
    public float VerticalImpulse { get; }
}
/// <summary>
/// 实际移动结果
/// </summary>
public readonly struct PlayerMotorResult
{
    public PlayerMotorResult(Vector3 actualDisplacement, Vector3 actualPlanarDisplacement, Vector3 horizontalVelocity, float verticalVelocity, bool isGrounded, bool justLanded, CollisionFlags collisionFlags)
    {
        //实际移动
        ActualDisplacement = actualDisplacement;
        //无水平分量
        ActualPlanarDisplacement = actualPlanarDisplacement;
        //实际运动速度
        HorizontalVelocity = horizontalVelocity;
        VerticalVelocity = verticalVelocity;
        IsGrounded = isGrounded;
        JustLanded = justLanded;
        //碰撞结果
        CollisionFlags = collisionFlags;
    }

    public Vector3 ActualDisplacement { get; }
    public Vector3 ActualPlanarDisplacement { get; }
    public Vector3 HorizontalVelocity { get; }
    public float HorizontalSpeed => HorizontalVelocity.magnitude;
    public float VerticalVelocity { get; }
    public bool IsGrounded { get; }
    public bool JustLanded { get; }
    public CollisionFlags CollisionFlags { get; }
}

/// <summary>
/// 提供 Motor 平移速度与旋转的共享运动学计算
/// </summary>
public static class PlayerMotorKinematics
{
    public static Quaternion CalculateSmoothRotation(Quaternion currentRotation, Vector3 targetFacing, float rotationSmoothSpeed, float deltaTime)
    {
        targetFacing.y = 0f;
        Quaternion targetRotation = Quaternion.LookRotation(targetFacing.normalized, Vector3.up);
        float t = 1f - Mathf.Exp(-rotationSmoothSpeed * Mathf.Max(0f, deltaTime));
        return Quaternion.Slerp(currentRotation, targetRotation, t);
    }

    public static Vector3 CalculateActualPlanarVelocity(Vector3 actualDisplacement, float deltaTime)
    {
        actualDisplacement.y = 0f;
        return deltaTime > 0f ? actualDisplacement / deltaTime : Vector3.zero;
    }
}
