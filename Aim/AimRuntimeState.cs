using System.Drawing;

namespace YOLOForAim;

/// <summary>
/// 瞄准辅助在运行过程中的可变状态。
/// 与 AimRuntimeSettings 的区别：Settings 是 UI 参数快照，State 是锁定目标、平滑点、丢帧计数等运行时数据。
/// </summary>
internal sealed class AimRuntimeState
{
    public PointF? LockedTargetScreenPoint { get; set; }
    public PointF? SmoothedTargetScreenPoint { get; set; }
    public PointF? PreviousObservedTargetPoint { get; set; }
    public long PreviousObservedTargetTick { get; set; }
    public int MissedTargetFrames { get; set; }
    public long LastFireActivityTick { get; set; }
    public bool WasLeftMouseButtonDown { get; set; }
    public long LastAimMoveTick { get; set; }
    public int LastAimMoveFrameVersion { get; set; } = -1;
    public int LastPendingCompensationFrameVersion { get; set; } = -1;
    public float StabilizedAimTargetHeight { get; set; }
    public PointF PendingAimCompensation { get; set; }
    public DetectionResult? StabilizedLockedDetection { get; set; }
    public int StabilizedLockedDetectionFrames { get; set; }
    public bool HasAppliedInitialLockPull { get; set; }
    public long StableTargetSizeHoldUntilTick { get; set; }
    public long PendingTargetSwitchTick { get; set; }
    public int SuppressOverlayFrameVersion { get; set; } = -1;
    public int SuspendAimUntilFrameVersion { get; set; } = -1;
    public long SuspendAimUntilTick { get; set; }

    public void ResetRuntime()
    {
        ResetTracking();
        LastFireActivityTick = 0;
        WasLeftMouseButtonDown = false;
        LastAimMoveTick = 0;
        LastAimMoveFrameVersion = -1;
        LastPendingCompensationFrameVersion = -1;
        PendingAimCompensation = PointF.Empty;
        StableTargetSizeHoldUntilTick = 0;
        SuppressOverlayFrameVersion = -1;
        SuspendAimUntilFrameVersion = -1;
        SuspendAimUntilTick = 0;
    }

    public void ResetTracking()
    {
        LockedTargetScreenPoint = null;
        SmoothedTargetScreenPoint = null;
        PreviousObservedTargetPoint = null;
        PreviousObservedTargetTick = 0;
        MissedTargetFrames = 0;
        StabilizedAimTargetHeight = 0f;
        StabilizedLockedDetection = null;
        StabilizedLockedDetectionFrames = 0;
        HasAppliedInitialLockPull = false;
        PendingTargetSwitchTick = 0;
    }
}
