using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 根据上一帧观测点估算目标速度，并对当前瞄准点做短期预测。
/// 预测状态保存在 AimRuntimeState 中，目标切换或重置时会重新开始观测。
/// </summary>
internal sealed class AimTargetPredictor
{
    private const float MaxPredictionSeconds = 0.085f;
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

    public TargetPrediction Predict(PointF observedTargetPoint, bool resetTargetTracking, long now, long capturedTick, AimRuntimeSettings settings)
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

        float predictionSeconds = GetPredictionSeconds(now, capturedTick, settings);
        return new TargetPrediction(kalmanFilter.Predict(predictionSeconds, Math.Max(0f, settings.MaxPredictionPixels)), kalmanFilter.Velocity);
    }

    public bool TryPredictCurrent(long now, long capturedTick, long lastUpdateTick, AimRuntimeSettings settings, out TargetPrediction prediction)
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

        float predictionSeconds = GetPredictionSeconds(now, capturedTick, settings);
        prediction = new TargetPrediction(kalmanFilter.Predict(predictionSeconds, Math.Max(0f, settings.MaxPredictionPixels)), kalmanFilter.Velocity);
        return true;
    }

    private static float GetPredictionSeconds(long now, long capturedTick, AimRuntimeSettings settings)
    {
        float leadSeconds = Math.Clamp(settings.PredictionLeadMilliseconds / 1000f, 0f, MaxPredictionSeconds);
        return Math.Clamp(((now - capturedTick) / 1000f) + leadSeconds, 0f, MaxPredictionSeconds);
    }
}

internal readonly record struct TargetPrediction(PointF PredictedPoint, PointF Velocity);
