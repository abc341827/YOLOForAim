using System.Drawing;

namespace YOLOForAim;

/// <summary>
/// 提供瞄准、追踪和覆盖层绘制共用的几何计算。
/// 这些方法不依赖窗体状态，便于单独理解和后续测试。
/// </summary>
internal static class AimGeometry
{
    public static float GetDistanceSquared(PointF a, PointF b)
    {
        float deltaX = a.X - b.X;
        float deltaY = a.Y - b.Y;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    public static PointF GetBoxCenter(RectangleF box)
    {
        return new PointF(box.Left + (box.Width / 2f), box.Top + (box.Height / 2f));
    }

    public static RectangleF CreateCenteredBox(PointF center, SizeF size)
    {
        return new RectangleF(
            center.X - (size.Width / 2f),
            center.Y - (size.Height / 2f),
            size.Width,
            size.Height);
    }

    public static RectangleF GetExpandedDetectionBounds(Rectangle captureBounds, DetectionResult detection, float padding)
    {
        return new RectangleF(
            captureBounds.Left + detection.Box.X - padding,
            captureBounds.Top + detection.Box.Y - padding,
            detection.Box.Width + (padding * 2f),
            detection.Box.Height + (padding * 2f));
    }

    public static float CalculateIou(RectangleF a, RectangleF b)
    {
        float left = Math.Max(a.Left, b.Left);
        float top = Math.Max(a.Top, b.Top);
        float right = Math.Min(a.Right, b.Right);
        float bottom = Math.Min(a.Bottom, b.Bottom);

        float intersectionWidth = Math.Max(0, right - left);
        float intersectionHeight = Math.Max(0, bottom - top);
        float intersectionArea = intersectionWidth * intersectionHeight;
        if (intersectionArea <= 0)
        {
            return 0f;
        }

        float unionArea = (a.Width * a.Height) + (b.Width * b.Height) - intersectionArea;
        return unionArea <= 0 ? 0f : intersectionArea / unionArea;
    }

    public static PointF LerpPoint(PointF from, PointF to, float amount)
    {
        return new PointF(
            from.X + ((to.X - from.X) * amount),
            from.Y + ((to.Y - from.Y) * amount));
    }

    public static PointF PredictPointFromVelocity(PointF point, PointF velocityPixelsPerSecond, float predictionSeconds, float maxPredictionPixels)
    {
        float offsetX = velocityPixelsPerSecond.X * predictionSeconds;
        float offsetY = velocityPixelsPerSecond.Y * predictionSeconds;
        float distance = MathF.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
        if (distance > maxPredictionPixels && distance > 0f)
        {
            float scale = maxPredictionPixels / distance;
            offsetX *= scale;
            offsetY *= scale;
        }

        return new PointF(point.X + offsetX, point.Y + offsetY);
    }

    public static SizeF LerpSize(SizeF from, SizeF to, float amount)
    {
        return new SizeF(
            from.Width + ((to.Width - from.Width) * amount),
            from.Height + ((to.Height - from.Height) * amount));
    }

    public static float GetFrameRateAdjustedBlend(float baseBlendAt60Fps, double deltaSeconds)
    {
        float clampedBaseBlend = Math.Clamp(baseBlendAt60Fps, 0.001f, 0.999f);
        double frameScale = Math.Max(0.001d, deltaSeconds * 60d);
        return (float)Math.Clamp(1d - Math.Pow(1d - clampedBaseBlend, frameScale), 0.001d, 0.999d);
    }

    public static float LerpFloat(float from, float to, float amount)
    {
        return from + ((to - from) * amount);
    }

    public static float GuardTrackedSize(float previousSize, float currentSize)
    {
        if (previousSize <= 0f || currentSize >= previousSize)
        {
            return currentSize;
        }

        return Math.Max(currentSize, previousSize * 0.92f);
    }
}
