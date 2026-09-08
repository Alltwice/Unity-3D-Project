/// <summary>
/// 落地动画枚举
/// </summary>
public enum PlayerLandingPresentationKey
{
    None = 0,
    Land1 = 1,
    Land2 = 2,
    Land3 = 3,
    HardLand = 4
}

/// <summary>
/// 解决落地事实
/// </summary>
public static class PlayerLandingPresentationResolver
{
    public static bool TryResolve(PlayerLandingSnapshot snapshot, out PlayerLandingPresentationKey presentation)
    {
        presentation = PlayerLandingPresentationKey.None;
        if (!snapshot.IsLandingEvent) return false;
        presentation = ResolveSeverity(snapshot.Severity);
        return true;
    }

    private static PlayerLandingPresentationKey ResolveSeverity(PlayerLandingSeverity severity)
    {
        switch (severity)
        {
            case PlayerLandingSeverity.Lv1: return PlayerLandingPresentationKey.Land1;
            case PlayerLandingSeverity.Lv2: return PlayerLandingPresentationKey.Land2;
            case PlayerLandingSeverity.Lv3: return PlayerLandingPresentationKey.Land3;
            default: return PlayerLandingPresentationKey.HardLand;
        }
    }
}
