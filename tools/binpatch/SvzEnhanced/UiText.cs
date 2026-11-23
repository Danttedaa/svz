using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Svz
{
	/// <summary>
	/// Readable bitmap-font text for the port (Store, Allies, dialogs, HUD), binary-patch route.
	///
	/// Readable native font is the default for small interface labels. The original
	/// bitmap renderer remains available for unsupported text and as a rollback.
	/// There is exactly ONE gesture: hold two fingers for two seconds to switch
	/// between readable and original text. No font-size, FPS or overlay holds.
	/// Original decorated headings, character art, and game data remain untouched.
	/// Settings: user://svz_ui.cfg, section [text], font_mode_v3.
	/// </summary>
	public static class UiText
	{
		public const string ConfigPath = "user://svz_ui.cfg";
		public static bool Contrast = false;
		public static int FontMode = UiInkPolicy.Native;
		private static bool sHaloPass, sLoaded;
		private static float sViewportHeight;
		private static ulong sOffsetFrame = ulong.MaxValue;
		private static float sOffset = 1f;
		private static readonly HoldGesture sTextToggle = new HoldGesture(2, 2.0);

		private sealed class HaloAtlas { public Texture2D Texture; public float Luminance, Chroma; }
		private static readonly ConditionalWeakTable<Texture2D, HaloAtlas> sAtlases = new();

		/// Narrow one-pixel outline at 1080p; avoid obscuring the thin legacy glyph interiors.
		public static float HaloOffset(float screenHeight) => Math.Max(0.8f, screenHeight / 1080f);

		/// Halo colour for the visible text colour (atlas luminance x modulate); null = no halo.
		public static Color? HaloColor(Color modulate, float atlasLuminance) =>
			UiFontQuality.HaloKind(modulate.R, modulate.G, modulate.B, modulate.A, atlasLuminance) switch
			{
				1 => new Color(1f, 0.96f, 0.85f, Math.Min(1f, modulate.A * 0.55f)),   // dark text: warm parchment halo
				-1 => new Color(0f, 0f, 0f, Math.Min(1f, modulate.A * 0.55f)),       // light text: dark halo
				_ => null,
			};

		/// True only when the extra outline pass should actually draw. The patched GUI
		/// skips the entire duplicate label traversal when this returns false.
		public static bool BeginHaloPass() { EnsureLoaded(); sHaloPass = Contrast; return Contrast; }
		public static void EndHaloPass() => sHaloPass = false;

		/// Replaces CanvasItem.DrawTextureRectRegion in GuiCanvas.DrawText (same arguments, `self` first).
		public static void DrawGlyph(CanvasItem self, Texture2D texture, Rect2 rect, Rect2 srcRect, Color? modulate, bool transpose, bool clipUv)
		{
			if (!sHaloPass)
			{
				// Keep the original silhouette but remove noisy baked RGB details from
				// small monochrome glyphs. Do not affect large art headings or colored text.
				if (FontMode == UiInkPolicy.Solid)
				{
					if (sViewportHeight <= 0f) sViewportHeight = ViewHeight(self);
					float height = Math.Abs(rect.Size.Y);
					if (height >= 4f && height <= sViewportHeight * 0.052f)
					{
						var fillAtlas = sAtlases.GetValue(texture, MakeHaloAtlas);
						var original = modulate ?? Colors.White;
						int ink = UiInkPolicy.InkKind(height, sViewportHeight,
							fillAtlas.Luminance, fillAtlas.Chroma, original.R, original.G,
							original.B, original.A);
						if (ink != 0 && fillAtlas.Texture != null)
						{
							float v = ink > 0 ? 0.055f : 0.96f;
							self.DrawTextureRectRegion(fillAtlas.Texture, rect, srcRect,
								new Color(v, v, v, original.A), transpose, clipUv);
							return;
						}
					}
				}
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
			// Four short cardinal strokes replace eight diagonal+cardinal halo draws.
			// Preserves the original glyph draw pass and text metrics, while halving
			// halo submissions and avoiding a thick glow that fills glyph interiors.
			for (int direction = 0; direction < 4; direction++)
			{
				float dx = direction == 0 ? -o : direction == 1 ? o : 0f;
				float dy = direction == 2 ? -o : direction == 3 ? o : 0f;
				var r = new Rect2(rect.Position + new Vector2(dx, dy), rect.Size);
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
				double lumSum = 0, alphaSum = 0, redSum = 0, greenSum = 0, blueSum = 0;
				for (int i = 0; i < px; i++)
				{
					byte a = rgba[i * 4 + 3];
					la[i * 2] = 255;
					la[i * 2 + 1] = a;
					if (a > 0)
					{
						lumSum += a * (0.299 * rgba[i * 4] + 0.587 * rgba[i * 4 + 1] + 0.114 * rgba[i * 4 + 2]) / 255.0;
						redSum += a * rgba[i * 4] / 255.0;
						greenSum += a * rgba[i * 4 + 1] / 255.0;
						blueSum += a * rgba[i * 4 + 2] / 255.0;
						alphaSum += a;
					}
				}
				result.Luminance = alphaSum > 0 ? (float)(lumSum / alphaSum) : 1f;
				if (alphaSum > 0)
				{
					float r = (float)(redSum / alphaSum);
					float g = (float)(greenSum / alphaSum);
					float b = (float)(blueSum / alphaSum);
					result.Chroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
				}
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

		/// Reads (and on first start writes) user://svz_ui.cfg. One two-finger toggle only.
		public static void EnsureLoaded()
		{
			if (sLoaded) return;
			sLoaded = true;
			try
			{
				var cf = new ConfigFile();
				bool exists = cf.Load(ConfigPath) == Error.Ok;
				// The earlier experimental release exposed three outline/style choices
				// and a size hold. Retire those controls, return to 100% layout scale
				// and upgrade to the readable font once. Only a user selection recorded
				// by this new version (v3) survives a restart.
				int requestedMode = exists && cf.HasSectionKey("text", "font_mode_v3")
					? cf.GetValue("text", "font_mode_v3").AsInt32() : UiInkPolicy.Native;
				FontMode = requestedMode == UiInkPolicy.Original ? UiInkPolicy.Original : UiInkPolicy.Native;
				Contrast = false;
				UiFontQuality.TextScale = 1f;
				if (!exists || !cf.HasSectionKey("text", "font_mode_v3") ||
					!cf.HasSectionKey("text", "scale") || cf.GetValue("text", "scale").AsInt32() != 100) Save();
				FramePacing.AfterFrame += OnFrame;
				GD.Print($"[svz-ui] text scale {UiFontQuality.TextScale:0.00}x, mode {UiInkPolicy.ModeName(FontMode)}, contrast {(Contrast ? "on" : "off")}");
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
			cf.SetValue("text", "contrast", Contrast); // compatibility if rolling back to v1012
			cf.SetValue("text", "outline_v2", Contrast);
			cf.SetValue("text", "font_mode_v1", FontMode == UiInkPolicy.Native ? UiInkPolicy.Solid : FontMode);
			cf.SetValue("text", "font_mode_v2", FontMode);
			cf.SetValue("text", "font_mode_v3", FontMode);
			cf.Save(ConfigPath);
		}

		private static void OnFrame(double delta, int touchCount)
		{
			if (!sTextToggle.Update(touchCount, delta)) return;
			FontMode = FontMode == UiInkPolicy.Native ? UiInkPolicy.Original : UiInkPolicy.Native;
			Contrast = false;
			Save();
			FramePacing.Notify($"Text: {UiInkPolicy.ModeName(FontMode)}");
			GD.Print($"[svz-ui] text mode {UiInkPolicy.ModeName(FontMode)}");
		}

		/// Hook after SUILabel's font load: read the settings, then scale the font once.
		public static void PrepareFont(object font)
		{
			EnsureLoaded();
			UiFontQuality.PrepareFont(font);
		}
	}
}
