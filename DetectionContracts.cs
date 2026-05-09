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
    TensorRtEngine
}

internal sealed record DetectionResult(RectangleF Box, float Score, int ClassId, string Label);
internal sealed record DetectionRunResult(IReadOnlyList<DetectionResult> Detections);
internal sealed record DetectorOptions(DetectorBackend Backend, bool PreferGpu, float ScoreThreshold, string? TensorRtEnginePath = null);
