using System.Runtime.InteropServices;
using System.Text;

namespace YOLOForAim;

internal static class TensorRtNativeMethods
{
    private const string DllName = "TensorRtNative";
    internal const int MaxTensorCount = 32;
    internal const int MaxDims = 8;
    private const int ErrorBufferCapacity = 2048;

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int trt_open_engine(
        string enginePath,
        [Out] TensorRtTensorInfoNative[] tensorInfos,
        int tensorInfoCapacity,
        out int tensorCount,
        out IntPtr detectorHandle,
        [MarshalAs(UnmanagedType.LPStr)]
        StringBuilder errorBuffer,
        int errorBufferCapacity);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void trt_close_engine(IntPtr detectorHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int trt_run_inference(
        IntPtr detectorHandle,
        [In] float[] inputData,
        long inputElementCount,
        [MarshalAs(UnmanagedType.LPStr)]
        StringBuilder errorBuffer,
        int errorBufferCapacity);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int trt_copy_output_to_float(
        IntPtr detectorHandle,
        int outputIndex,
        [Out] float[] destination,
        long destinationElementCount,
        [MarshalAs(UnmanagedType.LPStr)]
        StringBuilder errorBuffer,
        int errorBufferCapacity);

    internal static IntPtr OpenEngine(string enginePath, out TensorRtTensorInfoNative[] tensorInfos)
    {
        tensorInfos = new TensorRtTensorInfoNative[MaxTensorCount];
        for (int i = 0; i < tensorInfos.Length; i++)
        {
            tensorInfos[i].Dims = new long[MaxDims];
            tensorInfos[i].Name = string.Empty;
        }

        StringBuilder error = new(ErrorBufferCapacity);
        int success = trt_open_engine(enginePath, tensorInfos, tensorInfos.Length, out int tensorCount, out IntPtr handle, error, error.Capacity);
        if (success == 0 || handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(GetErrorMessage(error, $"无法打开 TensorRT engine: {enginePath}"));
        }

        if (tensorCount <= 0 || tensorCount > tensorInfos.Length)
        {
            trt_close_engine(handle);
            throw new InvalidOperationException($"TensorRT engine 返回的 I/O 张量数量无效: {tensorCount}");
        }

        if (tensorCount != tensorInfos.Length)
        {
            Array.Resize(ref tensorInfos, tensorCount);
        }

        return handle;
    }

    internal static void RunInference(IntPtr detectorHandle, float[] inputData)
    {
        StringBuilder error = new(ErrorBufferCapacity);
        int success = trt_run_inference(detectorHandle, inputData, inputData.LongLength, error, error.Capacity);
        if (success == 0)
        {
            throw new InvalidOperationException(GetErrorMessage(error, "TensorRT 推理失败。"));
        }
    }

    internal static void CopyOutputToFloat(IntPtr detectorHandle, int outputIndex, float[] destination)
    {
        StringBuilder error = new(ErrorBufferCapacity);
        int success = trt_copy_output_to_float(detectorHandle, outputIndex, destination, destination.LongLength, error, error.Capacity);
        if (success == 0)
        {
            throw new InvalidOperationException(GetErrorMessage(error, $"读取 TensorRT 输出 {outputIndex} 失败。"));
        }
    }

    private static string GetErrorMessage(StringBuilder errorBuffer, string fallbackMessage)
    {
        return string.IsNullOrWhiteSpace(errorBuffer.ToString()) ? fallbackMessage : errorBuffer.ToString();
    }
}

internal enum TensorRtElementType
{
    Unknown = 0,
    Float32 = 1,
    Float16 = 2,
    Int32 = 3,
    Int64 = 4,
    Int8 = 5,
    UInt8 = 6,
    Bool = 7
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct TensorRtTensorInfoNative
{
    public int IsInput;
    public int DataType;
    public int NbDims;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = TensorRtNativeMethods.MaxDims)]
    public long[] Dims;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Name;
}
