using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YOLOForAim
{
    public partial class Form1 : Form
    {
        private const int HotKeyIdSendMouseUp = 1;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NONE = 0x0000;
        private const uint VK_Z = 0x5A;

        private IntPtr selectedHwnd = IntPtr.Zero;
        private bool hotKeyRegistered;
        private readonly string modelPath = Path.Combine(AppContext.BaseDirectory, "dawn.onnx");
        private CancellationTokenSource? detectionCancellationTokenSource;
        private Task? captureTask;
        private Task? inferenceTask;
        private YoloDetector? yoloDetector;
        private int diagnosticsRefreshCounter;
        private int processedFrameCounter;
        private readonly object latestFrameLock = new();
        private Bitmap? latestCapturedFrame;
        private int latestCapturedFrameVersion;
        private bool currentCenterRoiOnly;
        private int currentRoiSize;
        private int currentPreviewInterval;

        public Form1()
        {
            InitializeComponent();
            lblStatus.Text = $"模型路径: {modelPath}";
            txtDiagnostics.Text = $"模型路径: {modelPath}";
            numRoiSize.Value = 640;
            numPreviewInterval.Value = 3;
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
            SendMouseMoveUp();
        }

        private void btnStartDetection_Click(object? sender, EventArgs e)
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
                yoloDetector = new YoloDetector(modelPath, new DetectorOptions(chkPreferGpu.Checked));
                txtDiagnostics.Text = yoloDetector.ModelSummary;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"模型加载失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            detectionCancellationTokenSource = new CancellationTokenSource();
            diagnosticsRefreshCounter = 0;
            processedFrameCounter = 0;
            currentCenterRoiOnly = chkCenterRoi.Checked;
            currentRoiSize = (int)numRoiSize.Value;
            currentPreviewInterval = Math.Max(1, (int)numPreviewInterval.Value);
            captureTask = Task.Run(() => RunCaptureLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);
            inferenceTask = Task.Run(() => RunInferenceLoopAsync(detectionCancellationTokenSource.Token), detectionCancellationTokenSource.Token);

            btnStartDetection.Enabled = false;
            btnStopDetection.Enabled = true;
            lblStatus.Text = $"检测中... ROI={(currentCenterRoiOnly ? $"中心 {currentRoiSize}" : "全窗口")}, 预览每 {currentPreviewInterval} 帧刷新";
        }

        private async void btnStopDetection_Click(object? sender, EventArgs e)
        {
            await StopDetectionAsync();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            hotKeyRegistered = RegisterHotKey(Handle, HotKeyIdSendMouseUp, MOD_NONE, VK_Z);
            if (!hotKeyRegistered)
            {
                MessageBox.Show("全局快捷键 Z 注册失败。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotKeyRegistered)
            {
                UnregisterHotKey(Handle, HotKeyIdSendMouseUp);
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
                captureTask?.Wait(500);
                inferenceTask?.Wait(500);
            }
            catch
            {
            }

            lock (latestFrameLock)
            {
                latestCapturedFrame?.Dispose();
                latestCapturedFrame = null;
            }

            pictureBoxPreview.Image?.Dispose();
            yoloDetector?.Dispose();
            detectionCancellationTokenSource?.Dispose();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam == (IntPtr)HotKeyIdSendMouseUp)
            {
                SendMouseMoveUp();
                return;
            }

            base.WndProc(ref m);
        }

        private void SendMouseMoveUp()
        {
            if (selectedHwnd == IntPtr.Zero)
            {
                MessageBox.Show("请先选择窗口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ShowWindowAsync(selectedHwnd, SW_RESTORE);
            SetForegroundWindow(selectedHwnd);

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = -100,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            if (sent == 0)
            {
                MessageBox.Show("发送输入失败。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                    latestCapturedFrame?.Dispose();
                    latestCapturedFrame = null;
                    latestCapturedFrameVersion = 0;
                }
                btnStartDetection.Enabled = true;
                btnStopDetection.Enabled = false;
                lblStatus.Text = "检测已停止。";
            }
        }

        private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Bitmap? frame = CaptureWindow(selectedHwnd, currentCenterRoiOnly, currentRoiSize);
                    if (frame is null)
                    {
                        await Task.Delay(30, cancellationToken);
                        continue;
                    }

                    lock (latestFrameLock)
                    {
                        latestCapturedFrame?.Dispose();
                        latestCapturedFrame = frame;
                        latestCapturedFrameVersion++;
                    }

                    await Task.Delay(5, cancellationToken);
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
                    Bitmap? frameToProcess = null;
                    int currentVersion;
                    lock (latestFrameLock)
                    {
                        currentVersion = latestCapturedFrameVersion;
                        if (latestCapturedFrame is not null && currentVersion != processedVersion)
                        {
                            frameToProcess = (Bitmap)latestCapturedFrame.Clone();
                            processedVersion = currentVersion;
                        }
                    }

                    if (frameToProcess is null)
                    {
                        await Task.Delay(5, cancellationToken);
                        continue;
                    }

                    using (frameToProcess)
                    {
                        DetectionRunResult result = yoloDetector?.Detect(frameToProcess) ?? new DetectionRunResult(Array.Empty<DetectionResult>(), "检测器未初始化。");
                        processedFrameCounter++;
                        diagnosticsRefreshCounter++;

                        bool refreshPreview = processedFrameCounter % currentPreviewInterval == 0;
                        string? diagnostics = diagnosticsRefreshCounter % 15 == 0 ? result.DebugSummary : null;

                        Bitmap? previewFrame = null;
                        if (refreshPreview)
                        {
                            previewFrame = (Bitmap)frameToProcess.Clone();
                            DrawDetections(previewFrame, result.Detections);
                        }

                        if (!IsDisposed && (previewFrame is not null || diagnostics is not null))
                        {
                            BeginInvoke(new Action(() => UpdatePreviewImage(previewFrame, result.Detections.Count, diagnostics)));
                        }
                        else
                        {
                            previewFrame?.Dispose();
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

        private void UpdatePreviewImage(Bitmap? previewFrame, int detectionCount, string? diagnostics)
        {
            if (previewFrame is not null)
            {
                var previousImage = pictureBoxPreview.Image;
                pictureBoxPreview.Image = previewFrame;
                previousImage?.Dispose();
            }

            lblStatus.Text = $"检测中，目标数: {detectionCount}";

            if (!string.IsNullOrWhiteSpace(diagnostics))
            {
                txtDiagnostics.Text = diagnostics;
            }
        }

        private static Bitmap? CaptureWindow(IntPtr hwnd, bool centerRoiOnly, int roiSize)
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
            return bitmap;
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
}
