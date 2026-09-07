using UnityEngine;
/// <summary>
/// 整合运动数据和输入意图为最终的运动命令
/// </summary>
public static class PlayerMotionComposer
{
    public static PlayerMotorCommand Compose(PlayerGameplayIntent intent, PlayerMotionFrame motionFrame, PlayerMotorResult previousMotorResult, PlayerMovementConfig config, float deltaTime, Vector3 currentFacing)
    {
        if (intent.HasPlanarVelocityOverride)
        {
            PlayerMotorRotationMode rotation = intent.DesiredFacingDirection.sqrMagnitude > 0.0001f ? PlayerMotorRotationMode.FaceDirection : PlayerMotorRotationMode.None;
            return new PlayerMotorCommand(PlayerMotorTranslationMode.ImmediateVelocityDriven, intent.PlanarVelocityOverride, 0f, Vector3.zero, rotation, intent.DesiredFacingDirection, 0f, intent.HasVerticalImpulse, intent.VerticalImpulse, intent.PlanarVelocityDuration);
        }
        //目标速度
        Vector3 targetVelocity = intent.DesiredMoveDirection * ResolveSpeed(intent.LocomotionMode, config.Locomotion);
        //在加速度影响下每帧真实速度
        Vector3 predictedVelocity = CalculateVelocity(previousMotorResult.HorizontalVelocity, targetVelocity, intent.LocomotionMode, config.Locomotion, deltaTime);
        PlayerMotorTranslationMode translationMode = PlayerMotorTranslationMode.VelocityDriven;
        Vector3 displacement = Vector3.zero;
        //烘焙和程序混合态时的位移信息
        if (motionFrame.IsValid && motionFrame.Definition.TranslationPolicy != PlayerMotionTranslationPolicy.VelocityDriven)
        {
            float entryTargetWeight = motionFrame.EntryHandoffActive ? Mathf.Clamp01(motionFrame.EntryTargetTranslationWeight) : 1f;
            float exitSourceWeight = Mathf.Clamp01(motionFrame.ExitTranslationAuthority);
            float entrySourceWeight = 1f - entryTargetWeight;
            float authoredWeight = entryTargetWeight * exitSourceWeight;
            float targetLocomotionWeight = entryTargetWeight * (1f - exitSourceWeight);
            displacement = motionFrame.EntrySourcePlanarVelocity * deltaTime * entrySourceWeight
                + motionFrame.AuthoredPlanarDisplacement * authoredWeight
                + predictedVelocity * deltaTime * targetLocomotionWeight;
            if (entrySourceWeight > 0f || authoredWeight > 0f) translationMode = PlayerMotorTranslationMode.DisplacementDriven;
        }
        ResolveRotation(intent, motionFrame, currentFacing, out PlayerMotorRotationMode rotationMode, out Vector3 facingDirection, out float yawDelta);
        float acceleration = ResolveAcceleration(previousMotorResult.HorizontalVelocity, targetVelocity, intent.LocomotionMode, config.Locomotion);
        return new PlayerMotorCommand(translationMode, targetVelocity, acceleration, displacement, rotationMode, facingDirection, yawDelta, intent.HasVerticalImpulse, intent.VerticalImpulse);
    }
    /// <summary>
    /// 每帧真实需要移动的数据
    /// </summary>
    public static Vector3 CalculateVelocity(Vector3 currentVelocity, Vector3 targetVelocity, PlayerLocomotionMode locomotionMode, PlayerMovementConfig.LocomotionSettings settings, float deltaTime)
    {
        currentVelocity.y = 0f;
        targetVelocity.y = 0f;
        float acceleration = ResolveAcceleration(currentVelocity, targetVelocity, locomotionMode, settings);
        return Vector3.MoveTowards(currentVelocity, targetVelocity, acceleration * Mathf.Max(0f, deltaTime));
    }
    /// <summary>
    /// 处理加减速以及地面转向
    /// </summary>
    private static float ResolveAcceleration(Vector3 currentVelocity, Vector3 targetVelocity, PlayerLocomotionMode locomotionMode, PlayerMovementConfig.LocomotionSettings settings)
    {
        float acceleration = locomotionMode == PlayerLocomotionMode.Air ? settings.AirAcceleration : settings.GroundAcceleration;
        if (locomotionMode == PlayerLocomotionMode.Idle || locomotionMode == PlayerLocomotionMode.HardLanding) return settings.GroundDeceleration;
        if (currentVelocity.sqrMagnitude > 0.0001f && targetVelocity.sqrMagnitude > 0.0001f)
        {
            float alignment = Vector3.Dot(currentVelocity.normalized, targetVelocity.normalized);
            if (alignment < 0.8f) acceleration = settings.GroundTurnAcceleration;
            else if (currentVelocity.magnitude > targetVelocity.magnitude) acceleration = settings.GroundDeceleration;
        }
        return acceleration;
    }
    /// <summary>
    /// 依据枚举拿到对应的速度
    /// </summary>
    private static float ResolveSpeed(PlayerLocomotionMode locomotionMode, PlayerMovementConfig.LocomotionSettings settings)
    {
        switch (locomotionMode)
        {
            case PlayerLocomotionMode.Walk: return settings.WalkSpeed;
            case PlayerLocomotionMode.Run: return settings.RunSpeed;
            case PlayerLocomotionMode.FastRun: return settings.FastRunSpeed;
            case PlayerLocomotionMode.Air: return settings.AirMoveSpeed;
            case PlayerLocomotionMode.HardLanding: return 0f;
            default: return 0f;
        }
    }
    /// <summary>
    /// 处理旋转
    /// </summary>
    private static void ResolveRotation(PlayerGameplayIntent intent, PlayerMotionFrame frame, Vector3 currentFacing, out PlayerMotorRotationMode mode, out Vector3 facingDirection, out float yawDelta)
    {
        facingDirection = intent.DesiredFacingDirection;
        yawDelta = 0f;
        if (intent.LocomotionMode == PlayerLocomotionMode.HardLanding)
        {
            mode = PlayerMotorRotationMode.None;
            return;
        }
        if (!frame.IsValid)
        {
            mode = facingDirection.sqrMagnitude > 0.0001f ? PlayerMotorRotationMode.FaceDirection : PlayerMotorRotationMode.None;
            return;
        }
        PlayerMotionRotationPolicy policy = frame.Definition.RotationPolicy;
        if (policy == PlayerMotionRotationPolicy.ProfileYaw)
        {
            //当前帧计划朝向
            float authoredYaw = frame.AuthoredYawDelta;
            currentFacing.y = 0f;
            //获取本帧的理论朝向
            Vector3 facingAfterAuthored = Quaternion.AngleAxis(authoredYaw, Vector3.up) * (currentFacing.sqrMagnitude > 0.0001f ? currentFacing.normalized : Vector3.forward);
            yawDelta = authoredYaw;
            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude > 0.0001f)
            {
                //在动画旋转的基础上加上本帧旋转预测最终朝向
                Vector3 predictedFinalFacing = Quaternion.AngleAxis(frame.RemainingAuthoredYaw, Vector3.up) * facingAfterAuthored;
                //误差
                float finalFacingError = Vector3.SignedAngle(predictedFinalFacing, facingDirection.normalized, Vector3.up);
                //当前帧产生旋转
                float progressDelta = Mathf.Max(0f, frame.CurrentProgress - frame.PreviousProgress);
                //计算剩余过程
                float remainingWindow = Mathf.Max(0.0001f, 1f - frame.PreviousProgress);
                //一帧内修正剩余角度的1/x°
                yawDelta += finalFacingError * Mathf.Clamp01(progressDelta / remainingWindow);
            }
            mode = PlayerMotorRotationMode.YawDelta;
            return;
        }
        //非 ProfileYaw 时，KeepFacing 不旋转；其余策略在存在目标朝向时交给 Motor 平滑朝向该目标
        mode = policy == PlayerMotionRotationPolicy.KeepFacing ? PlayerMotorRotationMode.None : facingDirection.sqrMagnitude > 0.0001f ? PlayerMotorRotationMode.FaceDirection : PlayerMotorRotationMode.None;
    }
}
