namespace YOLOForAim;

/// <summary>
/// 保存窗体上的可配置项，字段名保持稳定以兼容现有 ui-settings.json。
/// </summary>
internal sealed record UiSettings(
    bool CenterRoiOnly,
    int RoiSize,
    bool PreferGpu,
    bool OverlayEnabled,
    int ScoreThresholdPercent,
    int PreviewInterval,
    int AimHeightPercent,
    int AimDeadzone,
    int AimSmoothingPercent,
    int AimSpeedMultiplierPercent,
    int AimMaxStep,
    int AimSwitchDistance,
    int AimMaxMissedFrames,
    int AimFireGracePeriodMs = UiSettings.DefaultAimAssistFireGracePeriodMs,
    int AimTrackingBlendPercent = UiSettings.DefaultAimTargetTrackingBlendPercent,
    int AimCloseRangeSlowdownPixels = UiSettings.DefaultAimCloseRangeSlowdownPixels,
    int AimMoveCooldownMs = UiSettings.DefaultAimMoveCooldownMs,
    int AimFeedbackFrameDelay = UiSettings.DefaultAimFeedbackFrameDelay,
    int AimInitialAcquireDistancePixels = UiSettings.DefaultAimInitialAcquireDistancePixels,
    int AimTrackedAcquireDistancePixels = UiSettings.DefaultAimTrackedAcquireDistancePixels,
    int AimStopLockSquareSizePixels = UiSettings.DefaultAimStopLockSquareSizePixels,
    int AimStopLockTopOffsetPixels = UiSettings.DefaultAimStopLockTopOffsetPixels,
    string InferenceBackend = nameof(DetectorBackend.OnnxRuntimeDirectMl),
    ColorDetectionOptions? PrimaryColorDetection = null)
{
    public const int DefaultAimAssistFireGracePeriodMs = 120;
    public const int DefaultAimTargetTrackingBlendPercent = 35;
    public const int DefaultAimCloseRangeSlowdownPixels = 64;
    public const int DefaultAimMoveCooldownMs = 10;
    public const int DefaultAimFeedbackFrameDelay = 2;
    public const int DefaultAimInitialAcquireDistancePixels = 240;
    public const int DefaultAimTrackedAcquireDistancePixels = 90;
    public const int DefaultAimStopLockSquareSizePixels = 36;
    public const int DefaultAimStopLockTopOffsetPixels = 18;
}
