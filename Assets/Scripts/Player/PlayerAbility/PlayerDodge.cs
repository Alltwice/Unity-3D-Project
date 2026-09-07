using UnityEngine;

/// <summary>
/// 维护 Dodge 的方向、速度、持续时间与退出冷却，由状态输出移动意图
/// </summary>
public sealed class PlayerDodge : MonoBehaviour
{
    [Header("闪避配置")]
    [SerializeField] private PlayerDodgeConfig config;

    private float cooldownRemaining;
    private bool active;
    private float elapsedTime;
    private Vector3 direction;

    public bool CanDodge => !active && cooldownRemaining <= 0f;

    public void TickCooldown(float deltaTime) => cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Mathf.Max(0f, deltaTime));
    public float Progress => elapsedTime / config.Duration;
    public bool IsComplete => elapsedTime >= config.Duration;

    public void Begin()
    {
        active = true;
        elapsedTime = 0f;
        direction = Vector3.zero;
    }

    public void Tick(float deltaTime, ref PlayerGameplayIntent intent)
    {
        if (intent.DesiredMoveDirection.sqrMagnitude > 0.0001f) direction = intent.DesiredMoveDirection.normalized;
        else if (direction == Vector3.zero) direction = intent.DesiredFacingDirection;
        float moveTime = Mathf.Min(Mathf.Max(0f, deltaTime), config.Duration - elapsedTime);
        elapsedTime = Mathf.Min(config.Duration, elapsedTime + moveTime);
        intent.DesiredFacingDirection = direction;
        intent.RequestPlanarVelocity(direction * config.Speed, moveTime);
    }

    public void End()
    {
        if (!active) return;
        active = false;
        cooldownRemaining = config.Cooldown;
    }
}
