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
            nameof(DetectorBackend.ColorPriorityTensorRtEngine) => 3,
            "ColorPriorityTensorRt" => 3,
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
        SetNumericValue(numAimMaxStep, settings.AimMaxStep);
        SetNumericValue(numAimMoveCooldown, settings.AimMoveCooldownMs);
        SetNumericValue(numAimCalibrationStep, settings.AimCalibrationStepPixels);
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
            (int)numAimMaxStep.Value,
            (int)numAimMoveCooldown.Value,
            (int)numAimCalibrationStep.Value,
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
