using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace YOLOForAim;

internal sealed class YoloDetector : IDisposable
{
    private const float NmsThreshold = 0.45f;

    private readonly InferenceSession session;
    private readonly string inputName;
    private readonly int inputWidth;
    private readonly int inputHeight;
    private readonly TensorElementType inputElementType;
    private readonly string executionProvider;
    private readonly string executionProviderDetails;
    private readonly float scoreThreshold;

    public string ModelSummary { get; }

    public YoloDetector(string modelPath, DetectorOptions detectorOptions)
    {
        scoreThreshold = detectorOptions.ScoreThreshold;

        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        (executionProvider, executionProviderDetails) = ConfigureExecutionProvider(sessionOptions, detectorOptions.PreferGpu);

        session = new InferenceSession(modelPath, sessionOptions);
        inputName = session.InputMetadata.Keys.First();

        var inputMetadata = session.InputMetadata[inputName];
        var dimensions = inputMetadata.Dimensions;
        inputHeight = dimensions.Length > 2 && dimensions[2] > 0 ? dimensions[2] : 640;
        inputWidth = dimensions.Length > 3 && dimensions[3] > 0 ? dimensions[3] : 640;
        inputElementType = inputMetadata.ElementDataType;
        ModelSummary = BuildModelSummary();
    }

    public DetectionRunResult Detect(Bitmap sourceImage)
    {
        var preprocess = Preprocess(sourceImage);
        try
        {
            var inputs = CreateInputs(preprocess.TensorData);

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);
            var outputs = new List<OutputSnapshot>();
            foreach (var result in results)
            {
                outputs.Add(CreateOutputSnapshot(result));
            }

            IReadOnlyList<DetectionResult> detections = ParseOutputs(outputs, sourceImage.Size, preprocess.Scale, preprocess.PadX, preprocess.PadY);
            string debugSummary = BuildRuntimeSummary(outputs, detections.Count);
            return new DetectionRunResult(detections, debugSummary);
        }
        finally
        {
            preprocess.Dispose();
        }
    }

    public void Dispose()
    {
        session.Dispose();
    }

    private static (string Provider, string Details) ConfigureExecutionProvider(SessionOptions sessionOptions, bool preferGpu)
    {
        if (!preferGpu)
        {
            return ("CPU", "未勾选 GPU 优先，当前使用 CPU。");
        }

        try
        {
            sessionOptions.AppendExecutionProvider_DML(0);
            return ("DirectML(GPU)", "DirectML 初始化成功。");
        }
        catch (Exception ex)
        {
            return ("CPU(DirectML不可用，已回退)", ex.Message);
        }
    }

    private List<NamedOnnxValue> CreateInputs(float[] tensorData)
    {
        if (inputElementType == TensorElementType.Float16)
        {
            Half[] halfTensorData = tensorData.Select(static value => (Half)value).ToArray();
            var halfTensor = new DenseTensor<Half>(halfTensorData, new[] { 1, 3, inputHeight, inputWidth });
            return new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, halfTensor)
            };
        }

        var floatTensor = new DenseTensor<float>(tensorData, new[] { 1, 3, inputHeight, inputWidth });
        return new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, floatTensor)
        };
    }

    private PreprocessResult Preprocess(Bitmap image)
    {
        float scale = Math.Min((float)inputWidth / image.Width, (float)inputHeight / image.Height);
        int resizedWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        int resizedHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        float padX = (inputWidth - resizedWidth) / 2f;
        float padY = (inputHeight - resizedHeight) / 2f;

        var canvas = new Bitmap(inputWidth, inputHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, padX, padY, resizedWidth, resizedHeight);
        }

        var rect = new Rectangle(0, 0, inputWidth, inputHeight);
        BitmapData bitmapData = canvas.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int byteCount = Math.Abs(bitmapData.Stride) * inputHeight;
            byte[] pixelBytes = new byte[byteCount];
            System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixelBytes, 0, byteCount);

            float[] tensorData = new float[3 * inputWidth * inputHeight];
            int planeSize = inputWidth * inputHeight;

            for (int y = 0; y < inputHeight; y++)
            {
                int rowOffset = y * bitmapData.Stride;
                for (int x = 0; x < inputWidth; x++)
                {
                    int pixelOffset = rowOffset + (x * 3);
                    int tensorOffset = y * inputWidth + x;

                    byte blue = pixelBytes[pixelOffset];
                    byte green = pixelBytes[pixelOffset + 1];
                    byte red = pixelBytes[pixelOffset + 2];

                    tensorData[tensorOffset] = red / 255f;
                    tensorData[planeSize + tensorOffset] = green / 255f;
                    tensorData[(planeSize * 2) + tensorOffset] = blue / 255f;
                }
            }

            return new PreprocessResult(canvas, tensorData, scale, padX, padY);
        }
        finally
        {
            canvas.UnlockBits(bitmapData);
        }
    }

    private OutputSnapshot CreateOutputSnapshot(DisposableNamedOnnxValue result)
    {
        var metadata = session.OutputMetadata[result.Name];
        return metadata.ElementDataType switch
        {
            TensorElementType.Float => CreateOutputSnapshot(result.Name, metadata.ElementDataType, result.AsTensor<float>().Dimensions.ToArray(), result.AsTensor<float>().ToArray()),
            TensorElementType.Float16 => CreateOutputSnapshot(result.Name, metadata.ElementDataType, result.AsTensor<Half>().Dimensions.ToArray(), result.AsTensor<Half>().ToArray().Select(static value => (float)value).ToArray()),
            TensorElementType.Int64 => CreateOutputSnapshot(result.Name, metadata.ElementDataType, result.AsTensor<long>().Dimensions.ToArray(), result.AsTensor<long>().ToArray().Select(static value => (float)value).ToArray()),
            TensorElementType.Int32 => CreateOutputSnapshot(result.Name, metadata.ElementDataType, result.AsTensor<int>().Dimensions.ToArray(), result.AsTensor<int>().ToArray().Select(static value => (float)value).ToArray()),
            _ => throw new NotSupportedException($"暂不支持输出类型: {metadata.ElementDataType}")
        };
    }

    private static OutputSnapshot CreateOutputSnapshot(string name, TensorElementType elementType, int[] dimensions, float[] data)
    {
        return new OutputSnapshot(name, dimensions, elementType, data);
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

    private string BuildModelSummary()
    {
        var lines = new List<string>
        {
            "模型自检信息",
            $"输入: {inputName}",
            $"输入类型: {inputElementType}",
            $"执行设备: {executionProvider}",
            $"设备说明: {executionProviderDetails}",
            $"检测阈值: {scoreThreshold:0.00}",
            $"输入形状: [1, 3, {inputHeight}, {inputWidth}]",
            "预处理: RGB / 归一化到 0-1 / NCHW / Letterbox",
            "输出:"
        };

        foreach (var output in session.OutputMetadata)
        {
            lines.Add($"- {output.Key}: {output.Value.ElementDataType} [{FormatDimensions(output.Value.Dimensions)}]");
        }

        lines.Add("解析顺序: 多输出 End-to-End -> 单输出 6 列 End-to-End -> 原始 YOLO 输出");
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildRuntimeSummary(IReadOnlyList<OutputSnapshot> outputs, int detectionCount)
    {
        var lines = new List<string>
        {
            ModelSummary,
            string.Empty,
            "最近一次推理输出采样",
            $"检测数量: {detectionCount}"
        };

        foreach (var output in outputs)
        {
            string sample = string.Join(", ",
                output.Data.Take(6).Select(static value => value.ToString("0.####")));
            lines.Add($"- {output.Name}: {output.ElementType} [{FormatDimensions(output.Dimensions)}] sample=[{sample}]");
        }

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

    private sealed record PreprocessResult(Bitmap Canvas, float[] TensorData, float Scale, float PadX, float PadY) : IDisposable
    {
        public void Dispose()
        {
            Canvas.Dispose();
        }
    }

    private sealed record OutputSnapshot(string Name, int[] Dimensions, TensorElementType ElementType, float[] Data);
}

internal sealed record DetectionResult(RectangleF Box, float Score, int ClassId, string Label);
internal sealed record DetectionRunResult(IReadOnlyList<DetectionResult> Detections, string DebugSummary);
internal sealed record DetectorOptions(bool PreferGpu, float ScoreThreshold);
