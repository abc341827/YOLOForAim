using System.Diagnostics;

namespace YOLOForAim;

/// <summary>
/// 统计推理 FPS。调用方每处理一帧传入耗时，计数器按固定时间窗口刷新当前 FPS。
/// </summary>
internal sealed class InferenceFpsCounter
{
    private readonly Stopwatch uiStopwatch = new();
    private double accumulatedMilliseconds;
    private int frameCounter;

    public double CurrentFps { get; private set; }

    public void Restart()
    {
        CurrentFps = 0;
        accumulatedMilliseconds = 0;
        frameCounter = 0;
        uiStopwatch.Restart();
    }

    public void Reset()
    {
        CurrentFps = 0;
        accumulatedMilliseconds = 0;
        frameCounter = 0;
        uiStopwatch.Reset();
    }

    public void AddFrame(double elapsedMilliseconds)
    {
        accumulatedMilliseconds += elapsedMilliseconds;
        frameCounter++;

        if (uiStopwatch.ElapsedMilliseconds < 1000)
        {
            return;
        }

        CurrentFps = accumulatedMilliseconds <= 0
            ? 0
            : frameCounter * 1000d / accumulatedMilliseconds;
        accumulatedMilliseconds = 0;
        frameCounter = 0;
        uiStopwatch.Restart();
    }
}
