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
    private const float VelocityBlendAt60Fps = 0.28f;
    private const float MaxObservedSpeedPixelsPerSecond = 5200f;
    private const float MaxTrackingGapSeconds = 0.12f;

    private readonly AimRuntimeState state;

    public AimTargetPredictor(AimRuntimeState state)
    {
        this.state = state;
    }

    public PointF Predict(PointF observedTargetPoint, bool resetTargetTracking, long now, long capturedTick)
    {
        if (resetTargetTracking || state.PreviousObservedTargetPoint is null || state.PreviousObservedTargetTick <= 0)
        {
            state.PreviousObservedTargetPoint = observedTargetPoint;
            state.FilteredTargetVelocity = null;
            state.PreviousObservedTargetTick = now;
            return observedTargetPoint;
        }

        float deltaSeconds = Math.Max(0.001f, (now - state.PreviousObservedTargetTick) / 1000f);
        if (deltaSeconds > MaxTrackingGapSeconds)
        {
            state.PreviousObservedTargetPoint = observedTargetPoint;
            state.FilteredTargetVelocity = null;
            state.PreviousObservedTargetTick = now;
            return observedTargetPoint;
        }

        PointF velocity = new(
            (observedTargetPoint.X - state.PreviousObservedTargetPoint.Value.X) / deltaSeconds,
            (observedTargetPoint.Y - state.PreviousObservedTargetPoint.Value.Y) / deltaSeconds);

        velocity = LimitVelocity(velocity, MaxObservedSpeedPixelsPerSecond);
        float velocityBlend = GetFrameRateAdjustedBlend(VelocityBlendAt60Fps, deltaSeconds);
        PointF filteredVelocity = state.FilteredTargetVelocity is null
            ? velocity
            : LerpPoint(state.FilteredTargetVelocity.Value, velocity, velocityBlend);

        state.PreviousObservedTargetPoint = observedTargetPoint;
        state.FilteredTargetVelocity = filteredVelocity;
        state.PreviousObservedTargetTick = now;

        float predictionSeconds = Math.Clamp(((now - capturedTick) / 1000f) + PredictionLeadSeconds, 0f, MaxPredictionSeconds);
        return PredictPointFromVelocity(observedTargetPoint, filteredVelocity, predictionSeconds, MaxPredictionPixels);
    }

    private static PointF LimitVelocity(PointF velocity, float maxSpeedPixelsPerSecond)
    {
        float speed = MathF.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y));
        if (speed <= maxSpeedPixelsPerSecond || speed <= 0f)
        {
            return velocity;
        }

        float scale = maxSpeedPixelsPerSecond / speed;
        return new PointF(velocity.X * scale, velocity.Y * scale);
    }
}
