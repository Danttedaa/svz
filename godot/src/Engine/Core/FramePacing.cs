using System;
using System.Collections.Generic;
using System.Globalization;

namespace Svz
{
	// Inside the namespace on purpose: Godot types then win over same-named global types from the
	// decompiled game code (which is compiled into the same assembly).
	using Godot;

	/// High-refresh-rate support (SvZ Enhanced, milestone 1).
	///
	/// - Frame-rate target from user://svz_enhanced.cfg (default 120), VSync always on.
	/// - Android 15+ keeps games at 60 Hz unless they ask for more, and Godot 4.7 does not ask:
	///   this calls Surface.setFrameRate() on Godot's render surface through the AndroidRuntime bridge.
	/// - Explicit engine limit <=120 FPS even when VSync/display are at 120 Hz.
	/// - Optional one-way fallback (default 120 -> 60) when the target cannot be held (FallbackMonitor).
	/// - In-game toggles without touching the game's menus: hold 4 fingers for 1.5 s to switch
	///   120 <-> 60 FPS, hold 5 fingers for 1.5 s to show/hide the FPS readout.
	/// - Log lines start with "[svz-fps]" (adb logcat -s godot).
	public static class FramePacing
	{
		public const string ConfigPath = "user://svz_enhanced.cfg";
		private const string Section = "display";

		public static FrameSettings Settings { get; private set; } = new FrameSettings();
		public static int EffectiveFps => sFallenBack ? Settings.FallbackFps : Settings.TargetFps;
		public static double DisplayHz { get; private set; }
		public static PacingDecision Decision { get; private set; }
		/// Result of the last Android refresh-rate request ("" before the first one, or off Android).
		public static string LastBridgeStatus { get; private set; } = "";
		/// Last "stats ..." summary (5-second window), also written to the log.
		public static string LastStats => sStats.Last;
		public static bool FallenBack => sFallenBack;
		/// Called at the end of every Frame(delta, touchCount): lets other enhancements (binary-patch route)
		/// add gestures without their own call site in the game.
		public static event Action<double, int> AfterFrame;
		/// Show a short message with the game's toast (no-op before Init).
		public static void Notify(string text) => sNotify?.Invoke(text);

		private static Node sRoot;
		private static Action<string> sNotify;
		private static FallbackMonitor sMonitor;
		private static bool sFallenBack;
		private static string sFallbackReport = "";
		private static readonly HoldGesture sToggleFps = new HoldGesture(4, 1.5);
		private static readonly HoldGesture sToggleOverlay = new HoldGesture(5, 1.5);
		private static double sRecheckIn = -1, sPeriodic;
		private static int sGameRequested = int.MinValue;
		private static Label sOverlay;
		private static double sOverlayTimer;
		private static readonly FrameStats sStats = new FrameStats();

		public static void Init(Node root, Action<string> notify)
		{
			sRoot = root;
			sNotify = notify;
			Settings = Load();
			foreach (var a in OS.GetCmdlineUserArgs())
			{
				if (a.StartsWith("--fps=") && int.TryParse(a.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int f))
					Settings.TargetFps = FrameSettings.Sanitize(f, Settings.TargetFps);
				if (a == "--show-fps") Settings.ShowFps = true;
				if (a == "--no-fallback") Settings.AutoFallback = false;
			}
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
			sMonitor = new FallbackMonitor(Settings.TargetFps);
			Log($"settings: target={Settings.TargetFps} fallback={(Settings.FallbackEnabled ? Settings.FallbackFps.ToString(CultureInfo.InvariantCulture) : "off")} requestDisplayRate={Settings.RequestDisplayRate}");
			Apply("init");
			SetOverlay(Settings.ShowFps);
		}

		/// Unity's Application.targetFrameRate setter lands here. The player's setting wins; the
		/// request is only logged (SvZ 3.4.0 never sets it, but keep the original value visible).
		public static void GameRequested(int fps)
		{
			if (fps == sGameRequested) return;
			sGameRequested = fps;
			Log($"game asked for targetFrameRate={fps}; keeping {EffectiveFps}");
		}

		/// Call once per engine frame with Godot's real frame delta and the number of fingers down.
		public static void Frame(double delta, int touchCount)
		{
			if (sRoot == null) return;
			if (Settings.FallbackEnabled && !sFallenBack && sMonitor.Feed(delta))
			{
				// Capture the panel rate and measured load BEFORE switching to fallback:
				// after the switch, the overlay would otherwise report only stable 60Hz.
				sFallbackReport = string.Format(CultureInfo.InvariantCulture,
					"from {0}: {1}, panel {2:0}Hz", Settings.TargetFps,
					sMonitor.LastWindow, DisplayHz);
				sFallenBack = true;
				Log("fallback reason: " + sFallbackReport);
				Apply("fallback");
				sNotify?.Invoke($"{Settings.FallbackFps} FPS (target {Settings.TargetFps} not held)");
			}
			if (sToggleFps.Update(touchCount, delta))
			{
				SetTargetFps(Settings.TargetFps >= 120 ? 60 : 120, "toggle");
				sNotify?.Invoke($"Frame rate: {Settings.TargetFps} FPS");
			}
			if (sToggleOverlay.Update(touchCount, delta))
			{
				Settings.ShowFps = !Settings.ShowFps;
				Save();
				SetOverlay(Settings.ShowFps);
			}
			if (sRecheckIn > 0 && (sRecheckIn -= delta) <= 0) Recheck("after request");
			if ((sPeriodic += delta) >= 5.0) { sPeriodic = 0; Recheck(null); }
			if (Settings.LogStats && sStats.Add(delta, EffectiveFps, DisplayHz)) Log(sStats.Last);
			UpdateOverlay(delta);
			AfterFrame?.Invoke(delta, touchCount);
		}

		/// Change the saved frame-rate target (for menus and tools); clears a session fallback.
		public static void SetTargetFps(int fps, string why = "set")
		{
			if (sRoot == null) return;
			Settings.TargetFps = FrameSettings.Sanitize(fps, Settings.TargetFps);
			sFallenBack = false;
			sFallbackReport = "";
			sMonitor.Retarget(Settings.TargetFps);
			Save();
			Apply(why);
		}

		/// After loading a new level, try the selected target again if a previous
		/// crowded wave forced fallback. Never probe mid-wave, so a sustained
		/// overload does not cause repeated 120/60 Hz switching.
		public static void OnLevelLoaded()
		{
			if (!sFallenBack || sRoot == null)
			{
				sMonitor?.Reset(); // ignore loading hitches during an ordinary level change
				return;
			}
			// Reset() alone does not clear FallbackMonitor.Triggered; use Retarget()
			// so an overloaded new wave can safely fall back again.
			sMonitor?.Retarget(Settings.TargetFps);
			sFallenBack = false;
			sFallbackReport = "";
			Apply("new level retry");
			Log($"new level: restored {Settings.TargetFps} FPS target after previous fallback");
		}

		/// Main._Notification forwards every notification here (used by the binary patch, tools/binpatch).
		public static void OnNotification(int what)
		{
			if (what == (int)MainLoop.NotificationApplicationResumed) OnResume();
		}

		/// Android drops the surface's frame-rate vote when the surface is recreated (app switch).
		public static void OnResume()
		{
			if (sRoot == null) return;
			sMonitor.Reset();
			Apply("resume");
		}

		private static void Apply(string why)
		{
			DisplayHz = ReadDisplayHz();
			Decision = FramePacingPolicy.Decide(EffectiveFps, DisplayHz, Settings.RequestDisplayRate);
			Godot.Engine.MaxFps = Decision.EngineMaxFps;
			Log($"apply ({why}): target={EffectiveFps} display={DisplayHz:0.##}Hz -> {Decision}");
			if (Decision.RequestHz > 0 && OS.GetName() == "Android")
			{
				RequestAndroidFrameRate(Decision.RequestHz, Settings.AllowNonSeamlessSwitch);
				sRecheckIn = 1.5; // the switch is asynchronous
			}
		}

		/// Re-read the display rate and update pacing to match the panel while retaining the hard cap
		/// (the system may refuse the request, or lower the rate later for power or heat).
		private static void Recheck(string why)
		{
			double hz = ReadDisplayHz();
			if (why == null && Math.Abs(hz - DisplayHz) < 0.5) return;
			DisplayHz = hz;
			Decision = FramePacingPolicy.Decide(EffectiveFps, DisplayHz, Settings.RequestDisplayRate);
			Godot.Engine.MaxFps = Decision.EngineMaxFps;
			Log($"display {(why ?? "changed")}: {DisplayHz:0.##}Hz -> {Decision}");
		}

		private static double ReadDisplayHz()
		{
			try { return DisplayServer.ScreenGetRefreshRate(); }
			catch { return -1; }
		}

		// ---------------------------------------------------------------- Android

		/// Surface.setFrameRate(hz, FRAME_RATE_COMPATIBILITY_DEFAULT, strategy) on Godot's render surface,
		/// on the UI thread. Failures are logged, never fatal.
		private static void RequestAndroidFrameRate(float hz, bool allowNonSeamless)
		{
			try
			{
				if (!Godot.Engine.HasSingleton("AndroidRuntime")) { Log("AndroidRuntime singleton missing; cannot request refresh rate"); return; }
				var rt = Godot.Engine.GetSingleton("AndroidRuntime");
				var activity = rt.Call("getActivity").AsGodotObject();
				if (activity == null) { Log("no activity; cannot request refresh rate"); return; }
				int api = AndroidApiLevel();
				var runnable = rt.Call("createRunnableFromGodotCallable", Callable.From(() =>
				{
					LastBridgeStatus = SendFrameRate(activity, api, hz, allowNonSeamless);
					Log(LastBridgeStatus);
				}));
				activity.Call("runOnUiThread", runnable);
			}
			catch (Exception e)
			{
				Log("refresh-rate request failed: " + e.Message);
			}
		}

		// Android constants (android.view.Surface): FRAME_RATE_COMPATIBILITY_DEFAULT = 0,
		// CHANGE_FRAME_RATE_ONLY_IF_SEAMLESS = 0, CHANGE_FRAME_RATE_ALWAYS = 1.
		public const int FrameRateCompatibilityDefault = 0, ChangeOnlyIfSeamless = 0, ChangeAlways = 1;

		/// The Java call chain, separated from threading so it can be tested with stand-in objects.
		/// Path 1: activity.getGodot().getRenderView().getView() (Godot 4.7.2: GodotActivity / Godot /
		/// GodotRenderView, all public and un-minified in the official template).
		/// Path 2: search activity.getWindow().getDecorView() for a view with getHolder() (a SurfaceView).
		/// Then view.getHolder().getSurface().setFrameRate(...): (float, int, int) on API 31+, (float, int) on API 30.
		/// Every hop is checked with JavaObject.has_java_method, so a log line names the step that failed.
		/// Returns a one-line status for the log.
		public static string SendFrameRate(GodotObject activity, int api, float hz, bool allowNonSeamless)
		{
			if (api < 30) return $"Android API {api} < 30: Surface.setFrameRate unavailable";
			var missing = new List<string>();
			var view = Hop(Hop(Hop(activity, "getGodot", missing), "getRenderView", missing), "getView", missing);
			string path = "Godot.getRenderView";
			if (view == null)
			{
				view = FindSurfaceView(Hop(Hop(activity, "getWindow", missing), "getDecorView", missing), 0);
				path = "view-tree search";
			}
			if (view == null) return "render view not found (" + string.Join(", ", missing) + "); refresh-rate request skipped";
			var surface = Hop(Hop(view, "getHolder", missing), "getSurface", missing);
			if (surface == null || !HasJavaMethod(surface, "setFrameRate")) return "Surface.setFrameRate not reachable (" + string.Join(", ", missing) + ")";
			int strategy = allowNonSeamless ? ChangeAlways : ChangeOnlyIfSeamless;
			if (api >= 31) surface.Call("setFrameRate", hz, FrameRateCompatibilityDefault, strategy);
			else surface.Call("setFrameRate", hz, FrameRateCompatibilityDefault);
			return string.Format(CultureInfo.InvariantCulture, "Surface.setFrameRate({0:0.#}) sent via {1} (API {2}, {3})",
				hz, path, api, api >= 31 ? (allowNonSeamless ? "always" : "seamless only") : "2-arg");
		}

		private static bool HasJavaMethod(GodotObject obj, string method)
		{
			try { return obj != null && obj.HasMethod("has_java_method") && obj.Call("has_java_method", method).AsBool(); }
			catch { return false; }
		}

		private static GodotObject Hop(GodotObject obj, string method, List<string> missing, params Variant[] args)
		{
			if (obj == null) return null;
			if (!HasJavaMethod(obj, method)) { missing.Add("no " + method + "()"); return null; }
			var next = obj.Call(method, args).AsGodotObject();
			if (next == null) missing.Add(method + "() returned null");
			return next;
		}

		private static GodotObject FindSurfaceView(GodotObject view, int depth)
		{
			if (view == null || depth > 8) return null;
			if (HasJavaMethod(view, "getHolder")) return view;   // SurfaceView (Godot's GL/Vulkan render view)
			if (!HasJavaMethod(view, "getChildCount") || !HasJavaMethod(view, "getChildAt")) return null;
			int n = view.Call("getChildCount").AsInt32();
			for (int i = 0; i < n; i++)
			{
				var found = FindSurfaceView(view.Call("getChildAt", i).AsGodotObject(), depth + 1);
				if (found != null) return found;
			}
			return null;
		}

		/// OS.GetVersion() is Build.VERSION.RELEASE on Android ("17", "11", "8.1.0").
		private static int AndroidApiLevel()
		{
			string v = OS.GetVersion();
			int dot = v.IndexOf('.');
			if (!int.TryParse(dot >= 0 ? v.Substring(0, dot) : v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int major)) return 0;
			return AndroidApiForRelease(major);
		}

		/// Lowest API level of an Android major release (11 -> 30, 12 -> 31, ...); releases after 12L add one level each.
		public static int AndroidApiForRelease(int major)
		{
			if (major >= 13) return 33 + (major - 13);
			if (major == 12) return 31;
			if (major == 11) return 30;
			if (major == 10) return 29;
			return major > 0 ? 28 : 0;
		}

		// ---------------------------------------------------------------- settings + overlay

		private static FrameSettings Load()
		{
			var cf = new ConfigFile();
			if (cf.Load(ConfigPath) != Error.Ok)
			{
				var s = new FrameSettings();
				Save(s);   // write defaults so the file documents every option
				return s;
			}
			var values = new Dictionary<string, string>();
			if (cf.HasSection(Section))
				foreach (var k in cf.GetSectionKeys(Section))
					values[k] = cf.GetValue(Section, k).AsString();
			return FrameSettings.FromValues(values);
		}

		private static void Save() => Save(Settings);

		private static void Save(FrameSettings s)
		{
			var cf = new ConfigFile();
			cf.Load(ConfigPath);
			foreach (var kv in s.ToValues())
			{
				if (int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) cf.SetValue(Section, kv.Key, i);
				else cf.SetValue(Section, kv.Key, kv.Value == "true");
			}
			var err = cf.Save(ConfigPath);
			if (err != Error.Ok) Log($"could not save {ConfigPath}: {err}");
		}

		private static void SetOverlay(bool on)
		{
			if (!on) { sOverlay?.GetParent()?.QueueFree(); sOverlay = null; return; }
			if (sOverlay != null || sRoot == null) return;
			var layer = new CanvasLayer { Layer = 101, Name = "FpsOverlay" };
			sOverlay = new Label { Position = new Vector2(8, 8) };
			sOverlay.AddThemeFontSizeOverride("font_size", 22);
			sOverlay.AddThemeColorOverride("font_outline_color", Colors.Black);
			sOverlay.AddThemeConstantOverride("outline_size", 6);
			layer.AddChild(sOverlay);
			sRoot.AddChild(layer);
		}

		private static void UpdateOverlay(double delta)
		{
			if (sOverlay == null || (sOverlayTimer += delta) < 0.5) return;
			sOverlayTimer = 0;
			double fps = Godot.Engine.GetFramesPerSecond();
			sOverlay.Text = string.Format(CultureInfo.InvariantCulture, "{0:0} FPS  {1:0.0} ms  {2:0}Hz  target {3}{4}",
				fps, fps > 0 ? 1000.0 / fps : 0, DisplayHz, EffectiveFps, sFallenBack ? " (fallback)" : "");
			// Phone-first diagnosis: no adb or laptop needed to inspect the conditions
			// immediately before fallback. Shown only when the FPS overlay is enabled.
			if (sFallenBack && !string.IsNullOrEmpty(sFallbackReport))
				sOverlay.Text += "\nFallback " + sFallbackReport;
		}

		private static void Log(string msg) => GD.Print("[svz-fps] " + msg);
	}
}
