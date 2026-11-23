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
        // M9: normal wins wait 1 s; the one-time wave-70 story ending keeps the configured time.
        double Run(IEnumerator e)
        {
            var c = Stopwatch.StartNew();
            while (e.MoveNext()) { if (c.Elapsed.TotalSeconds > 6) throw new Exception("never finished"); Thread.Sleep(10); }
            return c.Elapsed.TotalSeconds;
        }
        double normal = Run(VictoryTiming.WaitAfterWin(7f, new object()));   // no Profile type: not the ending
        if (normal < 0.95 || normal > 2) throw new Exception($"normal win: {normal:F2}s");
        double nullGame = Run(VictoryTiming.WaitAfterWin(7f, null));
        if (nullGame < 0.95 || nullGame > 2) throw new Exception($"null game: {nullGame:F2}s");
        VictoryTiming.IsStoryEnding = _ => true;
        double ending = Run(VictoryTiming.WaitAfterWin(2.5f, new object()));
        if (ending < 2.45 || ending > 4) throw new Exception($"story ending: {ending:F2}s");
        double tiny = Run(VictoryTiming.WaitAfterWin(0.1f, new object()));
        if (tiny < 0.95 || tiny > 2) throw new Exception($"ending never shorter than 1 s: {tiny:F2}s");
        VictoryTiming.IsStoryEnding = _ => throw new InvalidOperationException("boom");
        double failed = Run(VictoryTiming.WaitAfterWin(7f, new object()));
        if (failed < 0.95 || failed > 2) throw new Exception($"detector failure keeps the fast wait: {failed:F2}s");
        Console.WriteLine($"PASS: normal {normal:F2}s, null {nullGame:F2}s, story ending {ending:F2}s, clamp {tiny:F2}s");
        return 0;
    }
}
