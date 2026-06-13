using System.Reflection;
using System.Runtime.InteropServices;

namespace YOLOForAim;

internal static class TensorRtNativeLibraryResolver
{
    private const string NativeLibraryName = "TensorRtNative";
    private const string RuntimeSelectorEnvironmentVariable = "YOLOFORAIM_TENSORRT_RUNTIME";
    private const string RuntimeSelectorFileName = "tensorrt-runtime.txt";

    private static readonly string[] AutoRuntimeFolders =
    [
        string.Empty,
        @"runtimes\tensorrt11-cuda13",
        @"runtimes\tensorrt11-cuda12",
        @"runtimes\tensorrt10-cuda12"
    ];

    private static bool registered;

    public static void Register()
    {
        if (registered)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(TensorRtNativeMethods).Assembly, ResolveTensorRtNativeLibrary);
        registered = true;
    }

    private static IntPtr ResolveTensorRtNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;

        if (!IsTensorRtNativeLibraryName(libraryName))
        {
            return IntPtr.Zero;
        }

        return LoadTensorRtNativeLibrary();
    }

    private static bool IsTensorRtNativeLibraryName(string libraryName)
    {
        return string.Equals(libraryName, NativeLibraryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(libraryName, NativeLibraryName + ".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static IntPtr LoadTensorRtNativeLibrary()
    {
        string baseDirectory = AppContext.BaseDirectory;
        IReadOnlyList<string> candidateDirectories = GetCandidateDirectories(baseDirectory);
        List<string> loadErrors = [];

        foreach (string candidateDirectory in candidateDirectories)
        {
            string nativeDllPath = Path.Combine(candidateDirectory, NativeLibraryName + ".dll");
            if (!File.Exists(nativeDllPath))
            {
                continue;
            }

            try
            {
                SetDllDirectory(candidateDirectory);
                return NativeLibrary.Load(nativeDllPath);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                loadErrors.Add($"{nativeDllPath}: {ex.Message}");
            }
        }

        string configuredRuntime = GetConfiguredRuntime();
        string runtimeHint = string.IsNullOrWhiteSpace(configuredRuntime)
            ? "未配置 TensorRT runtime，将按根目录 -> cuda13 -> cuda12 顺序自动尝试。"
            : $"当前 TensorRT runtime 配置为 '{configuredRuntime}'。";
        string details = loadErrors.Count == 0 ? "未找到可用的 TensorRtNative.dll。" : string.Join(Environment.NewLine, loadErrors);
        throw new DllNotFoundException($"无法加载 TensorRtNative.dll。{runtimeHint}{Environment.NewLine}{details}");
    }

    private static IReadOnlyList<string> GetCandidateDirectories(string baseDirectory)
    {
        string configuredRuntime = GetConfiguredRuntime();
        if (!string.IsNullOrWhiteSpace(configuredRuntime) && !IsAuto(configuredRuntime))
        {
            return [ResolveRuntimeDirectory(baseDirectory, configuredRuntime)];
        }

        return AutoRuntimeFolders
            .Select(folder => string.IsNullOrWhiteSpace(folder) ? baseDirectory : Path.Combine(baseDirectory, folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetConfiguredRuntime()
    {
        string? environmentValue = Environment.GetEnvironmentVariable(RuntimeSelectorEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        string selectorFilePath = Path.Combine(AppContext.BaseDirectory, RuntimeSelectorFileName);
        if (!File.Exists(selectorFilePath))
        {
            return string.Empty;
        }

        string? fileValue = File.ReadLines(selectorFilePath)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'));
        return fileValue ?? string.Empty;
    }

    private static bool IsAuto(string configuredRuntime)
    {
        return string.Equals(configuredRuntime, "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRuntimeDirectory(string baseDirectory, string configuredRuntime)
    {
        string normalizedRuntime = configuredRuntime.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalizedRuntime, "base", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedRuntime, "root", StringComparison.OrdinalIgnoreCase))
        {
            return baseDirectory;
        }

        string? aliasFolder = normalizedRuntime.ToLowerInvariant() switch
        {
            "cuda13" or "trt11-cuda13" or "tensorrt11-cuda13" or "11-cuda13" => @"runtimes\tensorrt11-cuda13",
            "cuda12" or "trt11-cuda12" or "tensorrt11-cuda12" or "11-cuda12" => @"runtimes\tensorrt11-cuda12",
            "trt10-cuda12" or "tensorrt10-cuda12" or "10-cuda12" => @"runtimes\tensorrt10-cuda12",
            _ => null
        };

        string runtimePath = aliasFolder ?? normalizedRuntime;
        return Path.IsPathRooted(runtimePath) ? runtimePath : Path.Combine(baseDirectory, runtimePath);
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);
}
