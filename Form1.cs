using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace YOLOForAim
{
    public partial class Form1 : Form
    {
        private const int HotKeyIdToggleDetection = 1;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NONE = 0x0000;
        private const uint VK_Z = 0x5A;
        private const int VK_LBUTTON = 0x01;
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
        private const float AimHeightHighConfidenceThreshold = 0.65f;
        private const float AimHeightHighConfidenceBlend = 0.45f;
        private const float AimHeightLowConfidenceBlend = 0.12f;
        private const float AimHeightLowConfidenceMinRatio = 0.92f;
        private const float StableTargetConfidenceThreshold = 0.45f;
        private const float StableTargetPositionTolerancePixels = 30f;
        private const float StableTargetPositionBlend = 0.72f;
        private const int StableTargetConfirmationFrames = 2;
        private const int StableTargetSizeHoldMs = 180;
        private const float StableTargetSizeUpdateCenterOffsetPixels = 28f;
        private const float StableTargetSizeBlend = 0.22f;
        private const int AimTargetSwitchHoldMs = 120;
        private const float AimSameTargetOverlapThreshold = 0.12f;
        private const float StableTargetIouThreshold = 0.35f;
        private const float AimLargePullDistancePixels = 72f;
        private const int AimReacquireAfterLargePullFrames = 2;
        private const int AimReacquireAfterLargePullMs = 90;
        private const float OverlayTrackMaxSpeedPixelsPerSecond = 900f;
        private const float OverlayTrackMinMatchDistancePixels = 18f;
        private const float OverlayTrackMinIou = 0.18f;
        private const float OverlayTrackPositionBlend = 0.9f;
        private const float OverlayTrackSizeBlend = 0.8f;
        private const float OverlayTrackJitterDeadzonePixels = 4f;
        private const float OverlayTrackPredictionLeadSeconds = 0.018f;
        private const float OverlayTrackMaxPredictionSeconds = 0.06f;
        private const float OverlayTrackMaxPredictionPixels = 48f;
        private const float AimTargetPredictionLeadSeconds = 0.02f;
        private const float AimTargetMaxPredictionSeconds = 0.07f;
        private const float AimTargetMaxPredictionPixels = 64f;
        private const int OverlayEmptyFrameHoldMs = 120;
        private static readonly string UiSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "ui-settings.json");
        private static readonly string[] DirectMlModelCandidates = ["exp.onnx", "dawn.onnx", "dawn01.onnx"];
        private static readonly string[] TensorRtEngineCandidates = ["dawn2.engine", "dawn.engine"];

        private IntPtr selectedHwnd = IntPtr.Zero;
        private bool hotKeyRegistered;
        private CancellationTokenSource? detectionCancellationTokenSource;
        private Task? captureTask;
        private Task? inferenceTask;
        private IDetector? detector;
        private int processedFrameCounter;
        private double currentInferenceFps;
        private double inferenceFpsAccumulatedMs;
        private int inferenceFpsFrameCounter;
        private string diagnosticsHeader = string.Empty;
        private readonly Stopwatch inferenceFpsUiStopwatch = new();
        private readonly object latestFrameLock = new();
        private readonly object overlayStateLock = new();
        private DesktopDuplicationCapture? windowCapture;
        private CapturedPixelFrame? latestCapturedFrame;
        private int latestCapturedFrameVersion;
        private Rectangle latestOverlayCaptureBounds;
        private DetectionResult[] latestOverlayDetections = Array.Empty<DetectionResult>();
        private DetectionResult? latestOverlayLockedDetection;
        private PointF? latestOverlayAimPoint;
        private Point latestOverlayCursorPoint;
        private OverlayTrack[] overlayTracks = Array.Empty<OverlayTrack>();
        private long overlayTracksTick;
        private long latestNonEmptyOverlayDetectionsTick;
        private readonly System.Windows.Forms.Timer overlayRefreshTimer;
        private DetectionOverlayForm? detectionOverlay;
        private bool currentCenterRoiOnly;
        private int currentRoiSize;
        private int currentPreviewInterval;
        private float currentAimPointBelowOffsetPixels;
        private float currentAimDeadzonePixels;
        private float currentAimSmoothingFactor;
        private float currentAimSpeedMultiplier;
        private float currentAimMaxStepPixels;
        private float currentAimLockSwitchDistancePixels;
        private int currentAimMaxMissedFrames;
        private int currentAimAssistFireGracePeriodMs;
        private float currentAimTargetTrackingBlend;
        private float currentAimCloseRangeSlowdownPixels;
        private int currentAimMoveCooldownMs;
        private int currentAimFeedbackFrameDelay;
        private float currentAimInitialAcquireDistancePixels;
        private float currentAimTrackedAcquireDistancePixels;
        private float currentAimStopLockSquareSizePixels;
        private float currentAimStopLockTopOffsetPixels;
        private PointF? lockedTargetScreenPoint;
        private PointF? smoothedTargetScreenPoint;
        private PointF? previousAimObservedTargetPoint;
        private long previousAimObservedTargetTick;
        private int missedTargetFrames;
        private long lastFireActivityTick;
        private bool wasLeftMouseButtonDown;
        private long lastAimMoveTick;
        private int lastAimMoveFrameVersion = -1;
        private int lastPendingCompensationFrameVersion = -1;
        private float stabilizedAimTargetHeight;
        private PointF pendingAimCompensation;
        private DetectionResult? stabilizedLockedDetection;
        private int stabilizedLockedDetectionFrames;
        private bool hasAppliedInitialLockPull;
        private long stableTargetSizeHoldUntilTick;
        private long pendingTargetSwitchTick;
        private int suppressOverlayFrameVersion = -1;
        private int suspendAimUntilFrameVersion = -1;
        private long suspendAimUntilTick;
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

        private void btnStartDetection_Click(object? sender, EventArgs e)
        {
            StartDetection();
        }

        private void StartDetection()
        {
            if (selectedHwnd == IntPtr.Zero)
            {
                MessageBox.Show("请先选择目标窗口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (captureTask is not null || inferenceTask is not null)
            {
                return;
            }

            DetectorBackend selectedBackend = GetSelectedBackend();
            string? resolvedModelPath = selectedBackend == DetectorBackend.OnnxRuntimeDirectMl ? ResolveDirectMlModelPath() : null;
            string? tensorRtEnginePath = selectedBackend == DetectorBackend.TensorRtEngine ? ResolveTensorRtEnginePath() : null;

            if (selectedBackend == DetectorBackend.TensorRtEngine && string.IsNullOrWhiteSpace(tensorRtEnginePath))
            {
                MessageBox.Show("未找到 TensorRT engine 文件。请将 .engine 放到输出目录后再启动。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (selectedBackend == DetectorBackend.OnnxRuntimeDirectMl && (resolvedModelPath is null || !File.Exists(resolvedModelPath)))
            {
                MessageBox.Show($"未找到模型文件: {resolvedModelPath}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                detector?.Dispose();
                DetectorOptions detectorOptions = new(
                    selectedBackend,
                    chkPreferGpu.Checked,
                    (float)numScoreThreshold.Value / 100f,
                    tensorRtEnginePath,
                    currentPrimaryColorDetectionOptions);

                detector = selectedBackend switch
                {
                    DetectorBackend.TensorRtEngine => new TensorRtEngineDetector(tensorRtEnginePath!, detectorOptions),
                    DetectorBackend.ColorRectangle => new ColorRectangleDetector(detectorOptions),
                    _ => new YoloDetector(resolvedModelPath!, detectorOptions)
                };
                windowCapture?.Dispose();
                windowCapture = new DesktopDuplicationCapture(selectedHwnd);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            detectionCancellationTokenSource = new CancellationTokenSource();
            processedFrameCounter = 0;
            inferenceFpsFrameCounter = 0;
            currentInferenceFps = 0;
            inferenceFpsAccumulatedMs = 0;
            inferenceFpsUiStopwatch.Restart();
            ResetAimRuntimeState();
            currentCenterRoiOnly = chkCenterRoi.Checked;
            currentRoiSize = (int)numRoiSize.Value;
            currentPreviewInterval = Math.Max(1, (int)numPreviewInterval.Value);
            currentAimPointBelowOffsetPixels = (float)numAimHeightPercent.Value;
            currentAimDeadzonePixels = (float)numAimDeadzone.Value;
            currentAimSmoothingFactor = (float)numAimSmoothing.Value / 100f;
            currentAimSpeedMultiplier = (float)numAimSpeedMultiplier.Value / 100f;
            currentAimMaxStepPixels = (float)numAimMaxStep.Value;
            currentAimLockSwitchDistancePixels = (float)numAimSwitchDistance.Value;
            currentAimMaxMissedFrames = Math.Max(1, (int)numAimMaxMissedFrames.Value);
            currentAimAssistFireGracePeriodMs = (int)numAimFireGracePeriod.Value;
            currentAimTargetTrackingBlend = (float)numAimTrackingBlend.Value / 100f;
            currentAimCloseRangeSlowdownPixels = (float)numAimCloseRangeSlowdown.Value;
            currentAimMoveCooldownMs = (int)numAimMoveCooldown.Value;
            currentAimFeedbackFrameDelay = Math.Max(0, (int)numAimFeedbackFrameDelay.Value);
            currentAimInitialAcquireDistancePixels = (float)numAimInitialAcquireDistance.Value;
            currentAimTrackedAcquireDistancePixels = (float)numAimTrackedAcquireDistance.Value;
            currentAimStopLockSquareSizePixels = (float)numAimStopInsideBoxArea.Value;
            currentAimStopLockTopOffsetPixels = (float)numAimStopBoxTopOffset.Value;
            diagnosticsHeader = detector?.ModelSummary ?? string.Empty;
            UpdateDiagnosticsText();
            ClearOverlayState();
            EnsureDetectionOverlay();
            overlayRefreshTimer.Start();
            ShowWindowAsync(selectedHwnd, SW_RESTORE);
            SetForegroundWindow(selectedHwnd);
            captureTask = Task.Run(() => RunCaptureLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);
            inferenceTask = Task.Run(() => RunInferenceLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);

            btnStartDetection.Enabled = false;
            btnStopDetection.Enabled = true;
            string backendText = selectedBackend switch
            {
                DetectorBackend.TensorRtEngine => "TensorRT Engine",
                DetectorBackend.ColorRectangle => "颜色检测",
                _ => "ONNX Runtime / DirectML"
            };
            string engineText = selectedBackend == DetectorBackend.TensorRtEngine
                ? $", engine={Path.GetFileName(tensorRtEnginePath)}"
                : string.Empty;
            string modelText = resolvedModelPath is null ? string.Empty : $", 模型={Path.GetFileName(resolvedModelPath)}";
            lblStatus.Text = $"检测中... 后端={backendText}{modelText}{engineText}, ROI={(currentCenterRoiOnly ? $"中心 {currentRoiSize}" : "全窗口")}";
        }

        private async Task ToggleDetectionAsync()
        {
            if (captureTask is not null || inferenceTask is not null)
            {
                await StopDetectionAsync();
                return;
            }

            StartDetection();
        }

        private async void btnStopDetection_Click(object? sender, EventArgs e)
        {
            await StopDetectionAsync();
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

        private async Task StopDetectionAsync()
        {
            if (captureTask is null && inferenceTask is null)
            {
                btnStartDetection.Enabled = true;
                btnStopDetection.Enabled = false;
                return;
            }

            detectionCancellationTokenSource?.Cancel();

            try
            {
                var runningTasks = new[] { captureTask, inferenceTask }
                    .Where(static task => task is not null)
                    .Cast<Task>()
                    .ToArray();
                await Task.WhenAll(runningTasks);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                captureTask = null;
                inferenceTask = null;
                detectionCancellationTokenSource?.Dispose();
                detectionCancellationTokenSource = null;
                lock (latestFrameLock)
                {
                    latestCapturedFrame = null;
                    latestCapturedFrameVersion = 0;
                }
                windowCapture?.Dispose();
                windowCapture = null;
                detector?.Dispose();
                detector = null;
                overlayRefreshTimer.Stop();
                ClearOverlayState();
                detectionOverlay?.HideOverlay();
                ResetAimRuntimeState();
                currentInferenceFps = 0;
                inferenceFpsAccumulatedMs = 0;
                inferenceFpsFrameCounter = 0;
                inferenceFpsUiStopwatch.Reset();
                diagnosticsHeader = string.Empty;
                btnStartDetection.Enabled = true;
                btnStopDetection.Enabled = false;
                lblStatus.Text = "检测已停止。";
                UpdateDiagnosticsText();
            }
        }

        private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (windowCapture is null || !windowCapture.TryGetLatestFrame(50, currentCenterRoiOnly, currentRoiSize, out CapturedPixelFrame capturedFrame))
                    {
                        await Task.Delay(1, cancellationToken);
                        continue;
                    }

                    lock (latestFrameLock)
                    {
                        latestCapturedFrame = capturedFrame;
                        latestCapturedFrameVersion++;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!IsDisposed)
                    {
                        BeginInvoke(new Action(async () =>
                        {
                            await StopDetectionAsync();
                            MessageBox.Show($"检测过程中发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }

                    break;
                }
            }
        }

        private async Task RunInferenceLoopAsync(CancellationToken cancellationToken)
        {
            int processedVersion = -1;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    CapturedPixelFrame? frameToProcess = null;
                    int currentVersion;
                    lock (latestFrameLock)
                    {
                        currentVersion = latestCapturedFrameVersion;
                        if (latestCapturedFrame is not null && currentVersion != processedVersion)
                        {
                            frameToProcess = latestCapturedFrame;
                            processedVersion = currentVersion;
                        }
                    }

                    if (frameToProcess is null)
                    {
                        await Task.Delay(1, cancellationToken);
                        continue;
                    }

                    if (frameToProcess is not null)
                    {
                        Rectangle sourceRegion = frameToProcess.IsRegionAlreadyCropped
                            ? new Rectangle(0, 0, frameToProcess.Width, frameToProcess.Height)
                            : GetSourceRegion(frameToProcess.Width, frameToProcess.Height);
                        var detectStopwatch = Stopwatch.StartNew();
                        DetectionRunResult result = detector?.Detect(
                            frameToProcess.Pixels,
                            frameToProcess.Width,
                            frameToProcess.Height,
                            frameToProcess.Stride,
                            sourceRegion,
                            frameToProcess.ReferenceWidth) ?? new DetectionRunResult(Array.Empty<DetectionResult>());
                        detectStopwatch.Stop();
                        TryMoveMouseToNearestDetection(result.Detections, frameToProcess.ScreenBounds, processedVersion, frameToProcess.CapturedTick);
                        UpdateOverlayState(frameToProcess.ScreenBounds, BuildOverlayDetections(result.Detections, frameToProcess.ScreenBounds, processedVersion, frameToProcess.CapturedTick));
                        processedFrameCounter++;
                        UpdateInferenceFps(detectStopwatch.Elapsed.TotalMilliseconds);

                        bool refreshUi = processedFrameCounter % 5 == 0;

                        if (!IsDisposed && refreshUi)
                        {
                            BeginInvoke(new Action(() => UpdatePreviewImage(null, result.Detections.Count)));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!IsDisposed)
                    {
                        BeginInvoke(new Action(async () =>
                        {
                            await StopDetectionAsync();
                            MessageBox.Show($"检测过程中发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }

                    break;
                }
            }
        }

        private void UpdatePreviewImage(Bitmap? previewFrame, int detectionCount)
        {
            if (previewFrame is not null)
            {
                var previousImage = pictureBoxPreview.Image;
                pictureBoxPreview.Image = previewFrame;
                previousImage?.Dispose();
            }

            lblStatus.Text = $"检测中，目标数: {detectionCount}，检测 FPS: {currentInferenceFps:F1}";
            UpdateDiagnosticsText();
        }

        private void AimRuntimeSetting_ValueChanged(object? sender, EventArgs e)
        {
            UpdateLiveAimRuntimeSettings();
        }

        private void UpdateLiveAimRuntimeSettings()
        {
            currentAimPointBelowOffsetPixels = (float)numAimHeightPercent.Value;
            currentAimDeadzonePixels = (float)numAimDeadzone.Value;
            currentAimSmoothingFactor = (float)numAimSmoothing.Value / 100f;
            currentAimSpeedMultiplier = (float)numAimSpeedMultiplier.Value / 100f;
            currentAimMaxStepPixels = (float)numAimMaxStep.Value;
            currentAimLockSwitchDistancePixels = (float)numAimSwitchDistance.Value;
            currentAimMaxMissedFrames = Math.Max(1, (int)numAimMaxMissedFrames.Value);
            currentAimAssistFireGracePeriodMs = (int)numAimFireGracePeriod.Value;
            currentAimTargetTrackingBlend = (float)numAimTrackingBlend.Value / 100f;
            currentAimCloseRangeSlowdownPixels = (float)numAimCloseRangeSlowdown.Value;
            currentAimMoveCooldownMs = (int)numAimMoveCooldown.Value;
            currentAimFeedbackFrameDelay = Math.Max(0, (int)numAimFeedbackFrameDelay.Value);
            currentAimInitialAcquireDistancePixels = (float)numAimInitialAcquireDistance.Value;
            currentAimTrackedAcquireDistancePixels = (float)numAimTrackedAcquireDistance.Value;
            currentAimStopLockSquareSizePixels = (float)numAimStopInsideBoxArea.Value;
            currentAimStopLockTopOffsetPixels = (float)numAimStopBoxTopOffset.Value;
        }

        private void UpdateDiagnosticsText()
        {
            string fpsLine = $"检测 FPS: {currentInferenceFps:F1}";
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
            string colorSource;
            Color color;
            if (TryGetCapturedFrameColorAtCursor(out Color capturedColor))
            {
                color = capturedColor;
                colorSource = "检测帧";
            }
            else if (TryGetDesktopDuplicationColorAtCursor(out Color duplicatedColor))
            {
                color = duplicatedColor;
                colorSource = "Desktop Duplication";
            }
            else
            {
                color = GetScreenColorAtCursor();
                colorSource = "GDI 截屏";
            }

            (float hue, int saturation, int value) = RgbToHsv(color.R, color.G, color.B);
            currentPrimaryColorDetectionOptions = new ColorDetectionOptions(hue, saturation, value, 0f, 0, 0, color.R, color.G, color.B);

            if (detector is ColorRectangleDetector colorRectangleDetector)
            {
                colorRectangleDetector.UpdateColorDetectionOptions(currentPrimaryColorDetectionOptions);
                diagnosticsHeader = colorRectangleDetector.ModelSummary;
                UpdateDiagnosticsText();
            }

            string pickedColorText = $"目标色 HEX #{color.R:X2}{color.G:X2}{color.B:X2} | RGB({color.R}, {color.G}, {color.B}) | HSV(H={hue:F1}, S={saturation}, V={value}) | 来源={colorSource} | 严格 RGB 等值匹配";
            txtPickedColor.Text = pickedColorText;
            lblStatus.Text = $"已取色: {pickedColorText}";
            SaveUiSettings();
        }

        private bool TryGetCapturedFrameColorAtCursor(out Color color)
        {
            color = Color.Empty;
            Point cursorPosition = Cursor.Position;
            lock (latestFrameLock)
            {
                if (latestCapturedFrame is null || !latestCapturedFrame.ScreenBounds.Contains(cursorPosition))
                {
                    return false;
                }

                int x = cursorPosition.X - latestCapturedFrame.ScreenBounds.Left;
                int y = cursorPosition.Y - latestCapturedFrame.ScreenBounds.Top;
                if ((uint)x >= (uint)latestCapturedFrame.Width || (uint)y >= (uint)latestCapturedFrame.Height)
                {
                    return false;
                }

                int pixelOffset = (y * latestCapturedFrame.Stride) + (x * 4);
                byte b = latestCapturedFrame.Pixels[pixelOffset];
                byte g = latestCapturedFrame.Pixels[pixelOffset + 1];
                byte r = latestCapturedFrame.Pixels[pixelOffset + 2];
                color = Color.FromArgb(r, g, b);
                return true;
            }
        }

        private bool TryGetDesktopDuplicationColorAtCursor(out Color color)
        {
            color = Color.Empty;
            if (selectedHwnd == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                using var capture = new DesktopDuplicationCapture(selectedHwnd);
                if (!capture.TryGetLatestFrame(100, false, 0, out CapturedPixelFrame frame))
                {
                    return false;
                }

                Point cursorPosition = Cursor.Position;
                if (!frame.ScreenBounds.Contains(cursorPosition))
                {
                    return false;
                }

                int x = cursorPosition.X - frame.ScreenBounds.Left;
                int y = cursorPosition.Y - frame.ScreenBounds.Top;
                int pixelOffset = (y * frame.Stride) + (x * 4);
                byte b = frame.Pixels[pixelOffset];
                byte g = frame.Pixels[pixelOffset + 1];
                byte r = frame.Pixels[pixelOffset + 2];
                color = Color.FromArgb(r, g, b);
                return true;
            }
            catch
            {
                return false;
            }
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
                string? modelPath = selectedBackend == DetectorBackend.OnnxRuntimeDirectMl ? ResolveDirectMlModelPath() : null;
                string? enginePath = isTensorRt ? ResolveTensorRtEnginePath() : null;
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

        private static string ResolveDirectMlModelPath()
        {
            string? existingFile = FindExistingFile(DirectMlModelCandidates);
            return existingFile ?? Path.Combine(AppContext.BaseDirectory, DirectMlModelCandidates[0]);
        }

        private static string? ResolveTensorRtEnginePath()
        {
            string? enginePath = FindExistingFile(TensorRtEngineCandidates);
            if (!string.IsNullOrWhiteSpace(enginePath))
            {
                return enginePath;
            }

            return null;
        }

        private static string? FindExistingFile(IEnumerable<string> candidateFileNames)
        {
            foreach (string candidateFileName in candidateFileNames)
            {
                string candidatePath = Path.Combine(AppContext.BaseDirectory, candidateFileName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return null;
        }

        private Rectangle GetSourceRegion(int frameWidth, int frameHeight)
        {
            if (!currentCenterRoiOnly)
            {
                return new Rectangle(0, 0, frameWidth, frameHeight);
            }

            int squareSize = Math.Max(64, Math.Min(currentRoiSize, Math.Min(frameWidth, frameHeight)));
            return new Rectangle(
                (frameWidth - squareSize) / 2,
                (frameHeight - squareSize) / 2,
                squareSize,
                squareSize);
        }

        private void UpdateInferenceFps(double detectElapsedMs)
        {
            inferenceFpsAccumulatedMs += detectElapsedMs;
            inferenceFpsFrameCounter++;
            long elapsedMilliseconds = inferenceFpsUiStopwatch.ElapsedMilliseconds;
            if (elapsedMilliseconds < 1000)
            {
                return;
            }

            currentInferenceFps = inferenceFpsAccumulatedMs <= 0
                ? 0
                : inferenceFpsFrameCounter * 1000d / inferenceFpsAccumulatedMs;
            inferenceFpsAccumulatedMs = 0;
            inferenceFpsFrameCounter = 0;
            inferenceFpsUiStopwatch.Restart();
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

        private void UpdateOverlayState(Rectangle captureBounds, IReadOnlyList<DetectionResult> detections)
        {
            long now = Environment.TickCount64;
            lock (overlayStateLock)
            {
                latestOverlayCaptureBounds = captureBounds;
                if (detections.Count > 0)
                {
                    latestOverlayDetections = detections.ToArray();
                    latestNonEmptyOverlayDetectionsTick = now;
                }
                else if (now - latestNonEmptyOverlayDetectionsTick > OverlayEmptyFrameHoldMs)
                {
                    latestOverlayDetections = Array.Empty<DetectionResult>();
                }

                latestOverlayLockedDetection = detections.Count == 0 ? null : stabilizedLockedDetection;
                latestOverlayAimPoint = detections.Count == 0 || stabilizedLockedDetection is null
                    ? null
                    : GetAimPoint(captureBounds, stabilizedLockedDetection);
                latestOverlayCursorPoint = Cursor.Position;
            }
        }

        private IReadOnlyList<DetectionResult> BuildOverlayDetections(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, int processedFrameVersion, long capturedTick)
        {
            _ = captureBounds;
            if (processedFrameVersion <= suppressOverlayFrameVersion)
            {
                return Array.Empty<DetectionResult>();
            }

            return TrackOverlayDetections(detections, capturedTick);
        }

        private IReadOnlyList<DetectionResult> TrackOverlayDetections(IReadOnlyList<DetectionResult> detections, long capturedTick)
        {
            long now = Environment.TickCount64;
            if (detections.Count == 0)
            {
                overlayTracks = Array.Empty<OverlayTrack>();
                overlayTracksTick = now;
                return Array.Empty<DetectionResult>();
            }

            double deltaSeconds = overlayTracksTick > 0
                ? Math.Max(0.001d, (now - overlayTracksTick) / 1000d)
                : 1d / Math.Max(1d, currentInferenceFps > 1d ? currentInferenceFps : 120d);
            float maxMatchDistance = Math.Max(OverlayTrackMinMatchDistancePixels, (float)(OverlayTrackMaxSpeedPixelsPerSecond * deltaSeconds));
            float positionBlend = GetFrameRateAdjustedBlend(OverlayTrackPositionBlend, deltaSeconds);
            float sizeBlend = GetFrameRateAdjustedBlend(OverlayTrackSizeBlend, deltaSeconds);
            float predictionSeconds = Math.Clamp(((now - capturedTick) / 1000f) + OverlayTrackPredictionLeadSeconds, 0f, OverlayTrackMaxPredictionSeconds);
            DetectionResult[] trackedDetections = new DetectionResult[detections.Count];
            var matchedTrackIndexes = new HashSet<int>();

            for (int detectionIndex = 0; detectionIndex < detections.Count; detectionIndex++)
            {
                DetectionResult detection = detections[detectionIndex];
                int matchedTrackIndex = -1;
                double bestDistanceSquared = double.MaxValue;

                for (int trackIndex = 0; trackIndex < overlayTracks.Length; trackIndex++)
                {
                    if (matchedTrackIndexes.Contains(trackIndex))
                    {
                        continue;
                    }

                    OverlayTrack track = overlayTracks[trackIndex];
                    if (track.Detection.ClassId != detection.ClassId)
                    {
                        continue;
                    }

                    PointF previousCenter = GetBoxCenter(track.Detection.Box);
                    PointF currentCenter = GetBoxCenter(detection.Box);
                    double distanceSquared = GetDistanceSquared(previousCenter, currentCenter);
                    float iou = CalculateIou(track.Detection.Box, detection.Box);
                    if (distanceSquared > (maxMatchDistance * maxMatchDistance) && iou < OverlayTrackMinIou)
                    {
                        continue;
                    }

                    if (distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        matchedTrackIndex = trackIndex;
                    }
                }

                DetectionResult trackedDetection = matchedTrackIndex >= 0
                    ? UpdateTrackedOverlayDetection(overlayTracks[matchedTrackIndex].Detection, detection, positionBlend, sizeBlend, (float)deltaSeconds, predictionSeconds)
                    : detection;

                if (matchedTrackIndex >= 0)
                {
                    matchedTrackIndexes.Add(matchedTrackIndex);
                }

                trackedDetections[detectionIndex] = trackedDetection;
            }

            overlayTracks = trackedDetections.Select(detection => new OverlayTrack(detection, now)).ToArray();
            overlayTracksTick = now;
            return trackedDetections;
        }

        private static DetectionResult UpdateTrackedOverlayDetection(DetectionResult previousDetection, DetectionResult currentDetection, float positionBlend, float sizeBlend, float deltaSeconds, float predictionSeconds)
        {
            PointF previousCenter = GetBoxCenter(previousDetection.Box);
            PointF currentCenter = GetBoxCenter(currentDetection.Box);
            if (GetDistanceSquared(previousCenter, currentCenter) <= OverlayTrackJitterDeadzonePixels * OverlayTrackJitterDeadzonePixels)
            {
                return currentDetection with
                {
                    Box = previousDetection.Box,
                    Score = Math.Max(previousDetection.Score, currentDetection.Score)
                };
            }

            PointF trackedCenter = LerpPoint(previousCenter, currentCenter, positionBlend);
            PointF velocity = new(
                (currentCenter.X - previousCenter.X) / Math.Max(0.001f, deltaSeconds),
                (currentCenter.Y - previousCenter.Y) / Math.Max(0.001f, deltaSeconds));
            trackedCenter = PredictPointFromVelocity(trackedCenter, velocity, predictionSeconds, OverlayTrackMaxPredictionPixels);
            SizeF guardedSize = new(
                GuardTrackedSize(previousDetection.Box.Width, currentDetection.Box.Width),
                GuardTrackedSize(previousDetection.Box.Height, currentDetection.Box.Height));
            SizeF trackedSize = LerpSize(previousDetection.Box.Size, guardedSize, sizeBlend);
            RectangleF trackedBox = CreateCenteredBox(trackedCenter, trackedSize);

            return currentDetection with
            {
                Box = trackedBox,
                Score = Math.Max(previousDetection.Score, currentDetection.Score)
            };
        }

        private void ClearOverlayState()
        {
            lock (overlayStateLock)
            {
                latestOverlayCaptureBounds = Rectangle.Empty;
                latestOverlayDetections = Array.Empty<DetectionResult>();
                latestOverlayLockedDetection = null;
                latestOverlayAimPoint = null;
                latestOverlayCursorPoint = Point.Empty;
                overlayTracks = Array.Empty<OverlayTrack>();
                overlayTracksTick = 0;
                latestNonEmptyOverlayDetectionsTick = 0;
            }
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

            Rectangle captureBounds;
            DetectionResult[] detections;
            DetectionResult? lockedDetection;
            PointF? aimPoint;
            Point cursorPoint;
            lock (overlayStateLock)
            {
                captureBounds = latestOverlayCaptureBounds;
                detections = latestOverlayDetections;
                lockedDetection = latestOverlayLockedDetection;
                aimPoint = latestOverlayAimPoint;
                cursorPoint = latestOverlayCursorPoint;
            }

            detectionOverlay?.UpdateDetections(selectedHwnd, captureBounds, detections, lockedDetection, aimPoint, cursorPoint, currentAimStopLockSquareSizePixels, currentAimStopLockTopOffsetPixels);
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

        private void TryMoveMouseToNearestDetection(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, int processedFrameVersion, long capturedTick)
        {
            long now = Environment.TickCount64;
            if (processedFrameVersion <= suspendAimUntilFrameVersion || now < suspendAimUntilTick)
            {
                return;
            }

            if (captureBounds.IsEmpty || !IsAimAssistActive())
            {
                ResetAimTrackingState();
                return;
            }

            if (detections.Count == 0)
            {
                missedTargetFrames++;
                if (missedTargetFrames >= currentAimMaxMissedFrames)
                {
                    ResetAimTrackingState();
                }
                return;
            }

            PointF aimReferencePoint = GetAimReferencePoint(captureBounds);
            TargetCandidate? nearestDetection = SelectTargetCandidate(detections, captureBounds, aimReferencePoint);
            if (nearestDetection is null)
            {
                missedTargetFrames++;
                if (missedTargetFrames >= currentAimMaxMissedFrames)
                {
                    ResetAimTrackingState();
                }

                return;
            }

            DetectionResult currentDetection = nearestDetection.Detection;
            bool resetTargetTracking = stabilizedLockedDetection is null || !IsLikelySameLockedTarget(currentDetection, captureBounds);
            DetectionResult stableDetection = GetStabilizedDetection(currentDetection, captureBounds, resetTargetTracking);
            PointF targetPoint = PredictAimTargetPoint(GetAimPoint(captureBounds, stableDetection), resetTargetTracking, now, capturedTick);
            lockedTargetScreenPoint = GetAimPoint(captureBounds, currentDetection);
            smoothedTargetScreenPoint = smoothedTargetScreenPoint is null || resetTargetTracking
                ? targetPoint
                : LerpPoint(smoothedTargetScreenPoint.Value, targetPoint, currentAimTargetTrackingBlend);
            targetPoint = smoothedTargetScreenPoint.Value;
            missedTargetFrames = 0;
            pendingTargetSwitchTick = 0;
            hasAppliedInitialLockPull = true;

            float rawMoveX = targetPoint.X - aimReferencePoint.X;
            float rawMoveY = targetPoint.Y - aimReferencePoint.Y;
            float distanceToAimPoint = MathF.Sqrt((rawMoveX * rawMoveX) + (rawMoveY * rawMoveY));
            if (currentAimPointBelowOffsetPixels <= 0f &&
                IsAimReferenceInsideStableBox(captureBounds, stableDetection, aimReferencePoint, currentAimStopLockSquareSizePixels, currentAimStopLockTopOffsetPixels))
            {
                return;
            }

            if (distanceToAimPoint <= currentAimDeadzonePixels || !CanSendAimMove(now, processedFrameVersion))
            {
                return;
            }

            float moveX = rawMoveX * currentAimSmoothingFactor * currentAimSpeedMultiplier;
            float moveY = rawMoveY * currentAimSmoothingFactor * currentAimSpeedMultiplier;
            if (currentAimCloseRangeSlowdownPixels > 1f && distanceToAimPoint < currentAimCloseRangeSlowdownPixels)
            {
                float slowdownScale = Math.Clamp(distanceToAimPoint / currentAimCloseRangeSlowdownPixels, 0.2f, 1f);
                moveX *= slowdownScale;
                moveY *= slowdownScale;
            }

            float smoothedDistance = MathF.Sqrt((moveX * moveX) + (moveY * moveY));
            float currentMaxStep = currentAimMaxStepPixels * currentAimSpeedMultiplier;
            if (smoothedDistance > currentMaxStep)
            {
                float scale = currentMaxStep / smoothedDistance;
                moveX *= scale;
                moveY *= scale;
            }

            int finalMoveX = (int)Math.Round(moveX);
            int finalMoveY = (int)Math.Round(moveY);
            if (finalMoveX == 0 && finalMoveY == 0)
            {
                return;
            }

            SendRelativeMouseMove(finalMoveX, finalMoveY);
            lastAimMoveTick = now;
            lastAimMoveFrameVersion = processedFrameVersion;
        }

        private void ResetAimRuntimeState()
        {
            ResetAimTrackingState();
            lastFireActivityTick = 0;
            wasLeftMouseButtonDown = false;
            lastAimMoveTick = 0;
            lastAimMoveFrameVersion = -1;
            lastPendingCompensationFrameVersion = -1;
            pendingAimCompensation = PointF.Empty;
            stableTargetSizeHoldUntilTick = 0;
            suppressOverlayFrameVersion = -1;
            suspendAimUntilFrameVersion = -1;
            suspendAimUntilTick = 0;
        }

        private void ResetAimTrackingState()
        {
            lockedTargetScreenPoint = null;
            smoothedTargetScreenPoint = null;
            previousAimObservedTargetPoint = null;
            previousAimObservedTargetTick = 0;
            missedTargetFrames = 0;
            stabilizedAimTargetHeight = 0f;
            stabilizedLockedDetection = null;
            stabilizedLockedDetectionFrames = 0;
            hasAppliedInitialLockPull = false;
            pendingTargetSwitchTick = 0;
        }

        private DetectionResult GetStabilizedDetection(DetectionResult detection, Rectangle captureBounds, bool resetTracking)
        {
            long now = Environment.TickCount64;
            if (resetTracking || stabilizedLockedDetection is null)
            {
                stabilizedLockedDetection = detection;
                stabilizedLockedDetectionFrames = 1;
                stableTargetSizeHoldUntilTick = now + StableTargetSizeHoldMs;
                return detection;
            }

            if (!ShouldKeepStableTarget(stabilizedLockedDetection, detection))
            {
                stabilizedLockedDetection = detection;
                stabilizedLockedDetectionFrames = 1;
                stableTargetSizeHoldUntilTick = now + StableTargetSizeHoldMs;
                return detection;
            }

            stabilizedLockedDetectionFrames++;
            if (stabilizedLockedDetectionFrames < StableTargetConfirmationFrames)
            {
                stabilizedLockedDetection = detection;
                return detection;
            }

            PointF previousCenter = GetBoxCenter(stabilizedLockedDetection.Box);
            PointF currentCenter = GetBoxCenter(detection.Box);
            PointF stabilizedCenter = LerpPoint(previousCenter, currentCenter, StableTargetPositionBlend);
            SizeF stabilizedSize = GetStableTargetSize(detection, captureBounds, now);
            RectangleF stabilizedBox = CreateCenteredBox(stabilizedCenter, stabilizedSize);
            stabilizedLockedDetection = detection with { Box = stabilizedBox };
            return stabilizedLockedDetection;
        }

        private SizeF GetStableTargetSize(DetectionResult detection, Rectangle captureBounds, long now)
        {
            _ = captureBounds;
            if (stabilizedLockedDetection is null)
            {
                return detection.Box.Size;
            }

            SizeF previousSize = stabilizedLockedDetection.Box.Size;
            SizeF currentSize = detection.Box.Size;
            if (now < stableTargetSizeHoldUntilTick)
            {
                return previousSize;
            }

            PointF previousCenter = GetBoxCenter(stabilizedLockedDetection.Box);
            PointF currentCenter = GetBoxCenter(detection.Box);
            if (GetDistanceSquared(previousCenter, currentCenter) > StableTargetSizeUpdateCenterOffsetPixels * StableTargetSizeUpdateCenterOffsetPixels &&
                CalculateIou(stabilizedLockedDetection.Box, detection.Box) < StableTargetIouThreshold)
            {
                return previousSize;
            }

            SizeF guardedSize = new(
                GuardTrackedSize(previousSize.Width, currentSize.Width),
                GuardTrackedSize(previousSize.Height, currentSize.Height));
            return LerpSize(previousSize, guardedSize, StableTargetSizeBlend);
        }

        private static float GuardTrackedSize(float previousSize, float currentSize)
        {
            if (previousSize <= 0f || currentSize >= previousSize)
            {
                return currentSize;
            }

            return Math.Max(currentSize, previousSize * 0.92f);
        }

        private static bool ShouldKeepStableTarget(DetectionResult previousDetection, DetectionResult currentDetection)
        {
            if (previousDetection.ClassId != currentDetection.ClassId)
            {
                return false;
            }

            if (currentDetection.Score < StableTargetConfidenceThreshold)
            {
                return CalculateIou(previousDetection.Box, currentDetection.Box) >= StableTargetIouThreshold;
            }

            PointF previousCenter = GetBoxCenter(previousDetection.Box);
            PointF currentCenter = GetBoxCenter(currentDetection.Box);
            return GetDistanceSquared(previousCenter, currentCenter) <=
                (StableTargetPositionTolerancePixels * StableTargetPositionTolerancePixels) ||
                CalculateIou(previousDetection.Box, currentDetection.Box) >= StableTargetIouThreshold;
        }

        private bool IsLikelySameLockedTarget(DetectionResult detection, Rectangle captureBounds)
        {
            if (stabilizedLockedDetection is null || detection.ClassId != stabilizedLockedDetection.ClassId)
            {
                return false;
            }

            if (CalculateIou(stabilizedLockedDetection.Box, detection.Box) >= AimSameTargetOverlapThreshold)
            {
                return true;
            }

            PointF lockedPoint = smoothedTargetScreenPoint
                ?? lockedTargetScreenPoint
                ?? GetAimPoint(captureBounds, stabilizedLockedDetection);
            RectangleF expandedBounds = GetExpandedDetectionBounds(captureBounds, detection, currentAimDeadzonePixels + 8f);
            return expandedBounds.Contains(lockedPoint);
        }

        private float GetEffectiveAimHeight(DetectionResult detection, bool resetHeightTracking)
        {
            float detectedHeight = Math.Max(1f, detection.Box.Height);
            if (resetHeightTracking || stabilizedAimTargetHeight <= 0f)
            {
                stabilizedAimTargetHeight = detectedHeight;
                return stabilizedAimTargetHeight;
            }

            bool highConfidence = detection.Score >= AimHeightHighConfidenceThreshold;
            float blend = highConfidence ? AimHeightHighConfidenceBlend : AimHeightLowConfidenceBlend;
            float candidateHeight = detectedHeight;
            if (!highConfidence && detectedHeight < stabilizedAimTargetHeight)
            {
                candidateHeight = Math.Max(detectedHeight, stabilizedAimTargetHeight * AimHeightLowConfidenceMinRatio);
            }

            stabilizedAimTargetHeight = LerpFloat(stabilizedAimTargetHeight, candidateHeight, blend);
            return Math.Max(1f, stabilizedAimTargetHeight);
        }

        private PointF PredictAimTargetPoint(PointF observedTargetPoint, bool resetTargetTracking, long now, long capturedTick)
        {
            if (resetTargetTracking || previousAimObservedTargetPoint is null || previousAimObservedTargetTick <= 0)
            {
                previousAimObservedTargetPoint = observedTargetPoint;
                previousAimObservedTargetTick = now;
                return observedTargetPoint;
            }

            float deltaSeconds = Math.Max(0.001f, (now - previousAimObservedTargetTick) / 1000f);
            PointF velocity = new(
                (observedTargetPoint.X - previousAimObservedTargetPoint.Value.X) / deltaSeconds,
                (observedTargetPoint.Y - previousAimObservedTargetPoint.Value.Y) / deltaSeconds);
            previousAimObservedTargetPoint = observedTargetPoint;
            previousAimObservedTargetTick = now;

            float predictionSeconds = Math.Clamp(((now - capturedTick) / 1000f) + AimTargetPredictionLeadSeconds, 0f, AimTargetMaxPredictionSeconds);
            return PredictPointFromVelocity(observedTargetPoint, velocity, predictionSeconds, AimTargetMaxPredictionPixels);
        }

        private bool CanSendAimMove(long now, int processedFrameVersion)
        {
            if (lastAimMoveFrameVersion == processedFrameVersion)
            {
                return false;
            }

            if (lastAimMoveTick > 0 && currentAimMoveCooldownMs > 0 && now - lastAimMoveTick < currentAimMoveCooldownMs)
            {
                return false;
            }

            return lastAimMoveFrameVersion < 0 || processedFrameVersion - lastAimMoveFrameVersion > currentAimFeedbackFrameDelay;
        }

        private TargetCandidate? SelectTargetCandidate(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, PointF referencePoint)
        {
            PointF? lockedAnchor = smoothedTargetScreenPoint ?? lockedTargetScreenPoint;
            float maxCandidateDistancePixels = GetCurrentAimAcquireDistancePixels();
            if (lockedAnchor is not null)
            {
                TargetCandidate? containingCandidate = FindContainingTargetCandidate(detections, captureBounds, lockedAnchor.Value);
                if (containingCandidate is not null)
                {
                    return IsTargetCandidateNearCursor(containingCandidate, referencePoint, maxCandidateDistancePixels) ? containingCandidate : null;
                }

                TargetCandidate? nearestCandidate = FindNearestTargetCandidate(detections, captureBounds, lockedAnchor.Value, maxCandidateDistancePixels);
                return nearestCandidate is not null && IsTargetCandidateNearCursor(nearestCandidate, referencePoint, maxCandidateDistancePixels)
                    ? nearestCandidate
                    : null;
            }

            return FindNearestTargetCandidate(detections, captureBounds, referencePoint, maxCandidateDistancePixels);
        }

        private TargetCandidate? FindContainingTargetCandidate(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, PointF referencePoint)
        {
            float padding = MathF.Max(currentAimDeadzonePixels, 12f);
            TargetCandidate? bestCandidate = null;

            foreach (DetectionResult detection in detections)
            {
                RectangleF detectionBounds = GetExpandedDetectionBounds(captureBounds, detection, padding);
                if (!detectionBounds.Contains(referencePoint))
                {
                    continue;
                }

                PointF targetPoint = GetAimPoint(captureBounds, detection);
                double distanceSquared = GetDistanceSquared(referencePoint, targetPoint);
                if (bestCandidate is null || distanceSquared < bestCandidate.DistanceSquared)
                {
                    bestCandidate = new TargetCandidate(detection, targetPoint, distanceSquared);
                }
            }

            return bestCandidate;
        }

        private TargetCandidate? FindNearestTargetCandidate(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, PointF referencePoint, float maxDistancePixels)
        {
            TargetCandidate? bestCandidate = null;
            double maxDistanceSquared = maxDistancePixels * maxDistancePixels;

            foreach (DetectionResult detection in detections)
            {
                PointF targetPoint = GetAimPoint(captureBounds, detection);
                double distanceSquared = GetDistanceSquared(referencePoint, targetPoint);
                if (distanceSquared > maxDistanceSquared)
                {
                    continue;
                }

                if (bestCandidate is null || distanceSquared < bestCandidate.DistanceSquared)
                {
                    bestCandidate = new TargetCandidate(detection, targetPoint, distanceSquared);
                }
            }

            return bestCandidate;
        }

        private float GetCurrentAimAcquireDistancePixels()
        {
            return !hasAppliedInitialLockPull
                ? Math.Max(1f, currentAimInitialAcquireDistancePixels)
                : Math.Max(1f, currentAimTrackedAcquireDistancePixels);
        }

        private static bool IsTargetCandidateNearCursor(TargetCandidate candidate, PointF cursorPoint, float maxDistancePixels)
        {
            return candidate.DistanceSquared <= (maxDistancePixels * maxDistancePixels) ||
                GetDistanceSquared(candidate.TargetPoint, cursorPoint) <= (maxDistancePixels * maxDistancePixels);
        }

        private static RectangleF GetExpandedDetectionBounds(Rectangle captureBounds, DetectionResult detection, float padding)
        {
            return new RectangleF(
                captureBounds.Left + detection.Box.X - padding,
                captureBounds.Top + detection.Box.Y - padding,
                detection.Box.Width + (padding * 2f),
                detection.Box.Height + (padding * 2f));
        }

        private static RectangleF GetDetectionScreenBounds(Rectangle captureBounds, DetectionResult detection)
        {
            return new RectangleF(
                captureBounds.Left + detection.Box.X,
                captureBounds.Top + detection.Box.Y,
                detection.Box.Width,
                detection.Box.Height);
        }

        private static bool IsAimReferenceInsideStableBox(Rectangle captureBounds, DetectionResult detection, PointF aimReferencePoint, float squareSizePixels, float topOffsetPixels)
        {
            RectangleF screenBounds = GetDetectionScreenBounds(captureBounds, detection);
            RectangleF lockSquareBounds = GetLockSquareBounds(screenBounds, squareSizePixels, topOffsetPixels);
            return lockSquareBounds.Contains(aimReferencePoint);
        }

        private PointF GetStopLockTargetPoint(Rectangle captureBounds, DetectionResult detection)
        {
            RectangleF screenBounds = GetDetectionScreenBounds(captureBounds, detection);
            RectangleF lockSquareBounds = GetLockSquareBounds(screenBounds, currentAimStopLockSquareSizePixels, currentAimStopLockTopOffsetPixels);
            return GetBoxCenter(lockSquareBounds);
        }

        private static RectangleF GetLockSquareBounds(RectangleF bounds, float squareSizePixels, float topOffsetPixels)
        {
            float squareSize = Math.Clamp(squareSizePixels, 8f, Math.Max(8f, Math.Min(bounds.Width, bounds.Height)));
            float left = bounds.Left + ((bounds.Width - squareSize) / 2f);
            float top = bounds.Top + Math.Clamp(topOffsetPixels, 0f, Math.Max(0f, bounds.Height - squareSize));
            return new RectangleF(left, top, squareSize, squareSize);
        }

        private PointF GetAimPoint(Rectangle captureBounds, DetectionResult detection)
        {
            return GetAimPoint(captureBounds, detection, detection.Box.Height);
        }

        private PointF GetAimPoint(Rectangle captureBounds, DetectionResult detection, float effectiveHeight)
        {
            _ = effectiveHeight;
            return new PointF(
                captureBounds.Left + detection.Box.X + (detection.Box.Width / 2f),
                captureBounds.Top + detection.Box.Bottom + currentAimPointBelowOffsetPixels);
        }

        private static PointF GetAimReferencePoint(Rectangle captureBounds)
        {
            _ = captureBounds;
            Point cursorPosition = Cursor.Position;
            return new PointF(cursorPosition.X, cursorPosition.Y);
        }

        private static Color GetScreenColorAtCursor()
        {
            Point cursorPosition = Cursor.Position;
            using Bitmap bitmap = new(1, 1);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(cursorPosition, Point.Empty, new Size(1, 1), CopyPixelOperation.SourceCopy);
            return bitmap.GetPixel(0, 0);
        }

        private static (float Hue, int Saturation, int Value) RgbToHsv(byte r, byte g, byte b)
        {
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            int delta = max - min;
            if (delta == 0)
            {
                return (0f, 0, max);
            }

            float hue;
            if (max == r)
            {
                hue = 60f * ((g - b) / (float)delta);
                if (hue < 0f)
                {
                    hue += 360f;
                }
            }
            else if (max == g)
            {
                hue = 60f * (((b - r) / (float)delta) + 2f);
            }
            else
            {
                hue = 60f * (((r - g) / (float)delta) + 4f);
            }

            int saturation = max == 0 ? 0 : delta * 255 / max;
            return (hue, saturation, max);
        }

        private static bool IsLeftMouseButtonDown()
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        }

        private bool IsAimAssistActive()
        {
            bool isLeftMouseButtonDown = IsLeftMouseButtonDown();
            long now = Environment.TickCount64;

            if (isLeftMouseButtonDown || wasLeftMouseButtonDown)
            {
                lastFireActivityTick = now;
            }

            wasLeftMouseButtonDown = isLeftMouseButtonDown;
            return isLeftMouseButtonDown || (now - lastFireActivityTick) <= currentAimAssistFireGracePeriodMs;
        }

        private static float GetDistanceSquared(PointF a, PointF b)
        {
            float deltaX = a.X - b.X;
            float deltaY = a.Y - b.Y;
            return (deltaX * deltaX) + (deltaY * deltaY);
        }

        private static PointF GetBoxCenter(RectangleF box)
        {
            return new PointF(box.Left + (box.Width / 2f), box.Top + (box.Height / 2f));
        }

        private static RectangleF CreateCenteredBox(PointF center, SizeF size)
        {
            return new RectangleF(
                center.X - (size.Width / 2f),
                center.Y - (size.Height / 2f),
                size.Width,
                size.Height);
        }

        private static float CalculateIou(RectangleF a, RectangleF b)
        {
            float left = Math.Max(a.Left, b.Left);
            float top = Math.Max(a.Top, b.Top);
            float right = Math.Min(a.Right, b.Right);
            float bottom = Math.Min(a.Bottom, b.Bottom);

            float intersectionWidth = Math.Max(0, right - left);
            float intersectionHeight = Math.Max(0, bottom - top);
            float intersectionArea = intersectionWidth * intersectionHeight;
            if (intersectionArea <= 0)
            {
                return 0f;
            }

            float unionArea = (a.Width * a.Height) + (b.Width * b.Height) - intersectionArea;
            return unionArea <= 0 ? 0f : intersectionArea / unionArea;
        }

        private static PointF LerpPoint(PointF from, PointF to, float amount)
        {
            return new PointF(
                from.X + ((to.X - from.X) * amount),
                from.Y + ((to.Y - from.Y) * amount));
        }

        private static PointF PredictPointFromVelocity(PointF point, PointF velocityPixelsPerSecond, float predictionSeconds, float maxPredictionPixels)
        {
            float offsetX = velocityPixelsPerSecond.X * predictionSeconds;
            float offsetY = velocityPixelsPerSecond.Y * predictionSeconds;
            float distance = MathF.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
            if (distance > maxPredictionPixels && distance > 0f)
            {
                float scale = maxPredictionPixels / distance;
                offsetX *= scale;
                offsetY *= scale;
            }

            return new PointF(point.X + offsetX, point.Y + offsetY);
        }

        private static SizeF LerpSize(SizeF from, SizeF to, float amount)
        {
            return new SizeF(
                from.Width + ((to.Width - from.Width) * amount),
                from.Height + ((to.Height - from.Height) * amount));
        }

        private static float GetFrameRateAdjustedBlend(float baseBlendAt60Fps, double deltaSeconds)
        {
            float clampedBaseBlend = Math.Clamp(baseBlendAt60Fps, 0.001f, 0.999f);
            double frameScale = Math.Max(0.001d, deltaSeconds * 60d);
            return (float)Math.Clamp(1d - Math.Pow(1d - clampedBaseBlend, frameScale), 0.001d, 0.999d);
        }

        private static float LerpFloat(float from, float to, float amount)
        {
            return from + ((to - from) * amount);
        }

        private static void SendRelativeMouseMove(int dx, int dy)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private void LoadUiSettings()
        {
            try
            {
                if (!File.Exists(UiSettingsFilePath))
                {
                    return;
                }

                string json = File.ReadAllText(UiSettingsFilePath);
                UiSettings? settings = JsonSerializer.Deserialize<UiSettings>(json);
                if (settings is null)
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
                SetNumericValue(numAimFeedbackFrameDelay, settings.AimFeedbackFrameDelay);
                SetNumericValue(numAimInitialAcquireDistance, settings.AimInitialAcquireDistancePixels);
                SetNumericValue(numAimTrackedAcquireDistance, settings.AimTrackedAcquireDistancePixels);
                SetNumericValue(numAimStopInsideBoxArea, settings.AimStopLockSquareSizePixels);
                SetNumericValue(numAimStopBoxTopOffset, settings.AimStopLockTopOffsetPixels);
                currentPrimaryColorDetectionOptions = settings.PrimaryColorDetection ?? ColorDetectionOptions.Default;
                UpdatePickedColorText("已加载颜色检测色值");
                UpdateInferenceBackendUi();
            }
            catch
            {
            }
        }

        private void SaveUiSettings()
        {
            try
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
                    (int)numAimFeedbackFrameDelay.Value,
                    (int)numAimInitialAcquireDistance.Value,
                    (int)numAimTrackedAcquireDistance.Value,
                    (int)numAimStopInsideBoxArea.Value,
                    (int)numAimStopBoxTopOffset.Value,
                    GetSelectedBackend().ToString(),
                    currentPrimaryColorDetectionOptions);

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(UiSettingsFilePath, json);
            }
            catch
            {
            }
        }

        private static void SetNumericValue(NumericUpDown numericUpDown, int value)
        {
            decimal clampedValue = Math.Min(numericUpDown.Maximum, Math.Max(numericUpDown.Minimum, value));
            numericUpDown.Value = clampedValue;
        }

        private void DrawDetections(Bitmap frame, IReadOnlyList<DetectionResult> detections)
        {
            using var graphics = Graphics.FromImage(frame);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.Lime, 2f);
            using var labelBackground = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            using var textBrush = new SolidBrush(Color.Yellow);

            foreach (var detection in detections)
            {
                graphics.DrawRectangle(pen, detection.Box.X, detection.Box.Y, detection.Box.Width, detection.Box.Height);

                string text = $"{detection.Label} {detection.Score:P0}";
                SizeF textSize = graphics.MeasureString(text, Font);
                float labelY = Math.Max(0, detection.Box.Y - textSize.Height);
                graphics.FillRectangle(labelBackground, detection.Box.X, labelY, textSize.Width + 6, textSize.Height + 2);
                graphics.DrawString(text, Font, textBrush, detection.Box.X + 3, labelY + 1);
            }
        }

        #region WinAPI
        private const int SW_RESTORE = 9;
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private sealed record CapturedFrame(Bitmap Frame, Rectangle ScreenBounds);
        private sealed record UiSettings(
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
            int AimFireGracePeriodMs = DefaultAimAssistFireGracePeriodMs,
            int AimTrackingBlendPercent = DefaultAimTargetTrackingBlendPercent,
            int AimCloseRangeSlowdownPixels = DefaultAimCloseRangeSlowdownPixels,
            int AimMoveCooldownMs = DefaultAimMoveCooldownMs,
            int AimFeedbackFrameDelay = DefaultAimFeedbackFrameDelay,
            int AimInitialAcquireDistancePixels = DefaultAimInitialAcquireDistancePixels,
            int AimTrackedAcquireDistancePixels = DefaultAimTrackedAcquireDistancePixels,
            int AimStopLockSquareSizePixels = DefaultAimStopLockSquareSizePixels,
            int AimStopLockTopOffsetPixels = DefaultAimStopLockTopOffsetPixels,
            string InferenceBackend = nameof(DetectorBackend.OnnxRuntimeDirectMl),
            ColorDetectionOptions? PrimaryColorDetection = null);

        private sealed record TargetCandidate(DetectionResult Detection, PointF TargetPoint, double DistanceSquared);
        #endregion
    }

    internal sealed record OverlayTrack(DetectionResult Detection, long Timestamp);

    internal class OverlayForm : Form
    {
        private readonly System.Windows.Forms.Timer selectionTimer;
        private readonly LowLevelMouseProc mouseProc;
        private IntPtr hoveredHandle = IntPtr.Zero;
        private IntPtr mouseHook = IntPtr.Zero;
        private bool selectionCompleted;
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = GetVirtualScreenBounds();
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            mouseProc = MouseHookCallback;
            selectionTimer = new System.Windows.Forms.Timer { Interval = 50 };
            selectionTimer.Tick += SelectionTimer_Tick;

            Shown += OverlayForm_Shown;
            this.KeyDown += OverlayForm_KeyDown;
        }

        private void OverlayForm_Shown(object? sender, EventArgs e)
        {
            Activate();
            UpdateHoveredWindow();
            InstallMouseHook();
            selectionTimer.Start();
        }

        private void OverlayForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                CancelSelection();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                ConfirmSelectionAtCursor();
            }
        }

        private void SelectionTimer_Tick(object? sender, EventArgs e)
        {
            UpdateHoveredWindow();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_TOOLWINDOW = 0x00000080;

                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;

            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            selectionTimer.Stop();
            UninstallMouseHook();
            base.OnFormClosed(e);
        }

        private void ConfirmSelectionAtCursor()
        {
            if (selectionCompleted)
            {
                return;
            }

            selectionCompleted = true;
            selectionTimer.Stop();
            hoveredHandle = FindWindowFromPoint(Cursor.Position, Handle);
            if (hoveredHandle != IntPtr.Zero)
            {
                SelectedHandle = hoveredHandle;
                DialogResult = DialogResult.OK;
            }
            else
            {
                DialogResult = DialogResult.Cancel;
            }

            Close();
        }

        private void CancelSelection()
        {
            if (selectionCompleted)
            {
                return;
            }

            selectionCompleted = true;
            selectionTimer.Stop();
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void InstallMouseHook()
        {
            if (mouseHook != IntPtr.Zero)
            {
                return;
            }

            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, IntPtr.Zero, 0);
        }

        private void UninstallMouseHook()
        {
            if (mouseHook == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !selectionCompleted && wParam == (IntPtr)WM_LBUTTONUP)
            {
                BeginInvoke(ConfirmSelectionAtCursor);
            }

            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);

            if (hoveredHandle != IntPtr.Zero && GetWindowRect(hoveredHandle, out var r))
            {
                var rect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                using var pen = new Pen(Color.Red, 3);
                e.Graphics.DrawRectangle(pen, RectangleToClient(rect));
            }
        }

        private static Rectangle GetVirtualScreenBounds()
        {
            int left = SystemInformation.VirtualScreen.Left;
            int top = SystemInformation.VirtualScreen.Top;
            int width = SystemInformation.VirtualScreen.Width;
            int height = SystemInformation.VirtualScreen.Height;
            return new Rectangle(left, top, width, height);
        }

        private static Rectangle RectangleToClient(Rectangle rect)
        {
            var clientOrigin = GetVirtualScreenBounds().Location;
            return new Rectangle(rect.Left - clientOrigin.X, rect.Top - clientOrigin.Y, rect.Width, rect.Height);
        }

        private void UpdateHoveredWindow()
        {
            var newHandle = FindWindowFromPoint(Cursor.Position, Handle);
            if (newHandle == hoveredHandle)
            {
                return;
            }

            hoveredHandle = newHandle;
            Invalidate();
        }

        private static IntPtr FindWindowFromPoint(Point point, IntPtr overlayHandle)
        {
            var hwnd = WindowFromPoint(point);
            if (hwnd == IntPtr.Zero || hwnd == overlayHandle)
            {
                return IntPtr.Zero;
            }

            var rootHwnd = GetAncestor(hwnd, GA_ROOT);
            if (rootHwnd != IntPtr.Zero && rootHwnd != overlayHandle)
            {
                hwnd = rootHwnd;
            }

            return IsWindowVisible(hwnd) ? hwnd : IntPtr.Zero;
        }

        #region WinAPI
        private const uint GA_ROOT = 2;
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONUP = 0x0202;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        #endregion
    }

    internal sealed class DetectionOverlayForm : Form
    {
        private IntPtr targetHandle = IntPtr.Zero;
        private Rectangle captureBounds = Rectangle.Empty;
        private IReadOnlyList<DetectionResult> detections = Array.Empty<DetectionResult>();
        private DetectionResult? lockedDetection;
        private PointF? aimPoint;
        private Point cursorPoint = Point.Empty;
        private float stopSquareSizePixels;
        private float stopSquareTopOffsetPixels;

        public DetectionOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            TopMost = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCaptureExclusion();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;

                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;

            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }

        public void UpdateDetections(IntPtr hwnd, Rectangle newCaptureBounds, IReadOnlyList<DetectionResult> newDetections, DetectionResult? newLockedDetection, PointF? newAimPoint, Point newCursorPoint, float newStopSquareSizePixels, float newStopSquareTopOffsetPixels)
        {
            if (hwnd == IntPtr.Zero ||
                !GetWindowRect(hwnd, out var rect) ||
                !IsWindowVisible(hwnd) ||
                IsIconic(hwnd))
            {
                HideOverlay();
                return;
            }

            targetHandle = hwnd;
            captureBounds = newCaptureBounds;
            detections = newDetections;
            lockedDetection = newLockedDetection;
            aimPoint = newAimPoint;
            cursorPoint = newCursorPoint;
            stopSquareSizePixels = newStopSquareSizePixels;
            stopSquareTopOffsetPixels = newStopSquareTopOffsetPixels;

            Rectangle overlayBounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            bool boundsChanged = Bounds != overlayBounds;
            if (boundsChanged)
            {
                Bounds = overlayBounds;
            }

            bool wasHidden = !Visible;
            if (wasHidden)
            {
                Show();
                ApplyCaptureExclusion();
            }

            if (boundsChanged || wasHidden)
            {
                SetWindowPos(Handle, HWND_TOPMOST, overlayBounds.Left, overlayBounds.Top, overlayBounds.Width, overlayBounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            Invalidate();
        }

        public void HideOverlay()
        {
            targetHandle = IntPtr.Zero;
            captureBounds = Rectangle.Empty;
            detections = Array.Empty<DetectionResult>();
            lockedDetection = null;
            aimPoint = null;
            cursorPoint = Point.Empty;

            if (Visible)
            {
                Hide();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);

            if (targetHandle == IntPtr.Zero || captureBounds.IsEmpty || detections.Count == 0 || !GetWindowRect(targetHandle, out var rect))
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.Lime, 2f);
            using var labelBackground = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            using var textBrush = new SolidBrush(Color.Yellow);
            using var stopAreaPen = new Pen(Color.Orange, 2f) { DashStyle = DashStyle.Dash };
            using var aimPointBrush = new SolidBrush(Color.Cyan);
            using var cursorPen = new Pen(Color.DeepSkyBlue, 1.5f);

            float offsetX = captureBounds.Left - rect.Left;
            float offsetY = captureBounds.Top - rect.Top;

            foreach (DetectionResult detection in detections)
            {
                float boxX = offsetX + detection.Box.X;
                float boxY = offsetY + detection.Box.Y;
                e.Graphics.DrawRectangle(pen, boxX, boxY, detection.Box.Width, detection.Box.Height);

                string text = $"{detection.Label} {detection.Score:P0}";
                SizeF textSize = e.Graphics.MeasureString(text, Font);
                float labelY = Math.Max(0, boxY - textSize.Height);
                e.Graphics.FillRectangle(labelBackground, boxX, labelY, textSize.Width + 6, textSize.Height + 2);
                e.Graphics.DrawString(text, Font, textBrush, boxX + 3, labelY + 1);
            }

            if (lockedDetection is not null)
            {
                RectangleF lockSquare = GetLockSquareBounds(GetDetectionOverlayBounds(lockedDetection, offsetX, offsetY), stopSquareSizePixels, stopSquareTopOffsetPixels);
                e.Graphics.DrawRectangle(stopAreaPen, lockSquare.X, lockSquare.Y, lockSquare.Width, lockSquare.Height);
            }

            if (aimPoint is not null)
            {
                float aimX = aimPoint.Value.X - rect.Left;
                float aimY = aimPoint.Value.Y - rect.Top;
                const float radius = 4f;
                e.Graphics.FillEllipse(aimPointBrush, aimX - radius, aimY - radius, radius * 2f, radius * 2f);
            }

            if (!cursorPoint.IsEmpty)
            {
                float cursorX = cursorPoint.X - rect.Left;
                float cursorY = cursorPoint.Y - rect.Top;
                const float cursorHalfSize = 6f;
                e.Graphics.DrawLine(cursorPen, cursorX - cursorHalfSize, cursorY, cursorX + cursorHalfSize, cursorY);
                e.Graphics.DrawLine(cursorPen, cursorX, cursorY - cursorHalfSize, cursorX, cursorY + cursorHalfSize);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        private void ApplyCaptureExclusion()
        {
            if (Handle == IntPtr.Zero)
            {
                return;
            }

            if (!SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE))
            {
                SetWindowDisplayAffinity(Handle, WDA_MONITOR);
            }
        }

        private static RectangleF GetDetectionOverlayBounds(DetectionResult detection, float offsetX, float offsetY)
        {
            return new RectangleF(offsetX + detection.Box.X, offsetY + detection.Box.Y, detection.Box.Width, detection.Box.Height);
        }

        private static RectangleF GetLockSquareBounds(RectangleF bounds, float squareSizePixels, float topOffsetPixels)
        {
            float squareSize = Math.Clamp(squareSizePixels, 8f, Math.Max(8f, Math.Min(bounds.Width, bounds.Height)));
            float left = bounds.Left + ((bounds.Width - squareSize) / 2f);
            float top = bounds.Top + Math.Clamp(topOffsetPixels, 0f, Math.Max(0f, bounds.Height - squareSize));
            return new RectangleF(left, top, squareSize, squareSize);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        private const uint WDA_MONITOR = 0x00000001;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
