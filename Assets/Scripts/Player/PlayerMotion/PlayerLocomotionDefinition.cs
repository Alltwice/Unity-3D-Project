using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地面循环的 Profile、起步脚变体与退出混合参数；Idle 不采样移动周期
/// </summary>
[CreateAssetMenu(fileName = "PlayerLocomotionDefinition", menuName = "Player/Motion/Locomotion Definition")]
public class PlayerLocomotionDefinition : ScriptableObject
{
    [SerializeField] private PlayerLocomotionMode mode;
    [SerializeField] private PlayerMotionProfile defaultProfile;
    [SerializeField] private PlayerMotionProfile leftProfile;
    [SerializeField] private PlayerMotionProfile rightProfile;

    [SerializeField] private PlayerHandoffSettings exitHandoff = PlayerHandoffSettings.Default();
    public PlayerHandoffSettings ExitHandoff => exitHandoff;

    public PlayerLocomotionMode Mode => mode;
    public PlayerMotionProfile DefaultProfile => defaultProfile;
    public PlayerMotionProfile LeftProfile => leftProfile;
    public PlayerMotionProfile RightProfile => rightProfile;

    public bool TryResolveProfile(PlayerFoot requestedVariantFoot, out PlayerMotionProfile profile, out PlayerFoot resolvedVariantFoot)
    {
        resolvedVariantFoot = requestedVariantFoot == PlayerFoot.Left || requestedVariantFoot == PlayerFoot.Right ? requestedVariantFoot : PlayerFoot.Unknown;
        profile = resolvedVariantFoot == PlayerFoot.Left ? leftProfile : resolvedVariantFoot == PlayerFoot.Right ? rightProfile : defaultProfile;
        return profile != null;
    }

    public bool Validate(ICollection<string> errors)
    {
        bool valid = exitHandoff.Validate(errors, name + ".ExitHandoff");
        if (mode == PlayerLocomotionMode.Idle) return valid;
        if (!IsGroundLoopMode(mode)) { errors?.Add("Locomotion Cycle: Mode 必须是 Walk、Run 或 FastRun。"); valid = false; }
        valid &= ValidateProfile(defaultProfile, "Default", errors);
        valid &= ValidateProfile(leftProfile, "Left", errors);
        valid &= ValidateProfile(rightProfile, "Right", errors);
        return valid;
    }

#if UNITY_EDITOR
    public void ConfigureExitHandoff(PlayerHandoffSettings settings) => exitHandoff = settings;

    public void Configure(PlayerLocomotionMode locomotionMode, PlayerMotionProfile defaultLoopProfile, PlayerMotionProfile leftLoopProfile, PlayerMotionProfile rightLoopProfile)
    {
        mode = locomotionMode;
        defaultProfile = defaultLoopProfile;
        leftProfile = leftLoopProfile;
        rightProfile = rightLoopProfile;
    }
#endif
    /// <summary>
    /// 确认地面移动模式
    /// </summary>
    public static bool IsGroundLoopMode(PlayerLocomotionMode locomotionMode)
    {
        return locomotionMode == PlayerLocomotionMode.Walk || locomotionMode == PlayerLocomotionMode.Run || locomotionMode == PlayerLocomotionMode.FastRun;
    }

    private static bool ValidateProfile(PlayerMotionProfile profile, string label, ICollection<string> errors)
    {
        if (profile == null)
        {
            errors?.Add("Locomotion Cycle." + label + ": 缺少 Loop Profile。");
            return false;
        }
        return profile.ValidateLoopPhase(errors);
    }
}
