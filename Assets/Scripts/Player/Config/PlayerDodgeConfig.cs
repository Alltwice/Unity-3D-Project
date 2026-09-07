using UnityEngine;

[CreateAssetMenu(fileName = "PlayerDodgeConfig", menuName = "Player/Config/Dodge")]
public sealed class PlayerDodgeConfig : ScriptableObject
{
    [Header("闪避 Gameplay Rule")]
    [Min(0f)] [SerializeField] private float cooldown = 0.35f;

    [Min(0.01f)] [SerializeField] private float duration = 0.4f;
    [Min(0f)] [SerializeField] private float speed = 8f;

    public float Cooldown => cooldown;
    public float Duration => duration;
    public float Speed => speed;
}
