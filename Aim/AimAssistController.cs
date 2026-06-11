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
        DetectionResult stableDetection = targetStabilizer.GetStabilizedDetection(currentDetection, captureBounds, resetTargetTracking);
        PointF targetPoint = targetPredictor.Predict(GetAimPoint(captureBounds, stableDetection, settings, targetWindowWidth), resetTargetTracking, now, capturedTick);
        state.LockedTargetScreenPoint = GetAimPoint(captureBounds, currentDetection, settings, targetWindowWidth);
        state.SmoothedTargetScreenPoint = state.SmoothedTargetScreenPoint is null || resetTargetTracking
            ? targetPoint
            : LerpPoint(state.SmoothedTargetScreenPoint.Value, targetPoint, settings.TargetTrackingBlend);
        targetPoint = state.SmoothedTargetScreenPoint.Value;
        missTracker.RegisterHit();
        state.PendingTargetSwitchTick = 0;
        state.HasAppliedInitialLockPull = true;

        float distanceToAimPoint = AimMovementCalculator.GetDistanceToTarget(targetPoint, aimReferencePoint);
        if (settings.PointBelowOffsetPixels <= 0f &&
            AimPointCalculator.IsAimReferenceInsideStableBox(captureBounds, stableDetection, aimReferencePoint, settings.StopLockSquareSizePixels, settings.StopLockTopOffsetPixels))
        {
            return;
        }

        if (distanceToAimPoint <= settings.DeadzonePixels || !assistGate.CanSendMove(settings, now, processedFrameVersion))
        {
            return;
        }

        Point finalMove = AimMovementCalculator.CalculateMove(targetPoint, aimReferencePoint, settings, distanceToAimPoint);
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

    private static PointF GetAimReferencePoint()
    {
        Point cursorPosition = Cursor.Position;
        return new PointF(cursorPosition.X, cursorPosition.Y);
    }
}
