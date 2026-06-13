using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YOLOForAim;

public partial class Form1
{
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
        if (!DetectionStartupPlan.TryCreate(selectedBackend, out DetectionStartupPlan startupPlan, out string? startupErrorMessage))
        {
            MessageBox.Show(startupErrorMessage, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            detector?.Dispose();
            detector = DetectionBackendFactory.Create(
                startupPlan,
                chkPreferGpu.Checked,
                (float)numScoreThreshold.Value / 100f,
                currentPrimaryColorDetectionOptions);
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
        inferenceFpsCounter.Restart();
        ResetAimRuntimeState();
        currentCaptureSettings = ReadCaptureRuntimeSettingsFromUi();
        UpdateLiveAimRuntimeSettings();
        diagnosticsHeader = detector?.ModelSummary ?? string.Empty;
        UpdateDiagnosticsText();
        ClearOverlayState();
        EnsureDetectionOverlay();
        overlayRefreshTimer.Start();
        ShowWindowAsync(selectedHwnd, SW_RESTORE);
        SetForegroundWindow(selectedHwnd);
        captureTask = Task.Run(() => RunCaptureLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);
        inferenceTask = Task.Run(() => RunInferenceLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);
        aimControlTask = Task.Run(() => RunAimControlLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);

        btnStartDetection.Enabled = false;
        btnStopDetection.Enabled = true;
        lblStatus.Text = startupPlan.GetStartupStatusText(currentCaptureSettings);
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
                .Append(aimControlTask)
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
            aimControlTask = null;
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
            inferenceFpsCounter.Reset();
            diagnosticsHeader = string.Empty;
            btnStartDetection.Enabled = true;
            btnStopDetection.Enabled = false;
            lblStatus.Text = "检测已停止。";
            UpdateDiagnosticsText();
        }
    }

    private async Task RunAimControlLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                aimAssistController.TryRunControlTick(currentAimSettings);
                await Task.Delay(4, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (windowCapture is null || !windowCapture.TryGetLatestFrame(50, currentCaptureSettings.CenterRoiOnly, currentCaptureSettings.RoiSize, out CapturedPixelFrame capturedFrame))
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
                    int targetWindowWidth = Math.Max(frameToProcess.ReferenceWidth, frameToProcess.Width);
                    DetectionResult[] controlDetections = result.Detections
                        .Select(detection => AimPointCalculator.GetControlDetection(detection, currentAimSettings, targetWindowWidth))
                        .ToArray();
                    Point aimReferencePoint = new(
                        frameToProcess.WindowBounds.Left + (frameToProcess.WindowBounds.Width / 2),
                        frameToProcess.WindowBounds.Top + (frameToProcess.WindowBounds.Height / 2));
                    IReadOnlyList<DetectionResult> primaryDetections = primaryTargetTracker.SelectPrimaryTarget(controlDetections, frameToProcess.ScreenBounds, aimReferencePoint, currentAimSettings);
                    TryMoveMouseToNearestDetection(primaryDetections, frameToProcess.ScreenBounds, frameToProcess.WindowBounds, targetWindowWidth, processedVersion, frameToProcess.CapturedTick);
                    UpdateOverlayState(frameToProcess.ScreenBounds, frameToProcess.WindowBounds, primaryDetections, targetWindowWidth, processedVersion, frameToProcess.CapturedTick);
                    processedFrameCounter++;
                    inferenceFpsCounter.AddFrame(detectStopwatch.Elapsed.TotalMilliseconds);

                    int previewInterval = Math.Max(1, currentCaptureSettings.PreviewInterval);
                    bool refreshUi = processedFrameCounter % previewInterval == 0;
                    Bitmap? previewBitmap = refreshUi
                        ? CreateDetectionPreviewBitmap(frameToProcess, primaryDetections)
                        : null;

                    if (!IsDisposed && refreshUi)
                    {
                        BeginInvoke(new Action(() => UpdatePreviewImage(previewBitmap, primaryDetections.Count)));
                    }
                    else
                    {
                        previewBitmap?.Dispose();
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

        lblStatus.Text = $"检测中，目标数: {detectionCount}，检测 FPS: {inferenceFpsCounter.CurrentFps:F1}";
        UpdateDiagnosticsText();
    }

    private static Bitmap CreateDetectionPreviewBitmap(CapturedPixelFrame frame, IReadOnlyList<DetectionResult> detections)
    {
        Bitmap bitmap = new(frame.Width, frame.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        Rectangle bounds = new(0, 0, frame.Width, frame.Height);
        System.Drawing.Imaging.BitmapData bitmapData = bitmap.LockBits(bounds, System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            for (int y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Pixels, y * frame.Stride, bitmapData.Scan0 + (y * bitmapData.Stride), frame.Stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        DetectionPreviewRenderer.DrawDetections(bitmap, detections, SystemFonts.DefaultFont);
        return bitmap;
    }
}
