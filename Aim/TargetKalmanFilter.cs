using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 2D 恒速模型卡尔曼滤波器，用检测点估计目标位置和速度。
/// </summary>
internal sealed class TargetKalmanFilter
{
    private const float InitialPositionVariance = 36f;
    private const float InitialVelocityVariance = 900000f;
    private const float MeasurementNoiseVariance = 64f;
    private const float AccelerationNoiseVariance = 180000f;

    private readonly AxisKalmanFilter xFilter;
    private readonly AxisKalmanFilter yFilter;

    public TargetKalmanFilter(PointF initialPosition)
    {
        xFilter = new AxisKalmanFilter(initialPosition.X);
        yFilter = new AxisKalmanFilter(initialPosition.Y);
    }

    public PointF Position => new(xFilter.Position, yFilter.Position);

    public PointF Velocity => new(xFilter.Velocity, yFilter.Velocity);

    public PointF Update(PointF measurement, float deltaSeconds)
    {
        float clampedDeltaSeconds = Math.Clamp(deltaSeconds, 0.001f, 0.12f);
        xFilter.Predict(clampedDeltaSeconds);
        yFilter.Predict(clampedDeltaSeconds);
        xFilter.Update(measurement.X);
        yFilter.Update(measurement.Y);
        return Position;
    }

    public PointF Predict(float predictionSeconds, float maxPredictionPixels)
    {
        return PredictPointFromVelocity(Position, Velocity, predictionSeconds, maxPredictionPixels);
    }

    private sealed class AxisKalmanFilter
    {
        private float p00 = InitialPositionVariance;
        private float p01;
        private float p10;
        private float p11 = InitialVelocityVariance;

        public AxisKalmanFilter(float initialPosition)
        {
            Position = initialPosition;
        }

        public float Position { get; private set; }

        public float Velocity { get; private set; }

        public void Predict(float deltaSeconds)
        {
            Position += Velocity * deltaSeconds;

            float dt2 = deltaSeconds * deltaSeconds;
            float dt3 = dt2 * deltaSeconds;
            float dt4 = dt2 * dt2;
            float q00 = 0.25f * dt4 * AccelerationNoiseVariance;
            float q01 = 0.5f * dt3 * AccelerationNoiseVariance;
            float q11 = dt2 * AccelerationNoiseVariance;

            float predictedP00 = p00 + (deltaSeconds * (p10 + p01)) + (dt2 * p11) + q00;
            float predictedP01 = p01 + (deltaSeconds * p11) + q01;
            float predictedP10 = p10 + (deltaSeconds * p11) + q01;
            float predictedP11 = p11 + q11;

            p00 = predictedP00;
            p01 = predictedP01;
            p10 = predictedP10;
            p11 = predictedP11;
        }

        public void Update(float measurement)
        {
            float innovation = measurement - Position;
            float innovationVariance = p00 + MeasurementNoiseVariance;
            if (innovationVariance <= 0f)
            {
                return;
            }

            float k0 = p00 / innovationVariance;
            float k1 = p10 / innovationVariance;

            float oldP00 = p00;
            float oldP01 = p01;
            float oldP10 = p10;
            float oldP11 = p11;

            Position += k0 * innovation;
            Velocity += k1 * innovation;

            p00 = (1f - k0) * oldP00;
            p01 = (1f - k0) * oldP01;
            p10 = oldP10 - (k1 * oldP00);
            p11 = oldP11 - (k1 * oldP01);
        }
    }
}
