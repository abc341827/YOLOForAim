using System.Drawing;

namespace YOLOForAim;

/// <summary>
/// 根据目标点和参考点计算最终鼠标相对移动量。
/// 只负责数学计算：P 控制、近距离减速、最大步长限制和整数化，不发送鼠标输入。
/// </summary>
internal static class AimMovementCalculator
{
    public static float GetDistanceToTarget(PointF targetPoint, PointF referencePoint)
    {
        float rawMoveX = targetPoint.X - referencePoint.X;
        float rawMoveY = targetPoint.Y - referencePoint.Y;
        return MathF.Sqrt((rawMoveX * rawMoveX) + (rawMoveY * rawMoveY));
    }

    public static Point CalculateMove(PointF targetPoint, PointF referencePoint, AimRuntimeSettings settings, float distanceToTarget)
    {
        float rawMoveX = targetPoint.X - referencePoint.X;
        float rawMoveY = targetPoint.Y - referencePoint.Y;
        float moveX = rawMoveX * settings.SmoothingFactor * settings.SpeedMultiplier;
        float moveY = rawMoveY * settings.SmoothingFactor * settings.SpeedMultiplier;

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
            ClampMoveAxisToTarget((int)Math.Round(moveX), rawMoveX),
            ClampMoveAxisToTarget((int)Math.Round(moveY), rawMoveY));
    }

    private static int ClampMoveAxisToTarget(int move, float rawMove)
    {
        if (move == 0 || MathF.Abs(rawMove) < 1f)
        {
            return 0;
        }

        int direction = Math.Sign(rawMove);
        if (Math.Sign(move) != direction)
        {
            return 0;
        }

        int maxAxisMove = (int)MathF.Floor(MathF.Abs(rawMove));
        return direction * Math.Min(Math.Abs(move), maxAxisMove);
    }
}
