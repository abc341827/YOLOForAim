using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 根据上一帧观测点估算目标速度，并对当前瞄准点做短期预测。
/// 预测状态保存在 AimRuntimeState 中，目标切换或重置时会重新开始观测。
/// </summary>
internal sealed class AimTargetPredictor
{
    private const float PredictionLeadSeconds = 0.02f;
    private const float MaxPredictionSeconds = 0.07f;
    private const float MaxPredictionPixels = 64f;

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
            state.PreviousObservedTargetTick = now;
            return observedTargetPoint;
        }

        float deltaSeconds = Math.Max(0.001f, (now - state.PreviousObservedTargetTick) / 1000f);
        PointF velocity = new(
            (observedTargetPoint.X - state.PreviousObservedTargetPoint.Value.X) / deltaSeconds,
            (observedTargetPoint.Y - state.PreviousObservedTargetPoint.Value.Y) / deltaSeconds);
        state.PreviousObservedTargetPoint = observedTargetPoint;
        state.PreviousObservedTargetTick = now;

        float predictionSeconds = Math.Clamp(((now - capturedTick) / 1000f) + PredictionLeadSeconds, 0f, MaxPredictionSeconds);
        return PredictPointFromVelocity(observedTargetPoint, velocity, predictionSeconds, MaxPredictionPixels);
    }
}
