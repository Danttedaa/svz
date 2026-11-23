using System;
using Godot;

namespace UnityEngine
{
	public sealed class Time { internal static float __smoothDt = 1f / 30f, __dt, __time; public static float time => __time; }
	// Stand-in with the port's AnimationClip.length shape (cached curve length); no game data.
	public sealed class AnimationEvent { internal float __time; }
	public sealed class AnimationClip
	{
		internal float __length = -1f;
		internal readonly System.Collections.Generic.List<float> __tracks = new();
		internal readonly System.Collections.Generic.List<AnimationEvent> __events = new();
		public float length => __length >= 0 ? __length : (__length = __tracks.Count == 0 ? 0f : System.Linq.Enumerable.Max(__tracks));
	}
	public struct Mathf { public static float Lerp(float a, float b, float t) => a + (b - a) * t; public static int Abs(int x) => Math.Abs(x); }
	public sealed class Screen { public static int height => 1080; }
	public sealed class Input { public static int touchCount => 0; }
	// Stand-in for the legacy coroutine yield instruction; no proprietary game code.
	public sealed class WaitForSeconds { public float Seconds { get; } public WaitForSeconds(float seconds) { Seconds = seconds; } }

	public sealed class Application
	{
		public static int targetFrameRate { get => Godot.Engine.MaxFps; set => Godot.Engine.MaxFps = value <= 0 ? 60 : value; }
	}
}

namespace Svz
{
	using Time = UnityEngine.Time;
	using Mathf = UnityEngine.Mathf;

	public static class Toast { public static void Show(string text) { GD.Print(text); } }
	public static class Movie { public static bool Playing => false; public static void Update() { } }

	public partial class Main : Node
	{
		public static Main Instance;

		public override void _Ready()
		{
			Instance = this;
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			Godot.Engine.MaxFps = 60;
			foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--level=")) GD.Print(a);
		}

		public override void _Notification(int what)
		{
			if (what == NotificationWMCloseRequest || what == NotificationApplicationPaused) GD.Print("pause");
			else if (what == NotificationApplicationResumed) GD.Print("resume");
		}

		public override void _Process(double delta)
		{
			if (Movie.Playing) { Movie.Update(); return; }
			float dt = Math.Min((float)delta, 1f / 3f);
			Time.__dt = dt;
			Time.__smoothDt = Mathf.Lerp(Time.__smoothDt, dt, 0.1f);
		}
	}
}


// Synthetic iterator with the same *control-flow contract* as the old game:
// one configurable delay, then reward registration once, then finish once.
namespace Svz
{
	public sealed class InGameImpl
	{
		public int Registered { get; private set; }
		public int Finished { get; private set; }
		private System.Collections.IEnumerator PostWinDelay()
		{
			float wait = float.Parse("7.0", System.Globalization.CultureInfo.InvariantCulture);
			yield return new UnityEngine.WaitForSeconds(wait);
			RegisterWin();
			onFinishGameRequest();
		}
		private void RegisterWin() { Registered++; }
		private void onFinishGameRequest() { Finished++; }
	}
}


// Synthetic dying-list pattern for the verified, fail-closed IL hook.
// No proprietary game code or assets are included here.
public class Character
{
	public virtual bool isOver => false;
	public virtual float health => 0f;
	public virtual bool isPlayer => false;
	public virtual bool isBase => false;
	public virtual void Destroy() { }
}

public class CharactersManager
{
	private readonly System.Collections.Generic.List<Character> mDyingCharacters = new();
	private void UpdateBattle()
	{
		for (int i = mDyingCharacters.Count - 1; i >= 0; i--)
		{
			Character dying = mDyingCharacters[i];
			if (dying.isOver && !dying.isPlayer && !dying.isBase)
			{
				dying.Destroy();
				mDyingCharacters.RemoveAt(i);
			}
			else if (dying.health > 0f)
			{
				mDyingCharacters.RemoveAt(i);
			}
		}
	}
}


// Stand-in for the original legacy font-asset resolution policy.
// The hook changes only the chosen resolution folder name.
internal sealed class FontSetFinder
{
	public string basePath => "Fonts/1536/";
	private string AutoFindClosestResolutionFolderPath(System.Collections.Generic.List<int> allRez)
	{
		int best = allRez[0];
		int diff = UnityEngine.Mathf.Abs(UnityEngine.Screen.height - best);
		foreach (int rez in allRez)
		{
			int n = UnityEngine.Mathf.Abs(UnityEngine.Screen.height - rez);
			if (n < diff) { diff = n; best = rez; }
		}
		return best.ToString();
	}
}


namespace Svz
{
	// Stand-in iteration shapes only; no proprietary particle/animation data.
	public class FakeParticleEmitter { }
	public class FakeAnimationState { }
	public class FakeAnimation
	{
		internal readonly System.Collections.Generic.List<FakeAnimationState> __states = new();
	}
	public static class Particles
	{
		public static readonly System.Collections.Generic.List<FakeParticleEmitter> Emitters = new();
		public static void Update(float dt)
		{
			foreach (var e in Emitters.ToArray()) { if (e != null) { } }
		}
	}
	public static class AnimationSystem
	{
		private static void Step(FakeAnimation a, float dt)
		{
			foreach (var s in a.__states.ToArray()) { if (s != null) { } }
		}
		private static void FinishStates(FakeAnimation a)
		{
			foreach (var s in a.__states.ToArray()) { if (s != null) { } }
		}
	}
}


// Stand-in shapes for the text patches (M10): SUILabel's two font loads, and GuiCanvas drawing each
// GUIText's glyphs with one DrawTextureRectRegion call. No game code, fonts or layouts.
namespace UnityEngine
{
	public class Object { }
	public class Font : Object { }
	public static class Resources { public static Object Load(string path, System.Type type) => path.Contains("default") ? new Font() : null; }
	public class GUIElement { }
	public class GUIText : GUIElement { public Font font { get; set; } }
}

public class SUILabel
{
	private readonly UnityEngine.GUIText mTextSprite = new UnityEngine.GUIText();
	private void initSprite(string fontFile)
	{
		string path = new FontSetFinder().basePath + fontFile;
		var font = (UnityEngine.Font)UnityEngine.Resources.Load(path, typeof(UnityEngine.Font));
		if (font == null) font = (UnityEngine.Font)UnityEngine.Resources.Load("Fonts/default/" + fontFile, typeof(UnityEngine.Font));
		mTextSprite.font = font;
	}
}

namespace Svz
{
	public static partial class GuiSystem
	{
		public static readonly System.Collections.Generic.List<UnityEngine.GUIElement> Elements = new();
		private partial class GuiCanvas : Godot.Node2D
		{
			public override void _Draw()
			{
				float sh = 1080f;
				foreach (var item in System.Linq.Enumerable.ToList(Elements))
				{
					try
					{
						if (item is UnityEngine.GUIText t) DrawText(t, sh);
					}
					catch (Exception e) { GD.Print(e.Message); }
				}
			}
			private void DrawText(UnityEngine.GUIText t, float sh)
			{
				Texture2D tex = null;
				if (t.font == null) return;
				DrawTextureRectRegion(tex, new Rect2(0, sh, 10, -10), new Rect2(0, 0, 10, 10), new Color(1, 1, 1, 1));
			}
		}
	}
}
