using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Svz
{
	/// <summary>
	/// Sharper, readable bitmap-font text for the legacy Unity GUIText renderer of the port.
	///
	/// The port draws every glyph at the font's native pixel size (it ignores the transform scale). The game
	/// picks the font set whose resolution is nearest to the screen height (320/480/640/768/1536), so a
	/// 1080 px-high phone gets the 768 set: small, thin, blurry text. M7 selected the 1536 set instead, which
	/// is crisp but drawn twice as large, and overflowed the Store dialogs and item captions.
	///
	/// M10: still load the 1536 atlas on 700..1536 px screens, but scale the loaded font's glyph metrics by
	/// (nearest / 1536) * textScale. Text keeps the game's original layout size times the readability factor
	/// and is sampled from the high-resolution atlas. The game's own word wrapping measures the same scaled
	/// glyphs, so wrapped labels stay inside their boxes. Font assets, colours and layout code are unchanged.
	/// </summary>
	public static class UiFontQuality
	{
		/// The set the original game would have chosen and the one actually loaded (0 = not chosen yet).
		public static int NearestResolution { get; private set; }
		public static int SelectedResolution { get; private set; }

		/// Readability factor relative to the original game's text size (1.0 = same size, sharper glyphs).
		public static float TextScale { get; set; } = DefaultTextScale;
		public const float DefaultTextScale = 1.0f;
		public const float MinTextScale = 0.8f, MaxTextScale = 1.5f;

		public static string SelectResolution(List<int> supported, int screenHeight)
		{
			if (supported == null || supported.Count == 0)
				throw new ArgumentException("No supported font resolutions", nameof(supported));

			int closest = supported[0];
			int distance = Math.Abs(screenHeight - closest);
			foreach (int candidate in supported)
			{
				int diff = Math.Abs(screenHeight - candidate);
				if (diff < distance) { distance = diff; closest = candidate; }
			}
			// Diagnostic startup override: restore the port's native font selection
			// without changing normal builds or saved player settings.
			bool nativeBaseline = Environment.GetCommandLineArgs().Contains("--test-native-font-baseline");
			int chosen = !nativeBaseline && screenHeight >= 700 && screenHeight <= 1536 && supported.Contains(1536) ? 1536 : closest;
			NearestResolution = closest;
			SelectedResolution = chosen;
			return chosen.ToString(CultureInfo.InvariantCulture);
		}

		/// Glyph scale for fonts of the selected set: original size (nearest/selected) times the readability factor.
		public static float GlyphScale(int nearest, int selected, float textScale)
		{
			if (nearest <= 0 || selected <= 0) return 1f;
			float ts = float.IsNaN(textScale) ? DefaultTextScale : Math.Clamp(textScale, MinTextScale, MaxTextScale);
			return (float)nearest / selected * ts;
		}

		private sealed class Applied { public float K; }
		private static readonly ConditionalWeakTable<object, Applied> sScaled = new();
		private static readonly List<WeakReference> sFonts = new();
		private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		/// Hook: called with the result of SUILabel's first Resources.Load(basePath + font) (the selected set).
		/// Scales each Font object once. Anything unexpected leaves the font untouched.
		public static void PrepareFont(object font)
		{
			if (font == null || SelectedResolution == 0) return;
			float k = GlyphScale(NearestResolution, SelectedResolution, TextScale);
			lock (sScaled)
			{
				if (sScaled.TryGetValue(font, out _)) return;
				if (Math.Abs(k - 1f) < 1e-4f || ScaleFontMetrics(font, k))
				{
					sScaled.Add(font, new Applied { K = Math.Abs(k - 1f) < 1e-4f ? 1f : k });
					sFonts.Add(new WeakReference(font));
				}
			}
		}

		/// Change the readability factor at run time: every prepared font is rescaled in place (the next frame
		/// draws the new size; labels re-wrap when their screen is opened again). Returns the applied factor.
		public static float SetTextScale(float textScale)
		{
			TextScale = Math.Clamp(textScale, MinTextScale, MaxTextScale);
			if (SelectedResolution == 0) return TextScale;
			float k = GlyphScale(NearestResolution, SelectedResolution, TextScale);
			lock (sScaled)
			{
				sFonts.RemoveAll(w => !w.IsAlive);
				foreach (var w in sFonts)
				{
					var font = w.Target;
					if (font == null || !sScaled.TryGetValue(font, out var applied)) continue;
					if (Math.Abs(applied.K - k) < 1e-5f) continue;
					if (ScaleFontMetrics(font, k / applied.K)) applied.K = k;
				}
			}
			return TextScale;
		}

		/// Multiplies the port's Font metrics (__lineSpacing, __ascent, every glyph's vert rect and advance
		/// width) by k. Returns false and changes nothing if the layout is not the expected one.
		internal static bool ScaleFontMetrics(object font, float k)
		{
			try
			{
				var ft = font.GetType();
				var glyphsField = ft.GetField("__glyphs", F);
				var lineField = ft.GetField("__lineSpacing", F);
				var ascentField = ft.GetField("__ascent", F);
				if (glyphsField?.GetValue(font) is not IDictionary glyphs || lineField?.FieldType != typeof(float)) return false;
				var updates = new List<(object key, object glyph)>();
				foreach (DictionaryEntry e in glyphs)
				{
					object g = e.Value;   // boxed copy of the Glyph struct
					var gt = g.GetType();
					var vertF = gt.GetField("vert", F);
					var widthF = gt.GetField("width", F);
					if (vertF == null || widthF?.FieldType != typeof(float)) return false;
					object vert = vertF.GetValue(g);   // boxed Rect
					var vt = vert.GetType();
					foreach (var name in new[] { "x", "y", "width", "height" })
					{
						var p = vt.GetProperty(name, F);
						if (p?.PropertyType != typeof(float) || !p.CanWrite) return false;
						p.SetValue(vert, (float)p.GetValue(vert) * k);
					}
					vertF.SetValue(g, vert);
					widthF.SetValue(g, (float)widthF.GetValue(g) * k);
					updates.Add((e.Key, g));
				}
				foreach (var (key, g) in updates) glyphs[key] = g;
				lineField.SetValue(font, (float)lineField.GetValue(font) * k);
				if (ascentField?.FieldType == typeof(float)) ascentField.SetValue(font, (float)ascentField.GetValue(font) * k);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// Contrast halo for text whose visible colour is modulate x atlas colour: 1 = warm light halo (dark
		/// text), -1 = dark halo (light text), 0 = none (invisible text).
		public static int HaloKind(float r, float g, float b, float a, float atlasLuminance)
		{
			if (!(a > 0.01f)) return 0;
			float lum = (0.299f * r + 0.587f * g + 0.114f * b) * atlasLuminance;
			return lum < 0.45f ? 1 : -1;
		}

		/// Text-size steps of the 3-finger gesture (percent).
		public static readonly int[] ScaleSteps = { 100, 115 };

		/// Next text-size step after `current` (percent), wrapping around.
		public static int NextScaleStep(int current)
		{
			foreach (int s in ScaleSteps) if (s > current) return s;
			return ScaleSteps[0];
		}

		internal static void ResetForTests() { NearestResolution = 0; SelectedResolution = 0; TextScale = DefaultTextScale; }
	}
}
