using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YOLOForAim
{
    public partial class Form1 : Form
    {
        private IntPtr selectedHwnd = IntPtr.Zero;

        public Form1()
        {
            InitializeComponent();
        }

        private void btnSelectWindow_Click(object? sender, EventArgs e)
        {
            using var overlay = new OverlayForm();
            if (overlay.ShowDialog(this) == DialogResult.OK)
            {
                selectedHwnd = overlay.SelectedHandle;
                lblHandle.Text = $"选中窗口句柄: {selectedHwnd}";
            }
        }
    }

    internal class OverlayForm : Form
    {
        private Point start;
        private Point end;
        private bool dragging = false;
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = GetVirtualScreenBounds();
            BackColor = Color.Black;
            Opacity = 0.01; // almost transparent
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;

            this.MouseDown += OverlayForm_MouseDown;
            this.MouseMove += OverlayForm_MouseMove;
            this.MouseUp += OverlayForm_MouseUp;
            this.KeyDown += OverlayForm_KeyDown;
        }

        private void OverlayForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void OverlayForm_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                start = PointToScreen(e.Location);
                end = start;
                Invalidate();
            }
        }

        private void OverlayForm_MouseMove(object? sender, MouseEventArgs e)
        {
            if (dragging)
            {
                end = PointToScreen(e.Location);
                Invalidate();
            }
            else
            {
                // highlight window under cursor
                Invalidate();
            }
        }

        private void OverlayForm_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragging)
            {
                dragging = false;
                var rect = GetRectangle(start, end);
                SelectedHandle = FindWindowFromRectangle(rect);
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (dragging)
            {
                var rect = GetRectangle(start, end);
                using var pen = new Pen(Color.Red, 3);
                e.Graphics.DrawRectangle(pen, RectangleToClient(rect));
            }
            else
            {
                var p = Cursor.Position;
                var hwnd = WindowFromPoint(p);
                if (hwnd != IntPtr.Zero)
                {
                    if (GetWindowRect(hwnd, out var r))
                    {
                        var rect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                        using var pen = new Pen(Color.Red, 3);
                        e.Graphics.DrawRectangle(pen, RectangleToClient(rect));
                    }
                }
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

        private static Rectangle GetRectangle(Point a, Point b)
        {
            int x1 = Math.Min(a.X, b.X);
            int y1 = Math.Min(a.Y, b.Y);
            int x2 = Math.Max(a.X, b.X);
            int y2 = Math.Max(a.Y, b.Y);
            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        private static Rectangle RectangleToClient(Rectangle rect)
        {
            var clientOrigin = GetVirtualScreenBounds().Location;
            return new Rectangle(rect.Left - clientOrigin.X, rect.Top - clientOrigin.Y, rect.Width, rect.Height);
        }

        private static IntPtr FindWindowFromRectangle(Rectangle rect)
        {
            IntPtr found = IntPtr.Zero;
            IntPtr hwnd = GetTopWindow(IntPtr.Zero);
            while (hwnd != IntPtr.Zero)
            {
                if (IsWindowVisible(hwnd))
                {
                    if (GetWindowRect(hwnd, out var r))
                    {
                        var wrect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                        if (rect.IntersectsWith(wrect))
                        {
                            found = hwnd;
                            break;
                        }
                    }
                }
                hwnd = GetNextWindow(hwnd, GW_HWNDNEXT);
            }
            return found;
        }

        #region WinAPI
        private const uint GW_HWNDNEXT = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point p);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetNextWindow(IntPtr hWnd, uint wCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

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
