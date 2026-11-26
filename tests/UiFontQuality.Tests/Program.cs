using System;
using System.Collections.Generic;
using Svz;

// Stand-ins with the port's Font layout (field names and types only; no font data).
public struct FakeRect
{
	private float m_X, m_Y, m_W, m_H;
	public FakeRect(float x, float y, float w, float h) { m_X = x; m_Y = y; m_W = w; m_H = h; }
	public float x { get => m_X; set => m_X = value; }
	public float y { get => m_Y; set => m_Y = value; }
	public float width { get => m_W; set => m_W = value; }
	public float height { get => m_H; set => m_H = value; }
}
public class FakeFont
{
	internal struct Glyph { public FakeRect uv, vert; public float width; }
	internal float __lineSpacing = 64f, __ascent = 50f, __kerning = 1f;
	internal readonly Dictionary<int, Glyph> __glyphs = new();
}
public class OddFont { internal float __lineSpacing = 10f; internal Dictionary<int, int> __glyphs = new() { [65] = 3 }; }

internal static class Test
{
	static int passed;
	static void Ok(bool c, string what) { if (!c) throw new Exception(what); passed++; }
	static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

	static void Main()
	{
		var supported = new List<int> { 320, 480, 640, 768, 1536 };
		void Check(int height, string want, int nearest)
		{
			string actual = UiFontQuality.SelectResolution(supported, height);
			Ok(actual == want, $"{height}: expected {want}, got {actual}");
			Ok(UiFontQuality.NearestResolution == nearest, $"{height}: nearest {UiFontQuality.NearestResolution}");
		}
		Check(320, "320", 320);
		Check(480, "480", 480);
		Check(640, "640", 640);
		Check(699, "640", 640);
		Check(709, "1536", 768);
		Check(768, "1536", 768);
		Check(1080, "1536", 768);
		Check(1440, "1536", 1536);
		Check(1536, "1536", 1536);
		Check(1600, "1536", 1536);
		Check(540, "480", 480);
		var reduced = new List<int> { 320, 480, 640, 768 };
		Ok(UiFontQuality.SelectResolution(reduced, 1080) == "768", "missing atlas fallback");
		Ok(supported.Count == 5, "input list mutated");

		// glyph scale: the original size (nearest/selected) times the readability factor, clamped
		Ok(Near(UiFontQuality.GlyphScale(768, 1536, 1f), 0.5f), "1080p: HD atlas drawn at the original size");
		Ok(Near(UiFontQuality.GlyphScale(768, 1536, 1.15f), 0.575f), "115 %");
		Ok(Near(UiFontQuality.GlyphScale(1536, 1536, 1f), 1f), "no upgrade, no change");
		Ok(Near(UiFontQuality.GlyphScale(768, 1536, 9f), 0.75f), "clamped to 150 %");
		Ok(Near(UiFontQuality.GlyphScale(768, 1536, float.NaN), 0.5f), "NaN uses the default");
		Ok(UiFontQuality.GlyphScale(0, 1536, 1f) == 1f, "unknown selection leaves fonts alone");

		// font metrics are scaled once, in place; uv (atlas region) is never touched
		var font = new FakeFont();
		font.__glyphs[65] = new FakeFont.Glyph { uv = new FakeRect(0.1f, 0.2f, 0.05f, 0.06f), vert = new FakeRect(2, -40, 30, -44), width = 32 };
		UiFontQuality.ResetForTests();
		UiFontQuality.SelectResolution(supported, 1080);
		UiFontQuality.PrepareFont(font);
		UiFontQuality.PrepareFont(font);   // second load of the same Font object: no double scaling
		var g = font.__glyphs[65];
		Ok(Near(g.vert.x, 1) && Near(g.vert.y, -20) && Near(g.vert.width, 15) && Near(g.vert.height, -22), "vert scaled by 0.5");
		Ok(Near(g.width, 16) && Near(font.__lineSpacing, 32) && Near(font.__ascent, 25), "advance, line spacing, ascent scaled");
		Ok(Near(g.uv.x, 0.1f) && Near(g.uv.width, 0.05f) && font.__kerning == 1f, "uv and kerning unchanged");

		// live change (3-finger gesture): 100 % -> 115 % -> 100 %
		UiFontQuality.SetTextScale(1.15f);
		Ok(Near(font.__glyphs[65].width, 32 * 0.575f) && Near(font.__lineSpacing, 64 * 0.575f), "rescaled to 115 %");
		UiFontQuality.SetTextScale(1f);
		Ok(Near(font.__glyphs[65].width, 16) && Near(font.__lineSpacing, 32), "back to 100 %");

		// unexpected layouts are left untouched
		var odd = new OddFont();
		UiFontQuality.PrepareFont(odd);
		Ok(odd.__lineSpacing == 10f && odd.__glyphs[65] == 3, "unknown font layout untouched");
		UiFontQuality.PrepareFont(null);
		var untouched = new FakeFont();
		untouched.__glyphs[66] = new FakeFont.Glyph { width = 10 };
		UiFontQuality.ResetForTests();   // nothing selected yet (e.g. fonts loaded before FontSetFinder)
		UiFontQuality.PrepareFont(untouched);
		Ok(untouched.__glyphs[66].width == 10, "no selection, no scaling");

		// contrast halo choice and gesture steps
		Ok(UiFontQuality.HaloKind(0.1f, 0.1f, 0.1f, 1f, 1f) == 1, "dark text gets a light halo");
		Ok(UiFontQuality.HaloKind(1f, 1f, 1f, 1f, 0.1f) == 1, "white modulate on a dark atlas is dark text");
		Ok(UiFontQuality.HaloKind(1f, 1f, 0.9f, 1f, 1f) == -1, "light text gets a dark halo");
		Ok(UiFontQuality.HaloKind(1f, 1f, 1f, 0f, 1f) == 0 && UiFontQuality.HaloKind(1, 1, 1, float.NaN, 1) == 0, "invisible text: no halo");
		Ok(UiFontQuality.NextScaleStep(100) == 115 && UiFontQuality.NextScaleStep(115) == 100 && UiFontQuality.NextScaleStep(130) == 100, "gesture steps");

		// Small glyphs on a 708px landscape display are eligible; untouched large
		// banners, colored atlases and colored reward labels are not.
		Ok(UiInkPolicy.InkKind(19, 708, 0.18f, 0.02f, 1, 1, 1, 1) == 1,
			"dark small glyph: solid dark ink");
		Ok(UiInkPolicy.InkKind(19, 708, 0.88f, 0.01f, 1, 1, 1, 1) == -1,
			"light small glyph: solid light ink");
		Ok(UiInkPolicy.InkKind(58, 708, 0.18f, 0.02f, 1, 1, 1, 1) == 0,
			"decorative heading remains original");
		Ok(UiInkPolicy.InkKind(19, 708, 0.18f, 0.5f, 1, 1, 1, 1) == 0,
			"colorful atlas remains original");
		Ok(UiInkPolicy.InkKind(19, 708, 0.18f, 0.02f, 1, 0, 0, 1) == 0,
			"red material color remains original");
		Ok(UiInkPolicy.InkKind(19, 708, 0.18f, 0.02f, 1, 1, 1, 0) == 0,
			"transparent text remains invisible");
		Ok(UiInkPolicy.InkKind(float.NaN, 708, 0.18f, 0.02f, 1, 1, 1, 1) == 0,
			"invalid dimensions always preserve original");
		Ok(UiInkPolicy.NextMode(UiInkPolicy.Original) == UiInkPolicy.SmoothedOriginal &&
			UiInkPolicy.NextMode(UiInkPolicy.SmoothedOriginal) == UiInkPolicy.Original &&
			UiInkPolicy.NextMode(UiInkPolicy.Native) == UiInkPolicy.Original &&
			UiInkPolicy.NextMode(UiInkPolicy.Solid) == UiInkPolicy.Original,
			"one hold toggles only original and prefiltered original atlas");

		Ok(UiGlyphSampling.UseHalfAtlas(768, 1536, 22, 708, 2048, 1024),
			"half-size original HD atlas may be prefiltered for small UI text");
		Ok(!UiGlyphSampling.UseHalfAtlas(1536, 1536, 22, 1440, 2048, 1024),
			"do not shrink font glyphs already at native resolution");
		Ok(!UiGlyphSampling.UseHalfAtlas(768, 1536, 80, 708, 2048, 1024),
			"preserve large original painted headings");
		Ok(!UiGlyphSampling.UseHalfAtlas(768, 1536, float.NaN, 708, 2048, 1024),
			"invalid text geometry uses unaltered atlas");
		Ok(!UiGlyphSampling.UseHalfAtlas(768, 1536, 22, 708, 8192, 8192),
			"avoid oversized runtime font texture copies");
		Ok(UiGlyphSampling.HalfSize(2048) == 1024 &&
			Math.Abs(UiGlyphSampling.MapCoordinate(512, 2048, 1024) - 256) < 0.001f &&
			Math.Abs(UiGlyphSampling.MapCoordinate(32, 1024, 512) - 16) < 0.001f,
			"downsampled UV coordinate and size mapping preserves text geometry");

		Ok(UiReadablePolicy.Eligible("LEVEL UP HERO", 30, 709, 1, 1, 1, 1),
			"small monochrome labels are eligible for readable font");
		Ok(UiReadablePolicy.Eligible("WAVE 16 DESCRIPTION", 35, 709, 1, 1, 1, 1),
			"small wave description eligible");
		Ok(!UiReadablePolicy.Eligible("SHOP", 80, 709, 1, 1, 1, 1),
			"large decorative headings stay original");
		Ok(!UiReadablePolicy.Eligible("DAY 1", 24, 709, 1, 0.2f, 0.2f, 1),
			"colored reward labels remain in original font");
		Ok(!UiReadablePolicy.Eligible(" ", 24, 709, 1, 1, 1, 1),
			"blank labels do not create native draw calls");
		Ok(!UiReadablePolicy.Eligible("HI", 24, 709, 1, 1, 1, 0),
			"transparent labels remain hidden");
		Ok(!UiReadablePolicy.Eligible("HI", float.NaN, 709, 1, 1, 1, 1),
			"invalid metrics fall back to original rendering");
		Ok(UiReadablePolicy.FontSize(32) == 27,
			"native font sized to match measured legacy line height");
		// A source-authentic pixel fill: only one isolated interior pinhole is
		// repaired; outer edges, letter counters, color and neighboring pixels
		// remain untouched. This is applied to a cached atlas, never in combat.
		var pixels = new byte[7 * 7 * 4];
		void Ink(int x, int y, byte r = 22, byte g = 44, byte b = 66)
		{
			int i = (y * 7 + x) * 4;
			pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
		}
		Ink(2, 3); Ink(4, 3); Ink(3, 2);
		Ok(UiGlyphAlphaRepair.Repair(pixels, 7, 7) == 1,
			"one three-sided pinhole healed without changing silhouette");
		Ok(pixels[(3 * 7 + 3) * 4 + 3] == 220 &&
			pixels[(3 * 7 + 3) * 4] == 22,
			"repaired pixel gets neighboring ink color and bounded opacity");
		Ok(pixels[(3 * 7 + 1) * 4 + 3] == 0,
			"outer transparent background remains untouched");
		Ok(UiGlyphAlphaRepair.Repair(pixels, 7, 7) == 0,
			"repeated repair is idempotent on filled pinholes");
		var ring = new byte[8 * 8 * 4];
		for (int y = 1; y < 7; y++) for (int x = 1; x < 7; x++)
		{
			int i = (y * 8 + x) * 4;
			ring[i] = 7; ring[i + 1] = 11; ring[i + 2] = 21; ring[i + 3] = 255;
		}
		for (int y = 3; y <= 4; y++) for (int x = 3; x <= 4; x++)
			ring[(y * 8 + x) * 4 + 3] = 0;
		Ok(UiGlyphAlphaRepair.Repair(ring, 8, 8) == 0 &&
			ring[(3 * 8 + 3) * 4 + 3] == 0,
			"two-pixel letter counters stay open");
		bool badSize = false;
		try { UiGlyphAlphaRepair.Repair(new byte[10], 7, 7); }
		catch (ArgumentException) { badSize = true; }
		Ok(badSize, "corrupt pixel buffers fail without drawing");
		Console.WriteLine($"[test] {passed} HD font selection and original-glyph repair assertions passed");
	}
}
