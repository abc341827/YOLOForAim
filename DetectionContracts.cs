using System.Drawing;

namespace YOLOForAim;

internal interface IDetector : IDisposable
{
    string ModelSummary { get; }

    DetectionRunResult Detect(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion);
}

internal enum DetectorBackend
{
    OnnxRuntimeDirectMl,
    TensorRtEngine,
    ColorRectangle
}

internal sealed record DetectionResult(RectangleF Box, float Score, int ClassId, string Label);
internal sealed record DetectionRunResult(IReadOnlyList<DetectionResult> Detections);
internal sealed record ColorDetectionOptions(float Hue, int Saturation, int Value, float HueTolerance, int SaturationTolerance, int ValueTolerance)
{
    public static ColorDetectionOptions Default { get; } = new(31f, 180, 220, 14f, 70, 90);
    public static ColorDetectionOptions DefaultSecondary { get; } = new(0f, 0, 20, 30f, 45, 55);
}

internal sealed record DetectorOptions(DetectorBackend Backend, bool PreferGpu, float ScoreThreshold, string? TensorRtEnginePath = null, ColorDetectionOptions? PrimaryColorDetection = null, ColorDetectionOptions? SecondaryColorDetection = null);
