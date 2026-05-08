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
        private const float AimPendingCompensationDecay = 0.45f;
        private const float AimPendingCompensationFactor = 0.75f;
        private static readonly string UiSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "ui-settings.json");

        private IntPtr selectedHwnd = IntPtr.Zero;
        private bool hotKeyRegistered;
        private readonly string modelPath = Path.Combine(AppContext.BaseDirectory, "exp.onnx");
        private CancellationTokenSource? detectionCancellationTokenSource;
        private Task? captureTask;
        private Task? inferenceTask;
        private YoloDetector? yoloDetector;
        private int processedFrameCounter;
        private double currentInferenceFps;
        private double inferenceFpsAccumulatedMs;
        private int inferenceFpsFrameCounter;
        private readonly Stopwatch inferenceFpsUiStopwatch = new();
        private readonly object latestFrameLock = new();
        private readonly object overlayStateLock = new();
        private DesktopDuplicationCapture? windowCapture;
        private CapturedPixelFrame? latestCapturedFrame;
        private int latestCapturedFrameVersion;
        private Rectangle latestOverlayCaptureBounds;
        private DetectionResult[] latestOverlayDetections = Array.Empty<DetectionResult>();
        private readonly System.Windows.Forms.Timer overlayRefreshTimer;
        private DetectionOverlayForm? detectionOverlay;
        private bool currentCenterRoiOnly;
        private int currentRoiSize;
        private int currentPreviewInterval;
        private float currentAimPointHeightRatio;
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
        private PointF? lockedTargetScreenPoint;
        private PointF? smoothedTargetScreenPoint;
        private int missedTargetFrames;
        private long lastFireActivityTick;
        private bool wasLeftMouseButtonDown;
        private long lastAimMoveTick;
        private int lastAimMoveFrameVersion = -1;
        private int lastPendingCompensationFrameVersion = -1;
        private PointF pendingAimCompensation;

        public Form1()
        {
            InitializeComponent();
            overlayRefreshTimer = new System.Windows.Forms.Timer { Interval = 33 };
            overlayRefreshTimer.Tick += OverlayRefreshTimer_Tick;
            pictureBoxPreview.Visible = false;
            lblStatus.Text = "请选择目标窗口。";
            txtDiagnostics.Text = "YOLO FPS: 0.0";
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
            numScoreThreshold.Value = 35;
            LoadUiSettings();
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

            if (!File.Exists(modelPath))
            {
                MessageBox.Show($"未找到模型文件: {modelPath}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                yoloDetector?.Dispose();
                yoloDetector = new YoloDetector(modelPath, new DetectorOptions(chkPreferGpu.Checked, (float)numScoreThreshold.Value / 100f));
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
            currentAimPointHeightRatio = (float)numAimHeightPercent.Value / 100f;
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
            txtDiagnostics.Text = "YOLO FPS: 0.0";
            ClearOverlayState();
            EnsureDetectionOverlay();
            overlayRefreshTimer.Start();
            ShowWindowAsync(selectedHwnd, SW_RESTORE);
            SetForegroundWindow(selectedHwnd);
            captureTask = Task.Run(() => RunCaptureLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);
            inferenceTask = Task.Run(() => RunInferenceLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);

            btnStartDetection.Enabled = false;
            btnStopDetection.Enabled = true;
            lblStatus.Text = $"检测中... ROI={(currentCenterRoiOnly ? $"中心 {currentRoiSize}" : "全窗口")}, 已关闭内置预览";
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
            yoloDetector?.Dispose();
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
                overlayRefreshTimer.Stop();
                ClearOverlayState();
                detectionOverlay?.HideOverlay();
                ResetAimRuntimeState();
                currentInferenceFps = 0;
                inferenceFpsAccumulatedMs = 0;
                inferenceFpsFrameCounter = 0;
                inferenceFpsUiStopwatch.Reset();
                btnStartDetection.Enabled = true;
                btnStopDetection.Enabled = false;
                lblStatus.Text = "检测已停止。";
                txtDiagnostics.Text = "YOLO FPS: 0.0";
            }
        }

        private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (windowCapture is null || !windowCapture.TryGetLatestFrame(50, out CapturedPixelFrame capturedFrame))
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
                        await Task.Delay(5, cancellationToken);
                        continue;
                    }

                    if (frameToProcess is not null)
                    {
                        Rectangle sourceRegion = GetSourceRegion(frameToProcess.Width, frameToProcess.Height);
                        var detectStopwatch = Stopwatch.StartNew();
                        DetectionRunResult result = yoloDetector?.Detect(
                            frameToProcess.Pixels,
                            frameToProcess.Width,
                            frameToProcess.Height,
                            frameToProcess.Stride,
                            sourceRegion) ?? new DetectionRunResult(Array.Empty<DetectionResult>());
                        detectStopwatch.Stop();
                        TryMoveMouseToNearestDetection(result.Detections, frameToProcess.ScreenBounds, processedVersion);
                        UpdateOverlayState(frameToProcess.ScreenBounds, result.Detections);
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

            lblStatus.Text = $"检测中，目标数: {detectionCount}";
            txtDiagnostics.Text = $"YOLO FPS: {currentInferenceFps:F1}";
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
            lock (overlayStateLock)
            {
                latestOverlayCaptureBounds = captureBounds;
                latestOverlayDetections = detections.Count == 0 ? Array.Empty<DetectionResult>() : detections.ToArray();
            }
        }

        private void ClearOverlayState()
        {
            lock (overlayStateLock)
            {
                latestOverlayCaptureBounds = Rectangle.Empty;
                latestOverlayDetections = Array.Empty<DetectionResult>();
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
            lock (overlayStateLock)
            {
                captureBounds = latestOverlayCaptureBounds;
                detections = latestOverlayDetections;
            }

            detectionOverlay?.UpdateDetections(selectedHwnd, captureBounds, detections);
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

        private void TryMoveMouseToNearestDetection(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, int processedFrameVersion)
        {
            UpdatePendingAimCompensation(processedFrameVersion);

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

            Point cursorPosition = Cursor.Position;
            PointF? previousLockedTargetScreenPoint = lockedTargetScreenPoint;
            TargetCandidate? nearestDetection = SelectTargetCandidate(detections, captureBounds, cursorPosition);

            if (nearestDetection is null)
            {
                return;
            }

            float distanceFromLockedTarget = MathF.Sqrt((float)nearestDetection.DistanceSquared);
            if (lockedTargetScreenPoint is not null && distanceFromLockedTarget > currentAimLockSwitchDistancePixels)
            {
                missedTargetFrames++;
                if (missedTargetFrames < currentAimMaxMissedFrames)
                {
                    return;
                }
            }

            lockedTargetScreenPoint = nearestDetection.TargetPoint;
            missedTargetFrames = 0;

            if (previousLockedTargetScreenPoint is null ||
                smoothedTargetScreenPoint is null ||
                GetDistanceSquared(previousLockedTargetScreenPoint.Value, nearestDetection.TargetPoint) >
                (currentAimLockSwitchDistancePixels * currentAimLockSwitchDistancePixels))
            {
                smoothedTargetScreenPoint = nearestDetection.TargetPoint;
            }
            else
            {
                smoothedTargetScreenPoint = LerpPoint(smoothedTargetScreenPoint.Value, nearestDetection.TargetPoint, currentAimTargetTrackingBlend);
            }

            PointF targetPointForMove = smoothedTargetScreenPoint.Value;
            float rawMoveX = (targetPointForMove.X - cursorPosition.X) - pendingAimCompensation.X;
            float rawMoveY = (targetPointForMove.Y - cursorPosition.Y) - pendingAimCompensation.Y;
            float distanceToAimPoint = MathF.Sqrt((rawMoveX * rawMoveX) + (rawMoveY * rawMoveY));
            if (distanceToAimPoint <= currentAimDeadzonePixels)
            {
                return;
            }

            long now = Environment.TickCount64;
            if (!CanSendAimMove(now, processedFrameVersion))
            {
                return;
            }

            float moveX = rawMoveX * currentAimSmoothingFactor * currentAimSpeedMultiplier;
            float moveY = rawMoveY * currentAimSmoothingFactor * currentAimSpeedMultiplier;
            float distanceScale = Math.Clamp((distanceToAimPoint - currentAimDeadzonePixels) / currentAimCloseRangeSlowdownPixels, 0.2f, 1f);
            moveX *= distanceScale;
            moveY *= distanceScale;
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
            pendingAimCompensation = new PointF(
                pendingAimCompensation.X + (finalMoveX * AimPendingCompensationFactor),
                pendingAimCompensation.Y + (finalMoveY * AimPendingCompensationFactor));
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
        }

        private void ResetAimTrackingState()
        {
            lockedTargetScreenPoint = null;
            smoothedTargetScreenPoint = null;
            missedTargetFrames = 0;
        }

        private void UpdatePendingAimCompensation(int processedFrameVersion)
        {
            if (processedFrameVersion < 0 || lastPendingCompensationFrameVersion == processedFrameVersion)
            {
                return;
            }

            if (lastPendingCompensationFrameVersion >= 0)
            {
                int frameDelta = Math.Max(1, processedFrameVersion - lastPendingCompensationFrameVersion);
                for (int i = 0; i < frameDelta; i++)
                {
                    pendingAimCompensation = new PointF(
                        pendingAimCompensation.X * AimPendingCompensationDecay,
                        pendingAimCompensation.Y * AimPendingCompensationDecay);
                }

                if (MathF.Abs(pendingAimCompensation.X) < 0.25f && MathF.Abs(pendingAimCompensation.Y) < 0.25f)
                {
                    pendingAimCompensation = PointF.Empty;
                }
            }

            lastPendingCompensationFrameVersion = processedFrameVersion;
        }

        private bool CanSendAimMove(long now, int processedFrameVersion)
        {
            if (lastAimMoveTick > 0 && (now - lastAimMoveTick) < currentAimMoveCooldownMs)
            {
                return false;
            }

            return lastAimMoveFrameVersion < 0 ||
                (processedFrameVersion - lastAimMoveFrameVersion) >= currentAimFeedbackFrameDelay;
        }

        private TargetCandidate? SelectTargetCandidate(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, Point cursorPosition)
        {
            PointF? lockedAnchor = smoothedTargetScreenPoint ?? lockedTargetScreenPoint;
            if (lockedAnchor is not null)
            {
                TargetCandidate? containingCandidate = FindContainingTargetCandidate(detections, captureBounds, lockedAnchor.Value);
                if (containingCandidate is not null)
                {
                    return containingCandidate;
                }

                return FindNearestTargetCandidate(detections, captureBounds, lockedAnchor.Value);
            }

            return FindNearestTargetCandidate(detections, captureBounds, cursorPosition);
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

        private TargetCandidate? FindNearestTargetCandidate(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, PointF referencePoint)
        {
            TargetCandidate? bestCandidate = null;

            foreach (DetectionResult detection in detections)
            {
                PointF targetPoint = GetAimPoint(captureBounds, detection);
                double distanceSquared = GetDistanceSquared(referencePoint, targetPoint);
                if (bestCandidate is null || distanceSquared < bestCandidate.DistanceSquared)
                {
                    bestCandidate = new TargetCandidate(detection, targetPoint, distanceSquared);
                }
            }

            return bestCandidate;
        }

        private static RectangleF GetExpandedDetectionBounds(Rectangle captureBounds, DetectionResult detection, float padding)
        {
            return new RectangleF(
                captureBounds.Left + detection.Box.X - padding,
                captureBounds.Top + detection.Box.Y - padding,
                detection.Box.Width + (padding * 2f),
                detection.Box.Height + (padding * 2f));
        }

        private PointF GetAimPoint(Rectangle captureBounds, DetectionResult detection)
        {
            return new PointF(
                captureBounds.Left + detection.Box.X + (detection.Box.Width / 2f),
                captureBounds.Top + detection.Box.Y + (detection.Box.Height * currentAimPointHeightRatio));
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

        private static PointF LerpPoint(PointF from, PointF to, float amount)
        {
            return new PointF(
                from.X + ((to.X - from.X) * amount),
                from.Y + ((to.Y - from.Y) * amount));
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
                    (int)numAimFeedbackFrameDelay.Value);

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
            int AimFeedbackFrameDelay = DefaultAimFeedbackFrameDelay);

        private sealed record TargetCandidate(DetectionResult Detection, PointF TargetPoint, double DistanceSquared);
        #endregion
    }

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

            selectionTimer = new System.Windows.Forms.Timer { Interval = 16 };
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

        public DetectionOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            TopMost = true;
            DoubleBuffered = true;
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

        public void UpdateDetections(IntPtr hwnd, Rectangle newCaptureBounds, IReadOnlyList<DetectionResult> newDetections)
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

            Rectangle overlayBounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (Bounds != overlayBounds)
            {
                Bounds = overlayBounds;
            }

            if (!Visible)
            {
                Show();
            }

            SetWindowPos(Handle, HWND_TOPMOST, overlayBounds.Left, overlayBounds.Top, overlayBounds.Width, overlayBounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

            Invalidate();
        }

        public void HideOverlay()
        {
            targetHandle = IntPtr.Zero;
            captureBounds = Rectangle.Empty;
            detections = Array.Empty<DetectionResult>();

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
