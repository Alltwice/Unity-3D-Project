using System;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// 移动设置
/// </summary>
[CreateAssetMenu(fileName = "PlayerMovementConfig", menuName = "Player/Movement Config")]
public class PlayerMovementConfig : ScriptableObject
{
    [Serializable]
    public sealed class LocomotionSettings
    {
        [Min(0f)] [SerializeField] private float walkSpeed = 1.5f;
        [Min(0f)] [SerializeField] private float runSpeed = 4f;
        [Min(0f)] [SerializeField] private float fastRunSpeed = 7f;
        [Min(0f)] [SerializeField] private float airMoveSpeed = 3.5f;
        [Min(0f)] [SerializeField] private float groundAcceleration = 25f;
        [Min(0f)] [SerializeField] private float groundDeceleration = 25f;
        [Min(0f)] [SerializeField] private float groundTurnAcceleration = 80f;
        [Min(0f)] [SerializeField] private float airAcceleration = 10f;
        [Min(0f)] [SerializeField] private float rotationSmoothSpeed = 12f;
        [Min(0f)] [SerializeField] private float groundMoveInputReleaseGraceTime = 0.1f;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float FastRunSpeed => fastRunSpeed;
        public float AirMoveSpeed => airMoveSpeed;
        public float GroundAcceleration => groundAcceleration;
        public float GroundDeceleration => groundDeceleration;
        public float GroundTurnAcceleration => groundTurnAcceleration;
        public float AirAcceleration => airAcceleration;
        public float RotationSmoothSpeed => rotationSmoothSpeed;
        public float GroundMoveInputReleaseGraceTime => groundMoveInputReleaseGraceTime;
    }

    [Serializable]
    public sealed class MotorPhysicsSettings
    {
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float groundedVerticalVelocity = -2f;
        [SerializeField] private LayerMask groundMask = 1 << 3;
        [Min(0f)] [SerializeField] private float probeStartOffset = 0.2f;
        [Min(0f)] [SerializeField] private float probeDistance = 1f;
        [Min(0f)] [SerializeField] private float groundSnapDistance = 0.3f;
        [Range(0.1f, 1f)] [SerializeField] private float probeRadiusScale = 0.9f;
        [Min(0f)] [SerializeField] private float landingAnticipationTime = 0.12f;
        [Min(0f)] [SerializeField] private float minAnticipationDistance = 0.15f;
        [Min(0f)] [SerializeField] private float maxAnticipationDistance = 0.8f;

        public float Gravity => gravity;
        public float GroundedVerticalVelocity => groundedVerticalVelocity;
        public LayerMask GroundMask => groundMask;
        public float ProbeStartOffset => probeStartOffset;
        public float ProbeDistance => probeDistance;
        public float GroundSnapDistance => groundSnapDistance;
        public float ProbeRadiusScale => probeRadiusScale;
        public float LandingAnticipationTime => landingAnticipationTime;
        public float MinAnticipationDistance => minAnticipationDistance;
        public float MaxAnticipationDistance => maxAnticipationDistance;
    }

    [Serializable]
    public class LandingSettings
    {
        [Min(0f)] [SerializeField] private float lv2MinFallDistance = 1f;
        [Min(0f)] [SerializeField] private float lv3MinFallDistance = 2f;
        [Min(0f)] [SerializeField] private float lv4MinFallDistance = 3f;
        [Min(0.01f)] [SerializeField] private float hardLandingDuration = 1.8667f;
        [Range(0f, 1f)] [SerializeField] private float hardLandingInterruptProgress = 0.6f;

        public float Lv2MinFallDistance => lv2MinFallDistance;
        public float Lv3MinFallDistance => lv3MinFallDistance;
        public float Lv4MinFallDistance => lv4MinFallDistance;
        public float HardLandingDuration => hardLandingDuration;
        public float HardLandingInterruptProgress => hardLandingInterruptProgress;

        public bool Validate(ICollection<string> errors)
        {
            bool valid = true;
            if (!(lv2MinFallDistance < lv3MinFallDistance && lv3MinFallDistance < lv4MinFallDistance)) { errors?.Add("Landing FallDistance 阈值必须满足 Lv2 < Lv3 < Lv4。"); valid = false; }
            return valid;
        }
    }

    [SerializeField] private LocomotionSettings locomotion = new LocomotionSettings();
    [SerializeField] private MotorPhysicsSettings motorPhysics = new MotorPhysicsSettings();
    [SerializeField] private LandingSettings landing = new LandingSettings();
    [Min(0f)] [SerializeField] private float jumpHeight = 1.5f;

    public LocomotionSettings Locomotion => locomotion;
    public MotorPhysicsSettings MotorPhysics => motorPhysics;
    public LandingSettings Landing => landing;
    public float JumpHeight => jumpHeight;
}
