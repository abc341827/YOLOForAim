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
    float PredictionLeadMilliseconds,
    float MaxPredictionPixels,
    float VelocityFeedForwardMaxPixels,
    float InitialAcquireDistancePixels,
    float TrackedAcquireDistancePixels,
    float StopLockSquareSizePixels,
    float StopLockTopOffsetPixels,
    bool CalibrationMode,
    float CalibrationStepPixels)
{
    public static AimRuntimeSettings Default { get; } = new(
        0f,
        4f,
        1f,
        1f,
        100f,
        1000f,
        1,
        UiSettings.DefaultAimAssistFireGracePeriodMs,
        1f,
        5f,
        UiSettings.DefaultAimMoveCooldownMs,
        UiSettings.DefaultAimPredictionLeadMs,
        UiSettings.DefaultAimMaxPredictionPixels,
        UiSettings.DefaultAimVelocityFeedForwardMaxPixels,
        UiSettings.DefaultAimInitialAcquireDistancePixels,
        UiSettings.DefaultAimTrackedAcquireDistancePixels,
        UiSettings.DefaultAimStopLockSquareSizePixels,
        UiSettings.DefaultAimStopLockTopOffsetPixels,
        false,
        UiSettings.DefaultAimCalibrationStepPixels);
}
