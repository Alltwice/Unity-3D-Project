using UnityEngine;

/// <summary>
/// 唯一 CharacterController 执行器。只解释 MotorCommand，不认识 Gameplay 动作或动画
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(PlayerGroundProbe))]
public class PlayerMotor : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig config;

    private CharacterController characterController;
    private PlayerGroundProbe groundProbe;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private bool isGrounded;
    private bool initialized;

    public PlayerMovementConfig Config => config;
    public PlayerMotorResult CurrentResult { get; private set; }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        groundProbe = GetComponent<PlayerGroundProbe>();
    }
    /// <summary>
    /// 初始化设定地面状态保存首次快照
    /// </summary>
    public void EnsureInitialized()
    {
        if (initialized) return;
        groundProbe.Refresh();
        isGrounded = characterController.isGrounded || groundProbe.CanSnapToGround;
        CurrentResult = new PlayerMotorResult(Vector3.zero, Vector3.zero, Vector3.zero, 0f, isGrounded, false, CollisionFlags.None);
        initialized = true;
    }
    /// <summary>
    /// 执行移动与动画更新
    /// </summary>
    public PlayerMotorResult Simulate(PlayerMotorCommand command, float deltaTime)
    {
        EnsureInitialized();
        float dt = Mathf.Max(0f, deltaTime);
        //执行跳跃
        if (command.HasVerticalImpulse) verticalVelocity = command.VerticalImpulse;
        //压在地上
        if (isGrounded && verticalVelocity < 0f) verticalVelocity = config.MotorPhysics.GroundedVerticalVelocity;
        //重力
        else verticalVelocity += config.MotorPhysics.Gravity * dt;
        Vector3 planarDisplacement;
        if (command.TranslationMode == PlayerMotorTranslationMode.ImmediateVelocityDriven)
        {
            horizontalVelocity = Vector3.ProjectOnPlane(command.TargetPlanarVelocity, Vector3.up);
            planarDisplacement = horizontalVelocity * Mathf.Clamp(command.PlanarVelocityDuration, 0f, dt);
        }
        else if (command.TranslationMode == PlayerMotorTranslationMode.VelocityDriven)
        {
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, command.TargetPlanarVelocity, command.PlanarAcceleration * dt);
            planarDisplacement = horizontalVelocity * dt;
        }
        else
        {
            planarDisplacement = command.PlanarDisplacement;
            planarDisplacement.y = 0f;
        }
        //记录先前的位置
        Vector3 positionBeforeMove = transform.position;
        bool wasGrounded = isGrounded;
        //真正执行移动
        CollisionFlags collisionFlags = characterController.Move(planarDisplacement + Vector3.up * (verticalVelocity * dt));
        //CollisionFlags 0001 below;0010 above;0100 aside;
        bool controllerGrounded = (collisionFlags & CollisionFlags.Below) != 0 || characterController.isGrounded;
        groundProbe.Refresh();
        bool snappedToGround = !controllerGrounded && wasGrounded && verticalVelocity <= 0f && groundProbe.CanSnapToGround;
        if (snappedToGround)
        {
            //注意这里按位或赋值拿到碰撞信息
            collisionFlags |= characterController.Move(-transform.up * groundProbe.GroundDistance);
            groundProbe.Refresh();
        }
        isGrounded = controllerGrounded || snappedToGround;
        bool justLanded = !wasGrounded && isGrounded;
        if (isGrounded && verticalVelocity < config.MotorPhysics.GroundedVerticalVelocity) verticalVelocity = config.MotorPhysics.GroundedVerticalVelocity;
        ApplyRotation(command, dt);
        Vector3 actualDisplacement = transform.position - positionBeforeMove;
        Vector3 actualPlanarDisplacement = Vector3.ProjectOnPlane(actualDisplacement, Vector3.up);
        horizontalVelocity = PlayerMotorKinematics.CalculateActualPlanarVelocity(actualDisplacement, dt);
        //拿到计算结果
        CurrentResult = new PlayerMotorResult(actualDisplacement, actualPlanarDisplacement, horizontalVelocity, verticalVelocity, isGrounded, justLanded, collisionFlags);
        return CurrentResult;
    }

    private void ApplyRotation(PlayerMotorCommand command, float deltaTime)
    {
        if (command.RotationMode == PlayerMotorRotationMode.YawDelta)
        {
            //四元数的乘法表示角度相加，绕x轴旋转x度，适合动画驱动旋转
            transform.rotation = Quaternion.AngleAxis(command.YawDelta, Vector3.up) * transform.rotation;
            return;
        }
        Vector3 facing = command.DesiredFacingDirection;
        facing.y = 0f;
        if (command.RotationMode != PlayerMotorRotationMode.FaceDirection || facing.sqrMagnitude < 0.0001f) return;
        transform.rotation = PlayerMotorKinematics.CalculateSmoothRotation(transform.rotation, facing, config.Locomotion.RotationSmoothSpeed, deltaTime);
    }
}
