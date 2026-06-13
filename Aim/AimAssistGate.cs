using static YOLOForAim.MouseInputController;

namespace YOLOForAim;

/// <summary>
/// 管理瞄准辅助的输入门控：左键激活、开火宽限期、移动冷却和反馈帧延迟。
/// 该类只判断是否允许移动，不计算移动量。
/// </summary>
internal sealed class AimAssistGate
{
    private readonly AimRuntimeState state;

    public AimAssistGate(AimRuntimeState state)
    {
        this.state = state;
    }

    public bool IsAssistActive(AimRuntimeSettings settings, long now)
    {
        bool isLeftMouseButtonDown = IsLeftMouseButtonDown();
        if (isLeftMouseButtonDown || state.WasLeftMouseButtonDown)
        {
            state.LastFireActivityTick = now;
        }

        state.WasLeftMouseButtonDown = isLeftMouseButtonDown;
        return isLeftMouseButtonDown || (now - state.LastFireActivityTick) <= settings.AssistFireGracePeriodMs;
    }

    public bool CanSendMove(AimRuntimeSettings settings, long now, int processedFrameVersion)
    {
        if (state.LastAimMoveFrameVersion == processedFrameVersion)
        {
            return false;
        }

        if (state.LastAimMoveTick > 0 && settings.MoveCooldownMs > 0 && now - state.LastAimMoveTick < settings.MoveCooldownMs)
        {
            return false;
        }

        return state.LastAimMoveFrameVersion < 0 || processedFrameVersion - state.LastAimMoveFrameVersion > settings.FeedbackFrameDelay;
    }

    public bool CanSendMoveByTime(AimRuntimeSettings settings, long now)
    {
        return state.LastAimMoveTick <= 0 || settings.MoveCooldownMs <= 0 || now - state.LastAimMoveTick >= settings.MoveCooldownMs;
    }

    public void MarkMoveSent(long now, int processedFrameVersion)
    {
        state.LastAimMoveTick = now;
        state.LastAimMoveFrameVersion = processedFrameVersion;
    }

    public void MarkMoveSent(long now)
    {
        state.LastAimMoveTick = now;
    }
}
