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
    private const int HorizontalCloseGapPixels = 6;
    private const float DuplicateIouThreshold = 0.45f;
    private const float DuplicateContainmentThreshold = 0.72f;
    private const float DuplicateCenterContainmentPaddingPixels = 2f;
    private const float DetectionBoxWidthWindowDivisor = 12.8f;
    private const float MinOriginalWidthToScaledWidthRatio = 0.28f;
    private const float MinOriginalAreaToScaledAreaRatio = 0.18f;
    private const int TemplateMinHeightPixels = 3;
    private const int TemplateMaxHeightPixels = 15;
    private const int TemplateAllowedRowGapPixels = 1;
    private const float TemplateMinPrimaryRowCoverageRatio = 0.03f;
    private const float TemplateMinPrimaryAreaCoverageRatio = 0.04f;
    private const float TemplateGoodPrimaryAreaCoverageRatio = 0.1f;
    private const float TemplateMinWeightedAreaCoverageRatio = 0.0f;
    private const float TemplateMinActiveRowRatio = 0.55f;
    private const float TemplateMinPrimaryRunRatio = 0.07f;
    private const float TemplateMinPrimaryColumnHeightRatio = 0.35f;
    private const int TemplateMaxCandidatesPerBand = 4;
    private const float PrimaryHueToleranceCap = 6f;
    private const int PrimarySaturationToleranceCap = 32;
    private const int PrimaryValueToleranceCap = 42;
    private const byte FixedTargetRed = 0xFF;
    private const byte FixedTargetGreen = 0xA4;
    private const byte FixedTargetBlue = 0x51;
    private const int AnchorMinWidthPixels = 1;
    private const int AnchorMinHeightPixels = 1;
    private const int AnchorMaxWidthPixels = 24;
    private const int AnchorMaxHeightPixels = 24;
    private const int AnchorOutputHeightPixels = 15;
    private const int AnchorMinAreaPixels = 1;
    private const float AnchorMinFillRatio = 0.55f;
    private const float AnchorMinAspectRatio = 0.45f;
    private const float AnchorMaxAspectRatio = 2.2f;

    private readonly float scoreThreshold;
    private ColorDetectionOptions primaryColorOptions;
    private ColorDetectionOptions secondaryColorOptions;
    private byte[] maskBuffer = Array.Empty<byte>();
    private int[] componentStackBuffer = Array.Empty<int>();
    private int[] rowCountBuffer = Array.Empty<int>();
    private int[] columnCountBuffer = Array.Empty<int>();
    private int[] primaryColumnCountBuffer = Array.Empty<int>();
    private int[] windowScoreBuffer = Array.Empty<int>();
    private int[] primaryWindowScoreBuffer = Array.Empty<int>();

    public string ModelSummary
    {
        get
        {
            ColorDetectionOptions primary = primaryColorOptions;
            ColorDetectionOptions secondary = secondaryColorOptions;
            return $"颜色检测: 固定色 #FFA451 / RGB({FixedTargetRed}, {FixedTargetGreen}, {FixedTargetBlue})；命中后按固定矩形输出并跳过该矩形区域。";
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

    public DetectionRunResult Detect(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion, int referenceWidth)
    {
        Rectangle region = NormalizeSourceRegion(sourceRegion, sourceWidth, sourceHeight);
        int regionWidth = region.Width;
        int regionHeight = region.Height;
        ColorDetectionOptions primary = primaryColorOptions;
        ColorDetectionOptions secondary = secondaryColorOptions;
        byte[] mask = BuildColorMask(sourcePixels, sourceStride, region, primary, secondary);
        IReadOnlyList<DetectionResult> anchorDetections = DetectPrimaryAnchorBlocks(mask, region, Math.Max(sourceWidth, referenceWidth));
        return new DetectionRunResult(anchorDetections);
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

    private byte[] BuildColorMask(byte[] pixels, int stride, Rectangle region, ColorDetectionOptions primary, ColorDetectionOptions secondary)
    {
        byte[] mask = RentMaskBuffer(region.Width * region.Height);
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
                if (IsPrimaryColorPixel(r, g, b, primary))
                {
                    mask[maskRowOffset + x] = (byte)ColorKind.Primary;
                }
            }
        }

        return mask;
    }

    private byte[] RentMaskBuffer(int requiredLength)
    {
        if (maskBuffer.Length < requiredLength)
        {
            maskBuffer = new byte[requiredLength];
        }
        else
        {
            Array.Clear(maskBuffer, 0, requiredLength);
        }

        return maskBuffer;
    }

    private static void CloseHorizontalMaskGaps(byte[] mask, int width, int height, int maxGapPixels)
    {
        if (maxGapPixels <= 0)
        {
            return;
        }

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            int lastPrimaryX = -1;
            int lastSecondaryX = -1;
            for (int x = 0; x < width; x++)
            {
                int index = rowOffset + x;
                ColorKind kind = (ColorKind)mask[index];
                if (kind == ColorKind.Primary)
                {
                    FillShortGap(mask, rowOffset, lastPrimaryX, x, maxGapPixels, ColorKind.Primary);
                    lastPrimaryX = x;
                }
                else if (kind == ColorKind.Secondary)
                {
                    FillShortGap(mask, rowOffset, lastSecondaryX, x, maxGapPixels, ColorKind.Secondary);
                    lastSecondaryX = x;
                }
            }
        }
    }

    private static void FillShortGap(byte[] mask, int rowOffset, int previousX, int currentX, int maxGapPixels, ColorKind kind)
    {
        int gapPixels = currentX - previousX - 1;
        if (previousX < 0 || gapPixels <= 0 || gapPixels > maxGapPixels)
        {
            return;
        }

        for (int x = previousX + 1; x < currentX; x++)
        {
            int index = rowOffset + x;
            if (mask[index] == 0)
            {
                mask[index] = (byte)kind;
            }
        }
    }

    private IReadOnlyList<DetectionResult> DetectPrimaryAnchorBlocks(byte[] mask, Rectangle region, int referenceWidth)
    {
        int width = region.Width;
        int height = region.Height;
        int outputWidth = Math.Clamp((int)MathF.Round(referenceWidth / DetectionBoxWidthWindowDivisor), MinBoxWidth, width);

        var detections = new List<DetectionResult>();
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int startIndex = rowOffset + x;
                if (mask[startIndex] != (byte)ColorKind.Primary)
                {
                    continue;
                }

                DetectionResult? detection = CreateAnchorDetection(x, y, region.Location, outputWidth);
                if (detection is null)
                {
                    continue;
                }

                detections.Add(detection);
                Rectangle consumedRegion = Rectangle.Intersect(
                    new Rectangle(x, y, (int)MathF.Round(detection.Box.Width), (int)MathF.Round(detection.Box.Height)),
                    new Rectangle(0, 0, width, height));
                ClearMaskRegion(mask, width, consumedRegion);
            }
        }

        return detections;
    }

    private DetectionResult? CreateAnchorDetection(int anchorX, int anchorY, Point sourceOffset, int outputWidth)
    {
        int boxWidth = outputWidth;
        int boxHeight = AnchorOutputHeightPixels;
        if (boxWidth < MinBoxWidth || boxHeight < MinBoxHeight)
        {
            return null;
        }

        float score = 1f;
        if (score < scoreThreshold)
        {
            return null;
        }

        RectangleF box = new(sourceOffset.X + anchorX, sourceOffset.Y + anchorY, boxWidth, boxHeight);
        return new DetectionResult(box, score, 0, "ColorAnchor");
    }

    private static void ClearMaskRegion(byte[] mask, int width, Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            return;
        }

        for (int y = region.Top; y < region.Bottom; y++)
        {
            Array.Clear(mask, (y * width) + region.Left, region.Width);
        }
    }

    private void EnsureComponentStack(int requiredLength)
    {
        if (componentStackBuffer.Length < requiredLength)
        {
            componentStackBuffer = new int[requiredLength];
        }
    }

    private IReadOnlyList<DetectionResult> DetectFixedWidthTemplates(byte[] mask, Rectangle region, int sourceWidth)
    {
        int width = region.Width;
        int height = region.Height;
        int templateWidth = Math.Clamp((int)MathF.Round(sourceWidth / DetectionBoxWidthWindowDivisor), MinBoxWidth, width);
        EnsureTemplateBuffers(width, height);
        Array.Clear(rowCountBuffer, 0, height);

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            int count = 0;
            for (int x = 0; x < width; x++)
            {
                if (mask[rowOffset + x] == (byte)ColorKind.Primary)
                {
                    count++;
                }
            }

            rowCountBuffer[y] = count;
        }

        int minRowPixels = Math.Max(1, (int)MathF.Ceiling(templateWidth * TemplateMinPrimaryRowCoverageRatio));
        var detections = new List<DetectionResult>();
        int bandStart = -1;
        int lastActiveRow = -1;
        int gapRows = 0;

        for (int y = 0; y <= height; y++)
        {
            bool activeRow = y < height && rowCountBuffer[y] >= minRowPixels;
            if (activeRow)
            {
                if (bandStart < 0)
                {
                    bandStart = y;
                }

                lastActiveRow = y;
                gapRows = 0;
                continue;
            }

            if (bandStart >= 0 && gapRows < TemplateAllowedRowGapPixels && y < height)
            {
                gapRows++;
                continue;
            }

            if (bandStart >= 0 && lastActiveRow >= bandStart)
            {
                AddTemplateDetectionsForBand(mask, region.Location, width, bandStart, lastActiveRow, templateWidth, detections);
            }

            bandStart = -1;
            lastActiveRow = -1;
            gapRows = 0;
        }

        return SuppressDuplicateDetections(detections);
    }

    private void AddTemplateDetectionsForBand(byte[] mask, Point sourceOffset, int width, int bandTop, int bandBottom, int templateWidth, List<DetectionResult> detections)
    {
        (int top, int bottom) = ClampTemplateBandToBestRows(bandTop, bandBottom);
        int bandHeight = bottom - top + 1;
        if (bandHeight < TemplateMinHeightPixels || bandHeight > TemplateMaxHeightPixels)
        {
            return;
        }

        Array.Clear(primaryColumnCountBuffer, 0, width);
        for (int y = top; y <= bottom; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                if (mask[rowOffset + x] == (byte)ColorKind.Primary)
                {
                    primaryColumnCountBuffer[x]++;
                }
            }
        }

        int maxWindowX = width - templateWidth;
        if (maxWindowX < 0)
        {
            return;
        }

        int primaryWindowScore = 0;
        for (int x = 0; x < templateWidth; x++)
        {
            primaryWindowScore += primaryColumnCountBuffer[x];
        }

        primaryWindowScoreBuffer[0] = primaryWindowScore;
        for (int x = 1; x <= maxWindowX; x++)
        {
            primaryWindowScore += primaryColumnCountBuffer[x + templateWidth - 1] - primaryColumnCountBuffer[x - 1];
            primaryWindowScoreBuffer[x] = primaryWindowScore;
        }

        int minPrimaryPixels = Math.Max(3, (int)MathF.Ceiling(templateWidth * bandHeight * TemplateMinPrimaryAreaCoverageRatio));
        var candidates = new List<(int X, int PrimaryPixels)>();
        for (int x = 0; x <= maxWindowX; x++)
        {
            int primaryPixels = primaryWindowScoreBuffer[x];
            if (primaryPixels < minPrimaryPixels)
            {
                continue;
            }

            int previous = x > 0 ? primaryWindowScoreBuffer[x - 1] : -1;
            int next = x < maxWindowX ? primaryWindowScoreBuffer[x + 1] : -1;
            if (primaryPixels >= previous && primaryPixels >= next && IsTemplateShapeCandidate(mask, width, top, bottom, x, templateWidth))
            {
                candidates.Add((x, primaryPixels));
            }
        }

        foreach ((int x, int primaryPixels) in candidates
            .OrderByDescending(static candidate => candidate.PrimaryPixels)
            .Take(TemplateMaxCandidatesPerBand))
        {
            float primaryScore = Math.Clamp(primaryPixels / Math.Max(1f, templateWidth * bandHeight * TemplateGoodPrimaryAreaCoverageRatio), 0f, 1f);
            float score = primaryScore;
            if (score < scoreThreshold)
            {
                continue;
            }

            RectangleF box = new(sourceOffset.X + x, sourceOffset.Y + top, templateWidth, bandHeight);
            detections.Add(new DetectionResult(box, score, 0, "ColorTemplate"));
        }
    }

    private static bool IsTemplateShapeCandidate(byte[] mask, int width, int top, int bottom, int left, int templateWidth)
    {
        int bandHeight = bottom - top + 1;
        int minRowPrimaryPixels = Math.Max(2, (int)MathF.Ceiling(templateWidth * TemplateMinPrimaryRowCoverageRatio));
        int activeRows = 0;
        int minColumnHeight = Math.Max(1, (int)MathF.Ceiling(bandHeight * TemplateMinPrimaryColumnHeightRatio));
        int longestRun = 0;
        int currentRun = 0;

        for (int x = left; x < left + templateWidth; x++)
        {
            int columnPrimaryPixels = 0;
            for (int y = top; y <= bottom; y++)
            {
                if (mask[(y * width) + x] == (byte)ColorKind.Primary)
                {
                    columnPrimaryPixels++;
                }
            }

            if (columnPrimaryPixels >= minColumnHeight)
            {
                currentRun++;
                longestRun = Math.Max(longestRun, currentRun);
            }
            else
            {
                currentRun = 0;
            }
        }

        for (int y = top; y <= bottom; y++)
        {
            int rowOffset = y * width;
            int rowPrimaryPixels = 0;
            for (int x = left; x < left + templateWidth; x++)
            {
                if (mask[rowOffset + x] == (byte)ColorKind.Primary)
                {
                    rowPrimaryPixels++;
                }
            }

            if (rowPrimaryPixels >= minRowPrimaryPixels)
            {
                activeRows++;
            }
        }

        int minActiveRows = Math.Max(TemplateMinHeightPixels, (int)MathF.Ceiling(bandHeight * TemplateMinActiveRowRatio));
        int minPrimaryRun = Math.Max(8, (int)MathF.Ceiling(templateWidth * TemplateMinPrimaryRunRatio));
        return activeRows >= minActiveRows && longestRun >= minPrimaryRun;
    }

    private (int Top, int Bottom) ClampTemplateBandToBestRows(int bandTop, int bandBottom)
    {
        int bandHeight = bandBottom - bandTop + 1;
        if (bandHeight <= TemplateMaxHeightPixels)
        {
            return (bandTop, bandBottom);
        }

        int bestTop = bandTop;
        int bestScore = int.MinValue;
        for (int top = bandTop; top + TemplateMaxHeightPixels - 1 <= bandBottom; top++)
        {
            int score = 0;
            for (int y = top; y < top + TemplateMaxHeightPixels; y++)
            {
                score += rowCountBuffer[y];
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestTop = top;
            }
        }

        return (bestTop, bestTop + TemplateMaxHeightPixels - 1);
    }

    private void EnsureTemplateBuffers(int width, int height)
    {
        if (rowCountBuffer.Length < height)
        {
            rowCountBuffer = new int[height];
        }

        if (columnCountBuffer.Length < width)
        {
            columnCountBuffer = new int[width];
        }

        if (primaryColumnCountBuffer.Length < width)
        {
            primaryColumnCountBuffer = new int[width];
        }

        if (windowScoreBuffer.Length < width)
        {
            windowScoreBuffer = new int[width];
        }

        if (primaryWindowScoreBuffer.Length < width)
        {
            primaryWindowScoreBuffer = new int[width];
        }
    }

    private static bool IsPrimaryColorPixel(byte r, byte g, byte b, ColorDetectionOptions options)
    {
        _ = options;
        return r == FixedTargetRed && g == FixedTargetGreen && b == FixedTargetBlue;
    }

    private static bool IsTargetColorPixel(byte r, byte g, byte b, ColorDetectionOptions options)
    {
        int value = Math.Max(r, Math.Max(g, b));
        if (Math.Abs(value - options.Value) > options.ValueTolerance)
        {
            return false;
        }

        int min = Math.Min(r, Math.Min(g, b));
        int delta = value - min;
        int saturation = value == 0 ? 0 : delta * 255 / value;
        if (Math.Abs(saturation - options.Saturation) > options.SaturationTolerance)
        {
            return false;
        }

        if (options.Saturation <= 35 || options.Value <= 45)
        {
            return true;
        }

        float hue = GetHue(r, g, b, value, delta);
        return GetHueDistance(hue, options.Hue) <= options.HueTolerance;
    }

    private static bool IsSecondaryColorPixel(byte r, byte g, byte b, ColorDetectionOptions options)
    {
        int value = Math.Max(r, Math.Max(g, b));
        int valueTolerance = Math.Max(options.ValueTolerance, 80);
        if (Math.Abs(value - options.Value) > valueTolerance)
        {
            return false;
        }

        int min = Math.Min(r, Math.Min(g, b));
        int delta = value - min;
        int saturation = value == 0 ? 0 : delta * 255 / value;
        int saturationTolerance = Math.Max(options.SaturationTolerance, 80);
        if (Math.Abs(saturation - options.Saturation) > saturationTolerance)
        {
            return false;
        }

        if (options.Saturation <= 60 || options.Value <= 90 || saturation <= 70)
        {
            return true;
        }

        float hue = GetHue(r, g, b, value, delta);
        return GetHueDistance(hue, options.Hue) <= Math.Max(options.HueTolerance, 35f);
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

        float hue = GetHue(r, g, b, max, delta);
        int saturation = max == 0 ? 0 : delta * 255 / max;
        return (hue, saturation, max);
    }

    private static float GetHue(byte r, byte g, byte b, int max, int delta)
    {
        if (delta == 0)
        {
            return 0f;
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

        return hue;
    }

    private static float GetHueDistance(float hue, float targetHue)
    {
        float distance = Math.Abs(hue - targetHue) % 360f;
        return distance > 180f ? 360f - distance : distance;
    }

    private List<ComponentBox> FindComponents(byte[] mask, int width, int height)
    {
        var components = new List<ComponentBox>();
        int requiredLength = width * height;
        if (componentStackBuffer.Length < requiredLength)
        {
            componentStackBuffer = new int[requiredLength];
        }

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

                ComponentBox component = FloodFill(mask, width, height, startIndex, componentStackBuffer);
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
            TryPush(mask, width, height, x - 1, y - 1, kind, stack, ref stackCount);
            TryPush(mask, width, height, x + 1, y - 1, kind, stack, ref stackCount);
            TryPush(mask, width, height, x - 1, y + 1, kind, stack, ref stackCount);
            TryPush(mask, width, height, x + 1, y + 1, kind, stack, ref stackCount);
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

    private DetectionResult? CreateDetection(ComponentBox component, Point sourceOffset, int sourceWidth, string label)
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

        float scaledWidth = sourceWidth / DetectionBoxWidthWindowDivisor;
        if (bounds.Width < scaledWidth * MinOriginalWidthToScaledWidthRatio)
        {
            return null;
        }

        float scaledAreaRatio = component.Area / Math.Max(1f, scaledWidth * bounds.Height);
        if (scaledAreaRatio < MinOriginalAreaToScaledAreaRatio)
        {
            return null;
        }

        float aspectScore = Math.Clamp((aspectRatio - MinAspectRatio) / 2.5f, 0f, 1f);
        float sizeScore = Math.Clamp(scaledAreaRatio / 0.65f, 0f, 1f);
        float score = Math.Clamp((fillRatio * 0.55f) + (aspectScore * 0.2f) + (sizeScore * 0.25f), 0f, 1f);
        if (score < scoreThreshold)
        {
            return null;
        }

        RectangleF box = CreateWindowScaledDetectionBox(bounds, sourceOffset, sourceWidth);
        return new DetectionResult(box, score, 0, label);
    }

    private static RectangleF CreateWindowScaledDetectionBox(Rectangle bounds, Point sourceOffset, int sourceWidth)
    {
        float boxX = bounds.X + sourceOffset.X;
        float targetWidth = Math.Max(bounds.Width, sourceWidth / DetectionBoxWidthWindowDivisor);
        float maxWidth = Math.Max(bounds.Width, sourceWidth - boxX);
        return new RectangleF(
            boxX,
            bounds.Y + sourceOffset.Y,
            Math.Min(targetWidth, maxWidth),
            bounds.Height);
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
            if (keptDetections.Any(kept => IsDuplicateOrPartialDetection(kept.Box, detection.Box)))
            {
                continue;
            }

            keptDetections.Add(detection);
        }

        return keptDetections;
    }

    private static bool IsDuplicateOrPartialDetection(RectangleF keptBox, RectangleF candidateBox)
    {
        if (CalculateIou(keptBox, candidateBox) >= DuplicateIouThreshold)
        {
            return true;
        }

        float candidateArea = candidateBox.Width * candidateBox.Height;
        if (candidateArea <= 0f)
        {
            return true;
        }

        float intersectionArea = CalculateIntersectionArea(keptBox, candidateBox);
        if (intersectionArea / candidateArea >= DuplicateContainmentThreshold)
        {
            return true;
        }

        RectangleF paddedKeptBox = RectangleF.Inflate(keptBox, DuplicateCenterContainmentPaddingPixels, DuplicateCenterContainmentPaddingPixels);
        PointF candidateCenter = new(candidateBox.Left + (candidateBox.Width / 2f), candidateBox.Top + (candidateBox.Height / 2f));
        return paddedKeptBox.Contains(candidateCenter) &&
            candidateBox.Width <= keptBox.Width * 1.1f &&
            candidateBox.Height <= keptBox.Height * 1.35f;
    }

    private static float CalculateIou(RectangleF a, RectangleF b)
    {
        float intersectionArea = CalculateIntersectionArea(a, b);
        if (intersectionArea <= 0)
        {
            return 0f;
        }

        float unionArea = (a.Width * a.Height) + (b.Width * b.Height) - intersectionArea;
        return unionArea <= 0 ? 0f : intersectionArea / unionArea;
    }

    private static float CalculateIntersectionArea(RectangleF a, RectangleF b)
    {
        float left = Math.Max(a.Left, b.Left);
        float top = Math.Max(a.Top, b.Top);
        float right = Math.Min(a.Right, b.Right);
        float bottom = Math.Min(a.Bottom, b.Bottom);
        float intersectionWidth = Math.Max(0, right - left);
        float intersectionHeight = Math.Max(0, bottom - top);
        return intersectionWidth * intersectionHeight;
    }

    private enum ColorKind : byte
    {
        Primary = 1,
        Secondary = 2,
        Pair = 3
    }

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
