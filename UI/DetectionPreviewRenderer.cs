using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YOLOForAim;

/// <summary>
/// 负责在预览位图上绘制检测框和标签。
/// 当前主界面默认不显示预览，但保留该渲染能力以便后续重新启用。
/// </summary>
internal static class DetectionPreviewRenderer
{
    public static void DrawDetections(Bitmap frame, IReadOnlyList<DetectionResult> detections, Font font)
    {
        using var graphics = Graphics.FromImage(frame);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.Lime, 2f);
        using var labelBackground = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
        using var textBrush = new SolidBrush(Color.Yellow);

        foreach (DetectionResult detection in detections)
        {
            graphics.DrawRectangle(pen, detection.Box.X, detection.Box.Y, detection.Box.Width, detection.Box.Height);

            string text = $"{detection.Label} {detection.Score:P0}";
            SizeF textSize = graphics.MeasureString(text, font);
            float labelY = Math.Max(0, detection.Box.Y - textSize.Height);
            graphics.FillRectangle(labelBackground, detection.Box.X, labelY, textSize.Width + 6, textSize.Height + 2);
            graphics.DrawString(text, font, textBrush, detection.Box.X + 3, labelY + 1);
        }
    }
}
