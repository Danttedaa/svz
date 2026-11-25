using System.Collections.Generic;
using Godot;
using Svz;

/// Runs FramePacing inside the real Godot 4.7 runtime (desktop, headless) and checks what it does to
/// the engine: Engine.MaxFps, persistent settings, fallback and the real frame rate.
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
	// 1: fresh install -> external configuration changes 60 FPS and the readout.
	private void Restart1()
	{
		DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FramePacing.ConfigPath));
		FramePacing.Init(this, msg => mNotes.Add(msg));
		Check(FramePacing.Settings.TargetFps == 120, "restart-1: fresh install defaults to 120");
		FramePacing.SetTargetFps(60, "test API");
		var cfg = new ConfigFile();
		cfg.Load(FramePacing.ConfigPath);
		cfg.SetValue("display", "show_fps", true);
		cfg.Save(FramePacing.ConfigPath);
		Check(FramePacing.Settings.TargetFps == 60 && Cfg("show_fps").AsBool(),
			"restart-1: saved 60 FPS and readout through explicit settings");
	}

	// 2: after restart the choices are back; then back to 120, and a session fallback happens.
	private void Restart2()
	{
		FramePacing.Init(this, msg => mNotes.Add(msg));
		Check(FramePacing.Settings.TargetFps == 60, "restart-2: 60 FPS reloaded after restart");
		Check(FramePacing.EffectiveFps == 60 && Engine.MaxFps == 60, $"restart-2: engine capped at 60 after restart (MaxFps={Engine.MaxFps})");
		Check(FramePacing.Settings.ShowFps && GetNodeOrNull("FpsOverlay") != null, "restart-2: FPS readout reloaded and shown");
		FramePacing.SetTargetFps(120, "test API");
		Check(FramePacing.Settings.TargetFps == 120, "restart-2: API restores 120");
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

		// Legacy 4- and 5-finger holds are intentionally disabled. Neither
		// must accidentally alter FPS, persistence, or on-screen diagnostics.
		for (int i = 0; i < 100; i++) FramePacing.Frame(1.0 / 120, 4);
		FramePacing.Frame(1.0 / 120, 0);
		Check(FramePacing.Settings.TargetFps == 120 && FramePacing.EffectiveFps == 120,
			"4-finger hold cannot change the 120 FPS target");
		Check(Engine.MaxFps == 120, "120 FPS ceiling survives legacy gesture");
		Check(Cfg("target_fps").AsInt32() == 120, "legacy hold does not change config");
		FramePacing.SetTargetFps(60, "test API");
		Check(FramePacing.EffectiveFps == 60 && Engine.MaxFps == 60,
			"explicit API still supports testing 60 FPS");
		FramePacing.OnLevelLoaded();
		Check(FramePacing.EffectiveFps == 60,
			"new level does not override deliberately selected 60 FPS");
		for (int i = 0; i < 100; i++) FramePacing.Frame(1.0 / 60, 5);
		FramePacing.Frame(1.0 / 60, 0);
		Check(!FramePacing.Settings.ShowFps && GetNodeOrNull("FpsOverlay") == null,
			"5-finger hold cannot change FPS overlay settings");
		FramePacing.SetTargetFps(120, "test API");
		Check(FramePacing.Settings.TargetFps == 120, "explicit API restores 120");

		// Simulate a device only reaching 60 -> automatic session-only fallback.
		for (int i = 0; i < 60 * 25; i++) FramePacing.Frame(1.0 / 60, 0);
		Check(FramePacing.EffectiveFps == 60 && FramePacing.Settings.TargetFps == 120, "fallback lowers effective rate only");
		Check(Engine.MaxFps == 60, "fallback caps engine at 60");
		Check(Cfg("target_fps").AsInt32() == 120, "fallback is not saved (session only)");
		Check(mNotes.Exists(n => n.Contains("target 120 not held")), "player notified of fallback");

		// game's own targetFrameRate request must not override the player
		FramePacing.GameRequested(30);
		Check(FramePacing.EffectiveFps == 60 && Engine.MaxFps == 60, "game request ignored");

		// New level restarts the chosen 120 FPS target, without reloading config or restarting the app.
		FramePacing.OnLevelLoaded();
		Check(!FramePacing.FallenBack && FramePacing.EffectiveFps == 120,
			"new level retries original 120 FPS target");
		Check(Engine.MaxFps == 120, "new level restores explicit 120 FPS cap");
		Check(Cfg("target_fps").AsInt32() == 120,
			"new level retry does not rewrite persisted player setting");
		// Still-overloaded hardware can fall back again after the normal slow windows.
		for (int i = 0; i < 60 * 25; i++) FramePacing.Frame(1.0 / 60, 0);
		Check(FramePacing.FallenBack && FramePacing.EffectiveFps == 60,
			"new level retry does not prevent fallback when load remains high");

		Check(FramePacing.AndroidApiForRelease(17) == 37 && FramePacing.AndroidApiForRelease(11) == 30, "Android API mapping");

		Check(FramePacing.ParseAndroidSdkVersion("34") == 34,
			"Godot SDK 34 is not remapped to API 54");
		Check(FramePacing.ParseAndroidSdkVersion("37.123456") == 37 &&
			FramePacing.ParseAndroidSdkVersion(" 30 build ") == 30,
			"SDK parser accepts incremental-build suffixes");
		Check(FramePacing.ParseAndroidSdkVersion("30") == 30 &&
			FramePacing.ParseAndroidSdkVersion("31") == 31,
			"Surface overload boundary preserves API 30 and 31");
		foreach (var version in new string[] { null, "", "LineageOS 21", "-1", "0", "34preview", "99999999999999" })
			Check(FramePacing.ParseAndroidSdkVersion(version) == 0,
				"unknown SDK version is conservative: " + (version ?? "<null>"));

		// Font coverage must ignore black/colored atlas RGB, retain material tint,
		// and preserve every alpha value (including antialiasing and invisible text).
		var fontPixels = new byte[256 * 4];
		for (int a = 0; a < 256; a++) {
			fontPixels[a * 4] = (byte)a;
			fontPixels[a * 4 + 1] = (byte)(255 - a);
			fontPixels[a * 4 + 2] = 17;
			fontPixels[a * 4 + 3] = (byte)a;
		}
		var originalPixels = (byte[])fontPixels.Clone();
		var coverage = FontTextureCoverage.AlphaOnly(fontPixels, 256);
		bool allCoverage = coverage.Length == 512, unchanged = true;
		for (int a = 0; a < 256; a++)
			allCoverage &= coverage[a * 2] == 255 && coverage[a * 2 + 1] == a;
		for (int i = 0; i < fontPixels.Length; i++) unchanged &= fontPixels[i] == originalPixels[i];
		Check(allCoverage, "font shader discards atlas RGB and preserves all 256 alpha levels");
		Check(unchanged, "font shader does not mutate the original atlas");
		var blackAtlas = FontTextureCoverage.AlphaOnly(new byte[] { 0, 0, 0, 255 }, 1);
		Check(blackAtlas[0] == 255 && blackAtlas[1] == 255,
			"black RGB atlas can render white foreground and colored labels");
		Check(blackAtlas[0] * 0 == 0, "black shadow material remains black");
		bool badCoverage = false;
		try { FontTextureCoverage.AlphaOnly(new byte[3], 1); }
		catch (System.ArgumentException) { badCoverage = true; }
		Check(badCoverage, "invalid coverage dimensions fail before texture allocation");

		var meshNode = new MeshInstance3D();
		AddChild(meshNode);
		long submittedBefore = RenderQuality.Submitted, skippedBefore = RenderQuality.Skipped;
		for (int f = 0; f < 1000; f++)
		{
			RenderQuality.Visible(meshNode, true);
			RenderQuality.Layers(meshNode, 3);
			RenderQuality.Shadow(meshNode, GeometryInstance3D.ShadowCastingSetting.Off);
			RenderQuality.Transform(meshNode, Transform3D.Identity);
		}
		Check(RenderQuality.Submitted - submittedBefore == 4 && RenderQuality.Skipped - skippedBefore == 3996,
			"1000 unchanged render frames submit four writes, skip 3996");
		Check(meshNode.Visible && meshNode.Layers == 3 && meshNode.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off,
			"initial render state actually reaches Godot");
		RenderQuality.Visible(meshNode, false); RenderQuality.Layers(meshNode, 7);
		var moved = Transform3D.Identity; moved.Origin = new Vector3(0.000001f, 2, 3);
		RenderQuality.Transform(meshNode, moved);
		Check(!meshNode.Visible && meshNode.Layers == 7 && meshNode.GlobalTransform == moved,
			"visibility, layer and subpixel transform changes reach Godot");
		var secondNode = new MeshInstance3D(); AddChild(secondNode);
		RenderQuality.Visible(secondNode, true); RenderQuality.Layers(secondNode, 3);
		Check(secondNode.Visible && secondNode.Layers == 3, "render state never crosses node instances");
		Check(RenderQuality.Antialiasing(false, 120) == Viewport.Msaa.Msaa2X &&
			RenderQuality.Antialiasing(false, 60) == Viewport.Msaa.Disabled &&
			RenderQuality.Antialiasing(true, 120) == Viewport.Msaa.Disabled,
			"2x AA releases cost on fallback and respects original graphics");
		FramePacing.SetTargetFps(120, "AA test");
		RenderQuality.ResetBudget();
		var skeleton = new Skeleton3D(); AddChild(skeleton); skeleton.AddBone("root"); skeleton.AddBone("child");
        long beforeBone = RenderQuality.Submitted;
        for (int f = 0; f < 1000; f++) RenderQuality.BoneScale(skeleton, 0, Vector3.One);
        Check(RenderQuality.Submitted - beforeBone == 1 && skeleton.GetBonePoseScale(0) == Vector3.One,
            "unchanged animation bone scales submit once, not every frame");
        RenderQuality.BoneScale(skeleton, 0, new Vector3(2, 3, 4));
        RenderQuality.BoneScale(skeleton, 1, Vector3.One);
        Check(skeleton.GetBonePoseScale(0) == new Vector3(2, 3, 4) && skeleton.GetBonePoseScale(1) == Vector3.One,
            "animated bone scale changes and different bones remain independent");
        var poseSkeleton = new Skeleton3D(); AddChild(poseSkeleton); poseSkeleton.AddBone("pose");
        var poseCases = new[] {
            Transform3D.Identity,
            new Transform3D(Basis.FromEuler(new Vector3(0.3f, -0.9f, 1.2f)).Scaled(new Vector3(2, 3, 4)), new Vector3(2, 4, -8)),
            new Transform3D(Basis.FromEuler(new Vector3(1.2f, 0.4f, -0.7f)).Scaled(new Vector3(-2, 3, 4)), Vector3.One),
            new Transform3D(new Basis(new Vector3(1, 0.1f, 0.2f), new Vector3(0, 2, 0.1f), new Vector3(0.1f, 0, 3)), Vector3.Zero)
        };
        foreach (var input in poseCases)
        {
            var pose = input;
            skeleton.SetBonePosePosition(0, pose.Origin);
            skeleton.SetBonePoseScale(0, pose.Basis.Scale);
            skeleton.SetBonePoseRotation(0, pose.Basis.Orthonormalized().GetRotationQuaternion());
            RenderQuality.BonePose(poseSkeleton, 0, ref pose);
            Check(poseSkeleton.GetBonePose(0).IsEqualApprox(skeleton.GetBonePose(0)),
                "single-call pose matches original decomposition including nonuniform scale, reflection and shear");
        }
        long beforePose = RenderQuality.Submitted;
        var samePose = poseCases[3];
        for (int f = 0; f < 1000; f++) RenderQuality.BonePose(poseSkeleton, 0, ref samePose);
        Check(RenderQuality.Submitted == beforePose, "unchanged bone poses skip all native submissions");
        samePose.Origin += new Vector3(0.00001f, 0, 0);
        RenderQuality.BonePose(poseSkeleton, 0, ref samePose);
        Check(poseSkeleton.GetBonePosePosition(0) == samePose.Origin, "subpixel animated bone movement survives pose caching");
        // Comparative interop benchmark; report measurements, never use a flaky
        // wall-clock threshold as a correctness test. Both loops animate poses.
        var samples = new Transform3D[256];
        for (int i = 0; i < samples.Length; i++) samples[i] = new Transform3D(
            Basis.FromEuler(new Vector3(i * 0.01f, i * 0.02f, -i * 0.01f)), new Vector3(i * 0.1f, 1, 2));
        var oldTimes = new List<double>(); var newTimes = new List<double>();
        void OldBone(int i)
        {
            var pose = samples[i & 255];
            skeleton.SetBonePosePosition(0, pose.Origin);
            RenderQuality.BoneScale(skeleton, 0, pose.Basis.Scale);
            skeleton.SetBonePoseRotation(0, pose.Basis.Orthonormalized().GetRotationQuaternion());
        }
        void NewBone(int i) { var pose = samples[i & 255]; RenderQuality.BonePose(poseSkeleton, 0, ref pose); }
        for (int i = 0; i < 2000; i++) { OldBone(i); NewBone(i); }
        for (int run = 0; run < 5; run++)
        {
            ulong started = Time.GetTicksUsec();
            for (int i = 0; i < 20000; i++) OldBone(i);
            oldTimes.Add((Time.GetTicksUsec() - started) / 1000.0);
            started = Time.GetTicksUsec();
            for (int i = 0; i < 20000; i++) NewBone(i);
            newTimes.Add((Time.GetTicksUsec() - started) / 1000.0);
        }
        oldTimes.Sort(); newTimes.Sort();
        GD.Print(System.FormattableString.Invariant($"[benchmark] 20000 animated bones median: old={oldTimes[2]:0.00}ms new={newTimes[2]:0.00}ms"));
        poseSkeleton.QueueFree();
        skeleton.QueueFree();
        var viewport = new SubViewport(); AddChild(viewport);
		RenderQuality.Size(viewport, new Vector2I(320, 240));
		var submittedSize = RenderQuality.Submitted;
		RenderQuality.Size(viewport, new Vector2I(320, 240));
		Check(viewport.Size == new Vector2I(320, 240) && RenderQuality.Submitted == submittedSize,
			"unchanged viewport size and AA avoid native writes");
		RenderQuality.Size(viewport, new Vector2I(640, 360));
		Check(viewport.Size == new Vector2I(640, 360), "viewport still resizes on orientation or resolution change");
		Check(RenderQuality.ReduceCostBeforeFallback(), "AA cost is removed before sacrificing selected FPS");
		RenderQuality.Size(viewport, new Vector2I(640, 360));
		Check(viewport.Msaa3D == Viewport.Msaa.Disabled && FramePacing.EffectiveFps == 120 &&
			!RenderQuality.ReduceCostBeforeFallback(), "AA downshift retains 120 and happens only once per level");
		RenderQuality.ResetBudget();
		RenderQuality.Size(viewport, new Vector2I(640, 360));
		Check(viewport.Msaa3D == Viewport.Msaa.Msaa2X, "new level can restore edge smoothing");
		meshNode.QueueFree(); secondNode.QueueFree(); viewport.QueueFree();

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
