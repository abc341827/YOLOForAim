namespace YOLOForAim;

/// <summary>
/// 检测启动前解析出的后端和模型路径信息。
/// 负责集中校验 ONNX/Engine 文件是否可用，并生成启动状态文本。
/// </summary>
internal sealed record DetectionStartupPlan(DetectorBackend Backend, string? DirectMlModelPath, string? TensorRtEnginePath)
{
    public static bool TryCreate(DetectorBackend backend, out DetectionStartupPlan plan, out string? errorMessage)
    {
        string? directMlModelPath = backend == DetectorBackend.OnnxRuntimeDirectMl
            ? ModelPathResolver.ResolveDirectMlModelPath()
            : null;
        string? tensorRtEnginePath = backend == DetectorBackend.TensorRtEngine
            ? ModelPathResolver.ResolveTensorRtEnginePath()
            : null;

        if (backend == DetectorBackend.TensorRtEngine && string.IsNullOrWhiteSpace(tensorRtEnginePath))
        {
            plan = new DetectionStartupPlan(backend, directMlModelPath, tensorRtEnginePath);
            errorMessage = "未找到 TensorRT engine 文件。";
            return false;
        }

        if (backend == DetectorBackend.OnnxRuntimeDirectMl && (directMlModelPath is null || !File.Exists(directMlModelPath)))
        {
            plan = new DetectionStartupPlan(backend, directMlModelPath, tensorRtEnginePath);
            errorMessage = $"未找到模型文件: {directMlModelPath}";
            return false;
        }

        plan = new DetectionStartupPlan(backend, directMlModelPath, tensorRtEnginePath);
        errorMessage = null;
        return true;
    }

    public string GetBackendDisplayName()
    {
        return Backend switch
        {
            DetectorBackend.TensorRtEngine => "TensorRT Engine",
            DetectorBackend.ColorRectangle => "颜色检测",
            _ => "ONNX Runtime / DirectML"
        };
    }

    public string GetStartupStatusText(CaptureRuntimeSettings captureSettings)
    {
        string engineText = Backend == DetectorBackend.TensorRtEngine
            ? $", engine={Path.GetFileName(TensorRtEnginePath)}"
            : string.Empty;
        string modelText = DirectMlModelPath is null ? string.Empty : $", 模型={Path.GetFileName(DirectMlModelPath)}";
        string roiText = captureSettings.CenterRoiOnly ? $"中心 {captureSettings.RoiSize}" : "全窗口";
        return $"检测中... 后端={GetBackendDisplayName()}{modelText}{engineText}, ROI={roiText}";
    }
}
