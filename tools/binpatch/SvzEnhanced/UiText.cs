using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Svz
{
	/// <summary>
	/// Readable bitmap-font text for the port (Store, Allies, dialogs, HUD), binary-patch route.
	///
	/// Contrast: the patched GuiCanvas draws each GUIText twice. The first pass draws a soft halo: every glyph
	/// at 8 small offsets from a white, alpha-only copy of its font atlas, tinted warm-light behind dark text
	/// and dark behind light text. The second pass is the game's normal text. (The atlases carry the glyph
	/// colour in RGB, so tinting the original atlas could only ever darken it.)
	///
	/// Size: see <see cref="UiFontQuality"/>. Hold 3 fingers for 2 s to switch between 100 % and 115 % (130 % clips the Store captions).
	///
	/// Settings: user://svz_ui.cfg, section [text]: scale (percent of the original text size, 80..150,
	/// default 100) and contrast (true/false, default true). Created with the defaults on first start.
	/// </summary>
	public static class UiText
	{
		public const string ConfigPath = "user://svz_ui.cfg";
		public static bool Contrast = true;
		private static bool sHaloPass, sLoaded;
		private static ulong sOffsetFrame = ulong.MaxValue;
		private static float sOffset = 1.5f;
		private static readonly HoldGesture sCycle = new HoldGesture(3, 2.0);

		private sealed class HaloAtlas { public Texture2D Texture; public float Luminance; }
		private static readonly ConditionalWeakTable<Texture2D, HaloAtlas> sAtlases = new();

		/// Halo offset in pixels: 1.5 px at 1080 p, never below 1 px.
		public static float HaloOffset(float screenHeight) => Math.Max(1f, screenHeight / 720f);

		/// Halo colour for the visible text colour (atlas luminance x modulate); null = no halo.
		public static Color? HaloColor(Color modulate, float atlasLuminance) =>
			UiFontQuality.HaloKind(modulate.R, modulate.G, modulate.B, modulate.A, atlasLuminance) switch
			{
				1 => new Color(1f, 0.94f, 0.78f, Math.Min(1f, modulate.A * 0.6f)),   // dark text: warm parchment halo
				-1 => new Color(0f, 0f, 0f, Math.Min(1f, modulate.A * 0.65f)),       // light text: dark halo
				_ => null,
			};

		public static void BeginHaloPass() { EnsureLoaded(); sHaloPass = Contrast; }
		public static void EndHaloPass() => sHaloPass = false;

		/// Replaces CanvasItem.DrawTextureRectRegion in GuiCanvas.DrawText (same arguments, `self` first).
		public static void DrawGlyph(CanvasItem self, Texture2D texture, Rect2 rect, Rect2 srcRect, Color? modulate, bool transpose, bool clipUv)
		{
			if (!sHaloPass)
			{
				self.DrawTextureRectRegion(texture, rect, srcRect, modulate, transpose, clipUv);
				return;
			}
			var atlas = sAtlases.GetValue(texture, MakeHaloAtlas);
			if (atlas.Texture == null) return;
			var halo = HaloColor(modulate ?? Colors.White, atlas.Luminance);
			if (halo == null) return;
			ulong frame = (ulong)Engine.GetFramesDrawn();
			if (frame != sOffsetFrame) { sOffsetFrame = frame; sOffset = HaloOffset(ViewHeight(self)); }
			float o = sOffset;
			for (int dx = -1; dx <= 1; dx++)
				for (int dy = -1; dy <= 1; dy++)
				{
					if (dx == 0 && dy == 0) continue;
					var r = new Rect2(rect.Position + new Vector2(dx * o, dy * o), rect.Size);
					self.DrawTextureRectRegion(atlas.Texture, r, srcRect, halo, transpose, clipUv);
				}
		}

		/// White copy of a font atlas that keeps only its alpha (LA8: half the memory of RGBA), plus the
		/// alpha-weighted luminance of the original glyph colours. Built once per atlas; on any failure the
		/// atlas gets no halo.
		private static HaloAtlas MakeHaloAtlas(Texture2D source)
		{
			var result = new HaloAtlas { Luminance = 1f };
			try
			{
				var img = source.GetImage();
				if (img == null || img.IsEmpty()) return result;
				if (img.IsCompressed()) img.Decompress();
				img.Convert(Image.Format.Rgba8);
				byte[] rgba = img.GetData();
				int px = rgba.Length / 4;
				var la = new byte[px * 2];
				double lumSum = 0, alphaSum = 0;
				for (int i = 0; i < px; i++)
				{
					byte a = rgba[i * 4 + 3];
					la[i * 2] = 255;
					la[i * 2 + 1] = a;
					if (a > 0)
					{
						lumSum += a * (0.299 * rgba[i * 4] + 0.587 * rgba[i * 4 + 1] + 0.114 * rgba[i * 4 + 2]) / 255.0;
						alphaSum += a;
					}
				}
				result.Luminance = alphaSum > 0 ? (float)(lumSum / alphaSum) : 1f;
				var white = Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.La8, la);
				result.Texture = ImageTexture.CreateFromImage(white);
			}
			catch (Exception e)
			{
				GD.Print("[svz-ui] no contrast halo for a font atlas: " + e.Message);
			}
			return result;
		}

		private static float ViewHeight(CanvasItem self)
		{
			try { return self.GetViewportRect().Size.Y; } catch (Exception) { return 1080f; }
		}

		/// Reads (and on first start writes) user://svz_ui.cfg and installs the 3-finger gesture.
		public static void EnsureLoaded()
		{
			if (sLoaded) return;
			sLoaded = true;
			try
			{
				var cf = new ConfigFile();
				bool exists = cf.Load(ConfigPath) == Error.Ok;
				int scale = exists && cf.HasSectionKey("text", "scale") ? cf.GetValue("text", "scale").AsInt32() : (int)Math.Round(UiFontQuality.DefaultTextScale * 100);
				Contrast = !exists || !cf.HasSectionKey("text", "contrast") || cf.GetValue("text", "contrast").AsBool();
				UiFontQuality.TextScale = Math.Clamp(scale, (int)(UiFontQuality.MinTextScale * 100), (int)(UiFontQuality.MaxTextScale * 100)) / 100f;
				if (!exists) Save();
				FramePacing.AfterFrame += OnFrame;
				GD.Print($"[svz-ui] text scale {UiFontQuality.TextScale:0.00}x, contrast {(Contrast ? "on" : "off")}");
			}
			catch (Exception e)
			{
				GD.Print("[svz-ui] settings not read, defaults used: " + e.Message);
			}
		}

		private static void Save()
		{
			var cf = new ConfigFile();
			cf.SetValue("text", "scale", (int)Math.Round(UiFontQuality.TextScale * 100));
			cf.SetValue("text", "contrast", Contrast);
			cf.Save(ConfigPath);
		}

		private static void OnFrame(double delta, int touchCount)
		{
			if (!sCycle.Update(touchCount, delta)) return;
			int next = UiFontQuality.NextScaleStep((int)Math.Round(UiFontQuality.TextScale * 100));
			UiFontQuality.SetTextScale(next / 100f);
			Save();
			FramePacing.Notify($"Text size {next}%");
			GD.Print($"[svz-ui] text scale {next}%");
		}

		/// Hook after SUILabel's font load: read the settings, then scale the font once.
		public static void PrepareFont(object font)
		{
			EnsureLoaded();
			UiFontQuality.PrepareFont(font);
		}
	}
}
