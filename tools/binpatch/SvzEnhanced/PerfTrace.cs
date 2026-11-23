using System;
using System.Diagnostics;
using System.Globalization;
using Godot;

namespace Svz
{
    /// <summary>
    /// Low-overhead, on-phone timing for the existing _Process render and GUI
    /// calls. Measures managed-thread *wall* time inside those calls; it does
    /// not measure GPU completion, physics, or the whole frame.
    ///
    /// The binary patcher brackets the original calls, preserving order and
    /// all inputs/outputs. No scene objects, textures or game data are changed.
    /// One [svz-perf] line every ~5 seconds while LogStats is enabled.
    /// </summary>
    public static class PerfTrace
    {
        private const double ReportSeconds = 5.0;
        private static long sRenderStart;
        private static long sGuiStart;
        private static long sReportAt;
        private static long sRenderTicks, sGuiTicks;
        private static long sRenderMax, sGuiMax;
        private static int sRenderCount, sGuiCount;

        public static void BeginRender()
        {
            sRenderStart = FramePacing.Settings.LogStats ? Stopwatch.GetTimestamp() : 0;
        }

        public static void EndRender()
        {
            if (sRenderStart == 0) return;
            long now = Stopwatch.GetTimestamp();
            long ticks = now - sRenderStart;
            sRenderStart = 0;
            if (ticks < 0) return;
            sRenderTicks += ticks;
            sRenderMax = Math.Max(sRenderMax, ticks);
            sRenderCount++;
        }

        public static void BeginGui()
        {
            sGuiStart = FramePacing.Settings.LogStats ? Stopwatch.GetTimestamp() : 0;
        }

        public static void EndGui()
        {
            if (sGuiStart == 0) return;
            long now = Stopwatch.GetTimestamp();
            long ticks = now - sGuiStart;
            sGuiStart = 0;
            if (ticks >= 0)
            {
                sGuiTicks += ticks;
                sGuiMax = Math.Max(sGuiMax, ticks);
                sGuiCount++;
            }
            if (sReportAt == 0) sReportAt = now;
            if ((double)(now - sReportAt) / Stopwatch.Frequency < ReportSeconds) return;
            if (sRenderCount > 0 && sGuiCount > 0)
            {
                double ms = 1000.0 / Stopwatch.Frequency;
                double renderAvg = sRenderTicks * ms / sRenderCount;
                double guiAvg = sGuiTicks * ms / sGuiCount;
                FramePacing.ExtraPerfOverlay = string.Format(CultureInfo.InvariantCulture,
                    "Render {0:0.0}ms max {1:0.0} | UI {2:0.0}ms (CPU)", renderAvg, sRenderMax * ms, guiAvg);
                GD.Print(string.Format(CultureInfo.InvariantCulture,
                    "[svz-perf] render-sync avg={0:0.00}ms max={1:0.00}ms n={2} gui avg={3:0.00}ms max={4:0.00}ms n={5} (CPU-thread wall time; GPU not included)",
                    renderAvg, sRenderMax * ms, sRenderCount, guiAvg, sGuiMax * ms, sGuiCount));
            }
            sRenderTicks = sGuiTicks = sRenderMax = sGuiMax = 0;
            sRenderCount = sGuiCount = 0;
            sReportAt = now;
        }
    }
}
