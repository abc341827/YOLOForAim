namespace YOLOForAim;

/// <summary>
/// 组合检测器：同一帧同时运行颜色检测和主检测器，颜色命中时优先使用颜色结果，否则使用主检测器结果。
/// </summary>
internal sealed class ColorPriorityDetector : IDetector, IColorDetectionOptionsSink
{
    private readonly ColorRectangleDetector colorDetector;
    private readonly IDetector fallbackDetector;

    public ColorPriorityDetector(ColorRectangleDetector colorDetector, IDetector fallbackDetector)
    {
        this.colorDetector = colorDetector;
        this.fallbackDetector = fallbackDetector;
    }

    public string ModelSummary => $"颜色优先检测: {colorDetector.ModelSummary}; 同帧运行并在无颜色结果时采用: {fallbackDetector.ModelSummary}";

    public DetectionRunResult Detect(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion, int referenceWidth)
    {
        DetectionRunResult colorResult = colorDetector.Detect(sourcePixels, sourceWidth, sourceHeight, sourceStride, sourceRegion, referenceWidth);
        DetectionRunResult fallbackResult = fallbackDetector.Detect(sourcePixels, sourceWidth, sourceHeight, sourceStride, sourceRegion, referenceWidth);
        return colorResult.Detections.Count > 0
            ? new DetectionRunResult(colorResult.Detections, fallbackResult.Detections)
            : new DetectionRunResult(fallbackResult.Detections, fallbackResult.Detections);
    }

    public void UpdateColorDetectionOptions(ColorDetectionOptions primaryOptions)
    {
        colorDetector.UpdateColorDetectionOptions(primaryOptions);
    }

    public void Dispose()
    {
        colorDetector.Dispose();
        fallbackDetector.Dispose();
    }
}
