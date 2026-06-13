using System.Drawing;

namespace YOLOForAim;

/// <summary>
/// 管理覆盖层显示状态，包括显示层目标追踪、空帧短暂保留和线程安全快照。
/// </summary>
internal sealed class OverlayStateManager
{
    private const int EmptyFrameHoldMs = 120;

    private readonly object syncRoot = new();
    private readonly OverlayTracker tracker = new();
    private Rectangle captureBounds;
    private DetectionResult[] detections = Array.Empty<DetectionResult>();
    private DetectionResult? lockedDetection;
    private PointF? aimPoint;
    private Point cursorPoint = Point.Empty;
    private long latestNonEmptyDetectionsTick;

    public void Update(
        Rectangle newCaptureBounds,
        IReadOnlyList<DetectionResult> rawDetections,
        int processedFrameVersion,
        int suppressOverlayFrameVersion,
        long capturedTick,
        double currentInferenceFps,
        DetectionResult? currentLockedDetection,
        Func<DetectionResult, PointF> getAimPoint,
        Point currentCursorPoint)
    {
        IReadOnlyList<DetectionResult> overlayDetections = processedFrameVersion <= suppressOverlayFrameVersion
            ? Array.Empty<DetectionResult>()
            : rawDetections.Take(1).ToArray();

        long now = Environment.TickCount64;
        lock (syncRoot)
        {
            captureBounds = newCaptureBounds;
            if (overlayDetections.Count > 0)
            {
                detections = overlayDetections.ToArray();
                latestNonEmptyDetectionsTick = now;
            }
            else if (now - latestNonEmptyDetectionsTick > EmptyFrameHoldMs)
            {
                detections = Array.Empty<DetectionResult>();
            }

            lockedDetection = overlayDetections.Count == 0 ? null : currentLockedDetection;
            aimPoint = overlayDetections.Count == 0 || currentLockedDetection is null
                ? null
                : getAimPoint(currentLockedDetection);
            cursorPoint = currentCursorPoint;
        }
    }

    public DetectionOverlaySnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            return new DetectionOverlaySnapshot(captureBounds, detections, lockedDetection, aimPoint, cursorPoint);
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            captureBounds = Rectangle.Empty;
            detections = Array.Empty<DetectionResult>();
            lockedDetection = null;
            aimPoint = null;
            cursorPoint = Point.Empty;
            latestNonEmptyDetectionsTick = 0;
            tracker.Clear();
        }
    }
}
