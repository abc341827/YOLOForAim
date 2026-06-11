using System.Drawing;

namespace YOLOForAim;

/// <summary>
/// 表示一次目标选择得到的候选结果，包含检测框、实际瞄准点和距离评分。
/// </summary>
internal sealed record TargetCandidate(DetectionResult Detection, PointF TargetPoint, double DistanceSquared);
