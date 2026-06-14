using System.Drawing;

namespace YOLOForAim;

/// <summary>
/// DetectionOverlayForm 绘制所需的一次状态快照。
/// </summary>
internal sealed record DetectionOverlaySnapshot(
    Rectangle CaptureBounds,
    DetectionResult[] DisplayDetections,
    DetectionResult[] Detections,
    DetectionResult? LockedDetection,
    PointF? AimPoint,
    Point CursorPoint);
