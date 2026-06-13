using System.Drawing;
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
    private const int MaxControlTargetAgeMs = 120;
    private const float ControlMoveAccelerationScale = 0.45f;
    private const float MaxControlDeltaSeconds = 1f / 30f;
    private const float MaxControlSmoothingFactor = 0.22f;

    private readonly object syncRoot = new();
    private readonly AimRuntimeState state = new();
    private readonly AimTargetSelector targetSelector = new();
    private readonly AimTargetStabilizer targetStabilizer;
    private readonly AimTargetPredictor targetPredictor;
    private readonly AimAssistGate assistGate;
    private readonly AimMissTracker missTracker;
    private bool hasControlTarget;
    private long lastTargetUpdateTick;
    private long lastTargetCapturedTick;
    private int lastTargetUpdateFrameVersion = -1;
    private Rectangle lastControlCaptureBounds;
    private Rectangle lastControlAimReferenceBounds;
    private DetectionResult? lastControlStableDetection;
    private PointF lastControlObservedTargetPoint;
    private Point lastControlMove = Point.Empty;
    private long lastControlMoveTick;
    private readonly List<PendingAimMove> pendingAimMoves = new();
    private PointF lastCalibrationError;
    private Point lastCalibrationMove;
    private long lastCalibrationMoveSentTick;
    private int calibrationSampleCount;
    private int calibrationSampleCountX;
    private int calibrationSampleCountY;
    private float calibrationSumPixelsPerMouseUnitX;
    private float calibrationSumPixelsPerMouseUnitY;
    private float calibratedPixelsPerMouseUnitX;
    private float calibratedPixelsPerMouseUnitY;
    private bool isCalibrationActive;
    private string calibrationDiagnostics = string.Empty;

    public AimAssistController()
    {
        targetStabilizer = new AimTargetStabilizer(state);
        targetPredictor = new AimTargetPredictor(state);
        assistGate = new AimAssistGate(state);
        missTracker = new AimMissTracker(state);
    }

    public string CalibrationDiagnostics
    {
        get
        {
            lock (syncRoot)
            {
                return calibrationDiagnostics;
            }
        }
    }

    public DetectionResult? StabilizedLockedDetection
    {
        get
        {
            lock (syncRoot)
            {
                return state.StabilizedLockedDetection;
            }
        }
    }

    public int SuppressOverlayFrameVersion
    {
        get
        {
            lock (syncRoot)
            {
                return state.SuppressOverlayFrameVersion;
            }
        }
    }

    public void ResetRuntime()
    {
        lock (syncRoot)
        {
            state.ResetRuntime();
            ClearControlTarget();
            targetPredictor.Reset();
            isCalibrationActive = false;
        }
    }

    public void StartCalibration()
    {
        lock (syncRoot)
        {
            ResetTrackingCore();
            pendingAimMoves.Clear();
            state.PendingAimCompensation = PointF.Empty;
            isCalibrationActive = true;
            lastCalibrationError = PointF.Empty;
            lastCalibrationMove = Point.Empty;
            lastCalibrationMoveSentTick = 0;
            calibrationSampleCount = 0;
            calibrationSampleCountX = 0;
            calibrationSampleCountY = 0;
            calibrationSumPixelsPerMouseUnitX = 0f;
            calibrationSumPixelsPerMouseUnitY = 0f;
            calibrationDiagnostics = "校准模式: 已启动。请保持目标可见，程序会慢慢把检测框移动到窗口中心。";
        }
    }

    public void ResetTracking()
    {
        lock (syncRoot)
        {
            ResetTrackingCore();
        }
    }

    public void TryMoveToNearestDetection(IReadOnlyList<DetectionResult> detections, Rectangle captureBounds, Rectangle aimReferenceBounds, int processedFrameVersion, long capturedTick, AimRuntimeSettings settings, int targetWindowWidth)
    {
        lock (syncRoot)
        {
            long now = Environment.TickCount64;
            if (processedFrameVersion <= state.SuspendAimUntilFrameVersion || now < state.SuspendAimUntilTick)
            {
                return;
            }

            bool calibrationMode = IsCalibrationMode(settings);
            if (captureBounds.IsEmpty || (!calibrationMode && !assistGate.IsAssistActive(settings, now)))
            {
                ResetTrackingCore();
                return;
            }

            RefreshPendingAimCompensation(capturedTick);

            if (detections.Count == 0)
            {
                if (missTracker.RegisterMiss(settings))
                {
                    ResetTrackingCore();
                }

                return;
            }

            PointF aimReferencePoint = GetAimReferencePoint(aimReferenceBounds);
            TargetCandidate? nearestDetection = SelectTargetCandidate(detections, captureBounds, aimReferencePoint, settings, targetWindowWidth);
            if (nearestDetection is null)
            {
                if (missTracker.RegisterMiss(settings))
                {
                    ResetTrackingCore();
                }

                return;
            }

            DetectionResult currentDetection = AimPointCalculator.GetControlDetection(nearestDetection.Detection, settings, targetWindowWidth);
            bool resetTargetTracking = state.StabilizedLockedDetection is null || !IsLikelySameLockedTarget(currentDetection, captureBounds, settings, targetWindowWidth);
            PointF rawObservedTargetPoint = GetAimPoint(captureBounds, currentDetection, settings, targetWindowWidth);
            if (ShouldRejectSuddenTargetJump(rawObservedTargetPoint, resetTargetTracking, settings))
            {
                if (missTracker.RegisterMiss(settings))
                {
                    ResetTrackingCore();
                }

                return;
            }

            DetectionResult stableDetection = targetStabilizer.GetStabilizedDetection(currentDetection, captureBounds, resetTargetTracking);
            PointF observedTargetPoint = GetAimPoint(captureBounds, stableDetection, settings, targetWindowWidth);
            UpdateCalibrationObservation(observedTargetPoint, aimReferencePoint, capturedTick, settings);
            TargetPrediction targetPrediction = targetPredictor.Predict(observedTargetPoint, resetTargetTracking, now, capturedTick, settings);
            PointF targetPoint = targetPrediction.PredictedPoint;
            state.LockedTargetScreenPoint = GetAimPoint(captureBounds, currentDetection, settings, targetWindowWidth);
            state.SmoothedTargetScreenPoint = state.SmoothedTargetScreenPoint is null || resetTargetTracking
                ? targetPoint
                : LerpPoint(state.SmoothedTargetScreenPoint.Value, targetPoint, GetAdaptiveTargetTrackingBlend(state.SmoothedTargetScreenPoint.Value, targetPoint, settings));
            missTracker.RegisterHit();
            state.PendingTargetSwitchTick = 0;
            state.HasAppliedInitialLockPull = true;

            hasControlTarget = true;
            lastTargetUpdateTick = now;
            lastTargetCapturedTick = capturedTick;
            lastTargetUpdateFrameVersion = processedFrameVersion;
            lastControlCaptureBounds = captureBounds;
            lastControlAimReferenceBounds = aimReferenceBounds;
            lastControlStableDetection = stableDetection;
            lastControlObservedTargetPoint = observedTargetPoint;
            state.LastPendingCompensationFrameVersion = processedFrameVersion;
        }
    }

    public void TryRunControlTick(AimRuntimeSettings settings)
    {
        Point finalMove;
        lock (syncRoot)
        {
            long now = Environment.TickCount64;
            bool calibrationMode = IsCalibrationMode(settings);
            bool assistActive = calibrationMode || assistGate.IsAssistActive(settings, now);
            if (!hasControlTarget || !assistActive)
            {
                if (!assistActive)
                {
                    ResetTrackingCore();
                }

                return;
            }

            if (now - lastTargetUpdateTick > MaxControlTargetAgeMs ||
                !targetPredictor.TryPredictCurrent(now, lastTargetCapturedTick, lastTargetUpdateTick, settings, out TargetPrediction targetPrediction))
            {
                ClearControlTarget();
                return;
            }

            PointF aimReferencePoint = GetAimReferencePoint(lastControlAimReferenceBounds);
            PointF observedTargetPoint = ApplyPendingCompensation(lastControlObservedTargetPoint);
            PointF predictedTargetPoint = ApplyPendingCompensation(targetPrediction.PredictedPoint);
            PointF targetPoint = settings.MaxPredictionPixels > 0f || settings.PredictionLeadMilliseconds > 0f
                ? predictedTargetPoint
                : observedTargetPoint;
            float distanceToAimPoint = AimMovementCalculator.GetDistanceToTarget(targetPoint, aimReferencePoint);
            if (distanceToAimPoint <= settings.DeadzonePixels)
            {
                if (calibrationMode)
                {
                    CompleteCalibration();
                }

                lastControlMove = Point.Empty;
                lastControlMoveTick = 0;
                return;
            }

            if (!assistGate.CanSendMoveByTime(settings, now))
            {
                return;
            }

            if (calibrationMode && !state.PendingAimCompensation.IsEmpty)
            {
                return;
            }

            if (lastTargetUpdateFrameVersion <= state.LastAimMoveFrameVersion)
            {
                return;
            }

            finalMove = calibrationMode
                ? CalculateCalibrationMove(targetPoint, aimReferencePoint, settings)
                : CalculateDirectCenteringMove(targetPoint, aimReferencePoint, settings, targetPrediction.Velocity, calibratedPixelsPerMouseUnitX, calibratedPixelsPerMouseUnitY);

            if (finalMove.IsEmpty)
            {
                lastControlMove = Point.Empty;
                return;
            }

            lastControlMove = finalMove;
            lastControlMoveTick = now;
            SendRelativeMouseMove(finalMove.X, finalMove.Y);
            long sentTick = Environment.TickCount64;
            if (calibrationMode)
            {
                lastCalibrationError = new PointF(targetPoint.X - aimReferencePoint.X, targetPoint.Y - aimReferencePoint.Y);
                lastCalibrationMove = finalMove;
                lastCalibrationMoveSentTick = sentTick;
            }

            RegisterPendingAimMove(finalMove, sentTick);
            assistGate.MarkMoveSent(sentTick);
            state.LastAimMoveFrameVersion = lastTargetUpdateFrameVersion;
            ResetPullStateForNextDetection();
        }
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
            detection => GetAimPoint(captureBounds, AimPointCalculator.GetControlDetection(detection, settings, targetWindowWidth), settings, targetWindowWidth));
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
            DetectionResult controlDetection = AimPointCalculator.GetControlDetection(detection, settings, targetWindowWidth);
            if (controlDetection.ClassId != lockedDetection.ClassId || controlDetection.Label != lockedDetection.Label)
            {
                continue;
            }

            PointF currentCenter = GetBoxCenter(controlDetection.Box);
            double centerDistanceSquared = GetDistanceSquared(lockedCenter, currentCenter);
            float iou = CalculateIou(lockedDetection.Box, controlDetection.Box);
            if (iou < LockedTargetMatchMinIou && centerDistanceSquared > maxCenterDistanceSquared)
            {
                continue;
            }

            double score = centerDistanceSquared - (iou * maxCenterDistanceSquared);
            if (score >= bestScore)
            {
                continue;
            }

            PointF targetPoint = GetAimPoint(captureBounds, controlDetection, settings, targetWindowWidth);
            bestCandidate = new TargetCandidate(controlDetection, targetPoint, GetDistanceSquared(state.SmoothedTargetScreenPoint ?? state.LockedTargetScreenPoint ?? targetPoint, targetPoint));
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

    private static Point CalculateDirectCenteringMove(PointF targetPoint, PointF aimReferencePoint, AimRuntimeSettings settings, PointF targetVelocity, float calibratedPixelsPerMouseUnitX, float calibratedPixelsPerMouseUnitY)
    {
        float errorX = targetPoint.X - aimReferencePoint.X;
        float errorY = targetPoint.Y - aimReferencePoint.Y;
        float distance = MathF.Sqrt((errorX * errorX) + (errorY * errorY));
        if (distance <= settings.DeadzonePixels)
        {
            return Point.Empty;
        }

        float gain = Math.Max(0.01f, settings.SmoothingFactor * settings.SpeedMultiplier);
        float moveX = MathF.Abs(calibratedPixelsPerMouseUnitX) > 0.001f
            ? (errorX / calibratedPixelsPerMouseUnitX) * gain
            : errorX * gain;
        float moveY = MathF.Abs(calibratedPixelsPerMouseUnitY) > 0.001f
            ? (errorY / calibratedPixelsPerMouseUnitY) * gain
            : errorY * gain;
        if (settings.VelocityFeedForwardMaxPixels > 0f)
        {
            float feedForwardSeconds = Math.Clamp(settings.PredictionLeadMilliseconds / 1000f, 0f, 0.085f);
            float feedForwardX = targetVelocity.X * feedForwardSeconds * settings.SpeedMultiplier;
            float feedForwardY = targetVelocity.Y * feedForwardSeconds * settings.SpeedMultiplier;
            float feedForwardDistance = MathF.Sqrt((feedForwardX * feedForwardX) + (feedForwardY * feedForwardY));
            if (feedForwardDistance > settings.VelocityFeedForwardMaxPixels && feedForwardDistance > 0.001f)
            {
                float feedForwardScale = settings.VelocityFeedForwardMaxPixels / feedForwardDistance;
                feedForwardX *= feedForwardScale;
                feedForwardY *= feedForwardScale;
            }

            moveX += feedForwardX;
            moveY += feedForwardY;
        }

        return ClampMoveLength(moveX, moveY, Math.Max(1f, settings.MaxStepPixels * Math.Max(0.01f, settings.SpeedMultiplier)));
    }

    private static Point CalculateCalibrationMove(PointF targetPoint, PointF aimReferencePoint, AimRuntimeSettings settings)
    {
        float errorX = targetPoint.X - aimReferencePoint.X;
        float errorY = targetPoint.Y - aimReferencePoint.Y;
        float distance = MathF.Sqrt((errorX * errorX) + (errorY * errorY));
        if (distance <= settings.DeadzonePixels)
        {
            return Point.Empty;
        }

        return ClampMoveLength(errorX, errorY, Math.Max(1f, settings.CalibrationStepPixels));
    }

    private static Point ClampMoveLength(float moveX, float moveY, float maxMove)
    {
        float moveDistance = MathF.Sqrt((moveX * moveX) + (moveY * moveY));
        if (moveDistance <= 0.001f)
        {
            return Point.Empty;
        }

        if (moveDistance > maxMove)
        {
            float scale = maxMove / moveDistance;
            moveX *= scale;
            moveY *= scale;
        }

        return new Point((int)Math.Round(moveX), (int)Math.Round(moveY));
    }

    private void UpdateCalibrationObservation(PointF observedTargetPoint, PointF aimReferencePoint, long capturedTick, AimRuntimeSettings settings)
    {
        if (!IsCalibrationMode(settings))
        {
            return;
        }

        PointF currentError = new(observedTargetPoint.X - aimReferencePoint.X, observedTargetPoint.Y - aimReferencePoint.Y);
        if (lastCalibrationMoveSentTick > 0 && capturedTick > lastCalibrationMoveSentTick)
        {
            float reducedX = lastCalibrationError.X - currentError.X;
            float reducedY = lastCalibrationError.Y - currentError.Y;
            if (lastCalibrationMove.X != 0 && MathF.Abs(reducedX) > 0.01f)
            {
                calibrationSumPixelsPerMouseUnitX += reducedX / lastCalibrationMove.X;
                calibrationSampleCountX++;
            }

            if (lastCalibrationMove.Y != 0 && MathF.Abs(reducedY) > 0.01f)
            {
                calibrationSumPixelsPerMouseUnitY += reducedY / lastCalibrationMove.Y;
                calibrationSampleCountY++;
            }

            calibrationSampleCount++;
            lastCalibrationMoveSentTick = 0;
        }

        float averageX = calibrationSampleCountX > 0 ? calibrationSumPixelsPerMouseUnitX / calibrationSampleCountX : calibratedPixelsPerMouseUnitX;
        float averageY = calibrationSampleCountY > 0 ? calibrationSumPixelsPerMouseUnitY / calibrationSampleCountY : calibratedPixelsPerMouseUnitY;
        calibrationDiagnostics = $"校准模式: 当前误差=({currentError.X:F1},{currentError.Y:F1}) px, 上次指令=({lastCalibrationMove.X},{lastCalibrationMove.Y}), 样本={calibrationSampleCount}, 画面px/鼠标单位≈X:{averageX:F3} Y:{averageY:F3}";
    }

    private bool IsCalibrationMode(AimRuntimeSettings settings)
    {
        return isCalibrationActive || settings.CalibrationMode;
    }

    private void CompleteCalibration()
    {
        float averageX = calibrationSampleCountX > 0 ? calibrationSumPixelsPerMouseUnitX / calibrationSampleCountX : calibratedPixelsPerMouseUnitX;
        float averageY = calibrationSampleCountY > 0 ? calibrationSumPixelsPerMouseUnitY / calibrationSampleCountY : calibratedPixelsPerMouseUnitY;
        if (MathF.Abs(averageX) > 0.001f)
        {
            calibratedPixelsPerMouseUnitX = averageX;
        }

        if (MathF.Abs(averageY) > 0.001f)
        {
            calibratedPixelsPerMouseUnitY = averageY;
        }

        isCalibrationActive = false;
        pendingAimMoves.Clear();
        state.PendingAimCompensation = PointF.Empty;
        calibrationDiagnostics = $"校准完成: 画面px/鼠标单位 X={calibratedPixelsPerMouseUnitX:F3}, Y={calibratedPixelsPerMouseUnitY:F3}。左键瞄准将按该比例换算移动。";
        ResetPullStateForNextDetection();
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

    private static Point ClampMoveToObservedTarget(Point move, PointF observedTargetPoint, PointF aimReferencePoint, AimRuntimeSettings settings, bool velocityFollow)
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
            return velocityFollow ? move : Point.Empty;
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
        float lateralScale = velocityFollow
            ? 1f
            : Math.Clamp((observedDistance - settings.DeadzonePixels) / (closeRangePixels - settings.DeadzonePixels), 0f, 1f);
        lateralMoveX *= lateralScale;
        lateralMoveY *= lateralScale;

        return new Point(
            (int)Math.Round((forwardMove * unitX) + lateralMoveX),
            (int)Math.Round((forwardMove * unitY) + lateralMoveY));
    }

    private static Point LimitMoveAcceleration(Point move, Point previousMove, PointF targetPoint, PointF aimReferencePoint, AimRuntimeSettings settings)
    {
        if (previousMove.IsEmpty || move.IsEmpty)
        {
            return move;
        }

        float errorX = targetPoint.X - aimReferencePoint.X;
        float errorY = targetPoint.Y - aimReferencePoint.Y;
        float desiredDotPrevious = (move.X * previousMove.X) + (move.Y * previousMove.Y);
        float previousDotError = (previousMove.X * errorX) + (previousMove.Y * errorY);
        if (desiredDotPrevious <= 0f || previousDotError <= 0f)
        {
            return move;
        }

        float maxDelta = Math.Max(1f, settings.MaxStepPixels * settings.SpeedMultiplier * ControlMoveAccelerationScale);
        float deltaX = move.X - previousMove.X;
        float deltaY = move.Y - previousMove.Y;
        float deltaDistance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (deltaDistance <= maxDelta || deltaDistance <= 0.001f)
        {
            return move;
        }

        float scale = maxDelta / deltaDistance;
        return new Point(
            (int)Math.Round(previousMove.X + (deltaX * scale)),
            (int)Math.Round(previousMove.Y + (deltaY * scale)));
    }

    private static Point ClampMoveNoOvershoot(Point move, PointF targetPoint, PointF aimReferencePoint, AimRuntimeSettings settings, bool velocityFollow)
    {
        if (move.IsEmpty)
        {
            return Point.Empty;
        }

        float errorX = targetPoint.X - aimReferencePoint.X;
        float errorY = targetPoint.Y - aimReferencePoint.Y;
        float distance = MathF.Sqrt((errorX * errorX) + (errorY * errorY));
        float stopDistance = Math.Max(1f, settings.DeadzonePixels * 0.85f);
        if (distance <= stopDistance)
        {
            return velocityFollow ? move : Point.Empty;
        }

        float unitX = errorX / distance;
        float unitY = errorY / distance;
        float forwardMove = (move.X * unitX) + (move.Y * unitY);
        if (forwardMove <= 0f)
        {
            return velocityFollow ? move : Point.Empty;
        }

        if (velocityFollow)
        {
            return move;
        }

        float maxForwardMove = Math.Max(0f, distance - stopDistance);
        forwardMove = Math.Min(forwardMove, maxForwardMove);
        if (forwardMove < 0.5f)
        {
            return Point.Empty;
        }

        return new Point(
            (int)Math.Round(unitX * forwardMove),
            (int)Math.Round(unitY * forwardMove));
    }

    private bool IsAimReferenceInsideCompensatedStableBox(Rectangle captureBounds, DetectionResult detection, PointF aimReferencePoint, AimRuntimeSettings settings)
    {
        RectangleF stopBounds = GetCompensatedStableBoxBounds(captureBounds, detection, settings);
        return stopBounds.Contains(aimReferencePoint);
    }

    private Point ClampMoveToCompensatedStableBox(Point move, Rectangle captureBounds, DetectionResult detection, PointF aimReferencePoint, AimRuntimeSettings settings)
    {
        if (move.IsEmpty)
        {
            return Point.Empty;
        }

        RectangleF stopBounds = GetCompensatedStableBoxBounds(captureBounds, detection, settings);
        if (stopBounds.Contains(aimReferencePoint))
        {
            return Point.Empty;
        }

        PointF moveEndPoint = new(aimReferencePoint.X + move.X, aimReferencePoint.Y + move.Y);
        if (!TryGetSegmentRectangleEntry(aimReferencePoint, moveEndPoint, stopBounds, out float entryAmount))
        {
            return move;
        }

        if (entryAmount <= 0.001f)
        {
            return Point.Empty;
        }

        int clampedMoveX = (int)Math.Round(move.X * entryAmount);
        int clampedMoveY = (int)Math.Round(move.Y * entryAmount);
        return clampedMoveX == 0 && clampedMoveY == 0
            ? Point.Empty
            : new Point(clampedMoveX, clampedMoveY);
    }

    private RectangleF GetCompensatedStableBoxBounds(Rectangle captureBounds, DetectionResult detection, AimRuntimeSettings settings)
    {
        RectangleF screenBounds = AimPointCalculator.GetDetectionScreenBounds(captureBounds, detection);
        RectangleF compensatedBounds = new(
            screenBounds.X - state.PendingAimCompensation.X,
            screenBounds.Y - state.PendingAimCompensation.Y,
            screenBounds.Width,
            screenBounds.Height);
        return AimPointCalculator.GetLockSquareBounds(compensatedBounds, settings.StopLockSquareSizePixels, settings.StopLockTopOffsetPixels);
    }

    private static bool TryGetSegmentRectangleEntry(PointF start, PointF end, RectangleF bounds, out float entryAmount)
    {
        entryAmount = 0f;
        float exitAmount = 1f;
        float deltaX = end.X - start.X;
        float deltaY = end.Y - start.Y;

        return ClipSegmentAxis(-deltaX, start.X - bounds.Left, ref entryAmount, ref exitAmount) &&
            ClipSegmentAxis(deltaX, bounds.Right - start.X, ref entryAmount, ref exitAmount) &&
            ClipSegmentAxis(-deltaY, start.Y - bounds.Top, ref entryAmount, ref exitAmount) &&
            ClipSegmentAxis(deltaY, bounds.Bottom - start.Y, ref entryAmount, ref exitAmount) &&
            entryAmount <= exitAmount &&
            entryAmount <= 1f &&
            exitAmount >= 0f;
    }

    private static bool ClipSegmentAxis(float direction, float distance, ref float entryAmount, ref float exitAmount)
    {
        if (MathF.Abs(direction) <= 0.001f)
        {
            return distance >= 0f;
        }

        float amount = distance / direction;
        if (direction < 0f)
        {
            if (amount > exitAmount)
            {
                return false;
            }

            if (amount > entryAmount)
            {
                entryAmount = amount;
            }

            return true;
        }

        if (amount < entryAmount)
        {
            return false;
        }

        if (amount < exitAmount)
        {
            exitAmount = amount;
        }

        return true;
    }

    private PointF ApplyPendingCompensation(PointF point)
    {
        return new PointF(
            point.X - state.PendingAimCompensation.X,
            point.Y - state.PendingAimCompensation.Y);
    }

    private void RefreshPendingAimCompensation(long capturedTick)
    {
        if (pendingAimMoves.Count == 0)
        {
            state.PendingAimCompensation = PointF.Empty;
            return;
        }

        float pendingX = 0f;
        float pendingY = 0f;
        int writeIndex = 0;
        for (int i = 0; i < pendingAimMoves.Count; i++)
        {
            PendingAimMove pendingMove = pendingAimMoves[i];
            if (pendingMove.SentTick <= capturedTick)
            {
                continue;
            }

            pendingAimMoves[writeIndex++] = pendingMove;
            pendingX += pendingMove.Move.X;
            pendingY += pendingMove.Move.Y;
        }

        if (writeIndex < pendingAimMoves.Count)
        {
            pendingAimMoves.RemoveRange(writeIndex, pendingAimMoves.Count - writeIndex);
        }

        state.PendingAimCompensation = new PointF(pendingX, pendingY);
    }

    private void RegisterPendingAimMove(Point move, long sentTick)
    {
        if (move.IsEmpty)
        {
            return;
        }

        pendingAimMoves.Add(new PendingAimMove(move, sentTick));
        state.PendingAimCompensation = new PointF(
            state.PendingAimCompensation.X + move.X,
            state.PendingAimCompensation.Y + move.Y);
    }

    private void ResetTrackingCore()
    {
        state.ResetTracking();
        targetPredictor.Reset();
        ClearControlTarget();
    }

    private void ClearControlTarget()
    {
        hasControlTarget = false;
        lastTargetUpdateTick = 0;
        lastTargetCapturedTick = 0;
        lastTargetUpdateFrameVersion = -1;
        lastControlCaptureBounds = Rectangle.Empty;
        lastControlAimReferenceBounds = Rectangle.Empty;
        lastControlStableDetection = null;
        lastControlMove = Point.Empty;
        lastControlMoveTick = 0;
        pendingAimMoves.Clear();
        state.PendingAimCompensation = PointF.Empty;
        state.LastPendingCompensationFrameVersion = -1;
    }

    private void CompletePullCycle(int suspendUntilFrameVersion)
    {
        state.ResetTracking();
        targetPredictor.Reset();
        hasControlTarget = false;
        lastTargetUpdateTick = 0;
        lastTargetCapturedTick = 0;
        lastTargetUpdateFrameVersion = -1;
        lastControlCaptureBounds = Rectangle.Empty;
        lastControlAimReferenceBounds = Rectangle.Empty;
        lastControlStableDetection = null;
        lastControlObservedTargetPoint = PointF.Empty;
        lastControlMove = Point.Empty;
        lastControlMoveTick = 0;
        pendingAimMoves.Clear();
        state.PendingAimCompensation = PointF.Empty;
        state.LastPendingCompensationFrameVersion = -1;
        state.SuspendAimUntilFrameVersion = Math.Max(state.SuspendAimUntilFrameVersion, suspendUntilFrameVersion);
    }

    private void ResetPullStateForNextDetection()
    {
        state.ResetTracking();
        targetPredictor.Reset();
        hasControlTarget = false;
        lastTargetUpdateTick = 0;
        lastTargetCapturedTick = 0;
        lastTargetUpdateFrameVersion = -1;
        lastControlCaptureBounds = Rectangle.Empty;
        lastControlAimReferenceBounds = Rectangle.Empty;
        lastControlStableDetection = null;
        lastControlObservedTargetPoint = PointF.Empty;
        lastControlMove = Point.Empty;
        lastControlMoveTick = 0;
        state.LastPendingCompensationFrameVersion = -1;
    }

    private static PointF GetAimReferencePoint(Rectangle captureBounds)
    {
        return new PointF(
            captureBounds.Left + (captureBounds.Width / 2f),
            captureBounds.Top + (captureBounds.Height / 2f));
    }

    private readonly record struct PendingAimMove(Point Move, long SentTick);
}
