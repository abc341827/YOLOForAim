using System.Windows.Forms;

namespace YOLOForAim;

public partial class Form1
{
    private void LoadUiSettings()
    {
        if (!UiSettingsStore.TryLoad(out UiSettings? settings) || settings is null)
        {
            return;
        }

        cmbInferenceBackend.SelectedIndex = settings.InferenceBackend switch
        {
            nameof(DetectorBackend.TensorRtEngine) => 1,
            "TensorRt" => 1,
            nameof(DetectorBackend.ColorRectangle) => 2,
            "ColorDetection" => 2,
            _ => 0
        };
        chkCenterRoi.Checked = settings.CenterRoiOnly;
        SetNumericValue(numRoiSize, settings.RoiSize);
        chkPreferGpu.Checked = settings.PreferGpu;
        chkOverlayEnabled.Checked = settings.OverlayEnabled;
        SetNumericValue(numScoreThreshold, settings.ScoreThresholdPercent);
        SetNumericValue(numPreviewInterval, settings.PreviewInterval);
        SetNumericValue(numAimHeightPercent, settings.AimHeightPercent);
        SetNumericValue(numAimDeadzone, settings.AimDeadzone);
        SetNumericValue(numAimSmoothing, settings.AimSmoothingPercent);
        SetNumericValue(numAimSpeedMultiplier, settings.AimSpeedMultiplierPercent);
        SetNumericValue(numAimMaxStep, settings.AimMaxStep);
        SetNumericValue(numAimSwitchDistance, settings.AimSwitchDistance);
        SetNumericValue(numAimMaxMissedFrames, settings.AimMaxMissedFrames);
        SetNumericValue(numAimFireGracePeriod, settings.AimFireGracePeriodMs);
        SetNumericValue(numAimTrackingBlend, settings.AimTrackingBlendPercent);
        SetNumericValue(numAimCloseRangeSlowdown, settings.AimCloseRangeSlowdownPixels);
        SetNumericValue(numAimMoveCooldown, settings.AimMoveCooldownMs);
        SetNumericValue(numAimPredictionLead, settings.AimPredictionLeadMs);
        SetNumericValue(numAimMaxPrediction, settings.AimMaxPredictionPixels);
        SetNumericValue(numAimVelocityFeedForward, settings.AimVelocityFeedForwardMaxPixels);
        SetNumericValue(numAimInitialAcquireDistance, settings.AimInitialAcquireDistancePixels);
        SetNumericValue(numAimTrackedAcquireDistance, settings.AimTrackedAcquireDistancePixels);
        SetNumericValue(numAimStopInsideBoxArea, settings.AimStopLockSquareSizePixels);
        SetNumericValue(numAimStopBoxTopOffset, settings.AimStopLockTopOffsetPixels);
        currentPrimaryColorDetectionOptions = settings.PrimaryColorDetection ?? ColorDetectionOptions.Default;
        UpdatePickedColorText("已加载颜色检测色值");
        UpdateInferenceBackendUi();
    }

    private void SaveUiSettings()
    {
        UiSettings settings = new(
            chkCenterRoi.Checked,
            (int)numRoiSize.Value,
            chkPreferGpu.Checked,
            chkOverlayEnabled.Checked,
            (int)numScoreThreshold.Value,
            (int)numPreviewInterval.Value,
            (int)numAimHeightPercent.Value,
            (int)numAimDeadzone.Value,
            (int)numAimSmoothing.Value,
            (int)numAimSpeedMultiplier.Value,
            (int)numAimMaxStep.Value,
            (int)numAimSwitchDistance.Value,
            (int)numAimMaxMissedFrames.Value,
            (int)numAimFireGracePeriod.Value,
            (int)numAimTrackingBlend.Value,
            (int)numAimCloseRangeSlowdown.Value,
            (int)numAimMoveCooldown.Value,
            (int)numAimPredictionLead.Value,
            (int)numAimMaxPrediction.Value,
            (int)numAimVelocityFeedForward.Value,
            (int)numAimInitialAcquireDistance.Value,
            (int)numAimTrackedAcquireDistance.Value,
            (int)numAimStopInsideBoxArea.Value,
            (int)numAimStopBoxTopOffset.Value,
            GetSelectedBackend().ToString(),
            currentPrimaryColorDetectionOptions);

        UiSettingsStore.Save(settings);
    }

    private static void SetNumericValue(NumericUpDown numericUpDown, int value)
    {
        decimal clampedValue = Math.Min(numericUpDown.Maximum, Math.Max(numericUpDown.Minimum, value));
        numericUpDown.Value = clampedValue;
    }
}
