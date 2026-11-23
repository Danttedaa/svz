using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UnityEngine;
using Time = UnityEngine.Time;
using Input = UnityEngine.Input;
using Mathf = UnityEngine.Mathf;
using Key = Godot.Key;

namespace Svz
{
	/// Godot entry point: drives the Unity 3.4 frame loop.
	public partial class Main : Node
	{
		public static Main Instance;
		public static bool Verbose = OS.GetCmdlineUserArgs().Contains("--verbose");
		public static int StartLevel = 0;

		public Node3D World3D;
		public CanvasLayer Gui;
		private double mPrefsTimer;

		public override void _Ready()
		{
			Instance = this;
			// Unity/Mono on phones ran with an invariant-like culture; the game parses floats with "."
			System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
			System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
			foreach (var a in OS.GetCmdlineUserArgs())
			{
				if (a.StartsWith("--level=")) StartLevel = int.Parse(a.Substring(8));
				// --shots=2,5,9 : save user://shot_<sec>.png at those seconds, quit after the last one
				if (a.StartsWith("--shots=")) mShots = new Queue<double>(a.Substring(8).Split(',').Select(double.Parse));
				if (a.StartsWith("--dump=")) mDumps = new Queue<double>(a.Substring(7).Split(',').Select(double.Parse));
				if (a.StartsWith("--killall=")) mKillAt = double.Parse(a.Substring(10));
				// --tap=sec:x:y[:holdSec];...
				if (a.StartsWith("--tap=")) mTaps = new Queue<(double, float, float, double)>(a.Substring(6).Split(';').Select(t => { var p = t.Split(':'); return (double.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]), p.Length > 3 ? double.Parse(p[3]) : 0.1); }));
			}
			World3D = new Node3D { Name = "World" };
			AddChild(World3D);
			Gui = new CanvasLayer { Name = "Gui", Layer = 10 };
			AddChild(Gui);
			Display.Init(this);
			FramePacing.Init(this, Toast.Show);   // SvZ Enhanced: 120 Hz target, 60 FPS fallback
			AssetDb.Init();
			Builtins.Init();
			GD.Print($"[svz] data: {AssetDb.Files.Length} files, levels: {Application.levelCount}");
			SceneManager.Request(StartLevel, false);
			Toast.Show("MADE BY MESFLIT");
		}

		private Queue<double> mShots;
		private Queue<(double t, float x, float y, double hold)> mTaps;
		private double mClock;
		private double mKillAt = -1;
		private Queue<double> mDumps;
		// PC debug flags: read the command line once, not twice per frame
		private static readonly bool sCorpses = OS.GetCmdlineUserArgs().Contains("--corpses"), sTraceCam = OS.GetCmdlineUserArgs().Contains("--trace-cam");

		/// debug: kill every enemy (exercises death animations / melting)
		private static void KillAll()
		{
			var mgr = WeakGlobalInstance<CharactersManager>.instance;
			if (mgr == null) return;
			int n = 0;
			foreach (var c in mgr.allEnemies.ToArray()) { c.health = 0; n++; }
			GD.Print($"[svz] killed {n} enemies");
		}

		private static void CountCorpses()
		{
			int dying = 0, alive = 0;
			foreach (var mb in Runtime.Behaviours)
				if (mb is CharacterModelController cmc && !mb.__destroyed && mb.gameObject.active)
				{
					var f = typeof(CharacterModelController).GetField("startedDieAnim", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
					var p = typeof(CharacterModelController).GetProperty("startedDieAnim");
					bool d = (bool)(p?.GetValue(cmc) ?? f?.GetValue(cmc) ?? false);
					if (d) dying++; else alive++;
				}
			GD.Print($"[corpses] t={Time.time:0} dying={dying} alive={alive}");
		}

		private static void Dump()
		{
			foreach (var c in Svz.Render.Cameras)
				GD.Print($"[dump] cam {c.gameObject.name} active={c.gameObject.active} en={c.enabled} pos={c.transform.position} rot={c.transform.eulerAngles} fov={c.fieldOfView} ortho={c.orthographic}/{c.orthographicSize} depth={c.depth} cull={c.cullingMask:X} clear={c.clearFlags} near={c.nearClipPlane} far={c.farClipPlane}");
			int n = 0;
			foreach (var r in Svz.Render.Renderers)
			{
				if (!r.__visible) continue;
				if (n++ > 400) break;
				var b = r.bounds;
				if (r is ParticleRenderer || r.__mats.Any(m => m?.shader?.__shaderName?.Contains("Billboard") == true || m?.shader?.__shaderName == "AlphaBlend"))
					GD.Print($"[dump]   {r.GetType().Name} {r.gameObject.name} mats={string.Join(";", r.__mats.Select(m => m == null ? "null" : m.name + "/" + m.shader?.__shaderName + "/tex=" + (m.GetTexture("_MainTex")?.name)))} emitter={(r.GetComponent<ParticleEmitter>() is ParticleEmitter pe ? $"n={pe.particleCount} size={pe.minSize}-{pe.maxSize} world={pe.useWorldSpace}" : "-")} maxSize={(r as ParticleRenderer)?.maxParticleSize}");
				continue;
				GD.Print($"[dump] r {r.gameObject.name} layer={r.gameObject.layer} pos={r.transform.position} scale={r.transform.lossyScale} bounds={b.center}/{b.size} mesh={(r.__Mesh?.name)} parent={(r.transform.parent?.name)}");
			}
		}

		public override void _Input(InputEvent e)
		{
			if (Movie.Playing) { Movie.OnInput(e); return; }
			InputState.Feed(e);
		}

		/// scripted screenshots / taps for automated testing on the PC
		private void DebugAutomation(double delta)
		{
			mClock += delta;
			if (mTaps != null && mTaps.Count > 0 && mClock >= mTaps.Peek().t)
			{
				var (_, x, y, hold) = mTaps.Dequeue();
				var pos = new Godot.Vector2(x * Display.Width, y * Display.Height);
				if (Movie.Playing) Movie.Stop();
				InputState.Feed(new InputEventScreenTouch { Index = 0, Pressed = true, Position = pos });
				GetTree().CreateTimer(hold).Timeout += () => InputState.Feed(new InputEventScreenTouch { Index = 0, Pressed = false, Position = pos });
				GD.Print($"[svz] tap {x},{y}");
			}
			if (mDumps != null && mDumps.Count > 0 && mClock >= mDumps.Peek()) { GD.Print($"[dump] ---- t={mDumps.Dequeue()}"); Dump(); }
			if (mKillAt >= 0 && mClock >= mKillAt) { mKillAt = -1; KillAll(); }
			if (sCorpses && Time.frameCount % 120 == 0) CountCorpses();
			if (mKillAt == -1 && sTraceCam && Camera.main != null && Time.frameCount % 10 == 0)
			{
				var ct = Camera.main.transform;
				GD.Print($"[cam] t={mClock:0.00} pos={ct.position.x:0.000},{ct.position.y:0.000},{ct.position.z:0.000} euler={ct.eulerAngles.x:0.0000},{ct.eulerAngles.y:0.0000},{ct.eulerAngles.z:0.0000} parent={ct.parent?.name}");
			}
			if (mShots != null && mShots.Count > 0 && mClock >= mShots.Peek())
			{
				double t = mShots.Dequeue();
				RenderingServer.ForceDraw();
				Screenshot($"shot_{t:0.#}.png");
				GD.Print($"[svz] screenshot {t}");
				if (mShots.Count == 0) GetTree().Quit();
			}
		}

		public override void _Notification(int what)
		{
			if (what == NotificationWMCloseRequest || what == NotificationApplicationPaused)
			{
				Runtime.Broadcast("OnApplicationPause", true);
				Prefs.Flush();
			}
			else if (what == NotificationApplicationResumed)
			{
				Runtime.Broadcast("OnApplicationPause", false);
				FramePacing.OnResume();
			}
			if (what == NotificationWMCloseRequest)
				Runtime.Broadcast("OnApplicationQuit", null);
		}

		public override void _Process(double delta)
		{
			FramePacing.Frame(delta, InputState.FingersDown);
			if (Movie.Playing) { Movie.Update(); DebugAutomation(delta); return; }
			float dt = Mathf.Min((float)delta, Time.__maxDt) * Time.__scale;
			Time.__dt = dt;
			Time.__unscaled += (float)delta;
			Time.__time += dt;
			Time.__smoothDt = FramePacingPolicy.SmoothDeltaTime(Time.__smoothDt, dt);   // was Lerp(.., 0.1f) per frame: 2x faster at 120 FPS
			Time.__frame++;

			InputState.BeginFrame();
			SceneManager.ProcessPending();
			Runtime.RunStarts();

			while (Time.__fixedTime + Time.__fixedDt <= Time.__time)
			{
				Time.__fixedTime += Time.__fixedDt;
				Clock.InFixed = true;
				Runtime.RunFixedUpdate();
				Physics.__Step(Time.__fixedDt);
				CoroutineRunner.Run(1);
				Clock.InFixed = false;
			}

			Runtime.RunUpdate();
			Invoker.Run();
			CoroutineRunner.Run(0);
			Runtime.RunStarts();
			AnimationSystem.Update(dt);
			Runtime.RunLateUpdate();
			Particles.Update(dt);
			Render.Sync();
			GuiSystem.Frame();
			CoroutineRunner.Run(2);
			Runtime.ProcessDestroyQueue();

			DebugAutomation(delta);
			mPrefsTimer += delta;
			if (mPrefsTimer > 5) { mPrefsTimer = 0; Prefs.Flush(); }
		}

		public void Screenshot(string filename)
		{
			var img = GetViewport().GetTexture().GetImage();
			img.SavePng(filename.StartsWith("/") || filename.Contains("://") ? filename : "user://" + filename);
		}
	}

	public static class SceneManager
	{
		public static int Pending = -1;
		private static bool sAdditive;

		public static string[] LevelNames()
		{
			int main = AssetDb.FileKey("mainData");
			foreach (var o in AssetDb.File(main).objects.Values)
				if (o.GetProperty("c").GetString() == "BuildSettings")
					return o.GetProperty("d").GetProperty("levels").EnumerateArray()
						.Select(e => System.IO.Path.GetFileNameWithoutExtension(e.GetString())).ToArray();
			return new string[0];
		}

		public static void Request(int level, bool additive)
		{
			Pending = level;
			sAdditive = additive;
		}

		public static void ProcessPending()
		{
			if (Pending < 0) return;
			int level = Pending;
			Pending = -1;
			GD.Print($"[svz] loading level {level} ({(level < Application.LevelNames.Length ? Application.LevelNames[level] : "?")})");
			var sw = System.Diagnostics.Stopwatch.StartNew();
			if (!sAdditive) Runtime.UnloadScene();
			Application.__loadedLevel = level;
			Time.__levelLoadTime = Time.__time;
			var built = AssetDb.LoadScene(level);
			GD.Print($"[svz] level {level}: {built.Count} objects in {sw.ElapsedMilliseconds} ms");
			Runtime.Broadcast("OnLevelWasLoaded", level);
			FramePacing.OnLevelLoaded();
			if (Main.Verbose) foreach (var mb in Runtime.Behaviours) GD.Print($"[svz]   mb {mb.GetType().Name} on {mb.gameObject?.name} awoken={mb.__awoken} en={mb.__enabled} active={mb.gameObject?.active} asset={mb.__isAsset}");
			if (Main.Verbose) foreach (var g in built) GD.Print($"[svz]   go {g.name} comps={string.Join(",", g.__components.Select(c => c.GetType().Name))} active={g.active}");
		}
	}

	/// Screen size (portrait/landscape handled by Godot's window/stretch settings)
	public static class Display
	{
		private static Node sRoot;
		public static void Init(Node root) => sRoot = root;
		public static int Width => (int)(sRoot?.GetViewport().GetVisibleRect().Size.X ?? 1024);
		public static int Height => (int)(sRoot?.GetViewport().GetVisibleRect().Size.Y ?? 768);
	}

	/// Collects Godot input events and exposes them with Unity's per-frame semantics.
	public static class InputState
	{
		private class Finger { public int id; public UnityEngine.Vector2 pos, start, delta; public TouchPhase phase; public bool down, ended, moved, began; public float lastTime; public int taps; }

		private static readonly Dictionary<int, Finger> sFingers = new Dictionary<int, Finger>();
		public static readonly List<Touch> Touches = new List<Touch>();
		private static readonly bool[] sMouse = new bool[3], sMouseDown = new bool[3], sMouseUp = new bool[3];
		private static readonly bool[] sMousePending = new bool[3], sMouseDownPending = new bool[3], sMouseUpPending = new bool[3];
		private static readonly HashSet<KeyCode> sKeys = new HashSet<KeyCode>(), sKeysDown = new HashSet<KeyCode>(), sKeysUp = new HashSet<KeyCode>();
		private static readonly HashSet<KeyCode> sKeysDownPending = new HashSet<KeyCode>(), sKeysUpPending = new HashSet<KeyCode>();
		public static UnityEngine.Vector3 MousePosition;
		public static string InputString = "";
		private static string sPendingString = "";
		/// Fingers currently on the screen (also while a movie plays, when BeginFrame does not run).
		public static int FingersDown { get { int n = 0; foreach (var f in sFingers.Values) if (f.down && !f.ended) n++; return n; } }
		public static bool AnyKey => sKeys.Count > 0 || sMouse.Any(b => b);
		public static bool AnyKeyDown => sKeysDown.Count > 0 || sMouseDown.Any(b => b);

		private static UnityEngine.Vector2 ToUnity(Godot.Vector2 p) => new UnityEngine.Vector2(p.X, Display.Height - p.Y);

		public static void Feed(InputEvent e)
		{
			switch (e)
			{
				case InputEventScreenTouch t:
				{
					if (!sFingers.TryGetValue(t.Index, out var f)) sFingers[t.Index] = f = new Finger { id = t.Index };
					var p = ToUnity(t.Position);
					if (t.Pressed)
					{
						f.down = true; f.began = true; f.ended = false; f.pos = f.start = p; f.delta = UnityEngine.Vector2.zero;
						f.taps = UnityEngine.Time.realtimeSinceStartup - f.lastTime < 0.3f ? f.taps + 1 : 1;
						f.lastTime = UnityEngine.Time.realtimeSinceStartup;
					}
					else { f.ended = true; f.pos = p; }
					break;
				}
				case InputEventScreenDrag d:
				{
					if (sFingers.TryGetValue(d.Index, out var f))
					{
						var p = ToUnity(d.Position);
						f.delta += p - f.pos; f.pos = p; f.moved = true;
					}
					break;
				}
				case InputEventMouseButton mb:
				{
					int b = mb.ButtonIndex == MouseButton.Left ? 0 : mb.ButtonIndex == MouseButton.Right ? 1 : mb.ButtonIndex == MouseButton.Middle ? 2 : -1;
					if (b < 0) break;
					if (mb.Pressed) { sMousePending[b] = true; sMouseDownPending[b] = true; }
					else { sMousePending[b] = false; sMouseUpPending[b] = true; }
					var mp = ToUnity(mb.Position);
					MousePosition = new UnityEngine.Vector3(mp.x, mp.y, 0f);
					break;
				}
				case InputEventMouseMotion mm:
				{
					var mp = ToUnity(mm.Position);
					MousePosition = new UnityEngine.Vector3(mp.x, mp.y, 0f);
					break;
				}
				case InputEventKey k:
				{
					KeyCode kc = MapKey(k.PhysicalKeycode == Key.None ? k.Keycode : k.PhysicalKeycode);
					if (k.Echo) break;
					if (k.Pressed) { sKeysDownPending.Add(kc); sKeys.Add(kc); if (k.Unicode > 0) sPendingString += (char)k.Unicode; }
					else { sKeysUpPending.Add(kc); }
					// Android back button
					if (k.Keycode == Key.Back || k.Keycode == Key.Escape) { if (k.Pressed) { sKeysDownPending.Add(KeyCode.Escape); sKeys.Add(KeyCode.Escape); } else sKeysUpPending.Add(KeyCode.Escape); }
					break;
				}
			}
		}

		public static void BeginFrame()
		{
			Touches.Clear();
			var gone = new List<int>();
			foreach (var f in sFingers.Values)
			{
				if (!f.down) continue;
				TouchPhase ph = f.began ? TouchPhase.Began : f.ended ? TouchPhase.Ended : f.moved ? TouchPhase.Moved : TouchPhase.Stationary;
				Touches.Add(Touch.__Make(f.id, f.pos, f.delta, UnityEngine.Time.deltaTime, f.taps, ph));
				if (f.began && f.ended) { /* tap within one frame: report Began now, Ended next frame */ f.began = false; continue; }
				f.began = false; f.moved = false; f.delta = UnityEngine.Vector2.zero;
				if (f.ended) { f.down = false; gone.Add(f.id); }
			}
			for (int i = 0; i < 3; i++)
			{
				sMouseDown[i] = sMouseDownPending[i]; sMouseUp[i] = sMouseUpPending[i];
				sMouse[i] = sMousePending[i] || sMouseDownPending[i];
				sMouseDownPending[i] = sMouseUpPending[i] = false;
			}
			sKeysDown.Clear(); sKeysDown.UnionWith(sKeysDownPending); sKeysDownPending.Clear();
			sKeysUp.Clear(); sKeysUp.UnionWith(sKeysUpPending);
			foreach (var k in sKeysUpPending) sKeys.Remove(k);
			sKeysUpPending.Clear();
			InputString = sPendingString; sPendingString = "";
		}

		public static bool Mouse(int b, int mode) => b >= 0 && b < 3 && (mode == 0 ? sMouse[b] : mode == 1 ? sMouseDown[b] : sMouseUp[b]);
		public static bool KeyState(KeyCode k, int mode) => mode == 0 ? sKeys.Contains(k) : mode == 1 ? sKeysDown.Contains(k) : sKeysUp.Contains(k);
		public static float Axis(string name) => 0f;

		public static bool Button(string name, int mode)
		{
			switch (name)
			{
				case "Fire1": return Mouse(0, mode);
				case "Fire2": return Mouse(1, mode);
				case "Cancel": return KeyState(KeyCode.Escape, mode);
				default: return false;
			}
		}

		public static KeyCode KeyFromName(string name)
		{
			if (string.IsNullOrEmpty(name)) return KeyCode.None;
			switch (name.ToLowerInvariant())
			{
				case "escape": return KeyCode.Escape;
				case "space": return KeyCode.Space;
				case "return": case "enter": return KeyCode.Return;
				case "menu": return KeyCode.Menu;
				case "left": return KeyCode.LeftArrow;
				case "right": return KeyCode.RightArrow;
				case "up": return KeyCode.UpArrow;
				case "down": return KeyCode.DownArrow;
			}
			if (name.Length == 1 && char.IsLetterOrDigit(name[0]))
				return Enum.TryParse<KeyCode>(char.IsDigit(name[0]) ? "Alpha" + name : name.ToUpperInvariant(), out var kc) ? kc : KeyCode.None;
			return KeyCode.None;
		}

		private static KeyCode MapKey(Key k)
		{
			switch (k)
			{
				case Key.Escape: return KeyCode.Escape;
				case Key.Space: return KeyCode.Space;
				case Key.Enter: return KeyCode.Return;
				case Key.Menu: return KeyCode.Menu;
				case Key.Left: return KeyCode.LeftArrow;
				case Key.Right: return KeyCode.RightArrow;
				case Key.Up: return KeyCode.UpArrow;
				case Key.Down: return KeyCode.DownArrow;
				case Key.Backspace: return KeyCode.Backspace;
			}
			if (k >= Key.A && k <= Key.Z) return KeyCode.A + (int)(k - Key.A);
			if (k >= Key.Key0 && k <= Key.Key9) return KeyCode.Alpha0 + (int)(k - Key.Key0);
			return KeyCode.None;
		}
	}
}
