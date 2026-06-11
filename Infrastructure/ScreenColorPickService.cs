using static YOLOForAim.ScreenColorSampler;

namespace YOLOForAim;

/// <summary>
/// 按优先级完成屏幕取色：当前检测帧、临时 Desktop Duplication、最后 GDI 截屏。
/// </summary>
internal static class ScreenColorPickService
{
    public static PickedScreenColor PickColorAtCursor(CapturedPixelFrame? latestCapturedFrame, IntPtr selectedHwnd)
    {
        if (latestCapturedFrame is not null && TryGetColorAtCursor(latestCapturedFrame, out Color capturedColor))
        {
            return new PickedScreenColor(capturedColor, "检测帧");
        }

        if (TryGetDesktopDuplicationColorAtCursor(selectedHwnd, out Color duplicatedColor))
        {
            return new PickedScreenColor(duplicatedColor, "Desktop Duplication");
        }

        return new PickedScreenColor(GetScreenColorAtCursor(), "GDI 截屏");
    }

    private static bool TryGetDesktopDuplicationColorAtCursor(IntPtr selectedHwnd, out Color color)
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

            return TryGetColorAtCursor(frame, out color);
        }
        catch
        {
            return false;
        }
    }
}
