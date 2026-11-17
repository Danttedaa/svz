using Godot;
using Svz;

namespace UnityEngine
{
	public sealed partial class iPhoneUtils
	{
		public static partial bool isApplicationGenuine => true;
		public static partial bool isApplicationGenuineAvailable => false;

		// Unity blocks the game while a full-screen movie plays; the frame loop pauses the same way
		private static partial void INTERNAL_CALL_PlayMovie(string path, ref Color bgColor, iPhoneMovieControlMode controlMode, iPhoneMovieScalingMode scalingMode) =>
			Movie.Play(path, bgColor, controlMode != iPhoneMovieControlMode.Hidden);

		private static partial void INTERNAL_CALL_PlayMovieURL(string url, ref Color bgColor, iPhoneMovieControlMode controlMode, iPhoneMovieScalingMode scalingMode) { }
		public static partial void Vibrate() => Input.__Vibrate();
	}

	public sealed partial class Input
	{
		internal static void __Vibrate() => Godot.Input.VibrateHandheld(200);
	}
}

namespace Svz
{
	/// full-screen movies (Unity's mp4 converted to Theora .ogv at build time: res://video/<name>.ogv)
	public static class Movie
	{
		public static bool Playing => sLayer != null;
		private static CanvasLayer sLayer;
		private static VideoStreamPlayer sPlayer;
		private static AspectRatioContainer sBox;
		private static bool sSkippable;
		private static double sStarted;

		public static void Play(string path, UnityEngine.Color bg, bool skippable)
		{
			string name = System.IO.Path.GetFileNameWithoutExtension(path ?? "");
			string res = $"res://video/{name}.ogv";
			var stream = ResourceLoader.Exists(res) ? ResourceLoader.Load<VideoStream>(res) : null;
			if (stream == null) { GD.Print("[svz] movie not available: " + path); return; }
			Stop();
			sLayer = new CanvasLayer { Layer = 200 };
			Main.Instance.AddChild(sLayer);
			var back = new ColorRect { Color = new Godot.Color(bg.r, bg.g, bg.b, 1f), MouseFilter = Control.MouseFilterEnum.Ignore };
			back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			sLayer.AddChild(back);
			sBox = new AspectRatioContainer { Ratio = 16f / 9f, StretchMode = AspectRatioContainer.StretchModeEnum.Fit, MouseFilter = Control.MouseFilterEnum.Ignore };
			sBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			sLayer.AddChild(sBox);
			sPlayer = new VideoStreamPlayer { Stream = stream, Expand = true, Autoplay = false };
			sBox.AddChild(sPlayer);
			sPlayer.Finished += Stop;
			sSkippable = skippable;
			sStarted = Time.GetTicksMsec() / 1000.0;
			sPlayer.Play();
		}

		/// called every frame while playing (the game loop is paused)
		public static void Update()
		{
			if (sPlayer == null) return;
			var tex = sPlayer.GetVideoTexture();
			if (tex != null && tex.GetHeight() > 0) sBox.Ratio = (float)tex.GetWidth() / tex.GetHeight();
		}

		public static void OnInput(InputEvent e)
		{
			if (!Playing || !sSkippable || Time.GetTicksMsec() / 1000.0 - sStarted < 0.5) return;
			bool tap = e is InputEventScreenTouch { Pressed: true } || e is InputEventMouseButton { Pressed: true }
				|| e is InputEventKey { Pressed: true } k && (k.Keycode == Key.Back || k.Keycode == Key.Escape);
			if (tap) Stop();
		}

		public static void Stop()
		{
			if (sLayer == null) return;
			sPlayer?.Stop();
			sLayer.QueueFree();
			sLayer = null; sPlayer = null; sBox = null;
		}
	}
}
