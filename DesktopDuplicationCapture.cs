using System.Drawing;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace YOLOForAim;

internal sealed class DesktopDuplicationCapture : IDisposable
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint DxgiErrorAccessLost = 0x887A0026;
    private const uint DxgiErrorWaitTimeout = 0x887A0027;

    private readonly IntPtr hwnd;
    private ID3D11Device? d3dDevice;
    private ID3D11DeviceContext? d3dContext;
    private IDXGIOutputDuplication? duplication;
    private ID3D11Texture2D? stagingTexture;
    private Rectangle outputBounds;
    private bool disposed;

    public DesktopDuplicationCapture(IntPtr hwnd)
    {
        this.hwnd = hwnd;
        InitializeDuplication();
    }

    public bool TryGetLatestFrame(int timeoutMilliseconds, out CapturedPixelFrame capturedFrame)
    {
        capturedFrame = default;
        if (disposed || duplication is null || d3dContext is null || stagingTexture is null)
        {
            return false;
        }

        try
        {
            duplication.AcquireNextFrame((uint)timeoutMilliseconds, out _, out IDXGIResource desktopResource).CheckError();
            using (desktopResource)
            using (ID3D11Texture2D desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>())
            {
                d3dContext.CopyResource(stagingTexture, desktopTexture);
            }

            duplication.ReleaseFrame();
        }
        catch (Exception ex) when ((uint)ex.HResult == DxgiErrorWaitTimeout)
        {
            return false;
        }
        catch (Exception ex) when ((uint)ex.HResult == DxgiErrorAccessLost)
        {
            ReinitializeDuplication();
            return false;
        }

        if (!GetWindowRect(hwnd, out RECT rect))
        {
            return false;
        }

        Rectangle windowBounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        Rectangle captureBounds = Rectangle.Intersect(windowBounds, outputBounds);
        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
        {
            return false;
        }

        MappedSubresource mapped = d3dContext.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int width = captureBounds.Width;
            int height = captureBounds.Height;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            int sourceOffsetX = captureBounds.Left - outputBounds.Left;
            int sourceOffsetY = captureBounds.Top - outputBounds.Top;

            unsafe
            {
                byte* sourceStart = (byte*)mapped.DataPointer;
                fixed (byte* destinationStart = pixels)
                {
                    for (int y = 0; y < height; y++)
                    {
                        byte* sourceRow = sourceStart + ((sourceOffsetY + y) * mapped.RowPitch) + (sourceOffsetX * 4);
                        byte* destinationRow = destinationStart + (y * stride);
                        Buffer.MemoryCopy(sourceRow, destinationRow, stride, stride);
                    }
                }
            }

            capturedFrame = new CapturedPixelFrame(pixels, width, height, stride, captureBounds);
            return true;
        }
        finally
        {
            d3dContext.Unmap(stagingTexture, 0);
        }
    }

    private void InitializeDuplication()
    {
        DisposeResources();

        IntPtr targetMonitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (targetMonitor == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法找到目标窗口所在的显示器。");
        }

        using IDXGIFactory1 factory = CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? selectedAdapter = null;
        IDXGIOutput1? selectedOutput = null;
        Rectangle selectedOutputBounds = Rectangle.Empty;

        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1 adapter).Success; adapterIndex++)
        {
            bool found = false;
            for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out IDXGIOutput output).Success; outputIndex++)
            {
                using (output)
                {
                    OutputDescription description = output.Description;
                    if (description.Monitor != targetMonitor)
                    {
                        continue;
                    }

                    selectedAdapter = adapter;
                    selectedOutput = output.QueryInterface<IDXGIOutput1>();
                    selectedOutputBounds = Rectangle.FromLTRB(
                        description.DesktopCoordinates.Left,
                        description.DesktopCoordinates.Top,
                        description.DesktopCoordinates.Right,
                        description.DesktopCoordinates.Bottom);
                    found = true;
                    break;
                }
            }

            if (found)
            {
                break;
            }

            adapter.Dispose();
        }

        if (selectedAdapter is null || selectedOutput is null)
        {
            selectedAdapter?.Dispose();
            selectedOutput?.Dispose();
            throw new InvalidOperationException("无法为目标窗口找到可用的输出设备。Desktop Duplication 仅支持活动显示器内容捕获。");
        }

        try
        {
            D3D11CreateDevice(
                selectedAdapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                null,
                out ID3D11Device device,
                out ID3D11DeviceContext context).CheckError();

            d3dDevice = device;
            d3dContext = context;
            duplication = selectedOutput.DuplicateOutput(d3dDevice);
            outputBounds = selectedOutputBounds;
            CreateStagingTexture(selectedOutputBounds.Width, selectedOutputBounds.Height);
        }
        finally
        {
            selectedOutput.Dispose();
            selectedAdapter.Dispose();
        }
    }

    private void ReinitializeDuplication()
    {
        try
        {
            InitializeDuplication();
        }
        catch
        {
            DisposeResources();
        }
    }

    private void CreateStagingTexture(int width, int height)
    {
        if (d3dDevice is null)
        {
            throw new InvalidOperationException("D3D11 设备未初始化。");
        }

        stagingTexture?.Dispose();
        stagingTexture = d3dDevice.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });
    }

    private void DisposeResources()
    {
        stagingTexture?.Dispose();
        stagingTexture = null;
        duplication?.Dispose();
        duplication = null;
        d3dContext?.Dispose();
        d3dContext = null;
        d3dDevice?.Dispose();
        d3dDevice = null;
        outputBounds = Rectangle.Empty;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeResources();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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