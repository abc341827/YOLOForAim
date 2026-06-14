using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YOLOForAim;

/// <summary>
/// 绘制实时检测框、锁定区域、瞄准点和鼠标参考点的非激活透明覆盖层。
/// 该窗体不参与检测和瞄准逻辑，只负责显示。
/// </summary>
internal sealed class DetectionOverlayForm : Form
{
    private IntPtr targetHandle = IntPtr.Zero;
    private Rectangle captureBounds = Rectangle.Empty;
    private IReadOnlyList<DetectionResult> displayDetections = Array.Empty<DetectionResult>();
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

    public void UpdateDetections(IntPtr hwnd, Rectangle newCaptureBounds, IReadOnlyList<DetectionResult> newDisplayDetections, IReadOnlyList<DetectionResult> newDetections, DetectionResult? newLockedDetection, PointF? newAimPoint, Point newCursorPoint, float newStopSquareSizePixels, float newStopSquareTopOffsetPixels)
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
        displayDetections = newDisplayDetections;
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
            if (!ApplyCaptureExclusion())
            {
                HideOverlay();
                return;
            }
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
        displayDetections = Array.Empty<DetectionResult>();
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

        if (targetHandle == IntPtr.Zero || captureBounds.IsEmpty || (displayDetections.Count == 0 && detections.Count == 0) || !GetWindowRect(targetHandle, out var rect))
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var yoloPen = new Pen(Color.Red, 2f);
        using var pen = new Pen(Color.Lime, 2f);
        using var labelBackground = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
        using var textBrush = new SolidBrush(Color.Yellow);
        using var stopAreaPen = new Pen(Color.Orange, 2f) { DashStyle = DashStyle.Dash };
        using var aimPointBrush = new SolidBrush(Color.Cyan);
        using var cursorPen = new Pen(Color.DeepSkyBlue, 1.5f);

        float offsetX = captureBounds.Left - rect.Left;
        float offsetY = captureBounds.Top - rect.Top;

        foreach (DetectionResult detection in displayDetections)
        {
            if (detection.Label.StartsWith("Color", StringComparison.Ordinal))
            {
                continue;
            }

            float boxX = offsetX + detection.Box.X;
            float boxY = offsetY + detection.Box.Y;
            e.Graphics.DrawRectangle(yoloPen, boxX, boxY, detection.Box.Width, detection.Box.Height);
        }

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

        _ = lockedDetection;
        _ = aimPoint;
        _ = cursorPoint;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    private bool ApplyCaptureExclusion()
    {
        if (Handle == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
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
