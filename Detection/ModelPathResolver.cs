namespace YOLOForAim;

/// <summary>
/// 负责解析检测模型和 TensorRT engine 文件路径。
/// 只在程序目录中按候选文件名查找，保持原有加载顺序和回退行为。
/// </summary>
internal static class ModelPathResolver
{
    private static readonly string[] DirectMlModelCandidates = ["exp.onnx", "dawn.onnx", "dawn01.onnx"];
    private static readonly string[] TensorRtEngineCandidates = ["dawn2.engine", "dawn.engine"];

    public static string ResolveDirectMlModelPath()
    {
        string? existingFile = FindExistingFile(DirectMlModelCandidates);
        return existingFile ?? Path.Combine(AppContext.BaseDirectory, DirectMlModelCandidates[0]);
    }

    public static string? ResolveTensorRtEnginePath()
    {
        return FindExistingFile(TensorRtEngineCandidates);
    }

    private static string? FindExistingFile(IEnumerable<string> candidateFileNames)
    {
        foreach (string candidateFileName in candidateFileNames)
        {
            string candidatePath = Path.Combine(AppContext.BaseDirectory, candidateFileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }
}
