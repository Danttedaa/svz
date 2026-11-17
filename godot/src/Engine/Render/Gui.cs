using System;
using System.Collections.Generic;
using System.Linq;
using Svz;
using G = Godot;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEngine
{
	public partial class GUIElement
	{
		internal override void __OnAttach() => GuiSystem.Elements.Add(this);
		internal override void __OnDestroyNative() => GuiSystem.Elements.Remove(this);

		private static partial bool INTERNAL_CALL_HitTest(GUIElement self, ref Vector3 screenPosition, Camera camera) =>
			self.GetScreenRect(camera).Contains(new Vector2(screenPosition.x, screenPosition.y));

		public partial Rect GetScreenRect(Camera camera) => __ScreenRect();

		internal virtual Rect __ScreenRect() => new Rect(0, 0, 0, 0);
	}

	public partial class GUITexture
	{
		internal Texture __texture;
		internal Color __color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
		internal Rect __inset;
		internal int __lb, __rb, __tb, __bb;

		public partial Texture texture { get => __texture; set => __texture = value; }
		private partial void INTERNAL_get_color(out Color value) => value = __color;
		private partial void INTERNAL_set_color(ref Color value) => __color = value;
		private partial void INTERNAL_get_pixelInset(out Rect value) => value = __inset;
		private partial void INTERNAL_set_pixelInset(ref Rect value) => __inset = value;

		internal override Rect __ScreenRect()
		{
			// Unity 3 GUITexture: centred normalized scale + pixel inset, positioned in viewport space
			float sw = Screen.width, sh = Screen.height;
			Vector3 p = transform.position, s = transform.lossyScale;
			float w = s.x * sw, h = s.y * sh;
			float x = p.x * sw - w * 0.5f, y = p.y * sh - h * 0.5f;
			return new Rect(x + __inset.x, y + __inset.y, w + __inset.width, h + __inset.height);
		}

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (GUITexture)src;
			__texture = s.__texture; __color = s.__color; __inset = s.__inset;
		}
	}

	public partial class GUIText
	{
		internal string __text = "";
		internal Material __material;
		internal Font __font;
		internal TextAlignment __align;
		internal TextAnchor __anchor;
		internal float __lineSpacing = 1f, __tabSize = 4f;
		internal int __fontSize;
		internal FontStyle __style;
		internal Vector2 __pixelOffset;
		internal Material __instMat;

		public partial string text { get => __text; set => __text = value ?? ""; }

		// GUIText.material returns an instance of the font material (Unity behaviour)
		public partial Material material
		{
			get
			{
				if (__instMat == null)
				{
					var baseMat = __material ?? __font?.__material;
					__instMat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("GUI/Text Shader"));
				}
				return __instMat;
			}
			set { __material = value; __instMat = value; }
		}

		public partial Font font { get => __font; set => __font = value; }
		public partial TextAlignment alignment { get => __align; set => __align = value; }
		public partial TextAnchor anchor { get => __anchor; set => __anchor = value; }
		public partial float lineSpacing { get => __lineSpacing; set => __lineSpacing = value; }
		public partial float tabSize { get => __tabSize; set => __tabSize = value; }
		public partial int fontSize { get => __fontSize; set => __fontSize = value; }
		public partial FontStyle fontStyle { get => __style; set => __style = value; }
		private partial void Internal_GetPixelOffset(out Vector2 output) => output = __pixelOffset;
		private static partial void INTERNAL_CALL_Internal_SetPixelOffset(GUIText self, ref Vector2 p) => self.__pixelOffset = p;

		internal Color __Color
		{
			get
			{
				var m = __instMat ?? __material ?? __font?.__material;
				return m != null && m.__colors.TryGetValue("_Color", out var c) ? c : Color.white;
			}
		}

		internal override Rect __ScreenRect()
		{
			var lay = TextLayout.Measure(this);
			return lay.rect;
		}

		internal override void __CopyFrom(Object src, Cloner c)
		{
			base.__CopyFrom(src, c);
			var s = (GUIText)src;
			__text = s.__text; __material = s.__material; __font = s.__font; __align = s.__align; __anchor = s.__anchor;
			__lineSpacing = s.__lineSpacing; __tabSize = s.__tabSize; __fontSize = s.__fontSize; __style = s.__style;
			__pixelOffset = s.__pixelOffset;
			__instMat = s.__instMat != null ? new Material(s.__instMat) : null;
		}
	}

	public partial class GUILayer
	{
		private static partial GUIElement INTERNAL_CALL_HitTest(GUILayer self, ref Vector3 screenPosition)
		{
			GUIElement best = null; float bestZ = float.NegativeInfinity;
			foreach (var e in GuiSystem.Elements)
			{
				if (!e.__enabled || e.gameObject == null || !e.gameObject.active) continue;
				if (!e.HitTest(screenPosition)) continue;
				float z = e.transform.position.z;
				if (z >= bestZ) { bestZ = z; best = e; }
			}
			return best;
		}
	}
}

namespace Svz
{
	/// Layout of a GUIText in Unity screen pixels (y up).
	internal struct TextLayout
	{
		public Rect rect;
		public List<(Font.Glyph g, float x, float y)> glyphs;

		public static TextLayout Measure(GUIText t, bool withGlyphs = false)
		{
			var lay = new TextLayout { glyphs = withGlyphs ? new List<(Font.Glyph, float, float)>() : null };
			var font = t.__font;
			string text = t.__text ?? "";
			if (font == null) { lay.rect = new Rect(0, 0, 0, 0); return lay; }
			float lineH = font.__lineSpacing * t.__lineSpacing;
			var lines = text.Split('\n');
			var widths = new float[lines.Length];
			for (int li = 0; li < lines.Length; li++)
			{
				float w = 0;
				foreach (char ch in lines[li])
				{
					if (ch == '\t') { w += SpaceWidth(font) * t.__tabSize; continue; }
					if (font.__glyphs.TryGetValue(ch, out var g)) w += g.width * font.__kerning;
				}
				widths[li] = w;
			}
			float boxW = widths.Length > 0 ? widths.Max() : 0, boxH = lines.Length * lineH;
			Vector3 p = t.transform.position;
			float ax = p.x * Screen.width + t.__pixelOffset.x, ay = p.y * Screen.height + t.__pixelOffset.y;
			// anchor -> top-left of the box
			int a = (int)t.__anchor;
			int col = a % 3, row = a / 3;   // row 0 upper, 1 middle, 2 lower
			float left = ax - (col == 1 ? boxW * 0.5f : col == 2 ? boxW : 0f);
			float top = ay + (row == 1 ? boxH * 0.5f : row == 2 ? boxH : 0f);
			lay.rect = new Rect(left, top - boxH, boxW, boxH);
			if (withGlyphs)
			{
				for (int li = 0; li < lines.Length; li++)
				{
					float x = left + (t.__align == TextAlignment.Center ? (boxW - widths[li]) * 0.5f : t.__align == TextAlignment.Right ? boxW - widths[li] : 0f);
					float y = top - li * lineH;
					foreach (char ch in lines[li])
					{
						if (ch == '\t') { x += SpaceWidth(font) * t.__tabSize; continue; }
						if (!font.__glyphs.TryGetValue(ch, out var g)) continue;
						lay.glyphs.Add((g, x, y));
						x += g.width * font.__kerning;
					}
				}
			}
			return lay;
		}

		private static float SpaceWidth(Font f) => f.__glyphs.TryGetValue(' ', out var g) ? g.width : f.__lineSpacing * 0.3f;
	}

	public static partial class GuiSystem
	{
		public static readonly List<GUIElement> Elements = new List<GUIElement>();
		private static GuiCanvas sCanvas;

		public static void Frame()
		{
			if (sCanvas == null)
			{
				sCanvas = new GuiCanvas { Name = "GuiCanvas" };
				Main.Instance.Gui.AddChild(sCanvas);
			}
			sCanvas.QueueRedraw();
			ImGui.Frame();
		}

		internal static bool AnyGuiLayer() => Render.Cameras.Any(c => c.__enabled && c.gameObject.active && c.GetComponent<GUILayer>() != null && c.GetComponent<GUILayer>().enabled);

		private partial class GuiCanvas : G.Node2D
		{
			public override void _Draw()
			{
				if (!AnyGuiLayer()) return;
				float sh = Screen.height;
				var list = Elements.Where(e => e.__enabled && !e.__destroyed && e.gameObject != null && e.gameObject.__active && !e.gameObject.__isAsset)
					.OrderBy(e => e.transform.position.z).ToList();
				foreach (var e in list)
				{
					try
					{
						if (e is GUITexture gt) DrawTexture(gt, sh);
						else if (e is GUIText tx) DrawText(tx, sh);
					}
					catch (Exception ex) { Log.Exception(ex, "GUI draw " + e.name); }
				}
			}

			private void DrawTexture(GUITexture gt, float sh)
			{
				var tex = gt.__texture?.__Godot;
				if (tex == null) return;
				var r = gt.__ScreenRect();
				if (r.width == 0 || r.height == 0) return;
				var c = gt.__color;
				var mod = new G.Color(Math.Min(1f, c.r * 2f), Math.Min(1f, c.g * 2f), Math.Min(1f, c.b * 2f), Math.Min(1f, c.a * 2f));
				float x = r.x, y = sh - (r.y + r.height), w = r.width, h = r.height;
				// negative sizes mirror the image
				DrawTextureRect(tex, new G.Rect2(x, y, w, h), false, mod);
			}

			private void DrawText(GUIText t, float sh)
			{
				var font = t.__font;
				var tex = (font?.__texture ?? (t.__instMat ?? t.__material)?.GetTexture("_MainTex"))?.__Godot;
				if (font == null || tex == null || string.IsNullOrEmpty(t.__text)) return;
				var lay = TextLayout.Measure(t, true);
				var c = t.__Color;
				var mod = new G.Color(c.r, c.g, c.b, c.a);
				float tw = tex.GetWidth(), th = tex.GetHeight();
				foreach (var (g, x, y) in lay.glyphs)
				{
					if (g.vert.width == 0 || g.vert.height == 0) continue;
					float left = x + g.vert.x, top = y + g.vert.y;           // Unity pixels, y up
					float bottom = top + g.vert.height;
					var dst = new G.Rect2(left, sh - top, g.vert.width, top - bottom);
					var uv = g.uv;
					var src = new G.Rect2(uv.x * tw, (1f - (uv.y + uv.height)) * th, uv.width * tw, uv.height * th);
					DrawTextureRectRegion(tex, dst, src, mod);
				}
			}
		}
	}
}
