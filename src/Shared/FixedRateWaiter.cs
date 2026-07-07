using System.Diagnostics;

internal static class FixedRateWaiter
{
    public static void WaitForNextTick(Stopwatch loopTimer, ref double nextLoopAtMs, double intervalMs)
    {
        if (nextLoopAtMs <= 0.0)
        {
            nextLoopAtMs = loopTimer.Elapsed.TotalMilliseconds;
        }

        nextLoopAtMs += intervalMs;
        while (true)
        {
            var remainingMs = nextLoopAtMs - loopTimer.Elapsed.TotalMilliseconds;
            if (remainingMs <= 0.0)
            {
                break;
            }

            if (remainingMs >= 1.5)
            {
                Thread.Sleep(1);
                continue;
            }

            Thread.SpinWait(64);
        }
    }
}
