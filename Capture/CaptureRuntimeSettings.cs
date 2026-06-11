namespace YOLOForAim;

/// <summary>
/// 捕获和预览相关的运行时参数快照。
/// 与瞄准参数分离，避免 Form1 中散落多个捕获状态字段。
/// </summary>
internal sealed record CaptureRuntimeSettings(
    bool CenterRoiOnly,
    int RoiSize,
    int PreviewInterval)
{
    public static CaptureRuntimeSettings Default { get; } = new(false, 640, 1);
}
