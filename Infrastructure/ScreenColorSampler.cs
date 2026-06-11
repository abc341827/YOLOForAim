using System.Drawing;
using System.Windows.Forms;

namespace YOLOForAim;

/// <summary>
/// 封装屏幕取色和 RGB/HSV 转换逻辑。
/// Form1 只负责决定取色来源优先级，像素读取细节集中在这里。
/// </summary>
internal static class ScreenColorSampler
{
    public static Color GetScreenColorAtCursor()
    {
        Point cursorPosition = Cursor.Position;
        using Bitmap bitmap = new(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(cursorPosition, Point.Empty, new Size(1, 1), CopyPixelOperation.SourceCopy);
        return bitmap.GetPixel(0, 0);
    }

    public static bool TryGetColorAtCursor(CapturedPixelFrame frame, out Color color)
    {
        return TryGetColorAtPoint(frame, Cursor.Position, out color);
    }

    public static bool TryGetColorAtPoint(CapturedPixelFrame frame, Point screenPoint, out Color color)
    {
        color = Color.Empty;
        if (!frame.ScreenBounds.Contains(screenPoint))
        {
            return false;
        }

        int x = screenPoint.X - frame.ScreenBounds.Left;
        int y = screenPoint.Y - frame.ScreenBounds.Top;
        if ((uint)x >= (uint)frame.Width || (uint)y >= (uint)frame.Height)
        {
            return false;
        }

        int pixelOffset = (y * frame.Stride) + (x * 4);
        byte b = frame.Pixels[pixelOffset];
        byte g = frame.Pixels[pixelOffset + 1];
        byte r = frame.Pixels[pixelOffset + 2];
        color = Color.FromArgb(r, g, b);
        return true;
    }

    public static (float Hue, int Saturation, int Value) RgbToHsv(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        int delta = max - min;
        if (delta == 0)
        {
            return (0f, 0, max);
        }

        float hue;
        if (max == r)
        {
            hue = 60f * ((g - b) / (float)delta);
            if (hue < 0f)
            {
                hue += 360f;
            }
        }
        else if (max == g)
        {
            hue = 60f * (((b - r) / (float)delta) + 2f);
        }
        else
        {
            hue = 60f * (((r - g) / (float)delta) + 4f);
        }

        int saturation = max == 0 ? 0 : delta * 255 / max;
        return (hue, saturation, max);
    }
}
