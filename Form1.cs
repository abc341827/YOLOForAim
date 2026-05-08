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

        private void btnSendMouseUp_Click(object? sender, EventArgs e)
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

        #region WinAPI
        private const int SW_RESTORE = 9;
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
        #endregion
    }

    internal class OverlayForm : Form
    {
        private readonly System.Windows.Forms.Timer selectionTimer;
        private IntPtr hoveredHandle = IntPtr.Zero;
        private bool leftButtonWasDown;
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

            selectionTimer = new System.Windows.Forms.Timer { Interval = 16 };
            selectionTimer.Tick += SelectionTimer_Tick;

            Shown += OverlayForm_Shown;
            this.KeyDown += OverlayForm_KeyDown;
        }

        private void OverlayForm_Shown(object? sender, EventArgs e)
        {
            Activate();
            leftButtonWasDown = IsLeftButtonDown();
            UpdateHoveredWindow();
            selectionTimer.Start();
        }

        private void OverlayForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                selectionTimer.Stop();
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void SelectionTimer_Tick(object? sender, EventArgs e)
        {
            UpdateHoveredWindow();

            bool isLeftButtonDown = IsLeftButtonDown();
            if (isLeftButtonDown)
            {
                leftButtonWasDown = true;
                return;
            }

            if (!leftButtonWasDown)
            {
                return;
            }

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

        private static bool IsLeftButtonDown()
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        }

        private static IntPtr FindWindowFromPoint(Point point, IntPtr overlayHandle)
        {
            IntPtr hwnd = GetTopWindow(IntPtr.Zero);
            while (hwnd != IntPtr.Zero)
            {
                if (hwnd != overlayHandle && IsWindowVisible(hwnd))
                {
                    if (GetWindowRect(hwnd, out var r))
                    {
                        var wrect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                        if (wrect.Contains(point))
                        {
                            return hwnd;
                        }
                    }
                }
                hwnd = GetWindow(hwnd, GW_HWNDNEXT);
            }

            return IntPtr.Zero;
        }

        #region WinAPI
        private const uint GW_HWNDNEXT = 2;
        private const int VK_LBUTTON = 0x01;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

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
