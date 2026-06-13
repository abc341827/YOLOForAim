using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 从 YOLO 原始结果中筛出一个主目标：首次选择离捕获区域中心最近的框，之后优先保持当前锁定目标。
/// </summary>
internal sealed class PrimaryTargetTracker
{
    private const int LostTargetIdentityHoldMs = 350;
    private const float MaxLockedCenterDistancePixels = 160f;
    private const float MinLockedIou = 0.08f;

    private DetectionResult? lockedDetection;
    private Rectangle lockedCaptureBounds;
    private long latestLockTick;

    public IReadOnlyList<DetectionResult> SelectPrimaryTarget(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, Point cursorPoint, AimRuntimeSettings settings)
    {
        long now = Environment.TickCount64;
        if (captureBounds.IsEmpty)
        {
            Clear();
            return Array.Empty<DetectionResult>();
        }

        if (detections.Count == 0)
        {
            ClearExpiredLockedTarget(now);
            return Array.Empty<DetectionResult>();
        }

        bool hasValidLockedIdentity = IsLockedIdentityValid(now);
        if (lockedDetection is not null && !hasValidLockedIdentity)
        {
            Clear();
        }

        if (lockedDetection is not null && IsCursorInsideLockedTarget(cursorPoint, settings))
        {
            DetectionResult? lockedTarget = FindLockedTarget(detections, captureBounds);
            if (lockedTarget is null)
            {
                return Array.Empty<DetectionResult>();
            }

            lockedDetection = lockedTarget;
            lockedCaptureBounds = captureBounds;
            latestLockTick = now;
            return new[] { lockedDetection };
        }

        DetectionResult? selectedDetection;
        if (lockedDetection is null)
        {
            selectedDetection = FindNearestToCaptureCenter(detections, captureBounds);
        }
        else
        {
            selectedDetection = FindLockedTarget(detections, captureBounds);
            if (selectedDetection is null)
            {
                return Array.Empty<DetectionResult>();
            }
        }

        if (selectedDetection is null)
        {
            Clear();
            return Array.Empty<DetectionResult>();
        }

        lockedDetection = selectedDetection;
        lockedCaptureBounds = captureBounds;
        latestLockTick = now;
        return new[] { selectedDetection };
    }

    public void Clear()
    {
        lockedDetection = null;
        lockedCaptureBounds = Rectangle.Empty;
        latestLockTick = 0;
    }

    private bool IsLockedIdentityValid(long now)
    {
        return lockedDetection is not null && now - latestLockTick <= LostTargetIdentityHoldMs;
    }

    private void ClearExpiredLockedTarget(long now)
    {
        if (!IsLockedIdentityValid(now))
        {
            Clear();
        }
    }

    private DetectionResult? FindNearestToCaptureCenter(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds)
    {
        PointF captureCenter = new(captureBounds.Left + (captureBounds.Width / 2f), captureBounds.Top + (captureBounds.Height / 2f));
        DetectionResult? bestDetection = null;
        float bestDistanceSquared = float.MaxValue;

        foreach (DetectionResult detection in detections)
        {
            PointF center = GetDetectionScreenCenter(captureBounds, detection);
            float distanceSquared = GetDistanceSquared(center, captureCenter);
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDetection = detection;
            bestDistanceSquared = distanceSquared;
        }

        return bestDetection;
    }

    private DetectionResult? FindLockedTarget(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds)
    {
        if (lockedDetection is null)
        {
            return null;
        }

        PointF lockedCenter = GetDetectionScreenCenter(lockedCaptureBounds, lockedDetection);
        float maxDistanceSquared = MaxLockedCenterDistancePixels * MaxLockedCenterDistancePixels;
        DetectionResult? bestDetection = null;
        double bestScore = double.MaxValue;

        foreach (DetectionResult detection in detections)
        {
            if (detection.ClassId != lockedDetection.ClassId || detection.Label != lockedDetection.Label)
            {
                continue;
            }

            PointF currentCenter = GetDetectionScreenCenter(captureBounds, detection);
            float distanceSquared = GetDistanceSquared(lockedCenter, currentCenter);
            float iou = CalculateIou(lockedDetection.Box, detection.Box);
            if (distanceSquared > maxDistanceSquared && iou < MinLockedIou)
            {
                continue;
            }

            double score = distanceSquared - (iou * maxDistanceSquared);
            if (score >= bestScore)
            {
                continue;
            }

            bestDetection = detection;
            bestScore = score;
        }

        return bestDetection;
    }

    private static PointF GetDetectionScreenCenter(Rectangle captureBounds, DetectionResult detection)
    {
        PointF center = GetBoxCenter(detection.Box);
        return new PointF(captureBounds.Left + center.X, captureBounds.Top + center.Y);
    }

    private bool IsCursorInsideLockedTarget(Point cursorPoint, AimRuntimeSettings settings)
    {
        return lockedDetection is not null &&
            !lockedCaptureBounds.IsEmpty &&
            AimPointCalculator.IsAimReferenceInsideStableBox(
                lockedCaptureBounds,
                lockedDetection,
                new PointF(cursorPoint.X, cursorPoint.Y),
                settings.StopLockSquareSizePixels,
                settings.StopLockTopOffsetPixels);
    }
}
