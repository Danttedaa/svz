using System;
using System.Collections.Generic;
using System.Globalization;

namespace Svz
{
	/// User-selectable frame-rate settings, stored in user://svz_enhanced.cfg ([display] section).
	/// Pure C# (no Godot types) so it can be unit tested off-device.
	public sealed class FrameSettings
	{
		public static readonly int[] AllowedTargets = { 30, 60, 90, 120 };

		/// Frame rate the game asks the display and the engine for.
		public int TargetFps = 120;
		/// Drop to FallbackFps for the rest of the session when TargetFps cannot be held.
		public bool AutoFallback = true;
		public int FallbackFps = 60;
		/// Small FPS / frame-time readout in the top-left corner.
		public bool ShowFps;
		/// Ask Android for a matching display refresh rate (Surface.setFrameRate). Needed on Android 15+,
		/// which otherwise keeps games at 60 Hz.
		public bool RequestDisplayRate = true;
		/// Surface.setFrameRate change strategy: false = only if seamless (no blank flash), true = always.
		public bool AllowNonSeamlessSwitch;

		public static int Sanitize(int fps, int fallback)
		{
			foreach (int a in AllowedTargets) if (a == fps) return fps;
			return fallback;
		}

		/// Build settings from raw key/value strings; unknown or invalid values keep their defaults.
		public static FrameSettings FromValues(IReadOnlyDictionary<string, string> v)
		{
			var s = new FrameSettings();
			if (v == null) return s;
			if (v.TryGetValue("target_fps", out var t) && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ti)) s.TargetFps = Sanitize(ti, s.TargetFps);
			if (v.TryGetValue("fallback_fps", out var f) && int.TryParse(f, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fi)) s.FallbackFps = Sanitize(fi, s.FallbackFps);
			if (v.TryGetValue("auto_fallback", out var a)) s.AutoFallback = ParseBool(a, s.AutoFallback);
			if (v.TryGetValue("show_fps", out var sf)) s.ShowFps = ParseBool(sf, s.ShowFps);
			if (v.TryGetValue("request_display_rate", out var r)) s.RequestDisplayRate = ParseBool(r, s.RequestDisplayRate);
			if (v.TryGetValue("allow_non_seamless_switch", out var n)) s.AllowNonSeamlessSwitch = ParseBool(n, s.AllowNonSeamlessSwitch);
			if (s.FallbackFps >= s.TargetFps) s.AutoFallback = false; // nothing lower to fall back to
			return s;
		}

		public Dictionary<string, string> ToValues() => new Dictionary<string, string>
		{
			["target_fps"] = TargetFps.ToString(CultureInfo.InvariantCulture),
			["fallback_fps"] = FallbackFps.ToString(CultureInfo.InvariantCulture),
			["auto_fallback"] = AutoFallback ? "true" : "false",
			["show_fps"] = ShowFps ? "true" : "false",
			["request_display_rate"] = RequestDisplayRate ? "true" : "false",
			["allow_non_seamless_switch"] = AllowNonSeamlessSwitch ? "true" : "false",
		};

		private static bool ParseBool(string s, bool def)
		{
			switch ((s ?? "").Trim().ToLowerInvariant())
			{
				case "1": case "true": case "yes": case "on": return true;
				case "0": case "false": case "no": case "off": return false;
				default: return def;
			}
		}
	}

	/// What to tell the engine and the display for a given frame-rate target.
	public readonly struct PacingDecision
	{
		/// Value for Godot's Engine.MaxFps. 0 = no software limiter, VSync paces frames.
		public readonly int EngineMaxFps;
		/// Refresh rate to request from Android (0 = do not request).
		public readonly float RequestHz;
		public readonly bool VsyncPaced;

		public PacingDecision(int maxFps, float requestHz, bool vsyncPaced)
		{
			EngineMaxFps = maxFps; RequestHz = requestHz; VsyncPaced = vsyncPaced;
		}

		public override string ToString() => $"maxFps={EngineMaxFps} requestHz={RequestHz:0.#} vsyncPaced={VsyncPaced}";
	}

	public static class FramePacingPolicy
	{
		/// Refresh rates closer than this are treated as equal (displays report e.g. 119.88 or 120.0).
		public const double HzTolerance = 1.5;

		/// Decide how to pace `targetFps` on a display that currently runs at `displayHz`
		/// (<= 0 when unknown). VSync is assumed on.
		///
		/// When the display already runs at the target rate, the software limiter is switched off and
		/// VSync alone paces frames: a sleep-based limiter running at exactly the refresh rate drifts
		/// against VSync and causes periodic doubled frames (judder). Otherwise the limiter caps at the
		/// target, and the display is asked to switch to the target rate.
		public static PacingDecision Decide(int targetFps, double displayHz, bool requestDisplayRate)
		{
			if (targetFps <= 0) targetFps = 60;
			bool matches = displayHz > 0 && Math.Abs(displayHz - targetFps) <= HzTolerance;
			return new PacingDecision(matches ? 0 : targetFps, requestDisplayRate ? targetFps : 0f, matches);
		}

		/// Frame-rate independent replacement for `smooth = Lerp(smooth, dt, 0.1)` (which was tuned per
		/// frame at 60 FPS): identical at 60 FPS, the same smoothing time constant at any other rate.
		public static float SmoothDeltaTime(float previous, float dt)
		{
			if (dt <= 0f) return previous;
			double alpha = 1.0 - Math.Pow(0.9, dt * 60.0);
			if (alpha > 1.0) alpha = 1.0;
			return (float)(previous + (dt - previous) * alpha);
		}
	}

	/// Detects that the target frame rate cannot be held and recommends a one-way fallback.
	///
	/// A frame is "missed" when it took longer than 1.5 target frame intervals (i.e. at least one
	/// VSync was skipped). After a startup grace period, frames are grouped into windows; a window is
	/// "slow" when more than `MissRatio` of its frames were missed or its average rate is below
	/// `MinRateRatio` of the target. `SlowWindowsToFallback` consecutive slow windows trigger the
	/// fallback. Long frames (> HitchSeconds: level loads, app switches) are ignored, and Reset()
	/// restarts the grace period (call it after level loads). Device temperature is not read.
	public sealed class FallbackMonitor
	{
		public double StartupGraceSeconds = 10.0;
		public double WindowSeconds = 5.0;
		public double MissRatio = 0.20;
		public double MinRateRatio = 0.80;
		public int SlowWindowsToFallback = 2;
		public double HitchSeconds = 0.25;

		private int mTarget;
		private double mGrace, mWindowTime;
		private int mFrames, mMissed, mSlowWindows;
		public bool Triggered { get; private set; }
		public string LastWindow { get; private set; } = "";

		public FallbackMonitor(int targetFps) { Retarget(targetFps); }

		public void Retarget(int targetFps)
		{
			mTarget = Math.Max(1, targetFps);
			Triggered = false;
			Reset();
		}

		public void Reset()
		{
			mGrace = 0; mWindowTime = 0; mFrames = 0; mMissed = 0; mSlowWindows = 0;
		}

		/// Feed one frame's real (unscaled) duration. Returns true exactly once, when the fallback should happen.
		public bool Feed(double frameSeconds)
		{
			if (Triggered || frameSeconds <= 0 || frameSeconds > HitchSeconds) return false;
			if (mGrace < StartupGraceSeconds) { mGrace += frameSeconds; return false; }
			mWindowTime += frameSeconds;
			mFrames++;
			if (frameSeconds > 1.5 / mTarget) mMissed++;
			if (mWindowTime < WindowSeconds) return false;

			double rate = mFrames / mWindowTime;
			double missed = (double)mMissed / mFrames;
			bool slow = missed > MissRatio || rate < MinRateRatio * mTarget;
			LastWindow = string.Format(CultureInfo.InvariantCulture, "{0:0.0} fps, {1:0}% missed{2}", rate, missed * 100, slow ? " (slow)" : "");
			mSlowWindows = slow ? mSlowWindows + 1 : 0;
			mWindowTime = 0; mFrames = 0; mMissed = 0;
			if (mSlowWindows >= SlowWindowsToFallback) { Triggered = true; return true; }
			return false;
		}
	}

	/// Fires once when exactly `Fingers` touches are held for `HoldSeconds`; re-arms after release.
	/// Exact count, so a 5-finger gesture never also fires the 4-finger one.
	/// Used for in-game toggles that need no changes to the game's own menus.
	public sealed class HoldGesture
	{
		public readonly int Fingers;
		public readonly double HoldSeconds;
		private double mHeld;
		private bool mFired;

		public HoldGesture(int fingers, double holdSeconds) { Fingers = fingers; HoldSeconds = holdSeconds; }

		public bool Update(int touchCount, double dt)
		{
			if (touchCount != Fingers) { mHeld = 0; if (touchCount < Fingers) mFired = false; return false; }
			if (mFired) return false;
			mHeld += dt;
			if (mHeld < HoldSeconds) return false;
			mFired = true;
			return true;
		}
	}
}
