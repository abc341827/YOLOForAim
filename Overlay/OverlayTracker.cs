using System.Drawing;
using static YOLOForAim.AimGeometry;

namespace YOLOForAim;

/// <summary>
/// 负责覆盖层检测框的跨帧匹配、平滑和短期预测。
/// 该类只处理显示层轨迹，不参与实际瞄准目标选择。
/// </summary>
internal sealed class OverlayTracker
{
    private const float MaxSpeedPixelsPerSecond = 900f;
    private const float MinMatchDistancePixels = 18f;
    private const float MinIou = 0.18f;
    private const float PositionBlend = 0.9f;
    private const float SizeBlend = 0.8f;
    private const float JitterDeadzonePixels = 4f;
    private const float PredictionLeadSeconds = 0.018f;
    private const float MaxPredictionSeconds = 0.06f;
    private const float MaxPredictionPixels = 48f;

    private OverlayTrack[] tracks = Array.Empty<OverlayTrack>();
    private long tracksTick;

    public IReadOnlyList<DetectionResult> Track(IReadOnlyList<DetectionResult> detections, long capturedTick, double currentInferenceFps)
    {
        long now = Environment.TickCount64;
        if (detections.Count == 0)
        {
            Clear(now);
            return Array.Empty<DetectionResult>();
        }

        double deltaSeconds = tracksTick > 0
            ? Math.Max(0.001d, (now - tracksTick) / 1000d)
            : 1d / Math.Max(1d, currentInferenceFps > 1d ? currentInferenceFps : 120d);
        float maxMatchDistance = Math.Max(MinMatchDistancePixels, (float)(MaxSpeedPixelsPerSecond * deltaSeconds));
        float positionBlend = GetFrameRateAdjustedBlend(PositionBlend, deltaSeconds);
        float sizeBlend = GetFrameRateAdjustedBlend(SizeBlend, deltaSeconds);
        float predictionSeconds = Math.Clamp(((now - capturedTick) / 1000f) + PredictionLeadSeconds, 0f, MaxPredictionSeconds);
        DetectionResult[] trackedDetections = new DetectionResult[detections.Count];
        var matchedTrackIndexes = new HashSet<int>();

        for (int detectionIndex = 0; detectionIndex < detections.Count; detectionIndex++)
        {
            DetectionResult detection = detections[detectionIndex];
            int matchedTrackIndex = FindBestTrackIndex(detection, maxMatchDistance, matchedTrackIndexes);
            DetectionResult trackedDetection = matchedTrackIndex >= 0
                ? UpdateTrackedDetection(tracks[matchedTrackIndex].Detection, detection, positionBlend, sizeBlend, (float)deltaSeconds, predictionSeconds)
                : detection;

            if (matchedTrackIndex >= 0)
            {
                matchedTrackIndexes.Add(matchedTrackIndex);
            }

            trackedDetections[detectionIndex] = trackedDetection;
        }

        tracks = trackedDetections.Select(detection => new OverlayTrack(detection, now)).ToArray();
        tracksTick = now;
        return trackedDetections;
    }

    public void Clear()
    {
        Clear(0);
    }

    private void Clear(long tick)
    {
        tracks = Array.Empty<OverlayTrack>();
        tracksTick = tick;
    }

    private int FindBestTrackIndex(DetectionResult detection, float maxMatchDistance, HashSet<int> matchedTrackIndexes)
    {
        int matchedTrackIndex = -1;
        double bestDistanceSquared = double.MaxValue;

        for (int trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            if (matchedTrackIndexes.Contains(trackIndex))
            {
                continue;
            }

            OverlayTrack track = tracks[trackIndex];
            if (track.Detection.ClassId != detection.ClassId)
            {
                continue;
            }

            PointF previousCenter = GetBoxCenter(track.Detection.Box);
            PointF currentCenter = GetBoxCenter(detection.Box);
            double distanceSquared = GetDistanceSquared(previousCenter, currentCenter);
            float iou = CalculateIou(track.Detection.Box, detection.Box);
            if (distanceSquared > (maxMatchDistance * maxMatchDistance) && iou < MinIou)
            {
                continue;
            }

            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                matchedTrackIndex = trackIndex;
            }
        }

        return matchedTrackIndex;
    }

    private static DetectionResult UpdateTrackedDetection(DetectionResult previousDetection, DetectionResult currentDetection, float positionBlend, float sizeBlend, float deltaSeconds, float predictionSeconds)
    {
        PointF previousCenter = GetBoxCenter(previousDetection.Box);
        PointF currentCenter = GetBoxCenter(currentDetection.Box);
        if (GetDistanceSquared(previousCenter, currentCenter) <= JitterDeadzonePixels * JitterDeadzonePixels)
        {
            return currentDetection with
            {
                Box = previousDetection.Box,
                Score = Math.Max(previousDetection.Score, currentDetection.Score)
            };
        }

        PointF trackedCenter = LerpPoint(previousCenter, currentCenter, positionBlend);
        PointF velocity = new(
            (currentCenter.X - previousCenter.X) / Math.Max(0.001f, deltaSeconds),
            (currentCenter.Y - previousCenter.Y) / Math.Max(0.001f, deltaSeconds));
        trackedCenter = PredictPointFromVelocity(trackedCenter, velocity, predictionSeconds, MaxPredictionPixels);
        SizeF guardedSize = new(
            GuardTrackedSize(previousDetection.Box.Width, currentDetection.Box.Width),
            GuardTrackedSize(previousDetection.Box.Height, currentDetection.Box.Height));
        SizeF trackedSize = LerpSize(previousDetection.Box.Size, guardedSize, sizeBlend);
        RectangleF trackedBox = CreateCenteredBox(trackedCenter, trackedSize);

        return currentDetection with
        {
            Box = trackedBox,
            Score = Math.Max(previousDetection.Score, currentDetection.Score)
        };
    }

    private sealed record OverlayTrack(DetectionResult Detection, long Timestamp);
}
