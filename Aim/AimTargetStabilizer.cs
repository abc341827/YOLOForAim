using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 负责瞄准目标的稳定、尺寸保持和同目标判断。
/// 该类只修改 AimRuntimeState 中的锁定/稳定相关状态，不发送鼠标输入。
/// </summary>
internal sealed class AimTargetStabilizer
{
    private const float AimHeightHighConfidenceThreshold = 0.65f;
    private const float AimHeightHighConfidenceBlend = 0.45f;
    private const float AimHeightLowConfidenceBlend = 0.12f;
    private const float AimHeightLowConfidenceMinRatio = 0.92f;
    private const float StableTargetConfidenceThreshold = 0.45f;
    private const float StableTargetPositionTolerancePixels = 30f;
    private const float StableTargetPositionBlend = 0.72f;
    private const int StableTargetConfirmationFrames = 2;
    private const int StableTargetSizeHoldMs = 180;
    private const float StableTargetSizeUpdateCenterOffsetPixels = 28f;
    private const float StableTargetSizeBlend = 0.22f;
    private const float AimSameTargetOverlapThreshold = 0.12f;
    private const float StableTargetIouThreshold = 0.35f;

    private readonly AimRuntimeState state;

    public AimTargetStabilizer(AimRuntimeState state)
    {
        this.state = state;
    }

    public DetectionResult GetStabilizedDetection(DetectionResult detection, Rectangle captureBounds, bool resetTracking)
    {
        long now = Environment.TickCount64;
        if (resetTracking || state.StabilizedLockedDetection is null)
        {
            state.StabilizedLockedDetection = detection;
            state.StabilizedLockedDetectionFrames = 1;
            state.StableTargetSizeHoldUntilTick = now + StableTargetSizeHoldMs;
            return detection;
        }

        if (!ShouldKeepStableTarget(state.StabilizedLockedDetection, detection))
        {
            state.StabilizedLockedDetection = detection;
            state.StabilizedLockedDetectionFrames = 1;
            state.StableTargetSizeHoldUntilTick = now + StableTargetSizeHoldMs;
            return detection;
        }

        state.StabilizedLockedDetectionFrames++;
        if (state.StabilizedLockedDetectionFrames < StableTargetConfirmationFrames)
        {
            state.StabilizedLockedDetection = detection;
            return detection;
        }

        PointF previousCenter = GetBoxCenter(state.StabilizedLockedDetection.Box);
        PointF currentCenter = GetBoxCenter(detection.Box);
        PointF stabilizedCenter = LerpPoint(previousCenter, currentCenter, StableTargetPositionBlend);
        SizeF stabilizedSize = GetStableTargetSize(detection, captureBounds, now);
        RectangleF stabilizedBox = CreateCenteredBox(stabilizedCenter, stabilizedSize);
        state.StabilizedLockedDetection = detection with { Box = stabilizedBox };
        return state.StabilizedLockedDetection;
    }

    public bool IsLikelySameLockedTarget(DetectionResult detection, Rectangle captureBounds, AimRuntimeSettings settings, Func<DetectionResult, PointF> getAimPoint)
    {
        if (state.StabilizedLockedDetection is null ||
            detection.ClassId != state.StabilizedLockedDetection.ClassId ||
            detection.Label != state.StabilizedLockedDetection.Label)
        {
            return false;
        }

        if (CalculateIou(state.StabilizedLockedDetection.Box, detection.Box) >= AimSameTargetOverlapThreshold)
        {
            return true;
        }

        PointF lockedPoint = state.SmoothedTargetScreenPoint
            ?? state.LockedTargetScreenPoint
            ?? getAimPoint(state.StabilizedLockedDetection);
        RectangleF expandedBounds = GetExpandedDetectionBounds(captureBounds, detection, settings.DeadzonePixels + 8f);
        return expandedBounds.Contains(lockedPoint);
    }

    public float GetEffectiveAimHeight(DetectionResult detection, bool resetHeightTracking)
    {
        float detectedHeight = Math.Max(1f, detection.Box.Height);
        if (resetHeightTracking || state.StabilizedAimTargetHeight <= 0f)
        {
            state.StabilizedAimTargetHeight = detectedHeight;
            return state.StabilizedAimTargetHeight;
        }

        bool highConfidence = detection.Score >= AimHeightHighConfidenceThreshold;
        float blend = highConfidence ? AimHeightHighConfidenceBlend : AimHeightLowConfidenceBlend;
        float candidateHeight = detectedHeight;
        if (!highConfidence && detectedHeight < state.StabilizedAimTargetHeight)
        {
            candidateHeight = Math.Max(detectedHeight, state.StabilizedAimTargetHeight * AimHeightLowConfidenceMinRatio);
        }

        state.StabilizedAimTargetHeight = LerpFloat(state.StabilizedAimTargetHeight, candidateHeight, blend);
        return Math.Max(1f, state.StabilizedAimTargetHeight);
    }

    private SizeF GetStableTargetSize(DetectionResult detection, Rectangle captureBounds, long now)
    {
        _ = captureBounds;
        if (state.StabilizedLockedDetection is null)
        {
            return detection.Box.Size;
        }

        SizeF previousSize = state.StabilizedLockedDetection.Box.Size;
        SizeF currentSize = detection.Box.Size;
        if (now < state.StableTargetSizeHoldUntilTick)
        {
            return previousSize;
        }

        PointF previousCenter = GetBoxCenter(state.StabilizedLockedDetection.Box);
        PointF currentCenter = GetBoxCenter(detection.Box);
        if (GetDistanceSquared(previousCenter, currentCenter) > StableTargetSizeUpdateCenterOffsetPixels * StableTargetSizeUpdateCenterOffsetPixels &&
            CalculateIou(state.StabilizedLockedDetection.Box, detection.Box) < StableTargetIouThreshold)
        {
            return previousSize;
        }

        SizeF guardedSize = new(
            GuardTrackedSize(previousSize.Width, currentSize.Width),
            GuardTrackedSize(previousSize.Height, currentSize.Height));
        return LerpSize(previousSize, guardedSize, StableTargetSizeBlend);
    }

    private static bool ShouldKeepStableTarget(DetectionResult previousDetection, DetectionResult currentDetection)
    {
        if (previousDetection.ClassId != currentDetection.ClassId || previousDetection.Label != currentDetection.Label)
        {
            return false;
        }

        if (currentDetection.Score < StableTargetConfidenceThreshold)
        {
            return CalculateIou(previousDetection.Box, currentDetection.Box) >= StableTargetIouThreshold;
        }

        PointF previousCenter = GetBoxCenter(previousDetection.Box);
        PointF currentCenter = GetBoxCenter(currentDetection.Box);
        return GetDistanceSquared(previousCenter, currentCenter) <=
            (StableTargetPositionTolerancePixels * StableTargetPositionTolerancePixels) ||
            CalculateIou(previousDetection.Box, currentDetection.Box) >= StableTargetIouThreshold;
    }
}
