using System.Drawing;

namespace YOLOForAim;

/// <summary>
/// 根据目标点和参考点计算最终鼠标相对移动量。
/// 只负责数学计算：P 控制、近距离减速、最大步长限制和整数化，不发送鼠标输入。
/// </summary>
internal static class AimMovementCalculator
{
    private const float VelocityFeedForwardSeconds = 0.028f;
    private const float MaxVelocityFeedForwardPixels = 12f;

    public static float GetDistanceToTarget(PointF targetPoint, PointF referencePoint)
    {
        float rawMoveX = targetPoint.X - referencePoint.X;
        float rawMoveY = targetPoint.Y - referencePoint.Y;
        return MathF.Sqrt((rawMoveX * rawMoveX) + (rawMoveY * rawMoveY));
    }

    public static Point CalculateMove(PointF targetPoint, PointF referencePoint, AimRuntimeSettings settings, float distanceToTarget, PointF targetVelocity)
    {
        float rawMoveX = targetPoint.X - referencePoint.X;
        float rawMoveY = targetPoint.Y - referencePoint.Y;
        float moveX = rawMoveX * settings.SmoothingFactor * settings.SpeedMultiplier;
        float moveY = rawMoveY * settings.SmoothingFactor * settings.SpeedMultiplier;

        PointF feedForwardMove = GetVelocityFeedForwardMove(targetVelocity, settings, distanceToTarget);
        moveX += feedForwardMove.X;
        moveY += feedForwardMove.Y;

        if (settings.CloseRangeSlowdownPixels > 1f && distanceToTarget < settings.CloseRangeSlowdownPixels)
        {
            float slowdownScale = Math.Clamp(distanceToTarget / settings.CloseRangeSlowdownPixels, 0.2f, 1f);
            moveX *= slowdownScale;
            moveY *= slowdownScale;
        }

        float smoothedDistance = MathF.Sqrt((moveX * moveX) + (moveY * moveY));
        float currentMaxStep = settings.MaxStepPixels * settings.SpeedMultiplier;
        if (smoothedDistance > currentMaxStep)
        {
            float scale = currentMaxStep / smoothedDistance;
            moveX *= scale;
            moveY *= scale;
        }

        return new Point(
            ClampMoveAxisToTarget((int)Math.Round(moveX), rawMoveX, MathF.Abs(feedForwardMove.X)),
            ClampMoveAxisToTarget((int)Math.Round(moveY), rawMoveY, MathF.Abs(feedForwardMove.Y)));
    }

    private static PointF GetVelocityFeedForwardMove(PointF targetVelocity, AimRuntimeSettings settings, float distanceToTarget)
    {
        float feedForwardX = targetVelocity.X * VelocityFeedForwardSeconds * settings.SpeedMultiplier;
        float feedForwardY = targetVelocity.Y * VelocityFeedForwardSeconds * settings.SpeedMultiplier;
        float feedForwardDistance = MathF.Sqrt((feedForwardX * feedForwardX) + (feedForwardY * feedForwardY));
        if (feedForwardDistance <= 0.001f)
        {
            return PointF.Empty;
        }

        float maxFeedForwardPixels = Math.Min(MaxVelocityFeedForwardPixels, Math.Max(2f, settings.MaxStepPixels * 0.35f));
        if (feedForwardDistance > maxFeedForwardPixels)
        {
            float scale = maxFeedForwardPixels / feedForwardDistance;
            feedForwardX *= scale;
            feedForwardY *= scale;
        }

        float closeRangePixels = Math.Max(settings.CloseRangeSlowdownPixels, settings.DeadzonePixels + 1f);
        float followScale = Math.Clamp((distanceToTarget + settings.DeadzonePixels) / closeRangePixels, 0.45f, 1f);
        return new PointF(feedForwardX * followScale, feedForwardY * followScale);
    }

    private static int ClampMoveAxisToTarget(int move, float rawMove, float feedForwardAllowance)
    {
        if (move == 0)
        {
            return 0;
        }

        if (MathF.Abs(rawMove) < 1f)
        {
            int feedForwardOnlyMaxMove = (int)MathF.Ceiling(feedForwardAllowance);
            return Math.Sign(move) * Math.Min(Math.Abs(move), feedForwardOnlyMaxMove);
        }

        int direction = Math.Sign(rawMove);
        if (Math.Sign(move) != direction)
        {
            int feedForwardOnlyMaxMove = (int)MathF.Ceiling(feedForwardAllowance);
            return Math.Sign(move) * Math.Min(Math.Abs(move), feedForwardOnlyMaxMove);
        }

        int maxAxisMove = (int)MathF.Floor(MathF.Abs(rawMove) + feedForwardAllowance);
        return direction * Math.Min(Math.Abs(move), maxAxisMove);
    }
}
