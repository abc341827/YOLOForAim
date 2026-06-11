namespace YOLOForAim;

/// <summary>
/// 管理瞄准目标丢失计数。
/// 连续未找到目标超过阈值时，通知调用方重置追踪状态。
/// </summary>
internal sealed class AimMissTracker
{
    private readonly AimRuntimeState state;

    public AimMissTracker(AimRuntimeState state)
    {
        this.state = state;
    }

    public bool RegisterMiss(AimRuntimeSettings settings)
    {
        state.MissedTargetFrames++;
        return state.MissedTargetFrames >= settings.MaxMissedFrames;
    }

    public void RegisterHit()
    {
        state.MissedTargetFrames = 0;
    }
}
