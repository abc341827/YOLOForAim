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
    private const float MinVerticalOverlapRatio = 0.65f;
    private const int MergeGapPixels = 3;
    private const float StabilizedPositionBlend = 0.35f;
    private const float StabilizedSizeBlend = 0.18f;
    private const float StabilizedMinMatchIou = 0.18f;
    private const float StabilizedMinMatchDistancePixels = 24f;
    private const int StabilizedMaxMissingFrames = 2;

    private readonly float scoreThreshold;
    private ColorDetectionOptions primaryColorOptions;
    private ColorDetectionOptions secondaryColorOptions;
    private List<TrackedColorDetection> trackedDetections = new();
    private int detectionFrameIndex;

    public string ModelSummary
    {
        get
        {
            ColorDetectionOptions primary = primaryColorOptions;
            ColorDetectionOptions secondary = secondaryColorOptions;
            return $"颜色检测: 主色 HSV(H={primary.Hue:F1}, S={primary.Saturation}, V={primary.Value})，副色 HSV(H={secondary.Hue:F1}, S={secondary.Saturation}, V={secondary.Value})；支持主色横向矩形或主色+副色水平相邻矩形。";
        }
    }

    public ColorRectangleDetector(DetectorOptions detectorOptions)
    {
        scoreThreshold = detectorOptions.ScoreThreshold;
        primaryColorOptions = detectorOptions.PrimaryColorDetection ?? ColorDetectionOptions.Default;
        secondaryColorOptions = detectorOptions.SecondaryColorDetection ?? ColorDetectionOptions.DefaultSecondary;
    }

    public void UpdateColorDetectionOptions(ColorDetectionOptions primaryOptions, ColorDetectionOptions secondaryOptions)
    {
        primaryColorOptions = primaryOptions;
        secondaryColorOptions = secondaryOptions;
    }

    public DetectionRunResult Detect(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion)
    {
        Rectangle region = NormalizeSourceRegion(sourceRegion, sourceWidth, sourceHeight);
        int regionWidth = region.Width;
        int regionHeight = region.Height;
        ColorDetectionOptions primary = primaryColorOptions;
        ColorDetectionOptions secondary = secondaryColorOptions;
        byte[] mask = BuildColorMask(sourcePixels, sourceStride, region, primary, secondary);
        List<ComponentBox> components = FindComponents(mask, regionWidth, regionHeight);
        List<ComponentBox> mergedComponents = MergeNearbyComponents(components);

        var detections = new List<DetectionResult>();
        var pairedPrimaryIndexes = new HashSet<int>();
        var pairedSecondaryIndexes = new HashSet<int>();

        for (int primaryIndex = 0; primaryIndex < mergedComponents.Count; primaryIndex++)
        {
            ComponentBox primaryComponent = mergedComponents[primaryIndex];
            if (primaryComponent.Kind != ColorKind.Primary)
            {
                continue;
            }

            for (int secondaryIndex = 0; secondaryIndex < mergedComponents.Count; secondaryIndex++)
            {
                ComponentBox secondaryComponent = mergedComponents[secondaryIndex];
                if (pairedSecondaryIndexes.Contains(secondaryIndex) ||
                    secondaryComponent.Kind != ColorKind.Secondary ||
                    !CanCreateHorizontalPair(primaryComponent, secondaryComponent))
                {
                    continue;
                }

                DetectionResult? pairDetection = CreateDetection(primaryComponent.MergeAsPair(secondaryComponent), region.Location, "ColorPair");
                if (pairDetection is not null)
                {
                    detections.Add(pairDetection);
                    pairedPrimaryIndexes.Add(primaryIndex);
                    pairedSecondaryIndexes.Add(secondaryIndex);
                    break;
                }
            }
        }

        for (int index = 0; index < mergedComponents.Count; index++)
        {
            ComponentBox component = mergedComponents[index];
            if (component.Kind != ColorKind.Primary || pairedPrimaryIndexes.Contains(index))
            {
                continue;
            }

            DetectionResult? detection = CreateDetection(component, region.Location, "ColorRect");
            if (detection is not null)
            {
                detections.Add(detection);
            }
        }

        IReadOnlyList<DetectionResult> filteredDetections = SuppressDuplicateDetections(detections);
        IReadOnlyList<DetectionResult> stabilizedDetections = StabilizeDetections(filteredDetections);
        return new DetectionRunResult(stabilizedDetections);
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

    private static byte[] BuildColorMask(byte[] pixels, int stride, Rectangle region, ColorDetectionOptions primary, ColorDetectionOptions secondary)
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
                if (IsTargetColorPixel(r, g, b, primary))
                {
                    mask[maskRowOffset + x] = (byte)ColorKind.Primary;
                }
                else if (IsTargetColorPixel(r, g, b, secondary))
                {
                    mask[maskRowOffset + x] = (byte)ColorKind.Secondary;
                }
            }
        }

        return mask;
    }

    private static bool IsTargetColorPixel(byte r, byte g, byte b, ColorDetectionOptions options)
    {
        (float hue, int saturation, int value) = RgbToHsv(r, g, b);
        bool hueMatches = options.Saturation <= 35 || options.Value <= 45 || GetHueDistance(hue, options.Hue) <= options.HueTolerance;
        return hueMatches &&
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
        ColorKind kind = (ColorKind)mask[startIndex];
        ComponentBox component = new(startX, startY, startX, startY, 0, kind);

        mask[startIndex] = 0;
        stack[stackCount++] = startIndex;

        while (stackCount > 0)
        {
            int index = stack[--stackCount];
            int x = index % width;
            int y = index / width;
            component = component.Include(x, y);

            TryPush(mask, width, height, x - 1, y, kind, stack, ref stackCount);
            TryPush(mask, width, height, x + 1, y, kind, stack, ref stackCount);
            TryPush(mask, width, height, x, y - 1, kind, stack, ref stackCount);
            TryPush(mask, width, height, x, y + 1, kind, stack, ref stackCount);
        }

        return component;
    }

    private static void TryPush(byte[] mask, int width, int height, int x, int y, ColorKind kind, int[] stack, ref int stackCount)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        int index = (y * width) + x;
        if (mask[index] != (byte)kind)
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
                    if (components[i].Kind != components[j].Kind || !ShouldMerge(components[i], components[j]))
                    {
                        continue;
                    }

                    components[i] = components[i].MergeSameKind(components[j]);
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

    private static bool CanCreateHorizontalPair(ComponentBox primary, ComponentBox secondary)
    {
        Rectangle primaryBounds = primary.Bounds;
        Rectangle secondaryBounds = secondary.Bounds;
        int horizontalGap = primaryBounds.Right <= secondaryBounds.Left
            ? secondaryBounds.Left - primaryBounds.Right
            : primaryBounds.Left <= secondaryBounds.Right ? 0 : primaryBounds.Left - secondaryBounds.Right;
        if (horizontalGap > MergeGapPixels + 1)
        {
            return false;
        }

        int verticalOverlap = Math.Min(primaryBounds.Bottom, secondaryBounds.Bottom) - Math.Max(primaryBounds.Top, secondaryBounds.Top);
        if (verticalOverlap <= 0)
        {
            return false;
        }

        float verticalOverlapRatio = verticalOverlap / (float)Math.Min(primaryBounds.Height, secondaryBounds.Height);
        if (verticalOverlapRatio < MinVerticalOverlapRatio)
        {
            return false;
        }

        Rectangle unionBounds = Rectangle.Union(primaryBounds, secondaryBounds);
        if (unionBounds.Width <= unionBounds.Height)
        {
            return false;
        }

        float fillRatio = (primary.Area + secondary.Area) / (float)(unionBounds.Width * unionBounds.Height);
        return fillRatio >= MinFillRatio;
    }

    private DetectionResult? CreateDetection(ComponentBox component, Point sourceOffset, string label)
    {
        Rectangle bounds = component.Bounds;
        if (bounds.Width < MinBoxWidth || bounds.Height < MinBoxHeight || bounds.Width <= bounds.Height)
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
        return new DetectionResult(box, score, 0, label);
    }

    private static IReadOnlyList<DetectionResult> SuppressDuplicateDetections(List<DetectionResult> detections)
    {
        if (detections.Count <= 1)
        {
            return detections;
        }

        var keptDetections = new List<DetectionResult>(detections.Count);
        foreach (DetectionResult detection in detections.OrderByDescending(static item => item.Box.Width * item.Box.Height).ThenByDescending(static item => item.Score))
        {
            if (keptDetections.Any(kept => CalculateIou(kept.Box, detection.Box) > 0.55f))
            {
                continue;
            }

            keptDetections.Add(detection);
        }

        return keptDetections;
    }

    private IReadOnlyList<DetectionResult> StabilizeDetections(IReadOnlyList<DetectionResult> detections)
    {
        detectionFrameIndex++;
        if (detections.Count == 0)
        {
            trackedDetections = trackedDetections
                .Where(track => detectionFrameIndex - track.LastSeenFrame <= StabilizedMaxMissingFrames)
                .ToList();
            return trackedDetections.Select(track => track.Detection).ToArray();
        }

        var stabilizedDetections = new List<DetectionResult>(detections.Count);
        var matchedTrackIndexes = new HashSet<int>();

        foreach (DetectionResult detection in detections)
        {
            int matchedTrackIndex = FindBestTrackMatch(detection, matchedTrackIndexes);
            DetectionResult stabilizedDetection = matchedTrackIndex >= 0
                ? SmoothDetection(trackedDetections[matchedTrackIndex].Detection, detection)
                : detection;

            if (matchedTrackIndex >= 0)
            {
                matchedTrackIndexes.Add(matchedTrackIndex);
                trackedDetections[matchedTrackIndex] = new TrackedColorDetection(stabilizedDetection, detectionFrameIndex);
            }
            else
            {
                trackedDetections.Add(new TrackedColorDetection(stabilizedDetection, detectionFrameIndex));
            }

            stabilizedDetections.Add(stabilizedDetection);
        }

        for (int index = trackedDetections.Count - 1; index >= 0; index--)
        {
            if (detectionFrameIndex - trackedDetections[index].LastSeenFrame > StabilizedMaxMissingFrames)
            {
                trackedDetections.RemoveAt(index);
            }
        }

        return SuppressDuplicateDetections(stabilizedDetections);
    }

    private int FindBestTrackMatch(DetectionResult detection, HashSet<int> matchedTrackIndexes)
    {
        int bestTrackIndex = -1;
        float bestScore = float.MinValue;
        PointF detectionCenter = GetBoxCenter(detection.Box);

        for (int index = 0; index < trackedDetections.Count; index++)
        {
            if (matchedTrackIndexes.Contains(index))
            {
                continue;
            }

            DetectionResult trackedDetection = trackedDetections[index].Detection;
            if (trackedDetection.ClassId != detection.ClassId || !string.Equals(trackedDetection.Label, detection.Label, StringComparison.Ordinal))
            {
                continue;
            }

            float iou = CalculateIou(trackedDetection.Box, detection.Box);
            PointF trackedCenter = GetBoxCenter(trackedDetection.Box);
            float distanceSquared = GetDistanceSquared(trackedCenter, detectionCenter);
            float maxDistance = Math.Max(StabilizedMinMatchDistancePixels, Math.Max(trackedDetection.Box.Width, trackedDetection.Box.Height) * 0.65f);
            if (iou < StabilizedMinMatchIou && distanceSquared > maxDistance * maxDistance)
            {
                continue;
            }

            float score = iou - (distanceSquared / Math.Max(1f, maxDistance * maxDistance));
            if (score > bestScore)
            {
                bestScore = score;
                bestTrackIndex = index;
            }
        }

        return bestTrackIndex;
    }

    private static DetectionResult SmoothDetection(DetectionResult previousDetection, DetectionResult currentDetection)
    {
        PointF previousCenter = GetBoxCenter(previousDetection.Box);
        PointF currentCenter = GetBoxCenter(currentDetection.Box);
        PointF smoothedCenter = LerpPoint(previousCenter, currentCenter, StabilizedPositionBlend);
        SizeF smoothedSize = LerpSize(previousDetection.Box.Size, currentDetection.Box.Size, StabilizedSizeBlend);
        RectangleF smoothedBox = CreateCenteredBox(smoothedCenter, smoothedSize);

        return currentDetection with
        {
            Box = smoothedBox,
            Score = Math.Max(previousDetection.Score, currentDetection.Score)
        };
    }

    private static PointF GetBoxCenter(RectangleF box)
    {
        return new PointF(box.Left + (box.Width / 2f), box.Top + (box.Height / 2f));
    }

    private static float GetDistanceSquared(PointF a, PointF b)
    {
        float deltaX = a.X - b.X;
        float deltaY = a.Y - b.Y;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    private static PointF LerpPoint(PointF from, PointF to, float amount)
    {
        return new PointF(
            from.X + ((to.X - from.X) * amount),
            from.Y + ((to.Y - from.Y) * amount));
    }

    private static SizeF LerpSize(SizeF from, SizeF to, float amount)
    {
        return new SizeF(
            from.Width + ((to.Width - from.Width) * amount),
            from.Height + ((to.Height - from.Height) * amount));
    }

    private static RectangleF CreateCenteredBox(PointF center, SizeF size)
    {
        return new RectangleF(
            center.X - (size.Width / 2f),
            center.Y - (size.Height / 2f),
            size.Width,
            size.Height);
    }

    private static float CalculateIou(RectangleF a, RectangleF b)
    {
        float left = Math.Max(a.Left, b.Left);
        float top = Math.Max(a.Top, b.Top);
        float right = Math.Min(a.Right, b.Right);
        float bottom = Math.Min(a.Bottom, b.Bottom);
        float intersectionWidth = Math.Max(0, right - left);
        float intersectionHeight = Math.Max(0, bottom - top);
        float intersectionArea = intersectionWidth * intersectionHeight;
        if (intersectionArea <= 0)
        {
            return 0f;
        }

        float unionArea = (a.Width * a.Height) + (b.Width * b.Height) - intersectionArea;
        return unionArea <= 0 ? 0f : intersectionArea / unionArea;
    }

    private enum ColorKind : byte
    {
        Primary = 1,
        Secondary = 2,
        Pair = 3
    }

    private sealed record TrackedColorDetection(DetectionResult Detection, int LastSeenFrame);

    private readonly record struct ComponentBox(int MinX, int MinY, int MaxX, int MaxY, int Area, ColorKind Kind)
    {
        public Rectangle Bounds => Rectangle.FromLTRB(MinX, MinY, MaxX + 1, MaxY + 1);

        public ComponentBox Include(int x, int y)
        {
            return new ComponentBox(
                Math.Min(MinX, x),
                Math.Min(MinY, y),
                Math.Max(MaxX, x),
                Math.Max(MaxY, y),
                Area + 1,
                Kind);
        }

        public ComponentBox MergeSameKind(ComponentBox other)
        {
            return new ComponentBox(
                Math.Min(MinX, other.MinX),
                Math.Min(MinY, other.MinY),
                Math.Max(MaxX, other.MaxX),
                Math.Max(MaxY, other.MaxY),
                Area + other.Area,
                Kind);
        }

        public ComponentBox MergeAsPair(ComponentBox other)
        {
            return new ComponentBox(
                Math.Min(MinX, other.MinX),
                Math.Min(MinY, other.MinY),
                Math.Max(MaxX, other.MaxX),
                Math.Max(MaxY, other.MaxY),
                Area + other.Area,
                ColorKind.Pair);
        }
    }
}
