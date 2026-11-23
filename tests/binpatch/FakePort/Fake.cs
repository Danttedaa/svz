using System;
using Godot;

namespace UnityEngine
{
	public sealed class Time { internal static float __smoothDt = 1f / 30f, __dt; }
	public struct Mathf { public static float Lerp(float a, float b, float t) => a + (b - a) * t; }
	public sealed class Input { public static int touchCount => 0; }
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
