namespace YOLOForAim;

/// <summary>
/// 瞄准辅助运行时参数快照。
/// Form1 从 UI 控件读取一次后保存为该对象，瞄准逻辑只读取快照，避免大量零散字段。
/// </summary>
internal sealed record AimRuntimeSettings(
    float PointBelowOffsetPixels,
    float DeadzonePixels,
    float SmoothingFactor,
    float SpeedMultiplier,
    float MaxStepPixels,
    float LockSwitchDistancePixels,
    int MaxMissedFrames,
    int AssistFireGracePeriodMs,
    float TargetTrackingBlend,
    float CloseRangeSlowdownPixels,
    int MoveCooldownMs,
    int FeedbackFrameDelay,
    float InitialAcquireDistancePixels,
    float TrackedAcquireDistancePixels,
    float StopLockSquareSizePixels,
    float StopLockTopOffsetPixels)
{
    public static AimRuntimeSettings Default { get; } = new(
        20f,
        12f,
        0.35f,
        1f,
        36f,
        140f,
        3,
        UiSettings.DefaultAimAssistFireGracePeriodMs,
        UiSettings.DefaultAimTargetTrackingBlendPercent / 100f,
        UiSettings.DefaultAimCloseRangeSlowdownPixels,
        UiSettings.DefaultAimMoveCooldownMs,
        UiSettings.DefaultAimFeedbackFrameDelay,
        UiSettings.DefaultAimInitialAcquireDistancePixels,
        UiSettings.DefaultAimTrackedAcquireDistancePixels,
        UiSettings.DefaultAimStopLockSquareSizePixels,
        UiSettings.DefaultAimStopLockTopOffsetPixels);
}
