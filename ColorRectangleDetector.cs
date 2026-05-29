using System.Drawing;

namespace YOLOForAim;

internal sealed class ColorRectangleDetector : IDetector
{
    private const int MinBoxWidth = 8;
    private const int MinBoxHeight = 3;
    private const int MinComponentArea = 24;
    private const float MinAspectRatio = 1.5f;
    private const float MaxAspectRatio = 24f;
    private const float MinFillRatio = 0.5f;
    private const int MergeGapPixels = 3;

    private readonly float scoreThreshold;
    private ColorDetectionOptions colorOptions;

    public string ModelSummary
    {
        get
        {
            ColorDetectionOptions options = colorOptions;
            return $"颜色检测: 目标 HSV(H={options.Hue:F1}, S={options.Saturation}, V={options.Value})，容差 H±{options.HueTolerance:F1}, S±{options.SaturationTolerance}, V±{options.ValueTolerance}，仅横向实心矩形。";
        }
    }

    public ColorRectangleDetector(DetectorOptions detectorOptions)
    {
        scoreThreshold = detectorOptions.ScoreThreshold;
        colorOptions = detectorOptions.ColorDetection ?? ColorDetectionOptions.Default;
    }

    public void UpdateColorDetectionOptions(ColorDetectionOptions options)
    {
        colorOptions = options;
    }

    public DetectionRunResult Detect(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion)
    {
        Rectangle region = NormalizeSourceRegion(sourceRegion, sourceWidth, sourceHeight);
        int regionWidth = region.Width;
        int regionHeight = region.Height;
        ColorDetectionOptions currentColorOptions = colorOptions;
        byte[] mask = BuildColorMask(sourcePixels, sourceStride, region, currentColorOptions);
        List<ComponentBox> components = FindComponents(mask, regionWidth, regionHeight);
        List<ComponentBox> mergedComponents = MergeNearbyComponents(components);

        var detections = new List<DetectionResult>(mergedComponents.Count);
        foreach (ComponentBox component in mergedComponents)
        {
            DetectionResult? detection = CreateDetection(component, region.Location);
            if (detection is not null)
            {
                detections.Add(detection);
            }
        }

        return new DetectionRunResult(detections);
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

    private static byte[] BuildColorMask(byte[] pixels, int stride, Rectangle region, ColorDetectionOptions options)
    {
        byte[] mask = new byte[region.Width * region.Height];
        for (int y = 0; y < region.Height; y++)
        {
            int sourceRowOffset = (region.Top + y) * stride;
            int maskRowOffset = y * region.Width;
            for (int x = 0; x < region.Width; x++)
            {
                int pixelOffset = sourceRowOffset + ((region.Left + x) * 4);
                byte b = pixels[pixelOffset];
                byte g = pixels[pixelOffset + 1];
                byte r = pixels[pixelOffset + 2];
                if (IsTargetColorPixel(r, g, b, options))
                {
                    mask[maskRowOffset + x] = 1;
                }
            }
        }

        return mask;
    }

    private static bool IsTargetColorPixel(byte r, byte g, byte b, ColorDetectionOptions options)
    {
        (float hue, int saturation, int value) = RgbToHsv(r, g, b);
        return GetHueDistance(hue, options.Hue) <= options.HueTolerance &&
            Math.Abs(saturation - options.Saturation) <= options.SaturationTolerance &&
            Math.Abs(value - options.Value) <= options.ValueTolerance;
    }

    private static (float Hue, int Saturation, int Value) RgbToHsv(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        int delta = max - min;
        if (delta == 0)
        {
            return (0f, 0, max);
        }

        float hue;
        if (max == r)
        {
            hue = 60f * ((g - b) / (float)delta);
            if (hue < 0f)
            {
                hue += 360f;
            }
        }
        else if (max == g)
        {
            hue = 60f * (((b - r) / (float)delta) + 2f);
        }
        else
        {
            hue = 60f * (((r - g) / (float)delta) + 4f);
        }

        int saturation = max == 0 ? 0 : delta * 255 / max;
        return (hue, saturation, max);
    }

    private static float GetHueDistance(float hue, float targetHue)
    {
        float distance = Math.Abs(hue - targetHue) % 360f;
        return distance > 180f ? 360f - distance : distance;
    }

    private static List<ComponentBox> FindComponents(byte[] mask, int width, int height)
    {
        var components = new List<ComponentBox>();
        int[] stack = new int[mask.Length];

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int startIndex = rowOffset + x;
                if (mask[startIndex] == 0)
                {
                    continue;
                }

                ComponentBox component = FloodFill(mask, width, height, startIndex, stack);
                if (component.Area >= MinComponentArea)
                {
                    components.Add(component);
                }
            }
        }

        return components;
    }

    private static ComponentBox FloodFill(byte[] mask, int width, int height, int startIndex, int[] stack)
    {
        int stackCount = 0;
        int startX = startIndex % width;
        int startY = startIndex / width;
        ComponentBox component = new(startX, startY, startX, startY, 0);

        mask[startIndex] = 0;
        stack[stackCount++] = startIndex;

        while (stackCount > 0)
        {
            int index = stack[--stackCount];
            int x = index % width;
            int y = index / width;
            component = component.Include(x, y);

            TryPush(mask, width, height, x - 1, y, stack, ref stackCount);
            TryPush(mask, width, height, x + 1, y, stack, ref stackCount);
            TryPush(mask, width, height, x, y - 1, stack, ref stackCount);
            TryPush(mask, width, height, x, y + 1, stack, ref stackCount);
        }

        return component;
    }

    private static void TryPush(byte[] mask, int width, int height, int x, int y, int[] stack, ref int stackCount)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        int index = (y * width) + x;
        if (mask[index] == 0)
        {
            return;
        }

        mask[index] = 0;
        stack[stackCount++] = index;
    }

    private static List<ComponentBox> MergeNearbyComponents(List<ComponentBox> components)
    {
        bool mergedAny;
        do
        {
            mergedAny = false;
            for (int i = 0; i < components.Count; i++)
            {
                for (int j = i + 1; j < components.Count; j++)
                {
                    if (!ShouldMerge(components[i], components[j]))
                    {
                        continue;
                    }

                    components[i] = components[i].Merge(components[j]);
                    components.RemoveAt(j);
                    mergedAny = true;
                    j--;
                }
            }
        }
        while (mergedAny);

        return components;
    }

    private static bool ShouldMerge(ComponentBox a, ComponentBox b)
    {
        Rectangle expandedA = Rectangle.Inflate(a.Bounds, MergeGapPixels, MergeGapPixels);
        return expandedA.IntersectsWith(b.Bounds);
    }

    private DetectionResult? CreateDetection(ComponentBox component, Point sourceOffset)
    {
        Rectangle bounds = component.Bounds;
        if (bounds.Width < MinBoxWidth || bounds.Height < MinBoxHeight)
        {
            return null;
        }

        if (bounds.Width <= bounds.Height)
        {
            return null;
        }

        float aspectRatio = bounds.Width / (float)bounds.Height;
        if (aspectRatio < MinAspectRatio || aspectRatio > MaxAspectRatio)
        {
            return null;
        }

        float fillRatio = component.Area / (float)(bounds.Width * bounds.Height);
        if (fillRatio < MinFillRatio)
        {
            return null;
        }

        float aspectScore = Math.Clamp((aspectRatio - MinAspectRatio) / 2.5f, 0f, 1f);
        float score = Math.Clamp((fillRatio * 0.75f) + (aspectScore * 0.25f), 0f, 1f);
        if (score < scoreThreshold)
        {
            return null;
        }

        RectangleF box = new(bounds.X + sourceOffset.X, bounds.Y + sourceOffset.Y, bounds.Width, bounds.Height);
        return new DetectionResult(box, score, 0, "ColorRect");
    }

    private readonly record struct ComponentBox(int MinX, int MinY, int MaxX, int MaxY, int Area)
    {
        public Rectangle Bounds => Rectangle.FromLTRB(MinX, MinY, MaxX + 1, MaxY + 1);

        public ComponentBox Include(int x, int y)
        {
            return new ComponentBox(
                Math.Min(MinX, x),
                Math.Min(MinY, y),
                Math.Max(MaxX, x),
                Math.Max(MaxY, y),
                Area + 1);
        }

        public ComponentBox Merge(ComponentBox other)
        {
            return new ComponentBox(
                Math.Min(MinX, other.MinX),
                Math.Min(MinY, other.MinY),
                Math.Max(MaxX, other.MaxX),
                Math.Max(MaxY, other.MaxY),
                Area + other.Area);
        }
    }
}
