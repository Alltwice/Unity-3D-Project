using UnityEngine;

/// <summary>
/// 地面移动目标朝向的解析模式
/// </summary>
public enum PlayerFacingMode
{
    MovementAligned,
    Independent
}

/// <summary>
/// 玩家输入接口，用于给具体逻辑实现引用后主动注入
/// </summary>
public interface IPlayerInputSource
{
    //写入具体输入事件
    Vector2 MoveInput { get; }
    Vector2 LookInput { get; }
    //仅提供 WalkToggle 的当前信号；有效 Walk 模式由 PlayerContext 维护。
    bool IsWalkMode { get; }
    PlayerFacingMode FacingMode { get; }
}
