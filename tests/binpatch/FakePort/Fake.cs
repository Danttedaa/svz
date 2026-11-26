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
	public static class Movie { public static bool Playing => false; public static void Update() { } public static void Play(VideoStreamPlayer player) { player.Play(); } }

	public partial class Main : Node
	{
		public static Main Instance;
		private int mCount;
		private void DebugAutomation(double delta)
		{
			if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--corpses")) mCount++;
			if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--trace-cam")) mCount++;
		}

		public override void _Ready()
		{
			Instance = this;
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			Godot.Engine.MaxFps = 60;
			foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--level=")) GD.Print(a);
		}

		public override void _Input(InputEvent input) { GD.Print("game input"); }

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


// Synthetic Unity structs/renderer used to validate retained mesh-array identity.
namespace UnityEngine
{
	public struct Vector3 { public float x, y, z; }
	public struct Vector2 { public float x, y; }
	public struct Color { public float r, g, b, a; }
	public sealed class Mesh
	{
		internal Vector3[] __v = Array.Empty<Vector3>();
		internal Vector2[] __uv = Array.Empty<Vector2>();
		internal Color[] __c = Array.Empty<Color>();
		internal System.Collections.Generic.List<int[]> __subs = new() { Array.Empty<int>() };
	}
	public sealed class ParticleRenderer
	{
		internal readonly Mesh __pmesh = new Mesh();
		public int ParticleCount { get; set; }
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
		// Mirrors the four allocations and the particle mesh field ownership in the port.
		public static UnityEngine.Mesh BuildMesh(UnityEngine.ParticleRenderer renderer, object camera)
		{
			var mesh = renderer.__pmesh;
			int n = renderer.ParticleCount;
			var v = new UnityEngine.Vector3[n * 4];
			var uv = new UnityEngine.Vector2[n * 4];
			var col = new UnityEngine.Color[n * 4];
			var tri = new int[n * 6];
			for (int i = 0; i < n * 4; i++)
			{
				v[i] = new UnityEngine.Vector3 { x = i + n };
				uv[i] = new UnityEngine.Vector2 { x = i };
				col[i] = new UnityEngine.Color { a = n };
			}
			for (int i = 0; i < n * 6; i++) tri[i] = i;
			mesh.__v = v; mesh.__uv = uv; mesh.__c = col;
			mesh.__subs = new System.Collections.Generic.List<int[]> { tri };
			return mesh;
		}
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

	/// Same Godot call shapes as the port's MeshCache (built, never run off-engine here).
	public static class MeshCache
	{
		public static ArrayMesh Get(ArrayMesh gm)
		{
            var pos = new Vector3[3]; var idx = new[] { 0, 1, 2 }; gm.ClearSurfaces();
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = pos;
			arrays[(int)Mesh.ArrayType.Index] = idx;
			gm.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			return gm;
		}
		public static ArrayMesh GetSubsets(ArrayMesh full, int surf)
		{
			var gm = new ArrayMesh();
			gm.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, full.SurfaceGetArrays(surf));
			return gm;
		}
	}

    public static class Skinning
    {
        public static void Sync(Skeleton3D skeleton, int bone, Transform3D value)
        {
            skeleton.SetBonePosePosition(bone, value.Origin);
            var basis = value.Basis;
            var scale = basis.Scale;
            skeleton.SetBonePoseScale(bone, scale);
            skeleton.SetBonePoseRotation(bone, basis.Orthonormalized().GetRotationQuaternion());
        }
    }

	public static class Render
	{
		public static int Hidden;
        public static ArrayMesh TestMesh; public static ShaderMaterial TestMaterial;
        public static void Sync() { }
        public static SubViewport TestViewport;
        private static void SyncCameras() { TestViewport.Size = new Vector2I(640, 360); }
		private static void SyncRenderer(object r)
		{
			if (r is string && System.Environment.GetEnvironmentVariable("SVZ_NOPARTICLES") == "1") Hidden++;
            if (r is not MeshInstance3D node) return;
            if (Hidden < 0) { node.Visible = false; return; }
            node.Visible = true;
            node.Layers = 3;
            node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (Hidden == 0) node.GlobalTransform = Transform3D.Identity;
            else if (Hidden == 1) node.GlobalTransform = new Transform3D(Basis.Identity, Vector3.One);
            else if (Hidden == 2) node.GlobalTransform = new Transform3D(Basis.Identity, Vector3.Up);
            else node.GlobalTransform = new Transform3D(Basis.Identity, Vector3.Right);
            var gm = TestMesh;
            if (node.Mesh != gm) node.Mesh = gm;
            int surfaces = gm.GetSurfaceCount();
            for (int i = 0; i < surfaces; i++)
            {
                var material = Materials.Get(TestMaterial, r);
                if (node.GetSurfaceOverrideMaterial(i) != material) node.SetSurfaceOverrideMaterial(i, material);
            }
		}
	}
}


// Stand-in shapes for the text patches (M10): SUILabel's two font loads, and GuiCanvas drawing each
// GUIText's glyphs with one DrawTextureRectRegion call. No game code, fonts or layouts.
namespace UnityEngine
{
	public class Object { public string name { get; set; } = ""; }
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


// Stand-in for the two obsolete market-rating branches in DebriefingImpl.
// This remains a synthetic behaviour test and contains no original game code.
public static class ApplicationUtilities
{
	public static bool IsBuildType(string build) => build == "google";
}
public sealed class DebriefingImpl
{
	public int RewardChecks { get; private set; }
	public int RatingPrompts { get; private set; }
	private void DisplayStats()
	{
		RewardChecks++;
		if (ApplicationUtilities.IsBuildType("amazon")) RatingPrompts++;
		else if (ApplicationUtilities.IsBuildType("google")) RatingPrompts++;
		// Original victory statistics and coin bookkeeping must still execute.
		string label = "debrief_victory";
		string eventName = "CoinsGained";
		if (label.Length > 0 && eventName.Length > 0) RewardChecks++;
	}
}

// Public stand-ins for settings integration: no game assets or proprietary logic.
public class OptionsImpl { private object mAboutDialog, mConfirmDialog; public void Start() { } public void Update() { } }
public class PauseMenu { public PauseMenu() { } public void Destroy() { } }
namespace UnityEngine {
    public class AudioClip : Object { }
    public class AudioSource {
        internal AudioClip __clip = new AudioClip();
        private Godot.AudioStreamPlayer player;
        private Godot.AudioStream stream;
        public void Play(ulong delay) { player.Stream = stream; }
    }
}
namespace Svz {
    public static class AudioSystem {
        private static Godot.AudioStreamPlayer player;
        public static void OneShot(Godot.AudioStream stream, float volume, float pitch) { player.Stream = stream; }
    }
    public static class InputState {
        public static readonly System.Collections.Generic.Dictionary<int,int> sFingers = new();
        public static readonly System.Collections.Generic.List<int> Touches = new();
        public static readonly System.Collections.Generic.HashSet<int> sKeys = new(), sKeysDown = new(), sKeysUp = new(), sKeysDownPending = new(), sKeysUpPending = new();
        public static readonly bool[] sMouse = new bool[3], sMouseDown = new bool[3], sMouseUp = new bool[3], sMousePending = new bool[3], sMouseDownPending = new bool[3], sMouseUpPending = new bool[3];
        public static string InputString = "", sPendingString = "";
    }
}

namespace Svz { public static class SceneManager { public static void Request(int level, bool additive) { } } }

namespace Svz {
    public static class Materials {
        private static string Build(int flags) => new System.Text.StringBuilder("shader_type spatial;").ToString();
        public static Godot.Material Get(ShaderMaterial material, object renderer) {
            material.SetShaderParameter("main_tex", (Texture2D)null);
            material.SetShaderParameter("second_tex", (Texture2D)null);
            material.SetShaderParameter("tint", Colors.White);
            material.SetShaderParameter("a", 1f); material.SetShaderParameter("b", 2f);
            material.SetShaderParameter("c", 3f); material.SetShaderParameter("d", 4f);
            material.SetShaderParameter("e", 5f); material.SetShaderParameter("f", 6f);
            material.SetShaderParameter("g", 7f); material.SetShaderParameter("h", 8f);
            return material;
        }
    }
}
