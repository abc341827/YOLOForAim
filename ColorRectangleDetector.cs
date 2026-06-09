namespace YOLOForAim;

internal sealed class ColorRectangleDetector : IDetector
{
    private ColorDetectionOptions colorOptions;

    public string ModelSummary
    {
        get
        {
            ColorDetectionOptions options = colorOptions;
            return $"颜色检测: 单像素严格 RGB 匹配 RGB({options.Red}, {options.Green}, {options.Blue})，从左上到右下取第一个命中像素。";
        }
    }

    public ColorRectangleDetector(DetectorOptions detectorOptions)
    {
        colorOptions = detectorOptions.PrimaryColorDetection ?? ColorDetectionOptions.Default;
    }

    public void UpdateColorDetectionOptions(ColorDetectionOptions primaryOptions)
    {
        colorOptions = primaryOptions;
    }

    public DetectionRunResult Detect(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion, int referenceWidth)
    {
        _ = referenceWidth;
        Rectangle region = NormalizeSourceRegion(sourceRegion, sourceWidth, sourceHeight);
        DetectionResult? detection = FindFirstMatchingPixel(sourcePixels, sourceStride, region, colorOptions);
        return detection is null
            ? new DetectionRunResult(Array.Empty<DetectionResult>())
            : new DetectionRunResult([detection]);
    }

    public void Dispose()
    {
    }

    private static Rectangle NormalizeSourceRegion(Rectangle sourceRegion, int sourceWidth, int sourceHeight)
    {
        Rectangle fullBounds = new(0, 0, sourceWidth, sourceHeight);
        Rectangle normalizedSourceRegion = sourceRegion.IsEmpty ? fullBounds : Rectangle.Intersect(fullBounds, sourceRegion);
        return normalizedSourceRegion.Width <= 0 || normalizedSourceRegion.Height <= 0
            ? fullBounds
            : normalizedSourceRegion;
    }

    private static DetectionResult? FindFirstMatchingPixel(byte[] pixels, int stride, Rectangle region, ColorDetectionOptions options)
    {
        if (options.Red < 0 || options.Green < 0 || options.Blue < 0)
        {
            return null;
        }

        byte targetR = (byte)Math.Clamp(options.Red, 0, 255);
        byte targetG = (byte)Math.Clamp(options.Green, 0, 255);
        byte targetB = (byte)Math.Clamp(options.Blue, 0, 255);

        for (int y = 0; y < region.Height; y++)
        {
            int sourceRowOffset = (region.Top + y) * stride;
            for (int x = 0; x < region.Width; x++)
            {
                int pixelOffset = sourceRowOffset + ((region.Left + x) * 4);
                if (pixels[pixelOffset + 2] == targetR &&
                    pixels[pixelOffset + 1] == targetG &&
                    pixels[pixelOffset] == targetB)
                {
                    RectangleF box = new(region.Left + x, region.Top + y, 1f, 1f);
                    return new DetectionResult(box, 1f, 0, "ColorPixel");
                }
            }
        }

        return null;
    }
}
