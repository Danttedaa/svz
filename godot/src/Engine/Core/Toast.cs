using System;
using Godot;

namespace Svz
{
	/// Launch toast ("MADE BY MESFLIT"): native Android Toast, drawn overlay elsewhere.
	public static class Toast
	{
		public static void Show(string text)
		{
			if (OS.GetName() == "Android" && ShowAndroid(text)) return;
			ShowOverlay(text);
		}

		private static bool ShowAndroid(string text)
		{
			try
			{
				if (!Godot.Engine.HasSingleton("AndroidRuntime")) return false;
				var rt = Godot.Engine.GetSingleton("AndroidRuntime");
				var activity = rt.Call("getActivity").AsGodotObject();
				var toast = JavaClassWrapper.Wrap("android.widget.Toast");
				if (activity == null || toast == null) return false;
				var runnable = rt.Call("createRunnableFromGodotCallable", Callable.From(() =>
				{
					var t = toast.Call("makeText", activity, text, 1).AsGodotObject();   // LENGTH_LONG
					t?.Call("show");
				}));
				activity.Call("runOnUiThread", runnable);
				return true;
			}
			catch (Exception e)
			{
				GD.PrintErr("[svz] toast: " + e.Message);
				return false;
			}
		}

		private static void ShowOverlay(string text)
		{
			var layer = new CanvasLayer { Layer = 100 };
			Main.Instance.AddChild(layer);
			var panel = new PanelContainer();
			var style = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f), CornerRadiusTopLeft = 24, CornerRadiusTopRight = 24, CornerRadiusBottomLeft = 24, CornerRadiusBottomRight = 24, ContentMarginLeft = 28, ContentMarginRight = 28, ContentMarginTop = 12, ContentMarginBottom = 12 };
			panel.AddThemeStyleboxOverride("panel", style);
			var label = new Label { Text = text };
			label.AddThemeFontSizeOverride("font_size", 28);
			panel.AddChild(label);
			layer.AddChild(panel);
			var vp = Main.Instance.GetViewport().GetVisibleRect().Size;
			panel.Position = new Vector2(vp.X * 0.5f - 160, vp.Y * 0.85f);
			panel.Resized += () => panel.Position = new Vector2((vp.X - panel.Size.X) * 0.5f, vp.Y * 0.85f);
			var tw = panel.CreateTween();
			tw.TweenInterval(3.0);
			tw.TweenProperty(panel, "modulate:a", 0.0, 0.5);
			tw.TweenCallback(Callable.From(layer.QueueFree));
		}
	}
}
