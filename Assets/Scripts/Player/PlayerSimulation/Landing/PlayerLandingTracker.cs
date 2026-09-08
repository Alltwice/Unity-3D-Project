using UnityEngine;

/// <summary>
/// 跨帧记录一次空中生命周期，并在落地帧生成一次性落地事实
/// </summary>
public class PlayerLandingTracker
{
    private readonly PlayerMovementConfig.LandingSettings settings;
    //是否开始进行记录
    private bool trackingAir;
    private float peakHeight;
    private ulong sequence;

    public PlayerLandingTracker(PlayerMovementConfig.LandingSettings landingSettings)
    {
        settings = landingSettings;
    }
    /// <summary>
    /// 在空中时进行的的状态演进，最终返回落地状态快照
    /// </summary>
    public PlayerLandingSnapshot Advance(PlayerMotorResult motorResult, float currentHeight)
    {
        //空中持续演进
        if (!motorResult.IsGrounded)
        {
            if (!trackingAir)
            {
                trackingAir = true;
                peakHeight = currentHeight;
            }
            else
            {
                peakHeight = Mathf.Max(peakHeight, currentHeight);
            }
            return default;
        }
        PlayerLandingSnapshot snapshot = default;
        //落地后开始处理最终数据
        if (motorResult.JustLanded)
        {
            float fallDistance = trackingAir ? Mathf.Max(0f, peakHeight - currentHeight) : 0f;
            snapshot = new PlayerLandingSnapshot(++sequence, ResolveSeverity(fallDistance), fallDistance);
        }
        trackingAir = false;
        peakHeight = 0f;
        return snapshot;
    }

    public void Reset()
    {
        trackingAir = false;
        peakHeight = 0f;
    }
    /// <summary>
    /// 依据坠落高度决定落地严重程度
    /// </summary>
    private PlayerLandingSeverity ResolveSeverity(float fallDistance)
    {
        if (fallDistance >= settings.Lv4MinFallDistance) return PlayerLandingSeverity.Lv4;
        if (fallDistance >= settings.Lv3MinFallDistance) return PlayerLandingSeverity.Lv3;
        if (fallDistance >= settings.Lv2MinFallDistance) return PlayerLandingSeverity.Lv2;
        return PlayerLandingSeverity.Lv1;
    }
}
