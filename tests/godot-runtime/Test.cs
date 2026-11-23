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

	public override void _Ready()
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
		GD.Print($"[test] {mPassed} passed, {mFailed} failed");
		GetTree().Quit(mFailed == 0 ? 0 : 1);
	}
}
