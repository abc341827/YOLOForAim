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
    int AimMaxStep,
    int AimMoveCooldownMs = UiSettings.DefaultAimMoveCooldownMs,
    int AimCalibrationStepPixels = UiSettings.DefaultAimCalibrationStepPixels,
    string InferenceBackend = nameof(DetectorBackend.OnnxRuntimeDirectMl),
    ColorDetectionOptions? PrimaryColorDetection = null)
{
    public const int DefaultAimAssistFireGracePeriodMs = 0;
    public const int DefaultAimTargetTrackingBlendPercent = 35;
    public const int DefaultAimCloseRangeSlowdownPixels = 64;
    public const int DefaultAimMoveCooldownMs = 16;
    public const int DefaultAimPredictionLeadMs = 0;
    public const int DefaultAimMaxPredictionPixels = 0;
    public const int DefaultAimVelocityFeedForwardMaxPixels = 0;
    public const int DefaultAimInitialAcquireDistancePixels = 1000;
    public const int DefaultAimTrackedAcquireDistancePixels = 1000;
    public const int DefaultAimStopLockSquareSizePixels = 36;
    public const int DefaultAimStopLockTopOffsetPixels = 18;
    public const int DefaultAimCalibrationStepPixels = 8;
}
