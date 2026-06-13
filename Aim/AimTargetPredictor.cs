using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 根据上一帧观测点估算目标速度，并对当前瞄准点做短期预测。
/// 预测状态保存在 AimRuntimeState 中，目标切换或重置时会重新开始观测。
/// </summary>
internal sealed class AimTargetPredictor
{
    private const float PredictionLeadSeconds = 0.018f;
    private const float MaxPredictionSeconds = 0.055f;
    private const float MaxPredictionPixels = 58f;
    private const float MaxTrackingGapSeconds = 0.12f;

    private readonly AimRuntimeState state;
    private TargetKalmanFilter? kalmanFilter;

    public AimTargetPredictor(AimRuntimeState state)
    {
        this.state = state;
    }

    public void Reset()
    {
        kalmanFilter = null;
    }

    public TargetPrediction Predict(PointF observedTargetPoint, bool resetTargetTracking, long now, long capturedTick)
    {
        if (resetTargetTracking || state.PreviousObservedTargetPoint is null || state.PreviousObservedTargetTick <= 0)
        {
            state.PreviousObservedTargetPoint = observedTargetPoint;
            kalmanFilter = new TargetKalmanFilter(observedTargetPoint);
            state.PreviousObservedTargetTick = now;
            return new TargetPrediction(observedTargetPoint, PointF.Empty);
        }

        float deltaSeconds = Math.Max(0.001f, (now - state.PreviousObservedTargetTick) / 1000f);
        if (deltaSeconds > MaxTrackingGapSeconds)
        {
            state.PreviousObservedTargetPoint = observedTargetPoint;
            kalmanFilter = new TargetKalmanFilter(observedTargetPoint);
            state.PreviousObservedTargetTick = now;
            return new TargetPrediction(observedTargetPoint, PointF.Empty);
        }

        kalmanFilter ??= new TargetKalmanFilter(state.PreviousObservedTargetPoint.Value);
        kalmanFilter.Update(observedTargetPoint, deltaSeconds);
        state.PreviousObservedTargetPoint = observedTargetPoint;
        state.PreviousObservedTargetTick = now;

        float predictionSeconds = Math.Clamp(((now - capturedTick) / 1000f) + PredictionLeadSeconds, 0f, MaxPredictionSeconds);
        return new TargetPrediction(kalmanFilter.Predict(predictionSeconds, MaxPredictionPixels), kalmanFilter.Velocity);
    }

    public bool TryPredictCurrent(long now, long capturedTick, long lastUpdateTick, out TargetPrediction prediction)
    {
        prediction = default;
        if (kalmanFilter is null || state.PreviousObservedTargetTick <= 0)
        {
            return false;
        }

        float staleSeconds = Math.Max(0f, (now - lastUpdateTick) / 1000f);
        if (staleSeconds > MaxTrackingGapSeconds)
        {
            return false;
        }

        float predictionSeconds = Math.Clamp(((now - capturedTick) / 1000f) + PredictionLeadSeconds, 0f, MaxPredictionSeconds);
        prediction = new TargetPrediction(kalmanFilter.Predict(predictionSeconds, MaxPredictionPixels), kalmanFilter.Velocity);
        return true;
    }
}

internal readonly record struct TargetPrediction(PointF PredictedPoint, PointF Velocity);
