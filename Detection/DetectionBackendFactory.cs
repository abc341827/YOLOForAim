namespace YOLOForAim;

/// <summary>
/// 根据用户选择的推理后端创建对应的检测器实例。
/// Form1 只负责收集 UI 参数和启动流程，具体 detector 构造集中在这里。
/// </summary>
internal static class DetectionBackendFactory
{
    public static IDetector Create(
        DetectionStartupPlan startupPlan,
        bool preferGpu,
        float scoreThreshold,
        ColorDetectionOptions primaryColorDetectionOptions)
    {
        DetectorOptions detectorOptions = new(
            startupPlan.Backend,
            preferGpu,
            scoreThreshold,
            startupPlan.TensorRtEnginePath,
            primaryColorDetectionOptions);

        return startupPlan.Backend switch
        {
            DetectorBackend.TensorRtEngine => new TensorRtEngineDetector(startupPlan.TensorRtEnginePath!, detectorOptions),
            DetectorBackend.ColorRectangle => new ColorRectangleDetector(detectorOptions),
            _ => new YoloDetector(startupPlan.DirectMlModelPath!, detectorOptions)
        };
    }
}
