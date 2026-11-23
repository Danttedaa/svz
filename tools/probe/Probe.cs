using System.Collections.Generic;
using System.Globalization;
using Godot;
using Svz;

/// SvZ Enhanced 120 Hz probe: shows what the game's frame-pacing code achieves on this phone.
/// No game files. Read the screen (or take a screenshot): display Hz, engine FPS, the Android
/// refresh-rate request result and the 5-second frame statistics. The moving bar should glide evenly;
/// regular stutter means frames are missed.
public partial class Probe : Node
{
	private Label mInfo, mToast;
	private ColorRect mBar;
	private double mInfoTimer, mToastTimer, mBarX;
	private readonly HashSet<int> mTouches = new HashSet<int>();

	public override void _Ready()
	{
		FramePacing.Init(this, ShowToast);

		var layer = new CanvasLayer { Layer = 1 };
		AddChild(layer);
		var bg = new ColorRect { Color = new Color(0.07f, 0.07f, 0.09f) };
		bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(bg);

		mBar = new ColorRect { Color = new Color(0.95f, 0.35f, 0.2f), Size = new Vector2(28, 180) };
		layer.AddChild(mBar);

		mInfo = new Label { Position = new Vector2(36, 24) };
		mInfo.AddThemeFontSizeOverride("font_size", 26);
		layer.AddChild(mInfo);

		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 24);
		buttons.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		buttons.Position = new Vector2(36, GetViewport().GetVisibleRect().Size.Y - 120);
		layer.AddChild(buttons);
		foreach (int fps in new[] { 120, 90, 60 })
		{
			var b = new Button { Text = $"{fps} FPS", CustomMinimumSize = new Vector2(190, 84) };
			b.AddThemeFontSizeOverride("font_size", 30);
			int target = fps;
			b.Pressed += () => { FramePacing.SetTargetFps(target, "probe button"); ShowToast($"Target {target} FPS"); };
			buttons.AddChild(b);
		}

		mToast = new Label { Position = new Vector2(36, GetViewport().GetVisibleRect().Size.Y - 190) };
		mToast.AddThemeFontSizeOverride("font_size", 28);
		mToast.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
		layer.AddChild(mToast);
	}

	public override void _Input(InputEvent e)
	{
		if (e is InputEventScreenTouch t)
		{
			if (t.Pressed) mTouches.Add(t.Index); else mTouches.Remove(t.Index);
		}
	}

	private void ShowToast(string text)
	{
		if (mToast == null) return;
		mToast.Text = text;
		mToastTimer = 4;
	}

	public override void _Process(double delta)
	{
		FramePacing.Frame(delta, mTouches.Count);

		var size = GetViewport().GetVisibleRect().Size;
		mBarX = (mBarX + delta * size.X * 0.5) % size.X;   // one screen width every 2 s
		mBar.Position = new Vector2((float)mBarX, size.Y * 0.5f - 40);

		if (mToastTimer > 0 && (mToastTimer -= delta) <= 0) mToast.Text = "";
		if ((mInfoTimer += delta) < 0.25) return;
		mInfoTimer = 0;
		var ci = CultureInfo.InvariantCulture;
		mInfo.Text = string.Join("\n", new[]
		{
			"SvZ Enhanced 120 Hz probe (no game files)",
			$"Device: {OS.GetModelName()}   Android {OS.GetVersion()}   {Engine.GetVersionInfo()["string"]}",
			string.Format(ci, "Display now: {0:0.0} Hz      Engine: {1:0} FPS", DisplayServer.ScreenGetRefreshRate(), Engine.GetFramesPerSecond()),
			$"Target: {FramePacing.EffectiveFps} FPS{(FramePacing.FallenBack ? " (fallback active)" : "")}   Pacing: {FramePacing.Decision}",
			"Refresh-rate request: " + (FramePacing.LastBridgeStatus == "" ? "(none yet)" : FramePacing.LastBridgeStatus),
			"Last 5 s: " + (FramePacing.LastStats == "" ? "(measuring...)" : FramePacing.LastStats),
			"The bar should glide evenly. Hold 4 fingers or use the buttons to switch rates.",
		});
	}
}
