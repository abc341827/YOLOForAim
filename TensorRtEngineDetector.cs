using System.Drawing;

namespace YOLOForAim;

internal sealed class TensorRtEngineDetector : IDetector
{
    private const float NmsThreshold = 0.45f;

    private IntPtr detectorHandle;
    private readonly TensorRtTensorInfo inputTensor;
    private readonly TensorRtTensorInfo[] outputTensors;
    private readonly int inputWidth;
    private readonly int inputHeight;
    private readonly float scoreThreshold;

    public string ModelSummary { get; }

    public TensorRtEngineDetector(string enginePath, DetectorOptions detectorOptions)
    {
        if (string.IsNullOrWhiteSpace(enginePath))
        {
            throw new ArgumentException("TensorRT engine 路径不能为空。", nameof(enginePath));
        }

        if (!File.Exists(enginePath))
        {
            throw new FileNotFoundException("未找到 TensorRT engine 文件。", enginePath);
        }

        scoreThreshold = detectorOptions.ScoreThreshold;
        detectorHandle = TensorRtNativeMethods.OpenEngine(enginePath, out TensorRtTensorInfoNative[] tensorInfos);

        TensorRtTensorInfo[] normalizedInfos = tensorInfos
            .Select(static info => new TensorRtTensorInfo(
                string.IsNullOrWhiteSpace(info.Name) ? "(unnamed)" : info.Name,
                info.IsInput != 0,
                (TensorRtElementType)info.DataType,
                NormalizeDimensions(info.Dims, info.NbDims)))
            .ToArray();

        inputTensor = normalizedInfos.SingleOrDefault(static info => info.IsInput)
            ?? throw new InvalidOperationException("TensorRT engine 未发现输入张量。");
        outputTensors = normalizedInfos.Where(static info => !info.IsInput).ToArray();
        if (outputTensors.Length == 0)
        {
            throw new InvalidOperationException("TensorRT engine 未发现输出张量。");
        }

        (inputHeight, inputWidth) = ResolveInputSize(inputTensor.Dimensions);
        ModelSummary = BuildModelSummary(enginePath);
    }

    public DetectionRunResult Detect(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion)
    {
        Rectangle normalizedSourceRegion = NormalizeSourceRegion(sourceRegion, sourceWidth, sourceHeight);
        PreprocessResult preprocess = Preprocess(sourcePixels, sourceWidth, sourceHeight, sourceStride, normalizedSourceRegion);

        TensorRtNativeMethods.RunInference(detectorHandle, preprocess.TensorData);

        OutputSnapshot[] outputs = new OutputSnapshot[outputTensors.Length];
        for (int outputIndex = 0; outputIndex < outputTensors.Length; outputIndex++)
        {
            TensorRtTensorInfo outputTensor = outputTensors[outputIndex];
            int elementCount = checked((int)GetElementCount(outputTensor.Dimensions));
            float[] data = new float[elementCount];
            TensorRtNativeMethods.CopyOutputToFloat(detectorHandle, outputIndex, data);
            outputs[outputIndex] = new OutputSnapshot(outputTensor.Name, outputTensor.Dimensions, outputTensor.DataType, data);
        }

        IReadOnlyList<DetectionResult> detections = ParseOutputs(outputs, normalizedSourceRegion.Size, preprocess.Scale, preprocess.PadX, preprocess.PadY);
        if (!normalizedSourceRegion.Location.IsEmpty)
        {
            detections = OffsetDetections(detections, normalizedSourceRegion.Location);
        }

        return new DetectionRunResult(detections);
    }

    public void Dispose()
    {
        if (detectorHandle != IntPtr.Zero)
        {
            TensorRtNativeMethods.trt_close_engine(detectorHandle);
            detectorHandle = IntPtr.Zero;
        }
    }

    private static int[] NormalizeDimensions(long[] rawDimensions, int nbDims)
    {
        int dimensionCount = Math.Max(0, Math.Min(nbDims, rawDimensions.Length));
        int[] dimensions = new int[dimensionCount];
        for (int index = 0; index < dimensionCount; index++)
        {
            long dimension = rawDimensions[index];
            dimensions[index] = dimension > int.MaxValue ? int.MaxValue : (int)dimension;
        }

        return dimensions;
    }

    private static (int Height, int Width) ResolveInputSize(IReadOnlyList<int> dimensions)
    {
        if (dimensions.Count >= 4)
        {
            return (NormalizeDimension(dimensions[^2], 640), NormalizeDimension(dimensions[^1], 640));
        }

        if (dimensions.Count == 3)
        {
            return (NormalizeDimension(dimensions[^2], 640), NormalizeDimension(dimensions[^1], 640));
        }

        return (640, 640);
    }

    private static int NormalizeDimension(int dimension, int fallback)
    {
        return dimension > 0 ? dimension : fallback;
    }

    private static long GetElementCount(IReadOnlyList<int> dimensions)
    {
        long elementCount = 1;
        foreach (int dimension in dimensions)
        {
            if (dimension <= 0)
            {
                throw new NotSupportedException($"暂不支持动态输出维度: [{string.Join(", ", dimensions)}]");
            }

            elementCount *= dimension;
        }

        return Math.Max(1, elementCount);
    }

    private Rectangle NormalizeSourceRegion(Rectangle sourceRegion, int sourceWidth, int sourceHeight)
    {
        Rectangle fullBounds = new(0, 0, sourceWidth, sourceHeight);
        Rectangle normalizedSourceRegion = sourceRegion.IsEmpty ? fullBounds : Rectangle.Intersect(fullBounds, sourceRegion);
        if (normalizedSourceRegion.Width <= 0 || normalizedSourceRegion.Height <= 0)
        {
            return fullBounds;
        }

        return normalizedSourceRegion;
    }

    private PreprocessResult Preprocess(byte[] sourcePixels, int sourceWidth, int sourceHeight, int sourceStride, Rectangle sourceRegion)
    {
        _ = sourceWidth;
        _ = sourceHeight;
        float scale = Math.Min((float)inputWidth / sourceRegion.Width, (float)inputHeight / sourceRegion.Height);
        float padX = (inputWidth - (sourceRegion.Width * scale)) / 2f;
        float padY = (inputHeight - (sourceRegion.Height * scale)) / 2f;
        float inverse255 = 1f / 255f;
        float[] tensorData = new float[3 * inputWidth * inputHeight];
        int planeSize = inputWidth * inputHeight;

        for (int y = 0; y < inputHeight; y++)
        {
            float sourceY = ((y + 0.5f - padY) / scale) - 0.5f;
            int sampleY = (int)MathF.Round(sourceY);
            if ((uint)sampleY >= (uint)sourceRegion.Height)
            {
                continue;
            }

            int sourceRowOffset = (sourceRegion.Top + sampleY) * sourceStride;
            int tensorRowOffset = y * inputWidth;
            for (int x = 0; x < inputWidth; x++)
            {
                float sourceX = ((x + 0.5f - padX) / scale) - 0.5f;
                int sampleX = (int)MathF.Round(sourceX);
                if ((uint)sampleX >= (uint)sourceRegion.Width)
                {
                    continue;
                }

                int pixelOffset = sourceRowOffset + ((sourceRegion.Left + sampleX) * 4);
                int tensorOffset = tensorRowOffset + x;
                tensorData[tensorOffset] = sourcePixels[pixelOffset + 2] * inverse255;
                tensorData[planeSize + tensorOffset] = sourcePixels[pixelOffset + 1] * inverse255;
                tensorData[(planeSize * 2) + tensorOffset] = sourcePixels[pixelOffset] * inverse255;
            }
        }

        return new PreprocessResult(tensorData, scale, padX, padY);
    }

    private IReadOnlyList<DetectionResult> ParseOutputs(
        IReadOnlyList<OutputSnapshot> outputs,
        Size originalSize,
        float scale,
        float padX,
        float padY)
    {
        if (outputs.Count == 0)
        {
            return Array.Empty<DetectionResult>();
        }

        if (TryParseEndToEndMultiOutput(outputs, originalSize, scale, padX, padY, out var endToEndMultiOutputDetections))
        {
            return endToEndMultiOutputDetections;
        }

        if (outputs.Count == 1)
        {
            if (TryParseEndToEndSingleOutput(outputs[0], originalSize, scale, padX, padY, out var endToEndSingleOutputDetections))
            {
                return endToEndSingleOutputDetections;
            }

            if (TryParseRawYoloOutput(outputs[0], originalSize, scale, padX, padY, out var rawYoloDetections))
            {
                return rawYoloDetections;
            }
        }

        return Array.Empty<DetectionResult>();
    }

    private bool TryParseEndToEndMultiOutput(
        IReadOnlyList<OutputSnapshot> outputs,
        Size originalSize,
        float scale,
        float padX,
        float padY,
        out IReadOnlyList<DetectionResult> detections)
    {
        detections = Array.Empty<DetectionResult>();

        var boxesOutput = outputs.FirstOrDefault(static output => output.Name.Contains("box", StringComparison.OrdinalIgnoreCase) && output.Dimensions.LastOrDefault() == 4);
        var scoresOutput = outputs.FirstOrDefault(static output => output.Name.Contains("score", StringComparison.OrdinalIgnoreCase));
        var classesOutput = outputs.FirstOrDefault(static output => output.Name.Contains("class", StringComparison.OrdinalIgnoreCase));
        var numDetectionsOutput = outputs.FirstOrDefault(static output => output.Name.Contains("num", StringComparison.OrdinalIgnoreCase));

        if (boxesOutput is null || scoresOutput is null || classesOutput is null)
        {
            return false;
        }

        int boxesCount = boxesOutput.Data.Length / 4;
        int scoresCount = scoresOutput.Data.Length;
        int classesCount = classesOutput.Data.Length;
        int detectionCount = Math.Min(boxesCount, Math.Min(scoresCount, classesCount));
        if (numDetectionsOutput is not null && numDetectionsOutput.Data.Length > 0)
        {
            detectionCount = Math.Min(detectionCount, Math.Max(0, (int)numDetectionsOutput.Data[0]));
        }

        var parsedDetections = new List<DetectionResult>(detectionCount);
        bool normalizedBoxes = boxesOutput.Data.Take(Math.Min(8, boxesOutput.Data.Length)).All(static value => value is >= 0f and <= 1.5f);
        float xScale = normalizedBoxes ? inputWidth : 1f;
        float yScale = normalizedBoxes ? inputHeight : 1f;

        for (int index = 0; index < detectionCount; index++)
        {
            float score = scoresOutput.Data[index];
            if (score < scoreThreshold)
            {
                continue;
            }

            int baseOffset = index * 4;
            float x1 = boxesOutput.Data[baseOffset] * xScale;
            float y1 = boxesOutput.Data[baseOffset + 1] * yScale;
            float x2 = boxesOutput.Data[baseOffset + 2] * xScale;
            float y2 = boxesOutput.Data[baseOffset + 3] * yScale;
            RectangleF box = MapBoxToOriginalImage(x1, y1, x2, y2, originalSize, scale, padX, padY, preferXyxy: true);
            if (box.Width <= 1 || box.Height <= 1)
            {
                continue;
            }

            int classId = Math.Max(0, (int)classesOutput.Data[index]);
            parsedDetections.Add(new DetectionResult(box, score, classId, $"Class {classId}"));
        }

        detections = parsedDetections;
        return true;
    }

    private bool TryParseEndToEndSingleOutput(
        OutputSnapshot output,
        Size originalSize,
        float scale,
        float padX,
        float padY,
        out IReadOnlyList<DetectionResult> detections)
    {
        detections = Array.Empty<DetectionResult>();

        int[] dimensions = output.Dimensions;
        if (dimensions.Length < 2)
        {
            return false;
        }

        int rowCount;
        int attributeCount;
        if (dimensions.Length == 3)
        {
            rowCount = dimensions[1];
            attributeCount = dimensions[2];
        }
        else
        {
            rowCount = dimensions[0];
            attributeCount = dimensions[1];
        }

        if (attributeCount < 6 || attributeCount > 8)
        {
            return false;
        }

        var parsedDetections = new List<DetectionResult>();
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int rowOffset = rowIndex * attributeCount;
            float score = output.Data[rowOffset + 4];
            if (score < scoreThreshold)
            {
                continue;
            }

            int classId = Math.Max(0, (int)output.Data[rowOffset + 5]);
            RectangleF box = MapBoxToOriginalImage(
                output.Data[rowOffset],
                output.Data[rowOffset + 1],
                output.Data[rowOffset + 2],
                output.Data[rowOffset + 3],
                originalSize,
                scale,
                padX,
                padY,
                preferXyxy: output.Data[rowOffset + 2] > output.Data[rowOffset] && output.Data[rowOffset + 3] > output.Data[rowOffset + 1]);

            if (box.Width <= 1 || box.Height <= 1)
            {
                continue;
            }

            parsedDetections.Add(new DetectionResult(box, score, classId, $"Class {classId}"));
        }

        detections = parsedDetections;
        return parsedDetections.Count > 0 || rowCount > 0;
    }

    private bool TryParseRawYoloOutput(
        OutputSnapshot output,
        Size originalSize,
        float scale,
        float padX,
        float padY,
        out IReadOnlyList<DetectionResult> detections)
    {
        detections = Array.Empty<DetectionResult>();

        int[] dimensions = output.Dimensions;
        if (dimensions.Length < 2)
        {
            return false;
        }

        bool transposed;
        int candidateCount;
        int attributeCount;

        if (dimensions.Length == 3)
        {
            transposed = dimensions[1] <= 256 && dimensions[2] > 256;
            candidateCount = transposed ? dimensions[2] : dimensions[1];
            attributeCount = transposed ? dimensions[1] : dimensions[2];
        }
        else
        {
            transposed = false;
            candidateCount = dimensions[0];
            attributeCount = dimensions[1];
        }

        if (attributeCount < 5)
        {
            return false;
        }

        float ReadValue(int candidateIndex, int attributeIndex)
        {
            if (dimensions.Length == 3)
            {
                return transposed
                    ? output.Data[(attributeIndex * candidateCount) + candidateIndex]
                    : output.Data[(candidateIndex * attributeCount) + attributeIndex];
            }

            return output.Data[(candidateIndex * attributeCount) + attributeIndex];
        }

        var parsedDetections = new List<DetectionResult>();
        bool hasObjectness = attributeCount > 6 && ReadValue(0, 4) is >= 0f and <= 1f;
        int classOffset = hasObjectness ? 5 : 4;
        int classCount = attributeCount - classOffset;
        if (classCount <= 0)
        {
            return false;
        }

        for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
        {
            float centerX = ReadValue(candidateIndex, 0);
            float centerY = ReadValue(candidateIndex, 1);
            float width = ReadValue(candidateIndex, 2);
            float height = ReadValue(candidateIndex, 3);
            float objectness = hasObjectness ? ReadValue(candidateIndex, 4) : 1f;

            float maxScore = 0f;
            int classId = 0;
            for (int classIndex = 0; classIndex < classCount; classIndex++)
            {
                float score = objectness * ReadValue(candidateIndex, classOffset + classIndex);
                if (score > maxScore)
                {
                    maxScore = score;
                    classId = classIndex;
                }
            }

            if (maxScore < scoreThreshold)
            {
                continue;
            }

            float left = (centerX - (width / 2f) - padX) / scale;
            float top = (centerY - (height / 2f) - padY) / scale;
            float right = (centerX + (width / 2f) - padX) / scale;
            float bottom = (centerY + (height / 2f) - padY) / scale;

            left = Math.Clamp(left, 0, originalSize.Width - 1);
            top = Math.Clamp(top, 0, originalSize.Height - 1);
            right = Math.Clamp(right, 0, originalSize.Width - 1);
            bottom = Math.Clamp(bottom, 0, originalSize.Height - 1);

            float boxWidth = right - left;
            float boxHeight = bottom - top;
            if (boxWidth <= 1 || boxHeight <= 1)
            {
                continue;
            }

            parsedDetections.Add(new DetectionResult(
                new RectangleF(left, top, boxWidth, boxHeight),
                maxScore,
                classId,
                $"Class {classId}"));
        }

        detections = ApplyNms(parsedDetections, NmsThreshold);
        return true;
    }

    private static RectangleF MapBoxToOriginalImage(
        float x1,
        float y1,
        float x2,
        float y2,
        Size originalSize,
        float scale,
        float padX,
        float padY,
        bool preferXyxy)
    {
        float left;
        float top;
        float right;
        float bottom;

        if (preferXyxy)
        {
            left = (x1 - padX) / scale;
            top = (y1 - padY) / scale;
            right = (x2 - padX) / scale;
            bottom = (y2 - padY) / scale;
        }
        else
        {
            left = (x1 - (x2 / 2f) - padX) / scale;
            top = (y1 - (y2 / 2f) - padY) / scale;
            right = (x1 + (x2 / 2f) - padX) / scale;
            bottom = (y1 + (y2 / 2f) - padY) / scale;
        }

        left = Math.Clamp(left, 0, Math.Max(0, originalSize.Width - 1));
        top = Math.Clamp(top, 0, Math.Max(0, originalSize.Height - 1));
        right = Math.Clamp(right, 0, Math.Max(0, originalSize.Width - 1));
        bottom = Math.Clamp(bottom, 0, Math.Max(0, originalSize.Height - 1));

        return new RectangleF(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static IReadOnlyList<DetectionResult> OffsetDetections(IReadOnlyList<DetectionResult> detections, Point offset)
    {
        var offsetDetections = new DetectionResult[detections.Count];
        for (int i = 0; i < detections.Count; i++)
        {
            DetectionResult detection = detections[i];
            offsetDetections[i] = detection with
            {
                Box = new RectangleF(
                    detection.Box.X + offset.X,
                    detection.Box.Y + offset.Y,
                    detection.Box.Width,
                    detection.Box.Height)
            };
        }

        return offsetDetections;
    }

    private string BuildModelSummary(string enginePath)
    {
        var lines = new List<string>
        {
            "模型自检信息",
            "后端: TensorRT Engine",
            $"Engine: {enginePath}",
            $"输入: {inputTensor.Name}",
            $"输入类型: {inputTensor.DataType}",
            $"检测阈值: {scoreThreshold:0.00}",
            $"输入形状: [{FormatDimensions(inputTensor.Dimensions)}]",
            "预处理: RGB / 归一化到 0-1 / NCHW / Letterbox",
            "输出:"
        };

        foreach (TensorRtTensorInfo output in outputTensors)
        {
            lines.Add($"- {output.Name}: {output.DataType} [{FormatDimensions(output.Dimensions)}]");
        }

        lines.Add("解析顺序: 多输出 End-to-End -> 单输出 6 列 End-to-End -> 原始 YOLO 输出");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDimensions(IReadOnlyList<int> dimensions)
    {
        return string.Join(", ", dimensions);
    }

    private static IReadOnlyList<DetectionResult> ApplyNms(List<DetectionResult> detections, float iouThreshold)
    {
        var ordered = detections.OrderByDescending(d => d.Score).ToList();
        var results = new List<DetectionResult>();

        while (ordered.Count > 0)
        {
            var current = ordered[0];
            results.Add(current);
            ordered.RemoveAt(0);

            ordered.RemoveAll(other => current.ClassId == other.ClassId && CalculateIou(current.Box, other.Box) >= iouThreshold);
        }

        return results;
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

        float unionArea = a.Width * a.Height + b.Width * b.Height - intersectionArea;
        return unionArea <= 0 ? 0f : intersectionArea / unionArea;
    }

    private sealed record PreprocessResult(float[] TensorData, float Scale, float PadX, float PadY);
    private sealed record OutputSnapshot(string Name, int[] Dimensions, TensorRtElementType ElementType, float[] Data);
    private sealed record TensorRtTensorInfo(string Name, bool IsInput, TensorRtElementType DataType, int[] Dimensions);
}
