using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Svz
{
	/// <summary>
	/// Readable bitmap-font text for the port (Store, Allies, dialogs, HUD), binary-patch route.
	///
	/// Original SvZ bitmap glyphs remain the default and retain the game's character.
	/// An optional HD sampling mode prefilters those SAME glyph pixels at half
	/// resolution, addressing 1536->768 atlas minification without replacing the
	/// font or changing positioning, layout, effects, colors, or glyph shapes.
	/// There is exactly ONE gesture: hold two fingers for two seconds to switch
	/// between original and original-HD for A/B screenshots.
	/// Settings: user://svz_ui.cfg, section [text], font_mode_v5.
	/// </summary>
	public static class UiText
	{
		public const string ConfigPath = "user://svz_ui.cfg";
		public static bool Contrast = false;
		public static int FontMode = UiInkPolicy.Original;
		private static bool sHaloPass, sLoaded;
		private static float sViewportHeight;
		private static ulong sOffsetFrame = ulong.MaxValue;
		private static float sOffset = 1f;
		private static readonly HoldGesture sTextToggle = new HoldGesture(2, 2.0);

		private sealed class HaloAtlas { public Texture2D Texture; public float Luminance, Chroma; }
		private static readonly ConditionalWeakTable<Texture2D, HaloAtlas> sAtlases = new();
		private sealed class PrefilterAtlas { public Texture2D Texture; }
		private static readonly ConditionalWeakTable<Texture2D, PrefilterAtlas> sPrefilter = new();
		private static bool sLoggedSmoothedGlyph, sLoggedSamplingGate;

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
			texture = FontTextureCoverage.ForFont(texture);
			if (!sHaloPass)
			{
				// Source is the ORIGINAL font atlas. This optional A/B mode
				// creates a Lanczos downsample ONCE per source texture to avoid
				// aliasing when 1536px font glyphs are displayed at half size.
				// The source rect maps to half-resolution texture coordinates,
				// while destination geometry and original RGBA stay unchanged.
				if (FontMode == UiInkPolicy.SmoothedOriginal)
				{
					if (sViewportHeight <= 0f) sViewportHeight = ViewHeight(self);
					int nearest = UiFontQuality.NearestResolution, selected = UiFontQuality.SelectedResolution;
					float glyphHeight = Math.Abs(rect.Size.Y);
					int atlasWidth = texture.GetWidth(), atlasHeight = texture.GetHeight();
					if (!sLoggedSamplingGate)
					{
						sLoggedSamplingGate = true;
						GD.Print($"[svz-ui] font prefilter diagnostics: nearest={nearest} selected={selected} glyph={glyphHeight:0.0} viewport={sViewportHeight:0.0} atlas={atlasWidth}x{atlasHeight}");
					}
					if (UiGlyphSampling.UseHalfAtlas(nearest, selected, glyphHeight,
						sViewportHeight, atlasWidth, atlasHeight))
					{
						var filtered = sPrefilter.GetValue(texture, MakePrefilterAtlas);
						if (filtered.Texture != null)
						{
							var fs = new Vector2(
								(float)filtered.Texture.GetWidth() / texture.GetWidth(),
								(float)filtered.Texture.GetHeight() / texture.GetHeight());
							var mapped = new Rect2(srcRect.Position * fs, srcRect.Size * fs);
							// Keep disabled labels visibly disabled, but avoid near-invisible
							// monochrome text on textured panels. Never revive zero-alpha text.
							var tint = modulate ?? Colors.White;
							float chroma = Math.Max(tint.R, Math.Max(tint.G, tint.B)) -
								Math.Min(tint.R, Math.Min(tint.G, tint.B));
							if (chroma <= 0.16f && tint.A >= 0.20f && tint.A < 0.65f)
								tint.A = 0.65f;
							self.DrawTextureRectRegion(filtered.Texture, rect, mapped, tint, transpose, clipUv);
							if (!sLoggedSmoothedGlyph)
							{
								sLoggedSmoothedGlyph = true;
								GD.Print("[svz-ui] prefiltered original font glyph rendered");
							}
							return;
						}
					}
				}
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

		/// One-time Lanczos antialiasing for the original high-resolution font.
		/// This never modifies source textures or changes any UI size, positions,
		/// color or glyph assets. If the atlas cannot be read, use it unchanged.
		private static PrefilterAtlas MakePrefilterAtlas(Texture2D source)
		{
			var result = new PrefilterAtlas();
			try
			{
				var img = source.GetImage();
				if (img == null || img.IsEmpty())
				{
					GD.Print("[svz-ui] original-font prefilter unavailable: source image empty");
					return result;
				}
				if (img.IsCompressed() && img.Decompress() != Error.Ok)
				{
					GD.Print("[svz-ui] original-font prefilter unavailable: source decompress failed");
					return result;
				}
				img.Convert(Image.Format.Rgba8);
				int width = UiGlyphSampling.HalfSize(img.GetWidth());
				int height = UiGlyphSampling.HalfSize(img.GetHeight());
				// Filter premultiplied RGB so transparent texels cannot bleed
				// arbitrary colors into visible letter edges. Restore straight
				// alpha before handing the texture to Godot's normal blend mode.
				img.PremultiplyAlpha();
				img.Resize(width, height, Image.Interpolation.Lanczos);
				var straight = img.GetData();
				for (int p = 0; p < straight.Length; p += 4)
				{
					int alpha = straight[p + 3];
					if (alpha == 0) continue;
					for (int c = 0; c < 3; c++)
						straight[p + c] = (byte)Math.Min(255, (straight[p + c] * 255 + alpha / 2) / alpha);
				}
				img = Image.CreateFromData(width, height, false, Image.Format.Rgba8, straight);
				// The hand-painted font contains narrow transparent scratches.
				// On 720p screens some scratches become isolated holes after
				// minification. Repair only pixels surrounded by existing ink;
				// preserve letter counters, outer silhouettes, palette and UVs.
				var pixels = img.GetData();
				int repaired = UiGlyphAlphaRepair.Repair(pixels, width, height);
				if (repaired > 0)
					img = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
				result.Texture = ImageTexture.CreateFromImage(img);
				GD.Print($"[svz-ui] original-font atlas prefiltered: {source.GetWidth()}x{source.GetHeight()} -> {width}x{height}, pinholes={repaired}");
			}
			catch (Exception e)
			{
				GD.Print("[svz-ui] original-font prefilter unavailable: " + e.Message);
			}
			return result;
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
				// The previous native-font experiment was less legible than the
				// original on S25 Ultra. Upgrade once to the original, then save
				// a choice between original and original-HD in font_mode_v5.
				int requestedMode = exists && cf.HasSectionKey("text", "font_mode_v5")
					? cf.GetValue("text", "font_mode_v5").AsInt32() : UiInkPolicy.Original;
				FontMode = requestedMode == UiInkPolicy.SmoothedOriginal
					? UiInkPolicy.SmoothedOriginal : UiInkPolicy.Original;
				Contrast = false;
				UiFontQuality.TextScale = 1f;
				if (!exists || !cf.HasSectionKey("text", "font_mode_v5") ||
					!cf.HasSectionKey("text", "scale") || cf.GetValue("text", "scale").AsInt32() != 100) Save();
				// Private emulator smoke can exercise original-HD without saving
				// the test mode into the player's own settings.
				if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--test-original-hd") >= 0)
					FontMode = UiInkPolicy.SmoothedOriginal;
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
			cf.SetValue("text", "font_mode_v4", FontMode == UiInkPolicy.SmoothedOriginal ? UiInkPolicy.Original : FontMode);
			cf.SetValue("text", "font_mode_v5", FontMode);
			cf.Save(ConfigPath);
		}

		private static void OnFrame(double delta, int touchCount)
		{
			if (!sTextToggle.Update(touchCount, delta)) return;
			FontMode = UiInkPolicy.NextMode(FontMode);
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
