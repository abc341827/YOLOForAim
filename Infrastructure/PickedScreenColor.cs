namespace YOLOForAim;

/// <summary>
/// 一次屏幕取色结果，包含颜色值和实际取色来源。
/// </summary>
internal sealed record PickedScreenColor(Color Color, string Source);
