namespace YOLOForAim;

/// <summary>
/// 组合检测器：先跑颜色检测，命中时直接使用颜色结果；颜色没有结果时回退到主检测器。
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

    public string ModelSummary => $"颜色优先检测: {colorDetector.ModelSummary}; 无颜色结果时回退: {fallbackDetector.ModelSummary}";

    public DetectionRunResult Detect(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion, int referenceWidth)
    {
        DetectionRunResult colorResult = colorDetector.Detect(sourcePixels, sourceWidth, sourceHeight, sourceStride, sourceRegion, referenceWidth);
        return colorResult.Detections.Count > 0
            ? colorResult
            : fallbackDetector.Detect(sourcePixels, sourceWidth, sourceHeight, sourceStride, sourceRegion, referenceWidth);
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
