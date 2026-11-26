using System;
using System.Collections.Generic;
using Svz;

// Minimal test runner (no xunit: the build must work without NuGet access).
internal static class Program
{
	private static int sFailed, sPassed;

	private static void Check(bool cond, string name)
	{
		if (cond) { sPassed++; return; }
		sFailed++;
		Console.WriteLine("FAIL: " + name);
	}

	private static void Near(double a, double b, double eps, string name) => Check(Math.Abs(a - b) <= eps, $"{name} (got {a}, want {b})");

	private static int Main()
	{
		Decide();
		Smoothing();
		Settings();
		Monitor();
		Gesture();
		ApiLevels();
		Stats();
		Console.WriteLine($"{sPassed} passed, {sFailed} failed");
		return sFailed == 0 ? 0 : 1;
	}

	private static void Decide()
	{
		var d = FramePacingPolicy.Decide(120, 120.0, true);
		Check(d.EngineMaxFps == 120 && d.VsyncPaced && d.RequestHz == 120f, "120 on 120 Hz: VSync with a hard 120 FPS cap");
		d = FramePacingPolicy.Decide(120, 119.88, true);
		Check(d.VsyncPaced && d.EngineMaxFps == 120, "119.88 Hz counts as 120 while cap remains 120");
		d = FramePacingPolicy.Decide(240, 240.0, true);
		Check(d.EngineMaxFps == 120 && d.RequestHz == 120f && !d.VsyncPaced, "caller requesting 240 cannot bypass the 120 cap");
		d = FramePacingPolicy.Decide(120, 60.0, true);
		Check(d.EngineMaxFps == 120 && !d.VsyncPaced && d.RequestHz == 120f, "120 on 60 Hz: limiter on, request 120");
		d = FramePacingPolicy.Decide(60, 120.0, true);
		Check(d.EngineMaxFps == 60 && d.RequestHz == 60f, "60 on 120 Hz: cap 60 and ask display for 60");
		d = FramePacingPolicy.Decide(60, -1, false);
		Check(d.EngineMaxFps == 60 && d.RequestHz == 0f, "unknown display, no request: plain cap");
		d = FramePacingPolicy.Decide(0, 60, true);
		Check(d.RequestHz == 60f, "invalid target defaults to 60");
	}

	private static void Smoothing()
	{
		// identical to the original Lerp(prev, dt, 0.1f) at 60 FPS
		float prev = 1f / 30f, dt = 1f / 60f;
		float old = prev + (dt - prev) * 0.1f;
		Near(FramePacingPolicy.SmoothDeltaTime(prev, dt), old, 1e-6, "smoothDt matches old formula at 60 FPS");

		// same response over the same wall time at 60 and 120 FPS (old formula converged twice as fast at 120)
		float s60 = 1f / 30f, s120 = 1f / 30f;
		for (int i = 0; i < 30; i++) s60 = FramePacingPolicy.SmoothDeltaTime(s60, 1f / 60f);
		for (int i = 0; i < 60; i++) s120 = FramePacingPolicy.SmoothDeltaTime(s120, 1f / 60f);
		float n60 = 1f / 30f, n120 = 1f / 30f;
		for (int i = 0; i < 30; i++) n60 = FramePacingPolicy.SmoothDeltaTime(n60, 0.5f / 30f);
		for (int i = 0; i < 60; i++) n120 = FramePacingPolicy.SmoothDeltaTime(n120, 0.5f / 60f);
		// compare normalised distance to the steady state after 0.5 s
		double r60 = (n60 - 1.0 / 60) / (1.0 / 30 - 1.0 / 60);
		double r120 = (n120 - 1.0 / 120) / (1.0 / 30 - 1.0 / 120);
		Near(r60, r120, 0.02, "smoothing time constant independent of frame rate");
		Check(FramePacingPolicy.SmoothDeltaTime(0.02f, 0f) == 0.02f, "zero dt keeps value");
		Check(FramePacingPolicy.SmoothDeltaTime(0.02f, 10f) == 10f, "huge dt clamps alpha to 1");
	}

	private static void Settings()
	{
		var s = FrameSettings.FromValues(null);
		Check(s.TargetFps == 120 && s.FallbackFps == 60 && s.AutoFallback && s.RequestDisplayRate && !s.ShowFps, "defaults");
		s = FrameSettings.FromValues(new Dictionary<string, string> { ["target_fps"] = "60", ["show_fps"] = "True", ["auto_fallback"] = "off" });
		Check(s.TargetFps == 60 && s.ShowFps && !s.AutoFallback, "parse values (Godot writes True/False)");
		s = FrameSettings.FromValues(new Dictionary<string, string> { ["target_fps"] = "144", ["fallback_fps"] = "abc" });
		Check(s.TargetFps == 120 && s.FallbackFps == 60, "invalid values keep defaults");
		s = FrameSettings.FromValues(new Dictionary<string, string> { ["target_fps"] = "60", ["fallback_fps"] = "60" });
		Check(!s.FallbackEnabled && s.AutoFallback, "fallback inactive at target 60, stored choice kept");
		s.TargetFps = 120;
		Check(s.FallbackEnabled, "fallback active again at 120 (regression: was saved as off)");
		Check(FrameSettings.FromValues(s.ToValues()).AutoFallback, "auto_fallback survives a save at 60");
		var round = FrameSettings.FromValues(new FrameSettings { TargetFps = 90, ShowFps = true, AllowNonSeamlessSwitch = true }.ToValues());
		Check(round.TargetFps == 90 && round.ShowFps && round.AllowNonSeamlessSwitch, "round trip");
	}

	private static void Monitor()
	{
		// steady 120 FPS: never triggers
		var m = new FallbackMonitor(120);
		bool fired = false;
		for (int i = 0; i < 120 * 60; i++) fired |= m.Feed(1.0 / 120);
		Check(!fired, "steady 120 FPS does not fall back");

		// Reproduces the S25 Ultra report: ~86.6 FPS with ~1% long (>12.5ms)
		// frames. The previous 80%-of-target threshold forced a 60 FPS cap even
		// though the screen was still delivering substantially more than 60.
		m = new FallbackMonitor(120);
		fired = false;
		for (int i = 0; i < 87 * 60; i++)
			fired |= m.Feed(i % 100 == 0 ? 1.0 / 60 : 1.0 / 87);
		Check(!fired, "~87 FPS with 1% long frames does not hard-lock to 60");
		Check(!m.Triggered, "moderate crowd slowdown leaves 120 target active");

		// steady 60 FPS while targeting 120: triggers after grace (10 s) + 2 windows (10 s)
		m = new FallbackMonitor(120);
		int frameAt = -1;
		for (int i = 0; i < 60 * 60 && frameAt < 0; i++) if (m.Feed(1.0 / 60)) frameAt = i;
		Check(frameAt > 0, "60 FPS against 120 target triggers");
		Near(frameAt / 60.0, 20.0, 0.1, "triggers after grace + two windows");
		Check(!m.Feed(1.0 / 60) && m.Triggered, "fires only once");

		// 15% missed frames: judder below threshold, average still high -> no trigger
		m = new FallbackMonitor(120);
		fired = false;
		for (int i = 0; i < 120 * 60; i++) fired |= m.Feed(i % 20 < 3 ? 2.0 / 120 : 1.0 / 120);
		Check(!fired, "15% missed frames tolerated");

		// 30% missed frames: triggers
		m = new FallbackMonitor(120);
		fired = false;
		for (int i = 0; i < 120 * 60; i++) fired |= m.Feed(i % 10 < 3 ? 2.0 / 120 : 1.0 / 120);
		Check(fired, "30% missed frames triggers");

		// level-load hitches are ignored, and one slow window followed by a good one resets the streak
		m = new FallbackMonitor(120);
		fired = false;
		for (int i = 0; i < 1200; i++) fired |= m.Feed(1.0 / 120);          // grace
		for (int i = 0; i < 300; i++) fired |= m.Feed(1.0 / 60);            // one slow window (5 s)
		fired |= m.Feed(3.0);                                                // hitch
		for (int i = 0; i < 600; i++) fired |= m.Feed(1.0 / 120);           // good window
		for (int i = 0; i < 300; i++) fired |= m.Feed(1.0 / 60);            // one slow window again
		Check(!fired, "non-consecutive slow windows and hitches do not trigger");

		// a loading hitch restarts the grace period: 9 s slow, hitch, 15 s slow -> no fallback yet
		m = new FallbackMonitor(120);
		fired = false;
		for (int i = 0; i < 60 * 9; i++) fired |= m.Feed(1.0 / 60);
		fired |= m.Feed(2.0);
		for (int i = 0; i < 60 * 15; i++) fired |= m.Feed(1.0 / 60);
		Check(!fired, "hitch restarts grace period");

		// Reset() restarts the grace period
		m = new FallbackMonitor(120);
		for (int i = 0; i < 1200; i++) m.Feed(1.0 / 120);
		m.Reset();
		fired = false;
		for (int i = 0; i < 60 * 15; i++) fired |= m.Feed(1.0 / 60);         // 15 s: 10 grace + 1 window
		Check(!fired, "Reset restarts grace period");
	}

	private static void Gesture()
	{
		var g = new HoldGesture(4, 1.5);
		int fires = 0;
		for (int i = 0; i < 120; i++) if (g.Update(3, 1.0 / 60)) fires++;
		Check(fires == 0, "3 fingers do not fire a 4-finger gesture");
		for (int i = 0; i < 80; i++) if (g.Update(4, 1.0 / 60)) fires++;
		Check(fires == 0, "fires only after the hold time");
		for (int i = 0; i < 200; i++) if (g.Update(4, 1.0 / 60)) fires++;
		Check(fires == 1, "fires once per hold");
		var g5 = new HoldGesture(4, 1.5);
		int f5 = 0;
		for (int i = 0; i < 200; i++) if (g5.Update(5, 1.0 / 60)) f5++;
		Check(f5 == 0, "5 fingers do not fire the 4-finger gesture");
		g.Update(0, 1.0 / 60);
		for (int i = 0; i < 100; i++) if (g.Update(4, 1.0 / 60)) fires++;
		Check(fires == 2, "re-arms after release");
	}

	private static void Stats()
	{
		var st = new FrameStats();
		bool done = false;
		for (int i = 0; i < 600 && !done; i++) done = st.Add(i % 100 == 0 ? 2.0 / 120 : 1.0 / 120, 120, 120);
		Check(done, "stats window completes after 5 s");
		// must match the regex in tools/verify/device_check.py (tests/tools/test_tools.py uses this exact line)
		Check(st.Last == "stats fps=118.8 p95=8.3ms max=16.7ms missed=1.0% display=120Hz target=120", "stats line format: " + st.Last);
		Check(!st.Add(1.0, 120, 120), "loading hitch ignored");
	}

	private static void ApiLevels()
	{
		Check(FramePacing_AndroidApiForRelease(17) == 37, "Android 17 -> API 37");
		Check(FramePacing_AndroidApiForRelease(15) == 35, "Android 15 -> API 35");
		Check(FramePacing_AndroidApiForRelease(12) == 31, "Android 12 -> API 31");
		Check(FramePacing_AndroidApiForRelease(11) == 30, "Android 11 -> API 30");
		Check(FramePacing_AndroidApiForRelease(9) == 28, "Android 9 -> below 30");
	}

	// Mirror of FramePacing.AndroidApiForRelease (that file needs Godot); kept in sync by the Godot runtime test.
	private static int FramePacing_AndroidApiForRelease(int major)
	{
		if (major >= 13) return 33 + (major - 13);
		if (major == 12) return 31;
		if (major == 11) return 30;
		if (major == 10) return 29;
		return major > 0 ? 28 : 0;
	}
}
