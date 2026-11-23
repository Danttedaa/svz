using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
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
        // Startup/loading and driver stalls must not dominate steady-state crowd timings.
        private const double MaxSampleSeconds = 0.25;
        private static long sRenderStart;
        private static long sGuiStart;
        private static long sReportAt;
        private static long sRenderTicks, sGuiTicks;
        private static long sRenderMax, sGuiMax;
        private static int sRenderCount, sGuiCount, sIgnoredRenderHitches, sIgnoredGuiHitches;
        private static readonly Dictionary<Type, int> sRendererKinds = new();
        private static long sRendererStart, sSkinnedTicks, sParticleTicks;
        private static int sRendererKind, sSkinnedCount, sParticleCount;
        private static long sFrameSkinTicks, sFrameParticleTicks;
        private static int sFrameSkinCount, sFrameParticleCount;

        public static void BeginRender()
        {
            sFrameSkinTicks = sFrameParticleTicks = 0;
            sFrameSkinCount = sFrameParticleCount = 0;
            sRenderStart = FramePacing.Settings.LogStats ? Stopwatch.GetTimestamp() : 0;
        }

        public static void EndRender()
        {
            if (sRenderStart == 0) return;
            long now = Stopwatch.GetTimestamp();
            long ticks = now - sRenderStart;
            sRenderStart = 0;
            if (ticks < 0) return;
            if ((double)ticks / Stopwatch.Frequency > MaxSampleSeconds)
            {
                sIgnoredRenderHitches++;
                return;
            }
            sRenderTicks += ticks;
            sRenderMax = Math.Max(sRenderMax, ticks);
            sRenderCount++;
            // Nested renderer samples belong to the same frame. Discard them together
            // when a long Render.Sync hitch was excluded from the denominator.
            sSkinnedTicks += sFrameSkinTicks;
            sParticleTicks += sFrameParticleTicks;
            sSkinnedCount += sFrameSkinCount;
            sParticleCount += sFrameParticleCount;
        }

        // Called around each original Render.SyncRenderer invocation. No geometry,
        // visibility, animation or material decisions are changed.
        // Type classification is cached so it is not repeated for every ally/frame.
        public static void BeginRenderer(object renderer)
        {
            if (!FramePacing.Settings.LogStats) { sRendererStart = 0; return; }
            Type type = renderer?.GetType();
            int kind = 0;
            if (type != null)
            {
                if (!sRendererKinds.TryGetValue(type, out kind))
                {
                    string name = type.Name;
                    kind = name == "SkinnedMeshRenderer" ? 1 : name == "ParticleRenderer" ? 2 : 0;
                    sRendererKinds[type] = kind;
                }
            }
            sRendererKind = kind;
            sRendererStart = Stopwatch.GetTimestamp();
        }

        public static void EndRenderer()
        {
            if (sRendererStart == 0) return;
            long ticks = Stopwatch.GetTimestamp() - sRendererStart;
            sRendererStart = 0;
            if (ticks < 0) return;
            if (sRendererKind == 1) { sFrameSkinTicks += ticks; sFrameSkinCount++; }
            else if (sRendererKind == 2) { sFrameParticleTicks += ticks; sFrameParticleCount++; }
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
                if ((double)ticks / Stopwatch.Frequency > MaxSampleSeconds) sIgnoredGuiHitches++;
                else
                {
                    sGuiTicks += ticks;
                    sGuiMax = Math.Max(sGuiMax, ticks);
                    sGuiCount++;
                }
            }
            if (sReportAt == 0) sReportAt = now;
            if ((double)(now - sReportAt) / Stopwatch.Frequency < ReportSeconds) return;
            if (sRenderCount > 0 && sGuiCount > 0)
            {
                double ms = 1000.0 / Stopwatch.Frequency;
                double renderAvg = sRenderTicks * ms / sRenderCount;
                double guiAvg = sGuiTicks * ms / sGuiCount;
                double skinAvg = sSkinnedTicks * ms / sRenderCount;
                double fxAvg = sParticleTicks * ms / sRenderCount;
                FramePacing.ExtraPerfOverlay = string.Format(CultureInfo.InvariantCulture,
                    "Render {0:0.0}ms | Skin {1:0.0} FX {2:0.0} UI {3:0.0}ms (CPU)",
                    renderAvg, skinAvg, fxAvg, guiAvg);
                GD.Print(string.Format(CultureInfo.InvariantCulture,
                    "[svz-perf] render-sync avg={0:0.00}ms max={1:0.00}ms n={2} gui avg={3:0.00}ms max={4:0.00}ms n={5} skin={6:0.00}ms ({7:0.0}/frame) fx={8:0.00}ms ({9:0.0}/frame) excluded_hitches={10}/{11} (CPU-thread wall time; GPU not included)",
                    renderAvg, sRenderMax * ms, sRenderCount, guiAvg, sGuiMax * ms, sGuiCount,
                    skinAvg, (double)sSkinnedCount / sRenderCount, fxAvg, (double)sParticleCount / sRenderCount,
                    sIgnoredRenderHitches, sIgnoredGuiHitches));
            }
            sRenderTicks = sGuiTicks = sRenderMax = sGuiMax = 0;
            sRenderCount = sGuiCount = 0;
            sIgnoredRenderHitches = sIgnoredGuiHitches = 0;
            sSkinnedTicks = sParticleTicks = 0;
            sSkinnedCount = sParticleCount = 0;
            sReportAt = now;
        }
    }
}
