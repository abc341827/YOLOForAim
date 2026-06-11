using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YOLOForAim;

/// <summary>
/// 用于选择目标窗口的全屏透明遮罩窗体。
/// 鼠标悬停时高亮窗口，点击/回车确认，Esc 取消。
/// </summary>
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
        KeyDown += OverlayForm_KeyDown;
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
            e.Graphics.DrawRectangle(pen, RectangleToOverlayClient(rect));
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

    private static Rectangle RectangleToOverlayClient(Rectangle rect)
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
}
