using System.Collections.Generic;
using Godot;
using Svz;

/// Runs FramePacing inside the real Godot 4.7 runtime (desktop, headless) and checks what it does to
/// the engine: Engine.MaxFps, the settings file, gestures, fallback and the real frame rate.
/// Android-only paths (Surface.setFrameRate) are not exercised here.
public partial class Test : Node
{
	private int mFailed, mPassed;
	private readonly List<string> mNotes = new List<string>();
	private int mPhase;
	private ulong mPhaseStartUsec;
	private int mFrames;

	private void Check(bool cond, string name)
	{
		if (cond) mPassed++;
		else { mFailed++; GD.PrintErr("FAIL: " + name); }
	}

	private static Variant Cfg(string key)
	{
		var cf = new ConfigFile();
		cf.Load(FramePacing.ConfigPath);
		return cf.GetValue("display", key);
	}

	private void Finish()
	{
		GD.Print($"[test] {mPassed} passed, {mFailed} failed");
		GetTree().Quit(mFailed == 0 ? 0 : 1);
	}

	private static string Phase()
	{
		foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--phase=")) return a.Substring(8);
		return "main";
	}

	private static void Hold(int fingers)
	{
		for (int i = 0; i < 100; i++) FramePacing.Frame(1.0 / 60, fingers);
		FramePacing.Frame(1.0 / 60, 0);
	}

	public override void _Ready()
	{
		switch (Phase())
		{
			case "main": MainChecks(); return;
			case "restart-1": Restart1(); break;
			case "restart-2": Restart2(); break;
			case "restart-3": Restart3(); break;
			case "bridge": Bridge(); break;
			default: Check(false, "unknown phase " + Phase()); break;
		}
		Finish();
	}

	// Restart regression: each phase is a separate Godot process, so nothing survives in static state.
	// 1: fresh install -> player picks 60 FPS and the FPS readout.
	private void Restart1()
	{
		DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FramePacing.ConfigPath));
		FramePacing.Init(this, msg => mNotes.Add(msg));
		Check(FramePacing.Settings.TargetFps == 120, "restart-1: fresh install defaults to 120");
		Hold(4);
		Hold(5);
		Check(FramePacing.Settings.TargetFps == 60 && FramePacing.Settings.ShowFps, "restart-1: 60 FPS + readout chosen");
	}

	// 2: after restart the choices are back; then back to 120, and a session fallback happens.
	private void Restart2()
	{
		FramePacing.Init(this, msg => mNotes.Add(msg));
		Check(FramePacing.Settings.TargetFps == 60, "restart-2: 60 FPS reloaded after restart");
		Check(FramePacing.EffectiveFps == 60 && Engine.MaxFps == 60, $"restart-2: engine capped at 60 after restart (MaxFps={Engine.MaxFps})");
		Check(FramePacing.Settings.ShowFps && GetNodeOrNull("FpsOverlay") != null, "restart-2: FPS readout reloaded and shown");
		Hold(4);
		Check(FramePacing.Settings.TargetFps == 120, "restart-2: switched back to 120");
		for (int i = 0; i < 60 * 25; i++) FramePacing.Frame(1.0 / 60, 0);
		Check(FramePacing.EffectiveFps == 60, "restart-2: session fallback active");
	}

	// 3: the 120 choice persisted, the session-only fallback did not.
	private void Restart3()
	{
		FramePacing.Init(this, msg => mNotes.Add(msg));
		Check(FramePacing.Settings.TargetFps == 120 && FramePacing.EffectiveFps == 120, "restart-3: 120 reloaded, fallback not persisted");
		Check(Engine.MaxFps == 120 || (FramePacing.Decision.VsyncPaced && Engine.MaxFps == 0), $"restart-3: engine follows reloaded target (MaxFps={Engine.MaxFps})");
		Check(FramePacing.Settings.ShowFps, "restart-3: readout setting still on");
		Check(Cfg("auto_fallback").AsBool() && Cfg("fallback_fps").AsInt32() == 60, "restart-3: other settings intact");
	}

	// Android refresh-rate bridge: FramePacing.SendFrameRate against stand-in Java objects.
	private void Bridge()
	{
		var a = FakeActivity.Standard(out var sv);
		string st = FramePacing.SendFrameRate(a, 37, 120f, false);
		GD.Print("[test] " + st);
		var c = sv.Holder.Surface.Calls;
		Check(c.Count == 1 && c[0] == (120f, 0, 0), "API 37: setFrameRate(120, DEFAULT, ONLY_IF_SEAMLESS) via Godot.getRenderView");
		Check(st.Contains("via Godot.getRenderView"), "status names path 1");

		a = FakeActivity.Standard(out sv);
		FramePacing.SendFrameRate(a, 37, 60f, true);
		Check(sv.Holder.Surface.Calls.Count == 1 && sv.Holder.Surface.Calls[0] == (60f, 0, 1), "allow_non_seamless_switch -> CHANGE_FRAME_RATE_ALWAYS (1)");

		a = FakeActivity.Standard(out sv);
		sv.Holder.SurfaceApi30 = new FakeSurfaceApi30();
		FramePacing.SendFrameRate(a, 30, 120f, false);
		Check(sv.Holder.SurfaceApi30.Calls.Count == 1 && sv.Holder.SurfaceApi30.Calls[0] == (120f, 0), "API 30: 2-argument overload");

		a = FakeActivity.Standard(out sv);
		a.Hidden.Add("getGodot");
		st = FramePacing.SendFrameRate(a, 37, 120f, false);
		GD.Print("[test] " + st);
		Check(sv.Holder.Surface.Calls.Count == 1 && st.Contains("view-tree search"), "falls back to the view-tree search when getGodot() is missing");

		a = FakeActivity.Standard(out sv);
		a.Godot.RenderView.View = null;   // render view not attached yet
		st = FramePacing.SendFrameRate(a, 37, 120f, false);
		Check(sv.Holder.Surface.Calls.Count == 1 && st.Contains("view-tree search"), "falls back when getView() returns null");

		a = FakeActivity.Standard(out sv);
		a.Hidden.Add("getGodot");
		a.Hidden.Add("getWindow");
		st = FramePacing.SendFrameRate(a, 37, 120f, false);
		GD.Print("[test] " + st);
		Check(sv.Holder.Surface.Calls.Count == 0 && st.Contains("no getGodot()") && st.Contains("no getWindow()"), "nothing reachable: no call, log names the missing hops");

		a = FakeActivity.Standard(out sv);
		sv.Holder.Surface.Hidden.Add("setFrameRate");
		st = FramePacing.SendFrameRate(a, 37, 120f, false);
		Check(sv.Holder.Surface.Calls.Count == 0 && st.Contains("not reachable"), "missing setFrameRate is reported, not called");

		a = FakeActivity.Standard(out sv);
		st = FramePacing.SendFrameRate(a, 29, 120f, false);
		Check(sv.Holder.Surface.Calls.Count == 0 && st.Contains("< 30"), "API 29: no call");
	}

	private void MainChecks()
	{
		DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FramePacing.ConfigPath));
		FramePacing.Init(this, msg => mNotes.Add(msg));

		Check(FramePacing.Settings.TargetFps == 120, "default target 120");
		Check(Engine.MaxFps == 120 || (FramePacing.Decision.VsyncPaced && Engine.MaxFps == 0), $"engine cap applied (MaxFps={Engine.MaxFps}, display={FramePacing.DisplayHz})");
		Check(FileAccess.FileExists(FramePacing.ConfigPath), "defaults written to settings file");
		Check(Cfg("target_fps").AsInt32() == 120 && Cfg("auto_fallback").AsBool(), "settings file content");

		// 4-finger hold for 1.5 s -> 60 FPS, saved
		for (int i = 0; i < 100; i++) FramePacing.Frame(1.0 / 60, 4);
		FramePacing.Frame(1.0 / 60, 0);
		Check(FramePacing.Settings.TargetFps == 60 && FramePacing.EffectiveFps == 60, "4-finger hold switches to 60");
		Check(Engine.MaxFps == 60, $"engine capped at 60 (MaxFps={Engine.MaxFps})");
		Check(Cfg("target_fps").AsInt32() == 60, "60 saved");
		Check(mNotes.Contains("Frame rate: 60 FPS"), "player notified of switch");

		// 5-finger hold -> FPS overlay
		for (int i = 0; i < 100; i++) FramePacing.Frame(1.0 / 60, 5);
		FramePacing.Frame(1.0 / 60, 0);
		Check(FramePacing.Settings.ShowFps && GetNodeOrNull("FpsOverlay") != null, "5-finger hold shows FPS overlay");

		// back to 120, then simulate a device that only reaches 60 -> fallback to 60 for the session
		for (int i = 0; i < 100; i++) FramePacing.Frame(1.0 / 60, 4);
		FramePacing.Frame(1.0 / 60, 0);
		Check(FramePacing.Settings.TargetFps == 120, "toggle back to 120");
		for (int i = 0; i < 60 * 25; i++) FramePacing.Frame(1.0 / 60, 0);
		Check(FramePacing.EffectiveFps == 60 && FramePacing.Settings.TargetFps == 120, "fallback lowers effective rate only");
		Check(Engine.MaxFps == 60, "fallback caps engine at 60");
		Check(Cfg("target_fps").AsInt32() == 120, "fallback is not saved (session only)");
		Check(mNotes.Exists(n => n.Contains("could not hold 120")), "player notified of fallback");

		// game's own targetFrameRate request must not override the player
		FramePacing.GameRequested(30);
		Check(FramePacing.EffectiveFps == 60 && Engine.MaxFps == 60, "game request ignored");

		Check(FramePacing.AndroidApiForRelease(17) == 37 && FramePacing.AndroidApiForRelease(11) == 30, "Android API mapping");

		// real-time check: does Engine.MaxFps actually limit this runtime? measure 30 FPS for 2 s
		Engine.MaxFps = 30;
		mPhase = 1;
		mPhaseStartUsec = Time.GetTicksUsec();
	}

	public override void _Process(double delta)
	{
		if (mPhase != 1) return;
		mFrames++;
		double secs = (Time.GetTicksUsec() - mPhaseStartUsec) / 1e6;
		if (secs < 2.0) return;
		double fps = mFrames / secs;
		GD.Print($"[test] measured {fps:0.0} FPS with Engine.MaxFps=30");
		Check(fps > 24 && fps < 33, $"Engine.MaxFps limits real frame rate (measured {fps:0.0})");
		mPhase = 2;
		Finish();
	}
}
