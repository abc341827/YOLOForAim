using System;
using System.Drawing;
using System.Windows.Forms;
using static YOLOForAim.ScreenColorSampler;

namespace YOLOForAim
{
    public partial class Form1 : Form
    {
        private const int HotKeyIdToggleDetection = 1;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NONE = 0x0000;
        private const uint VK_Z = 0x5A;
        private const int DefaultAimAssistFireGracePeriodMs = 120;
        private const int DefaultAimTargetTrackingBlendPercent = 35;
        private const int DefaultAimCloseRangeSlowdownPixels = 64;
        private const int DefaultAimMoveCooldownMs = 10;
        private const int DefaultAimFeedbackFrameDelay = 2;
        private const int DefaultAimInitialAcquireDistancePixels = 240;
        private const int DefaultAimTrackedAcquireDistancePixels = 90;
        private const int DefaultAimStopLockSquareSizePixels = 36;
        private const int DefaultAimStopLockTopOffsetPixels = 18;
        private const int PickScreenColorDelayMs = 1500;
        private IntPtr selectedHwnd = IntPtr.Zero;
        private bool hotKeyRegistered;
        private CancellationTokenSource? detectionCancellationTokenSource;
        private Task? captureTask;
        private Task? inferenceTask;
        private IDetector? detector;
        private int processedFrameCounter;
        private string diagnosticsHeader = string.Empty;
        private readonly InferenceFpsCounter inferenceFpsCounter = new();
        private readonly object latestFrameLock = new();
        private DesktopDuplicationCapture? windowCapture;
        private CapturedPixelFrame? latestCapturedFrame;
        private int latestCapturedFrameVersion;
        private readonly AimAssistController aimAssistController = new();
        private readonly OverlayStateManager overlayStateManager = new();
        private readonly System.Windows.Forms.Timer overlayRefreshTimer;
        private DetectionOverlayForm? detectionOverlay;
        private CaptureRuntimeSettings currentCaptureSettings = CaptureRuntimeSettings.Default;
        private AimRuntimeSettings currentAimSettings = AimRuntimeSettings.Default;
        private ColorDetectionOptions currentPrimaryColorDetectionOptions = ColorDetectionOptions.Default;

        public Form1()
        {
            InitializeComponent();
            overlayRefreshTimer = new System.Windows.Forms.Timer { Interval = 16 };
            overlayRefreshTimer.Tick += OverlayRefreshTimer_Tick;
            pictureBoxPreview.Visible = false;
            lblStatus.Text = "请选择目标窗口。";
            txtDiagnostics.Text = "检测 FPS: 0.0";
            chkOverlayEnabled.Checked = true;
            chkCenterRoi.Checked = false;
            numRoiSize.Value = 640;
            numPreviewInterval.Value = 1;
            numAimHeightPercent.Value = 20;
            numAimDeadzone.Value = 12;
            numAimSmoothing.Value = 35;
            numAimSpeedMultiplier.Value = 100;
            numAimMaxStep.Value = 36;
            numAimSwitchDistance.Value = 140;
            numAimMaxMissedFrames.Value = 3;
            numAimFireGracePeriod.Value = DefaultAimAssistFireGracePeriodMs;
            numAimTrackingBlend.Value = DefaultAimTargetTrackingBlendPercent;
            numAimCloseRangeSlowdown.Value = DefaultAimCloseRangeSlowdownPixels;
            numAimMoveCooldown.Value = DefaultAimMoveCooldownMs;
            numAimFeedbackFrameDelay.Value = DefaultAimFeedbackFrameDelay;
            numAimInitialAcquireDistance.Value = DefaultAimInitialAcquireDistancePixels;
            numAimTrackedAcquireDistance.Value = DefaultAimTrackedAcquireDistancePixels;
            numAimStopInsideBoxArea.Value = DefaultAimStopLockSquareSizePixels;
            numAimStopBoxTopOffset.Value = DefaultAimStopLockTopOffsetPixels;
            numScoreThreshold.Value = 35;
            cmbInferenceBackend.SelectedIndex = 0;
            numAimInitialAcquireDistance.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimTrackedAcquireDistance.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimStopInsideBoxArea.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimStopBoxTopOffset.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimHeightPercent.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimDeadzone.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimSmoothing.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimSpeedMultiplier.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimMaxStep.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimSwitchDistance.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimMaxMissedFrames.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimFireGracePeriod.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimTrackingBlend.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimCloseRangeSlowdown.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimMoveCooldown.ValueChanged += AimRuntimeSetting_ValueChanged;
            numAimFeedbackFrameDelay.ValueChanged += AimRuntimeSetting_ValueChanged;
            UpdateInferenceBackendUi();
            LoadUiSettings();
            UpdateLiveAimRuntimeSettings();
        }

        private void btnSelectWindow_Click(object? sender, EventArgs e)
        {
            using var overlay = new OverlayForm();
            if (overlay.ShowDialog(this) == DialogResult.OK)
            {
                selectedHwnd = overlay.SelectedHandle;
                lblHandle.Text = $"选中窗口句柄: {selectedHwnd}";
                lblStatus.Text = "已选中目标窗口。";
            }
        }

        private void btnSendMouseUp_Click(object? sender, EventArgs e)
        {
            _ = ToggleDetectionAsync();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            hotKeyRegistered = RegisterHotKey(Handle, HotKeyIdToggleDetection, MOD_NONE, VK_Z);
            if (!hotKeyRegistered)
            {
                MessageBox.Show("全局快捷键 Z 注册失败。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotKeyRegistered)
            {
                UnregisterHotKey(Handle, HotKeyIdToggleDetection);
                hotKeyRegistered = false;
            }

            detectionCancellationTokenSource?.Cancel();

            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            detectionCancellationTokenSource?.Cancel();

            try
            {
                overlayRefreshTimer.Stop();
                captureTask?.Wait(500);
                inferenceTask?.Wait(500);
            }
            catch
            {
            }

            lock (latestFrameLock)
            {
                latestCapturedFrame = null;
            }

            pictureBoxPreview.Image?.Dispose();
            detectionOverlay?.Close();
            detectionOverlay?.Dispose();
            windowCapture?.Dispose();
            detector?.Dispose();
            detectionCancellationTokenSource?.Dispose();
            SaveUiSettings();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == (IntPtr)HotKeyIdToggleDetection)
            {
                BeginInvoke(new Action(async () => await ToggleDetectionAsync()));
                return;
            }

            base.WndProc(ref m);
        }

        private void AimRuntimeSetting_ValueChanged(object? sender, EventArgs e)
        {
            UpdateLiveAimRuntimeSettings();
        }

        private CaptureRuntimeSettings ReadCaptureRuntimeSettingsFromUi()
        {
            return new CaptureRuntimeSettings(
                chkCenterRoi.Checked,
                (int)numRoiSize.Value,
                Math.Max(1, (int)numPreviewInterval.Value));
        }

        private void UpdateLiveAimRuntimeSettings()
        {
            currentAimSettings = ReadAimRuntimeSettingsFromUi();
        }

        private AimRuntimeSettings ReadAimRuntimeSettingsFromUi()
        {
            return new AimRuntimeSettings(
                (float)numAimHeightPercent.Value,
                (float)numAimDeadzone.Value,
                (float)numAimSmoothing.Value / 100f,
                (float)numAimSpeedMultiplier.Value / 100f,
                (float)numAimMaxStep.Value,
                (float)numAimSwitchDistance.Value,
                Math.Max(1, (int)numAimMaxMissedFrames.Value),
                (int)numAimFireGracePeriod.Value,
                (float)numAimTrackingBlend.Value / 100f,
                (float)numAimCloseRangeSlowdown.Value,
                (int)numAimMoveCooldown.Value,
                Math.Max(0, (int)numAimFeedbackFrameDelay.Value),
                (float)numAimInitialAcquireDistance.Value,
                (float)numAimTrackedAcquireDistance.Value,
                (float)numAimStopInsideBoxArea.Value,
                (float)numAimStopBoxTopOffset.Value);
        }

        private void UpdateDiagnosticsText()
        {
            string fpsLine = $"检测 FPS: {inferenceFpsCounter.CurrentFps:F1}";
            txtDiagnostics.Text = string.IsNullOrWhiteSpace(diagnosticsHeader)
                ? fpsLine
                : $"{diagnosticsHeader}{Environment.NewLine}{Environment.NewLine}{fpsLine}";
        }

        private void cmbInferenceBackend_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateInferenceBackendUi();
        }

        private async void btnPickScreenColor_Click(object? sender, EventArgs e)
        {
            await PickScreenColorAsync();
        }

        private async Task PickScreenColorAsync()
        {
            btnPickScreenColor.Enabled = false;
            string originalText = btnPickScreenColor.Text;
            btnPickScreenColor.Text = "准备取色...";
            lblStatus.Text = $"请在 {PickScreenColorDelayMs / 1000d:F1} 秒内把鼠标移动到目标像素上。";

            try
            {
                await Task.Delay(PickScreenColorDelayMs);
                if (IsDisposed)
                {
                    return;
                }

                ApplyScreenColorAtCursor();
            }
            finally
            {
                if (!IsDisposed)
                {
                    btnPickScreenColor.Enabled = true;
                    btnPickScreenColor.Text = originalText;
                }
            }
        }

        private void ApplyScreenColorAtCursor()
        {
            CapturedPixelFrame? frameSnapshot;
            lock (latestFrameLock)
            {
                frameSnapshot = latestCapturedFrame;
            }

            PickedScreenColor pickedColor = ScreenColorPickService.PickColorAtCursor(frameSnapshot, selectedHwnd);
            Color color = pickedColor.Color;

            (float hue, int saturation, int value) = RgbToHsv(color.R, color.G, color.B);
            currentPrimaryColorDetectionOptions = new ColorDetectionOptions(hue, saturation, value, 0f, 0, 0, color.R, color.G, color.B);

            if (detector is ColorRectangleDetector colorRectangleDetector)
            {
                colorRectangleDetector.UpdateColorDetectionOptions(currentPrimaryColorDetectionOptions);
                diagnosticsHeader = colorRectangleDetector.ModelSummary;
                UpdateDiagnosticsText();
            }

            string pickedColorText = $"目标色 HEX #{color.R:X2}{color.G:X2}{color.B:X2} | RGB({color.R}, {color.G}, {color.B}) | HSV(H={hue:F1}, S={saturation}, V={value}) | 来源={pickedColor.Source} | 严格 RGB 等值匹配";
            txtPickedColor.Text = pickedColorText;
            lblStatus.Text = $"已取色: {pickedColorText}";
            SaveUiSettings();
        }

        private void UpdatePickedColorText(string prefix)
        {
            txtPickedColor.Text = $"{prefix}: RGB({currentPrimaryColorDetectionOptions.Red}, {currentPrimaryColorDetectionOptions.Green}, {currentPrimaryColorDetectionOptions.Blue}) | 严格 RGB 等值匹配";
        }

        private void UpdateInferenceBackendUi()
        {
            DetectorBackend selectedBackend = GetSelectedBackend();
            bool isTensorRt = selectedBackend == DetectorBackend.TensorRtEngine;
            bool isColorDetection = selectedBackend == DetectorBackend.ColorRectangle;
            chkPreferGpu.Checked = isTensorRt || (!isColorDetection && chkPreferGpu.Checked);
            chkPreferGpu.Enabled = !isTensorRt && !isColorDetection;
            chkPreferGpu.Text = isTensorRt
                ? "TensorRT 模式固定使用 GPU"
                : isColorDetection ? "颜色检测不使用 GPU" : "优先使用 GPU(DML)";

            if (captureTask is null && inferenceTask is null)
            {
                string? modelPath = selectedBackend == DetectorBackend.OnnxRuntimeDirectMl ? ModelPathResolver.ResolveDirectMlModelPath() : null;
                string? enginePath = isTensorRt ? ModelPathResolver.ResolveTensorRtEnginePath() : null;
                lblStatus.Text = selectedBackend switch
                {
                    DetectorBackend.TensorRtEngine => $"TensorRT Engine 待命: engine={(enginePath is null ? "(未找到)" : Path.GetFileName(enginePath))}",
                    DetectorBackend.ColorRectangle => $"颜色检测待命: RGB({currentPrimaryColorDetectionOptions.Red}, {currentPrimaryColorDetectionOptions.Green}, {currentPrimaryColorDetectionOptions.Blue}) 严格单像素匹配",
                    _ => $"DirectML 待命: ONNX={Path.GetFileName(modelPath)}"
                };
            }
        }

        private DetectorBackend GetSelectedBackend()
        {
            return cmbInferenceBackend.SelectedIndex switch
            {
                1 => DetectorBackend.TensorRtEngine,
                2 => DetectorBackend.ColorRectangle,
                _ => DetectorBackend.OnnxRuntimeDirectMl
            };
        }

        private Rectangle GetSourceRegion(int frameWidth, int frameHeight)
        {
            if (!currentCaptureSettings.CenterRoiOnly)
            {
                return new Rectangle(0, 0, frameWidth, frameHeight);
            }

            int squareSize = Math.Max(64, Math.Min(currentCaptureSettings.RoiSize, Math.Min(frameWidth, frameHeight)));
            return new Rectangle(
                (frameWidth - squareSize) / 2,
                (frameHeight - squareSize) / 2,
                squareSize,
                squareSize);
        }

        private void OverlayRefreshTimer_Tick(object? sender, EventArgs e)
        {
            RefreshDetectionOverlay();
        }

        private void chkOverlayEnabled_CheckedChanged(object? sender, EventArgs e)
        {
            if (!chkOverlayEnabled.Checked)
            {
                detectionOverlay?.HideOverlay();
                return;
            }

            RefreshDetectionOverlay();
        }

        private void UpdateOverlayState(Rectangle captureBounds, IReadOnlyList<DetectionResult> detections, int targetWindowWidth, int processedFrameVersion, long capturedTick)
        {
            overlayStateManager.Update(
                captureBounds,
                detections,
                processedFrameVersion,
                aimAssistController.SuppressOverlayFrameVersion,
                capturedTick,
                inferenceFpsCounter.CurrentFps,
                aimAssistController.StabilizedLockedDetection,
                detection => aimAssistController.GetAimPoint(captureBounds, detection, currentAimSettings, targetWindowWidth),
                Cursor.Position);
        }

        private void ClearOverlayState()
        {
            overlayStateManager.Clear();
        }

        private void EnsureDetectionOverlay()
        {
            if (detectionOverlay is not null && !detectionOverlay.IsDisposed)
            {
                return;
            }

            detectionOverlay = new DetectionOverlayForm();
        }

        private void RefreshDetectionOverlay()
        {
            if (!chkOverlayEnabled.Checked || selectedHwnd == IntPtr.Zero)
            {
                detectionOverlay?.HideOverlay();
                return;
            }

            EnsureDetectionOverlay();

            DetectionOverlaySnapshot snapshot = overlayStateManager.GetSnapshot();
            detectionOverlay?.UpdateDetections(selectedHwnd, snapshot.CaptureBounds, snapshot.Detections, snapshot.LockedDetection, snapshot.AimPoint, snapshot.CursorPoint, currentAimSettings.StopLockSquareSizePixels, currentAimSettings.StopLockTopOffsetPixels);
        }

        private static CapturedFrame? CaptureWindow(IntPtr hwnd, bool centerRoiOnly, int roiSize)
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            {
                return null;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            int captureLeft = rect.Left;
            int captureTop = rect.Top;
            int captureWidth = width;
            int captureHeight = height;

            if (centerRoiOnly)
            {
                int squareSize = Math.Max(64, Math.Min(roiSize, Math.Min(width, height)));
                captureLeft = rect.Left + ((width - squareSize) / 2);
                captureTop = rect.Top + ((height - squareSize) / 2);
                captureWidth = squareSize;
                captureHeight = squareSize;
            }

            var bitmap = new Bitmap(captureWidth, captureHeight);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(captureLeft, captureTop, 0, 0, new Size(captureWidth, captureHeight), CopyPixelOperation.SourceCopy);
            return new CapturedFrame(bitmap, new Rectangle(captureLeft, captureTop, captureWidth, captureHeight));
        }

        private void TryMoveMouseToNearestDetection(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, int targetWindowWidth, int processedFrameVersion, long capturedTick)
        {
            aimAssistController.TryMoveToNearestDetection(detections, captureBounds, processedFrameVersion, capturedTick, currentAimSettings, targetWindowWidth);
        }

        private void ResetAimRuntimeState()
        {
            aimAssistController.ResetRuntime();
        }

        private void ResetAimTrackingState()
        {
            aimAssistController.ResetTracking();
        }
    }

}
