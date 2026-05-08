using System.Drawing;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using static Vortice.Direct3D11.D3D11;

namespace YOLOForAim;

internal sealed class WindowGraphicsCapture : IDisposable
{
    private const DirectXPixelFormat CapturePixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    private readonly IntPtr hwnd;
    private readonly object frameLock = new();
    private readonly AutoResetEvent frameArrivedEvent = new(false);
    private readonly ID3D11Device d3dDevice;
    private readonly ID3D11DeviceContext d3dContext;
    private readonly GraphicsCaptureItem captureItem;
    private readonly IDirect3DDevice winrtDevice;
    private Direct3D11CaptureFramePool? framePool;
    private GraphicsCaptureSession? captureSession;
    private Direct3D11CaptureFrame? latestFrame;
    private bool disposed;

    public WindowGraphicsCapture(IntPtr hwnd)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException("当前系统不支持 Windows Graphics Capture。");
        }

        this.hwnd = hwnd;
        D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null,
            out d3dDevice,
            out d3dContext).CheckError();

        winrtDevice = CreateDirect3DDevice(d3dDevice);
        captureItem = CreateCaptureItemForWindow(hwnd);
        framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, CapturePixelFormat, 2, captureItem.Size);
        framePool.FrameArrived += OnFrameArrived;
        captureSession = framePool.CreateCaptureSession(captureItem);
        captureSession.IsCursorCaptureEnabled = false;
        captureSession.StartCapture();
    }

    public bool TryGetLatestFrame(int timeoutMilliseconds, out CapturedPixelFrame capturedFrame)
    {
        capturedFrame = default;
        if (disposed)
        {
            return false;
        }

        if (!frameArrivedEvent.WaitOne(timeoutMilliseconds))
        {
            return false;
        }

        Direct3D11CaptureFrame? frame;
        lock (frameLock)
        {
            frame = latestFrame;
            latestFrame = null;
        }

        if (frame is null)
        {
            return false;
        }

        using (frame)
        {
            var contentSize = frame.ContentSize;
            if (contentSize.Width <= 0 || contentSize.Height <= 0)
            {
                return false;
            }

            using SoftwareBitmap softwareBitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            using BitmapBuffer bitmapBuffer = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Read);
            using var reference = bitmapBuffer.CreateReference();
            BitmapPlaneDescription planeDescription = bitmapBuffer.GetPlaneDescription(0);
            byte[] pixels = new byte[contentSize.Width * contentSize.Height * 4];

            unsafe
            {
                IMemoryBufferByteAccess byteAccess = GetMemoryBufferByteAccess(reference);
                byteAccess.GetBuffer(out byte* sourceBytes, out _);
                if (sourceBytes is null)
                {
                    return false;
                }

                sourceBytes += planeDescription.StartIndex;
                fixed (byte* destinationBytes = pixels)
                {
                    int destinationStride = contentSize.Width * 4;
                    for (int y = 0; y < contentSize.Height; y++)
                    {
                        byte* sourceRow = sourceBytes + (y * planeDescription.Stride);
                        byte* destinationRow = destinationBytes + (y * destinationStride);
                        Buffer.MemoryCopy(sourceRow, destinationRow, destinationStride, destinationStride);
                    }
                }
            }

            if (framePool is not null && (captureItem.Size.Width != contentSize.Width || captureItem.Size.Height != contentSize.Height))
            {
                framePool.Recreate(winrtDevice, CapturePixelFormat, 2, contentSize);
            }

            if (!GetWindowRect(hwnd, out RECT rect))
            {
                return false;
            }

            capturedFrame = new CapturedPixelFrame(
                pixels,
                contentSize.Width,
                contentSize.Height,
                contentSize.Width * 4,
                Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
            return true;
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (frameLock)
        {
            latestFrame?.Dispose();
            latestFrame = sender.TryGetNextFrame();
        }

        frameArrivedEvent.Set();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        lock (frameLock)
        {
            latestFrame?.Dispose();
            latestFrame = null;
        }

        if (framePool is not null)
        {
            framePool.FrameArrived -= OnFrameArrived;
        }

        captureSession?.Dispose();
        framePool?.Dispose();
        d3dContext.Dispose();
        d3dDevice.Dispose();
        frameArrivedEvent.Dispose();
    }

    private static GraphicsCaptureItem CreateCaptureItemForWindow(IntPtr hwnd)
    {
        IntPtr factoryPointer = GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", typeof(IGraphicsCaptureItemInterop).GUID);
        try
        {
            IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPointer);
            Guid iid = typeof(GraphicsCaptureItem).GUID;
            IntPtr itemPointer = interop.CreateForWindow(hwnd, iid);
            try
            {
                return FromAbi<GraphicsCaptureItem>(itemPointer);
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }
        finally
        {
            Marshal.Release(factoryPointer);
        }
    }

    private static IDirect3DDevice CreateDirect3DDevice(ID3D11Device device)
    {
        int hr = CreateDirect3D11DeviceFromDXGIDevice(device.NativePointer, out IntPtr graphicsDevicePointer);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return FromAbi<IDirect3DDevice>(graphicsDevicePointer);
        }
        finally
        {
            Marshal.Release(graphicsDevicePointer);
        }
    }

    private static IMemoryBufferByteAccess GetMemoryBufferByteAccess(object reference)
    {
        IntPtr unknownPointer = Marshal.GetIUnknownForObject(reference);
        try
        {
            Guid iid = typeof(IMemoryBufferByteAccess).GUID;
            Marshal.QueryInterface(unknownPointer, ref iid, out IntPtr accessPointer);
            try
            {
                return (IMemoryBufferByteAccess)Marshal.GetObjectForIUnknown(accessPointer);
            }
            finally
            {
                if (accessPointer != IntPtr.Zero)
                {
                    Marshal.Release(accessPointer);
                }
            }
        }
        finally
        {
            Marshal.Release(unknownPointer);
        }
    }

    private static IntPtr GetActivationFactory(string runtimeClassName, Guid interfaceId)
    {
        int createStringHr = WindowsCreateString(runtimeClassName, runtimeClassName.Length, out IntPtr className);
        Marshal.ThrowExceptionForHR(createStringHr);
        try
        {
            int activationHr = RoGetActivationFactory(className, ref interfaceId, out IntPtr factoryPointer);
            Marshal.ThrowExceptionForHR(activationHr);
            return factoryPointer;
        }
        finally
        {
            WindowsDeleteString(className);
        }
    }

    private static T FromAbi<T>(IntPtr abiPointer) where T : class
    {
        MethodInfo? fromAbiMethod = typeof(T).GetMethod(
            "FromAbi",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(IntPtr) },
            modifiers: null);
        if (fromAbiMethod is not null)
        {
            return (T)fromAbiMethod.Invoke(null, new object[] { abiPointer })!;
        }

        return (T)Marshal.GetObjectForIUnknown(abiPointer);
    }

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, Guid iid);
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865B-BC3A4B46D8B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* value, out uint capacity);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed record CapturedPixelFrame(byte[] Pixels, int Width, int Height, int Stride, Rectangle ScreenBounds);