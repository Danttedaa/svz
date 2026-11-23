using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Svz;

internal static class Program
{
    private static int Main()
    {
        if (VictoryTiming.WinDelayRealSeconds != 1.0) throw new Exception("unexpected target");
        // Different legacy configured durations must yield the same wall-clock wait.
        foreach (float legacyGameSeconds in new[] { 7f, 9f })
        {
            IEnumerator e = VictoryTiming.WaitRealSeconds(legacyGameSeconds);
            if (e == null) throw new Exception("no iterator");
            var clock = Stopwatch.StartNew();
            int steps = 0;
            while (e.MoveNext())
            {
                if (e.Current != null) throw new Exception("timer should yield null each frame");
                if (clock.Elapsed.TotalSeconds > 4) throw new Exception("unscaled timer never finished");
                Thread.Sleep(15);
                steps++;
            }
            if (steps < 2 || clock.Elapsed.TotalSeconds < 0.95 || clock.Elapsed.TotalSeconds > 4)
                throw new Exception($"invalid wall wait: {clock.Elapsed.TotalSeconds:F2}s, {steps} frames");
            Console.WriteLine($"PASS: input {legacyGameSeconds} game-seconds -> {clock.Elapsed.TotalSeconds:F2} seconds real");
        }
        return 0;
    }
}
