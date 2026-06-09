namespace YOLOForAim;

internal sealed class ColorRectangleDetector : IDetector
{
    private ColorDetectionOptions colorOptions;

    public string ModelSummary
    {
        get
        {
            ColorDetectionOptions options = colorOptions;
            return $"颜色检测: 横线严格 RGB 匹配，第一段 RGB({options.Red}, {options.Green}, {options.Blue})，第二段 #5D5B61，第三段 #2C3038，从左上到右下取第一条命中横线。";
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
        DetectionResult? detection = FindFirstMatchingLine(sourcePixels, sourceStride, region, colorOptions);
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

    private static DetectionResult? FindFirstMatchingLine(byte[] pixels, int stride, Rectangle region, ColorDetectionOptions options)
    {
        if (options.Red < 0 || options.Green < 0 || options.Blue < 0)
        {
            return null;
        }

        byte targetR = (byte)Math.Clamp(options.Red, 0, 255);
        byte targetG = (byte)Math.Clamp(options.Green, 0, 255);
        byte targetB = (byte)Math.Clamp(options.Blue, 0, 255);
        RgbColor primaryColor = new(targetR, targetG, targetB);
        RgbColor secondaryColor = new(0x5d, 0x5b, 0x61);
        RgbColor tertiaryColor = new(0x2c, 0x30, 0x38);

        for (int y = 0; y < region.Height; y++)
        {
            int sourceRowOffset = (region.Top + y) * stride;
            for (int x = 0; x < region.Width; x++)
            {
                int lineStart = region.Left + x;
                int currentX = lineStart;
                if (!PixelEquals(pixels, sourceRowOffset, currentX, primaryColor))
                {
                    continue;
                }

                currentX = ConsumeColorRun(pixels, sourceRowOffset, currentX, region.Right, primaryColor);
                currentX = ConsumeColorRun(pixels, sourceRowOffset, currentX, region.Right, secondaryColor);
                currentX = ConsumeColorRun(pixels, sourceRowOffset, currentX, region.Right, tertiaryColor);

                RectangleF box = new(lineStart, region.Top + y, currentX - lineStart, 1f);
                return new DetectionResult(box, 1f, 0, "ColorLine");
            }
        }

        return null;
    }

    private static int ConsumeColorRun(byte[] pixels, int rowOffset, int startX, int right, RgbColor color)
    {
        int currentX = startX;
        while (currentX < right && PixelEquals(pixels, rowOffset, currentX, color))
        {
            currentX++;
        }

        return currentX;
    }

    private static bool PixelEquals(byte[] pixels, int rowOffset, int x, RgbColor color)
    {
        int pixelOffset = rowOffset + (x * 4);
        return pixels[pixelOffset + 2] == color.Red &&
            pixels[pixelOffset + 1] == color.Green &&
            pixels[pixelOffset] == color.Blue;
    }

    private readonly record struct RgbColor(byte Red, byte Green, byte Blue);
}
