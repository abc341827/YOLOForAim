using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 负责把检测框转换为屏幕坐标下的瞄准点、锁定框和稳定区域。
/// 这里不保存任何状态，所有计算都由调用方传入当前捕获区域和运行时设置。
/// </summary>
internal static class AimPointCalculator
{
    private const float ColorPixelAimHorizontalOffsetScreenDivisor = 25.6f;

    public static RectangleF GetDetectionScreenBounds(Rectangle captureBounds, DetectionResult detection)
    {
        return new RectangleF(
            captureBounds.Left + detection.Box.X,
            captureBounds.Top + detection.Box.Y,
            detection.Box.Width,
            detection.Box.Height);
    }

    public static bool IsAimReferenceInsideStableBox(Rectangle captureBounds, DetectionResult detection, PointF aimReferencePoint, float squareSizePixels, float topOffsetPixels)
    {
        RectangleF screenBounds = GetDetectionScreenBounds(captureBounds, detection);
        RectangleF lockSquareBounds = GetLockSquareBounds(screenBounds, squareSizePixels, topOffsetPixels);
        return lockSquareBounds.Contains(aimReferencePoint);
    }

    public static PointF GetStopLockTargetPoint(Rectangle captureBounds, DetectionResult detection, AimRuntimeSettings settings)
    {
        RectangleF screenBounds = GetDetectionScreenBounds(captureBounds, detection);
        RectangleF lockSquareBounds = GetLockSquareBounds(screenBounds, settings.StopLockSquareSizePixels, settings.StopLockTopOffsetPixels);
        return GetBoxCenter(lockSquareBounds);
    }

    public static DetectionResult GetControlDetection(DetectionResult detection, AimRuntimeSettings settings, int targetWindowWidth)
    {
        if (!IsColorDetection(detection))
        {
            return detection;
        }

        float horizontalOffset = Math.Max(targetWindowWidth, detection.Box.Width) / ColorPixelAimHorizontalOffsetScreenDivisor;
        PointF targetCenter = new(
            detection.Box.X + horizontalOffset,
            detection.Box.Y + settings.PointBelowOffsetPixels);
        float boxSize = Math.Max(8f, settings.StopLockSquareSizePixels);
        RectangleF targetBox = CreateCenteredBox(targetCenter, new SizeF(boxSize, boxSize));
        return detection with { Box = targetBox };
    }

    public static RectangleF GetLockSquareBounds(RectangleF bounds, float squareSizePixels, float topOffsetPixels)
    {
        float squareSize = Math.Clamp(squareSizePixels, 8f, Math.Max(8f, Math.Min(bounds.Width, bounds.Height)));
        float left = bounds.Left + ((bounds.Width - squareSize) / 2f);
        float top = bounds.Top + Math.Clamp(topOffsetPixels, 0f, Math.Max(0f, bounds.Height - squareSize));
        return new RectangleF(left, top, squareSize, squareSize);
    }

    public static PointF GetAimPoint(Rectangle captureBounds, DetectionResult detection, AimRuntimeSettings settings, int targetWindowWidth)
    {
        _ = settings;
        _ = targetWindowWidth;
        RectangleF screenBounds = GetDetectionScreenBounds(captureBounds, detection);
        return GetBoxCenter(screenBounds);
    }

    private static bool IsColorDetection(DetectionResult detection)
    {
        return detection.Label.StartsWith("Color", StringComparison.Ordinal);
    }
}
