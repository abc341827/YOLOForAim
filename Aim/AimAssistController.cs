using System.Drawing;
using System.Windows.Forms;
using static YOLOForAim.AimGeometry;
using static YOLOForAim.MouseInputController;

namespace YOLOForAim;

/// <summary>
/// 串联瞄准辅助主流程：激活判断、目标选择、稳定/预测、移动计算和鼠标输入。
/// Form1 只负责在每帧推理完成后调用该控制器。
/// </summary>
internal sealed class AimAssistController
{
    private const float LockedTargetMatchMaxCenterDistancePixels = 90f;
    private const float LockedTargetMatchMinIou = 0.08f;
    private const float AdaptiveTrackingDistancePixels = 80f;
    private const float MaxAdaptiveTrackingBlend = 0.85f;
    private const float MinOutlierRejectDistancePixels = 120f;

    private readonly AimRuntimeState state = new();
    private readonly AimTargetSelector targetSelector = new();
    private readonly AimTargetStabilizer targetStabilizer;
    private readonly AimTargetPredictor targetPredictor;
    private readonly AimAssistGate assistGate;
    private readonly AimMissTracker missTracker;

    public AimAssistController()
    {
        targetStabilizer = new AimTargetStabilizer(state);
        targetPredictor = new AimTargetPredictor(state);
        assistGate = new AimAssistGate(state);
        missTracker = new AimMissTracker(state);
    }

    public DetectionResult? StabilizedLockedDetection => state.StabilizedLockedDetection;

    public int SuppressOverlayFrameVersion => state.SuppressOverlayFrameVersion;

    public void ResetRuntime()
    {
        state.ResetRuntime();
    }

    public void ResetTracking()
    {
        state.ResetTracking();
    }

    public void TryMoveToNearestDetection(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, int processedFrameVersion, long capturedTick, AimRuntimeSettings settings, int targetWindowWidth)
    {
        long now = Environment.TickCount64;
        if (processedFrameVersion <= state.SuspendAimUntilFrameVersion || now < state.SuspendAimUntilTick)
        {
            return;
        }

        if (captureBounds.IsEmpty || !assistGate.IsAssistActive(settings, now))
        {
            ResetTracking();
            return;
        }

        if (detections.Count == 0)
        {
            if (missTracker.RegisterMiss(settings))
            {
                ResetTracking();
            }

            return;
        }

        PointF aimReferencePoint = GetAimReferencePoint();
        TargetCandidate? nearestDetection = SelectTargetCandidate(detections, captureBounds, aimReferencePoint, settings, targetWindowWidth);
        if (nearestDetection is null)
        {
            if (missTracker.RegisterMiss(settings))
            {
                ResetTracking();
            }

            return;
        }

        DetectionResult currentDetection = nearestDetection.Detection;
        bool resetTargetTracking = state.StabilizedLockedDetection is null || !IsLikelySameLockedTarget(currentDetection, captureBounds, settings, targetWindowWidth);
        PointF rawObservedTargetPoint = GetAimPoint(captureBounds, currentDetection, settings, targetWindowWidth);
        if (ShouldRejectSuddenTargetJump(rawObservedTargetPoint, resetTargetTracking, settings))
        {
            if (missTracker.RegisterMiss(settings))
            {
                ResetTracking();
            }

            return;
        }

        DetectionResult stableDetection = targetStabilizer.GetStabilizedDetection(currentDetection, captureBounds, resetTargetTracking);
        PointF observedTargetPoint = GetAimPoint(captureBounds, stableDetection, settings, targetWindowWidth);
        TargetPrediction targetPrediction = targetPredictor.Predict(observedTargetPoint, resetTargetTracking, now, capturedTick);
        PointF targetPoint = observedTargetPoint;
        state.LockedTargetScreenPoint = GetAimPoint(captureBounds, currentDetection, settings, targetWindowWidth);
        state.SmoothedTargetScreenPoint = state.SmoothedTargetScreenPoint is null || resetTargetTracking
            ? targetPoint
            : LerpPoint(state.SmoothedTargetScreenPoint.Value, targetPoint, GetAdaptiveTargetTrackingBlend(state.SmoothedTargetScreenPoint.Value, targetPoint, settings));
        targetPoint = state.SmoothedTargetScreenPoint.Value;
        missTracker.RegisterHit();
        state.PendingTargetSwitchTick = 0;
        state.HasAppliedInitialLockPull = true;

        float distanceToObservedAimPoint = AimMovementCalculator.GetDistanceToTarget(observedTargetPoint, aimReferencePoint);
        float distanceToAimPoint = AimMovementCalculator.GetDistanceToTarget(targetPoint, aimReferencePoint);
        if (settings.PointBelowOffsetPixels <= 0f &&
            AimPointCalculator.IsAimReferenceInsideStableBox(captureBounds, stableDetection, aimReferencePoint, settings.StopLockSquareSizePixels, settings.StopLockTopOffsetPixels))
        {
            return;
        }

        if (distanceToObservedAimPoint <= settings.DeadzonePixels && !ShouldVelocityFollow(targetPrediction.Velocity, settings))
        {
            return;
        }

        if (!assistGate.CanSendMove(settings, now, processedFrameVersion))
        {
            return;
        }

        Point finalMove = AimMovementCalculator.CalculateMove(targetPoint, aimReferencePoint, settings, distanceToAimPoint, targetPrediction.Velocity);
        finalMove = ClampMoveToObservedTarget(finalMove, observedTargetPoint, aimReferencePoint, settings);
        if (finalMove.IsEmpty)
        {
            return;
        }

        SendRelativeMouseMove(finalMove.X, finalMove.Y);
        assistGate.MarkMoveSent(now, processedFrameVersion);
    }

    public PointF GetAimPoint(Rectangle captureBounds, DetectionResult detection, AimRuntimeSettings settings, int targetWindowWidth)
    {
        return AimPointCalculator.GetAimPoint(captureBounds, detection, settings, targetWindowWidth);
    }

    private TargetCandidate? SelectTargetCandidate(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, PointF referencePoint, AimRuntimeSettings settings, int targetWindowWidth)
    {
        if (state.StabilizedLockedDetection is not null)
        {
            TargetCandidate? lockedCandidate = FindCurrentFrameLockedTarget(detections, captureBounds, settings, targetWindowWidth);
            if (lockedCandidate is not null)
            {
                return lockedCandidate;
            }
        }

        PointF? lockedAnchor = state.SmoothedTargetScreenPoint ?? state.LockedTargetScreenPoint;
        float maxCandidateDistancePixels = GetCurrentAimAcquireDistancePixels(settings);
        float containingPaddingPixels = MathF.Max(settings.DeadzonePixels, 12f);
        return targetSelector.Select(
            detections,
            captureBounds,
            referencePoint,
            lockedAnchor,
            maxCandidateDistancePixels,
            containingPaddingPixels,
            detection => GetAimPoint(captureBounds, detection, settings, targetWindowWidth));
    }

    private TargetCandidate? FindCurrentFrameLockedTarget(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, AimRuntimeSettings settings, int targetWindowWidth)
    {
        DetectionResult lockedDetection = state.StabilizedLockedDetection!;
        PointF lockedCenter = GetBoxCenter(lockedDetection.Box);
        TargetCandidate? bestCandidate = null;
        double bestScore = double.MaxValue;
        double maxCenterDistanceSquared = LockedTargetMatchMaxCenterDistancePixels * LockedTargetMatchMaxCenterDistancePixels;

        foreach (DetectionResult detection in detections)
        {
            if (detection.ClassId != lockedDetection.ClassId)
            {
                continue;
            }

            PointF currentCenter = GetBoxCenter(detection.Box);
            double centerDistanceSquared = GetDistanceSquared(lockedCenter, currentCenter);
            float iou = CalculateIou(lockedDetection.Box, detection.Box);
            if (iou < LockedTargetMatchMinIou && centerDistanceSquared > maxCenterDistanceSquared)
            {
                continue;
            }

            double score = centerDistanceSquared - (iou * maxCenterDistanceSquared);
            if (score >= bestScore)
            {
                continue;
            }

            PointF targetPoint = GetAimPoint(captureBounds, detection, settings, targetWindowWidth);
            bestCandidate = new TargetCandidate(detection, targetPoint, GetDistanceSquared(state.SmoothedTargetScreenPoint ?? state.LockedTargetScreenPoint ?? targetPoint, targetPoint));
            bestScore = score;
        }

        return bestCandidate;
    }

    private bool IsLikelySameLockedTarget(DetectionResult detection, Rectangle captureBounds, AimRuntimeSettings settings, int targetWindowWidth)
    {
        return targetStabilizer.IsLikelySameLockedTarget(
            detection,
            captureBounds,
            settings,
            lockedDetection => GetAimPoint(captureBounds, lockedDetection, settings, targetWindowWidth));
    }

    private float GetCurrentAimAcquireDistancePixels(AimRuntimeSettings settings)
    {
        return !state.HasAppliedInitialLockPull
            ? Math.Max(1f, settings.InitialAcquireDistancePixels)
            : Math.Max(1f, settings.TrackedAcquireDistancePixels);
    }

    private bool ShouldRejectSuddenTargetJump(PointF observedTargetPoint, bool resetTargetTracking, AimRuntimeSettings settings)
    {
        if (!resetTargetTracking || state.SmoothedTargetScreenPoint is null)
        {
            return false;
        }

        float maxAllowedJump = Math.Max(MinOutlierRejectDistancePixels, settings.LockSwitchDistancePixels);
        return GetDistanceSquared(state.SmoothedTargetScreenPoint.Value, observedTargetPoint) > maxAllowedJump * maxAllowedJump;
    }

    private static float GetAdaptiveTargetTrackingBlend(PointF smoothedPoint, PointF targetPoint, AimRuntimeSettings settings)
    {
        float baseBlend = Math.Clamp(settings.TargetTrackingBlend, 0.01f, MaxAdaptiveTrackingBlend);
        float distance = MathF.Sqrt(GetDistanceSquared(smoothedPoint, targetPoint));
        float distanceScale = Math.Clamp(distance / AdaptiveTrackingDistancePixels, 0f, 1f);
        return LerpFloat(baseBlend, MaxAdaptiveTrackingBlend, distanceScale);
    }

    private static bool ShouldVelocityFollow(PointF targetVelocity, AimRuntimeSettings settings)
    {
        float velocityPixelsPerSecond = MathF.Sqrt((targetVelocity.X * targetVelocity.X) + (targetVelocity.Y * targetVelocity.Y));
        float minFollowVelocity = Math.Max(80f, settings.DeadzonePixels * 8f);
        return velocityPixelsPerSecond >= minFollowVelocity;
    }

    private static PointF LimitCloseRangePrediction(PointF observedTargetPoint, PointF predictedTargetPoint, PointF aimReferencePoint, AimRuntimeSettings settings)
    {
        float distanceToObservedPoint = AimMovementCalculator.GetDistanceToTarget(observedTargetPoint, aimReferencePoint);
        float closeRangePixels = Math.Max(settings.CloseRangeSlowdownPixels, settings.DeadzonePixels + 1f);
        if (distanceToObservedPoint >= closeRangePixels)
        {
            return predictedTargetPoint;
        }

        float predictionOffsetX = predictedTargetPoint.X - observedTargetPoint.X;
        float predictionOffsetY = predictedTargetPoint.Y - observedTargetPoint.Y;
        float predictionOffsetDistance = MathF.Sqrt((predictionOffsetX * predictionOffsetX) + (predictionOffsetY * predictionOffsetY));
        if (predictionOffsetDistance <= 0.001f)
        {
            return predictedTargetPoint;
        }

        float closeRangeScale = Math.Clamp((distanceToObservedPoint - settings.DeadzonePixels) / (closeRangePixels - settings.DeadzonePixels), 0f, 1f);
        float allowedPredictionOffset = predictionOffsetDistance * closeRangeScale;
        if (predictionOffsetDistance <= allowedPredictionOffset)
        {
            return predictedTargetPoint;
        }

        float offsetScale = allowedPredictionOffset / predictionOffsetDistance;
        return new PointF(
            observedTargetPoint.X + (predictionOffsetX * offsetScale),
            observedTargetPoint.Y + (predictionOffsetY * offsetScale));
    }

    private static Point ClampMoveToObservedTarget(Point move, PointF observedTargetPoint, PointF aimReferencePoint, AimRuntimeSettings settings)
    {
        if (move.IsEmpty)
        {
            return Point.Empty;
        }

        float observedOffsetX = observedTargetPoint.X - aimReferencePoint.X;
        float observedOffsetY = observedTargetPoint.Y - aimReferencePoint.Y;
        float observedDistance = MathF.Sqrt((observedOffsetX * observedOffsetX) + (observedOffsetY * observedOffsetY));
        if (observedDistance <= settings.DeadzonePixels)
        {
            return Point.Empty;
        }

        float unitX = observedOffsetX / observedDistance;
        float unitY = observedOffsetY / observedDistance;
        float forwardMove = (move.X * unitX) + (move.Y * unitY);
        float lateralMoveX = move.X - (forwardMove * unitX);
        float lateralMoveY = move.Y - (forwardMove * unitY);

        float minimumRemainingDistance = Math.Max(1f, settings.DeadzonePixels * 0.5f);
        float maxForwardMove = Math.Max(0f, observedDistance - minimumRemainingDistance);
        forwardMove = Math.Clamp(forwardMove, 0f, maxForwardMove);

        float closeRangePixels = Math.Max(settings.CloseRangeSlowdownPixels, settings.DeadzonePixels + 1f);
        float lateralScale = Math.Clamp((observedDistance - settings.DeadzonePixels) / (closeRangePixels - settings.DeadzonePixels), 0f, 1f);
        lateralMoveX *= lateralScale;
        lateralMoveY *= lateralScale;

        return new Point(
            (int)Math.Round((forwardMove * unitX) + lateralMoveX),
            (int)Math.Round((forwardMove * unitY) + lateralMoveY));
    }

    private static PointF GetAimReferencePoint()
    {
        Point cursorPosition = Cursor.Position;
        return new PointF(cursorPosition.X, cursorPosition.Y);
    }
}
