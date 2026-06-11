using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 从当前帧检测结果中选择最适合瞄准的目标。
/// 该类不保存锁定状态，只根据调用方传入的锚点、距离阈值和瞄准点计算函数进行选择。
/// </summary>
internal sealed class AimTargetSelector
{
    public TargetCandidate? Select(
        IReadOnlyList<DetectionResult> detections,
        Rectangle captureBounds,
        PointF referencePoint,
        PointF? lockedAnchor,
        float maxCandidateDistancePixels,
        float containingPaddingPixels,
        Func<DetectionResult, PointF> getAimPoint)
    {
        if (lockedAnchor is not null)
        {
            TargetCandidate? containingCandidate = FindContainingCandidate(detections, captureBounds, lockedAnchor.Value, containingPaddingPixels, getAimPoint);
            if (containingCandidate is not null)
            {
                return IsNearCursor(containingCandidate, referencePoint, maxCandidateDistancePixels) ? containingCandidate : null;
            }

            TargetCandidate? nearestCandidate = FindNearestCandidate(detections, lockedAnchor.Value, maxCandidateDistancePixels, getAimPoint);
            return nearestCandidate is not null && IsNearCursor(nearestCandidate, referencePoint, maxCandidateDistancePixels)
                ? nearestCandidate
                : null;
        }

        return FindNearestCandidate(detections, referencePoint, maxCandidateDistancePixels, getAimPoint);
    }

    private static TargetCandidate? FindContainingCandidate(
        IReadOnlyList<DetectionResult> detections,
        Rectangle captureBounds,
        PointF referencePoint,
        float paddingPixels,
        Func<DetectionResult, PointF> getAimPoint)
    {
        TargetCandidate? bestCandidate = null;

        foreach (DetectionResult detection in detections)
        {
            RectangleF detectionBounds = GetExpandedDetectionBounds(captureBounds, detection, paddingPixels);
            if (!detectionBounds.Contains(referencePoint))
            {
                continue;
            }

            PointF targetPoint = getAimPoint(detection);
            double distanceSquared = GetDistanceSquared(referencePoint, targetPoint);
            if (bestCandidate is null || distanceSquared < bestCandidate.DistanceSquared)
            {
                bestCandidate = new TargetCandidate(detection, targetPoint, distanceSquared);
            }
        }

        return bestCandidate;
    }

    private static TargetCandidate? FindNearestCandidate(
        IReadOnlyList<DetectionResult> detections,
        PointF referencePoint,
        float maxDistancePixels,
        Func<DetectionResult, PointF> getAimPoint)
    {
        TargetCandidate? bestCandidate = null;
        double maxDistanceSquared = maxDistancePixels * maxDistancePixels;

        foreach (DetectionResult detection in detections)
        {
            PointF targetPoint = getAimPoint(detection);
            double distanceSquared = GetDistanceSquared(referencePoint, targetPoint);
            if (distanceSquared > maxDistanceSquared)
            {
                continue;
            }

            if (bestCandidate is null || distanceSquared < bestCandidate.DistanceSquared)
            {
                bestCandidate = new TargetCandidate(detection, targetPoint, distanceSquared);
            }
        }

        return bestCandidate;
    }

    private static bool IsNearCursor(TargetCandidate candidate, PointF cursorPoint, float maxDistancePixels)
    {
        return candidate.DistanceSquared <= (maxDistancePixels * maxDistancePixels) ||
            GetDistanceSquared(candidate.TargetPoint, cursorPoint) <= (maxDistancePixels * maxDistancePixels);
    }
}
